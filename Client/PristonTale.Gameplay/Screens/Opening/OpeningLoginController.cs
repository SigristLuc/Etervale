using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using PristonTale.Application.Services;
using PristonTale.Client.Net;
using PristonTale.Gameplay.Screens.Framework;
using PristonTale.Gameplay.UI;

namespace PristonTale.Gameplay.Screens.Opening;

internal sealed class OpeningLoginController : IDisposable
{
    private enum Phase { Login, Authenticating }
    private const int ClientVersion = 1;

    private readonly ScreenContext _context;
    private readonly LoginPanel _login;

    private Phase _phase = Phase.Login;
    private Task<LoginOutcome> _loginTask;
    private CancellationTokenSource _loginCts;
    private OfflineBackend _pendingOfflineBackend;

    public OpeningLoginController(ScreenContext context)
    {
        _context = context;
        _login = new LoginPanel(context.SpriteBatch, context.Font, context.UiAtlas);
    }

    public enum LoginResult { None, Succeeded }

    public LoginResult Update(in KeyboardState keyboard, in KeyboardState lastKeyboard, float dt)
    {
        switch (_phase)
        {
            case Phase.Login:
                switch (_login.Update(keyboard, lastKeyboard, dt, _context.GraphicsDevice.Viewport))
                {
                    case LoginIntent.SubmitLogin: BeginLogin(); break;
                    case LoginIntent.PlayOffline: BeginOfflineLogin(); break;
                }
                break;

            case Phase.Authenticating:
                return PollLogin();
        }
        return LoginResult.None;
    }

    private void BeginLogin()
    {
        if (string.IsNullOrWhiteSpace(_login.Account.Text))
        {
            _login.Status = "Enter an account name.";
            return;
        }
        _login.Status = "Connecting…";
        _login.InputEnabled = false;
        _phase = Phase.Authenticating;
        _loginCts = new CancellationTokenSource();
        _loginTask = _context.Backend.LoginAsync(_login.Account.Text, _login.Password.Text, ClientVersion, _loginCts.Token);
    }

    private void BeginOfflineLogin()
    {
        var profile = string.IsNullOrWhiteSpace(_login.Account.Text) ? "Player" : _login.Account.Text;
        _login.Status = "Loading local save…";
        _login.InputEnabled = false;
        _phase = Phase.Authenticating;
        _loginCts = new CancellationTokenSource();
        var cancellationToken = _loginCts.Token;
        _loginTask = Task.Run(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            _pendingOfflineBackend = new OfflineBackend(OfflineSaveLocation.Default(), message => _context.Log.Error(message));
            return await _pendingOfflineBackend.LoginAsync(profile, string.Empty, ClientVersion, cancellationToken);
        }, cancellationToken);
    }

    private LoginResult PollLogin()
    {
        if (_loginTask is not { IsCompleted: true })
            return LoginResult.None;

        var result = LoginResult.None;
        if (_loginTask.IsCompletedSuccessfully && _loginTask.Result.Ok)
        {
            if (_pendingOfflineBackend != null)
            {
                _context.UseBackend(_pendingOfflineBackend);
                _pendingOfflineBackend = null;
            }
            result = LoginResult.Succeeded;
        }
        else
        {
            _login.Status = _loginTask.IsCompletedSuccessfully
                ? (string.IsNullOrWhiteSpace(_loginTask.Result.Message) ? "Login failed." : _loginTask.Result.Message)
                : (_pendingOfflineBackend != null ? "Could not open local save." : "Could not reach server.");
            _login.InputEnabled = true;
            _phase = Phase.Login;
            DiscardPendingOfflineBackend();
        }
        _loginTask = null;
        _loginCts?.Dispose();
        _loginCts = null;
        return result;
    }

    private void DiscardPendingOfflineBackend()
    {
        if (_pendingOfflineBackend == null)
        {
            return;
        }

        var backend = _pendingOfflineBackend;
        _pendingOfflineBackend = null;
        _ = DisposeBackendAsync(backend);
    }

    private async Task DisposeBackendAsync(OfflineBackend backend)
    {
        try
        {
            await backend.DisposeAsync();
        }
        catch (Exception ex)
        {
            _context.Log.Error($"[Login] offline cleanup failed: {ex}");
        }
    }

    private async Task FinishCanceledLoginAsync(Task<LoginOutcome> login)
    {
        try
        {
            await login;
        }
        catch (Exception ex)
        {
            if (ex is not OperationCanceledException)
            {
                _context.Log.Error($"[Login] canceled login failed: {ex}");
            }
        }

        DiscardPendingOfflineBackend();
    }

    public void Draw(Viewport viewport) => _login.Draw(viewport);

    public void Dispose()
    {
        _loginCts?.Cancel();
        _loginCts?.Dispose();
        if (_loginTask is not null)
        {
            _ = FinishCanceledLoginAsync(_loginTask);
        }
        else
        {
            DiscardPendingOfflineBackend();
        }

    }
}
