using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Client.Net;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Gameplay.Audio;

namespace PristonTale.Gameplay.Screens.Framework;

public sealed class ScreenContext
{
    private readonly Action<GameScreen> _requestScreen;

    public ScreenContext(
        GraphicsDevice graphicsDevice,
        ContentManager content,
        SpriteBatch spriteBatch,
        SpriteFont font,
        string assetRoot,
        IGameBackend backend,
        IClientLog log,
        Action<GameScreen> requestScreen,
        PristonTale.Engine3D.Rendering.PresentationSettings presentation = null,
        PristonTale.Gameplay.UI.Rendering.HudAtlas uiAtlas = null,
        PristonTale.Engine3D.Rendering.PristonSkyLibrary sky = null,
        PristonTale.Gameplay.Effects.LegacyEffectLibrary effects = null,
        PristonAudioLibrary audio = null)
    {
        GraphicsDevice = graphicsDevice;
        Content = content;
        SpriteBatch = spriteBatch;
        Font = font;
        AssetRoot = assetRoot;
        Backend = backend;
        Log = log ?? ConsoleClientLog.Instance;
        _requestScreen = requestScreen;
        Presentation = presentation ?? new PristonTale.Engine3D.Rendering.PresentationSettings();
        UiAtlas = uiAtlas;
        Sky = sky;
        Effects = effects;
        Audio = audio;
    }

    public PristonTale.Engine3D.Rendering.PristonSkyLibrary Sky { get; }
    public PristonTale.Gameplay.Effects.LegacyEffectLibrary Effects { get; }
    public PristonAudioLibrary Audio { get; }
    public GraphicsDevice GraphicsDevice { get; }
    public ContentManager Content { get; }
    public SpriteBatch SpriteBatch { get; }
    public SpriteFont Font { get; }
    public string AssetRoot { get; }
    public bool IsWindowActive { get; internal set; } = true;
    public PristonTale.Engine3D.Rendering.PresentationSettings Presentation { get; }
    public PristonTale.Gameplay.UI.Rendering.HudAtlas UiAtlas { get; }

    public IClientLog Log { get; }

    public IGameBackend Backend { get; }

    public void RequestScreen(GameScreen next) => _requestScreen(next);
}
