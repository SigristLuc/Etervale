using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Effects;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Gameplay.Effects;

namespace PristonTale.Tests;

internal sealed class PotionCaptureProbe : Game
{
    private readonly string _assets;
    private readonly string _output;
    private LegacyEffectLibrary _library = null!;
    private EffectsEngine _effects = null!;
    private RenderTarget2D _target = null!;
    private readonly float[] _times = [0.15f, 0.45f, 0.8f, 1.2f];
    private int _sample;

    public PotionCaptureProbe(string assets, string content, string output)
    {
        _assets = assets;
        Content = new AbsoluteContentManager(Services, content);
        _output = output;
        _ = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 960,
            PreferredBackBufferHeight = 540,
            GraphicsProfile = GraphicsProfile.HiDef,
            SynchronizeWithVerticalRetrace = false
        };
        IsFixedTimeStep = false;
    }

    protected override void LoadContent()
    {
        Directory.CreateDirectory(_output);
        _library = LegacyEffectLibrary.LoadBootstrap(GraphicsDevice, Content, _assets, ConsoleClientLog.Instance);
        // No HDR scene here: the particles write display-referred colour straight into the 8-bit capture.
        _effects = new EffectsEngine(GraphicsDevice, Content) { DisplayOutput = true };
        _target = new RenderTarget2D(GraphicsDevice, 960, 540, false, SurfaceFormat.Color, DepthFormat.Depth24);
    }

    protected override void Draw(GameTime gameTime)
    {
        _effects.Clear();
        for (var resource = 0; resource < 3; resource++)
        {
            _effects.Spawn(_library.Potion(resource), new Vector3((resource - 1) * 95f, 48f, 0));
        }

        var elapsed = new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(_times[_sample]));
        _effects.Update(elapsed);
        GraphicsDevice.SetRenderTarget(_target);
        GraphicsDevice.Clear(new Color(18, 22, 25));
        var camera = new Vector3(0, 95, 240);
        _effects.Draw(Matrix.CreateLookAt(camera, new Vector3(0, 35, 0), Vector3.Up),
            Matrix.CreatePerspectiveFieldOfView(MathHelper.PiOver4, 960f / 540f, 1, 1000), camera);
        GraphicsDevice.SetRenderTarget(null);
        const string name = "potions";
        var path = Path.Combine(_output, $"{name}-{_sample}.png");
        using (var stream = File.Create(path))
        {
            _target.SaveAsPng(stream, 960, 540);
        }

        Console.WriteLine($"{name}, t={_times[_sample]:0.00}s: {path}");
        _sample++;
        if (_sample == _times.Length)
        {
            Exit();
        }
    }

    protected override void UnloadContent()
    {
        _effects.Dispose();
        _library.Dispose();
        _target.Dispose();
    }
}
