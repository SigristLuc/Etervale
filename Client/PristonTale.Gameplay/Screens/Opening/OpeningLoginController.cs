using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using PristonTale.Application.Services;
using PristonTale.Gameplay.Screens.Framework;
using PristonTale.Gameplay.UI;

namespace PristonTale.Gameplay.Screens.Opening;

internal sealed class OpeningLoginController : IDisposable
{
    private enum Phase { Login, Authenticating }
    private const int ClientVersion = PristonTale.Client.Net.GameServerConnection.EnglishClientVersion;

    private readonly ScreenContext _context;
    private readonly LoginPanel _login;
    private Phase _phase = Phase.Login;
    private Task<LoginOutcome> _loginTask;
    private CancellationTokenSource _loginCts;

    public OpeningLoginController(ScreenContext context)
    {
        _context = context;
        _login = new LoginPanel(context.SpriteBatch, context.Font, context.UiAtlas);
    }

    public enum LoginResult { None, Succeeded }

    public LoginResult Update(in KeyboardState keyboard, in KeyboardState lastKeyboard, float dt)
    {
        if (_phase == Phase.Login && _login.Update(keyboard, lastKeyboard, dt, _context.GraphicsDevice.Viewport) == LoginIntent.SubmitLogin)
        {
            BeginLogin();
        }

        return _phase == Phase.Authenticating ? PollLogin() : LoginResult.None;
    }

    private void BeginLogin()
    {
        if (string.IsNullOrWhiteSpace(_login.Account.Text) || string.IsNullOrEmpty(_login.Password.Text))
        {
            _login.Status = "Enter your account and password.";
            return;
        }

        _login.Status = "Connecting…";
        _login.InputEnabled = false;
        _phase = Phase.Authenticating;
        _loginCts = new CancellationTokenSource();
        _loginTask = _context.Backend.LoginAsync(_login.Account.Text, _login.Password.Text, ClientVersion, _loginCts.Token);
    }

    private LoginResult PollLogin()
    {
        if (_loginTask is not { IsCompleted: true })
        {
            return LoginResult.None;
        }

        var succeeded = _loginTask.IsCompletedSuccessfully && _loginTask.Result.Ok;
        if (!succeeded)
        {
            _login.Status = _loginTask.IsCompletedSuccessfully
                ? _loginTask.Result.Message
                : "Could not reach the game server.";
            _login.InputEnabled = true;
            _phase = Phase.Login;
        }

        _loginTask = null;
        _loginCts?.Dispose();
        _loginCts = null;
        return succeeded ? LoginResult.Succeeded : LoginResult.None;
    }

    public void Draw(Viewport viewport) => _login.Draw(viewport);

    public void Dispose()
    {
        _loginCts?.Cancel();
        _loginCts?.Dispose();
        if (_loginTask is not null)
        {
            _ = ObserveLoginAsync(_loginTask);
        }
    }

    private async Task ObserveLoginAsync(Task<LoginOutcome> login)
    {
        try
        {
            await login;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _context.Log.Error($"[Login] canceled request failed: {ex}");
        }
    }
}
