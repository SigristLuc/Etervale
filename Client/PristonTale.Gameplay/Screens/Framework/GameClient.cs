using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Client.Net;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.Screens.Opening;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Gameplay.UI;
using PristonTale.Gameplay.Audio;

namespace PristonTale.Gameplay.Screens.Framework;

public sealed class GameClient : IDisposable
{
    private readonly GraphicsDevice _graphicsDevice;
    private readonly ContentManager _content;
    private readonly string _assetRoot;
    private readonly string _serverAddress;

    private SpriteBatch _spriteBatch;
    private IGameBackend _backend;
    private ScreenContext _context;
    private GameScreen _screen;
    private GameScreen _pendingScreen;
    private Task _shutdown;
    private SpriteFont _font;
    private HudAtlas _uiAtlas;
    private PristonSkyLibrary _sky;
    private PristonTale.Gameplay.Effects.LegacyEffectLibrary _effects;
    private PristonAudioLibrary _audio;
    private readonly PresentationSettings _presentation;
    private DisplayConfirmation _displayConfirmation;

    public GameClient(GraphicsDevice graphicsDevice, ContentManager content, string assetRoot, string serverAddress, PresentationSettings presentation = null)
    {
        _graphicsDevice = graphicsDevice;
        _content = content;
        _assetRoot = assetRoot;
        _serverAddress = serverAddress;
        _presentation = presentation ?? new PresentationSettings();
    }

    public string StatusText
    {
        get
        {
            if (_shutdown is not null)
            {
                return "Saving and disconnecting…";
            }

            return _screen?.StatusText ?? "Loading…";
        }
    }

    public void LoadContent()
    {
        _spriteBatch = new SpriteBatch(_graphicsDevice);
        var font = _content.Load<SpriteFont>("Fonts/UI");
        _font = font;
        _uiAtlas = HudAtlas.LoadBootstrap(_graphicsDevice, _assetRoot, ConsoleClientLog.Instance);
        _sky = PristonSkyLibrary.LoadBootstrap(_graphicsDevice, _assetRoot, ConsoleClientLog.Instance);
        _effects = PristonTale.Gameplay.Effects.LegacyEffectLibrary.LoadBootstrap(_graphicsDevice, _content, _assetRoot, ConsoleClientLog.Instance);
        _audio = PristonAudioLibrary.LoadBootstrap(_assetRoot, ConsoleClientLog.Instance);
        _audio.ApplySettings(_presentation.Requested);
        _backend = new GameServerConnection(_serverAddress);
        _context = new ScreenContext(
            _graphicsDevice,
            _content,
            _spriteBatch,
            font,
            _assetRoot,
            _backend,
            ConsoleClientLog.Instance,
            RequestScreen,
            _presentation,
            _uiAtlas,
            _sky,
            _effects,
            _audio);
        _displayConfirmation = new DisplayConfirmation(_context);
        // First screen: the opening / authentication scene (the login form overlays it next).
        SetScreen(new OpeningScreen(_context));
    }

    public bool HandleEscape() => _screen?.HandleEscape() == true;

    public void Update(GameTime gameTime, bool isWindowActive = true)
    {
        _audio?.ApplySettings(_presentation.Requested);
        if (_context is not null)
        {
            _context.IsWindowActive = isWindowActive;
        }

        _displayConfirmation?.Update();
        _screen?.Update(gameTime);
        if (_pendingScreen != null)
        {
            var next = _pendingScreen;
            _pendingScreen = null;
            SetScreen(next);
        }
    }

    public void Draw()
    {
        if (_shutdown is null)
        {
            _screen?.Draw();
            _displayConfirmation?.Draw();
            return;
        }

        _graphicsDevice.Clear(Color.Black);
        _spriteBatch.Begin();
        _spriteBatch.DrawString(_font, StatusText, new Vector2(24f, 24f), Color.White);
        _spriteBatch.End();
    }

    private void RequestScreen(GameScreen next) => _pendingScreen = next;

    private void SetScreen(GameScreen next)
    {
        _screen?.Dispose();
        _screen = next;
        _screen.Enter();
    }

    private static async Task DisposeBackendAsync(IGameBackend backend)
    {
        try
        {
            await backend.DisposeAsync();
        }
        catch (Exception ex)
        {
            ConsoleClientLog.Instance.Error($"[Client] backend shutdown failed: {ex}");
        }
    }

    public Task BeginShutdown()
    {
        if (_shutdown is not null)
        {
            return _shutdown;
        }

        // GPU resources are released on the game thread. Network/database cleanup runs asynchronously.
        _screen?.Dispose();
        _screen = null;
        _pendingScreen = null;
        _shutdown = _backend is null ? Task.CompletedTask : Task.Run(() => DisposeBackendAsync(_backend));
        return _shutdown;
    }

    public void Dispose()
    {
        _ = BeginShutdown();
        _uiAtlas?.Dispose();
        _sky?.Dispose();
        _effects?.Dispose();
        _audio?.Dispose();
        _spriteBatch?.Dispose();
    }
}
