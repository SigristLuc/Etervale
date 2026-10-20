using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Client.Net.Presence;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.World;
using PristonTale.Gameplay.World.Remote;
using PristonTale.Simulation.World.Loot;

namespace PristonTale.Tests;

internal sealed class GroundLootCaptureProbe : Game
{
    private readonly string _root;
    private readonly string _output;
    private PristonRenderer _renderer = null!;
    private GroundLootPresentation _ground = null!;
    private PristonCamera _camera = null!;
    private SpriteBatch _batch = null!;
    private SpriteFont _font = null!;
    private GroundDropView[] _drops = [];
    private int _frames;

    public GroundLootCaptureProbe(string root, string content, string output)
    {
        _root = root;
        _output = output;
        Content = new AbsoluteContentManager(Services, content);
        _ = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 960,
            PreferredBackBufferHeight = 540,
            GraphicsProfile = GraphicsProfile.HiDef,
            SynchronizeWithVerticalRetrace = false
        };
    }

    protected override void LoadContent()
    {
        _renderer = new PristonRenderer(GraphicsDevice, Content) { ShadowsEnabled = false, DayLengthSeconds = 0 };
        _renderer.SetTimeOfDay(12);
        _ground = new GroundLootPresentation(new RendererActorScene(_renderer), new PristonActorLoader(), _root, ConsoleClientLog.Instance);
        _camera = new PristonCamera(Vector3.Zero, Vector3.Forward, new PristonCameraSettings());
        _batch = new SpriteBatch(GraphicsDevice);
        _font = Content.Load<SpriteFont>("Fonts/UI");
        _drops =
        [
            Drop("GG101", "Gold", 13, 0),
            Drop("GG101", "Gold", 15, 0),
            Drop("WA101", "Stone Axe", 1, -60),
            Drop("PL101", "Mini Life Potion", 1, 60)
        ];
        _ground.Apply(new PresenceUpdate(1, [], [], [], []) { GroundAdded = _drops });
        _ground.Update(MissingFloor);
        Assert.True(!_ground.TryGet(_drops[0].Id, out _), "drop published without a resident floor");
        _ground.InvalidateFloor();
    }

    private static GroundDropView Drop(string key, string name, int amount, float x) =>
        new(Guid.NewGuid(), 0, key, name, amount, key == "GG101", new System.Numerics.Vector3(x, 0, -20));

    private static bool MissingFloor(float x, float z, out float height)
    {
        height = 0;
        return false;
    }

    private static bool Floor(float x, float z, out float height)
    {
        height = 50;
        return true;
    }

    private static bool TraversalFloor(Vector3 point, float reference, out float height) => Floor(point.X, point.Z, out height);

    protected override void Update(GameTime gameTime)
    {
        _ground.Update(Floor);
        _renderer.Update(gameTime);
        _frames++;
    }

    protected override void Draw(GameTime gameTime)
    {
        if (_frames < 180)
        {
            return;
        }

        using var target = new RenderTarget2D(GraphicsDevice, 960, 540, false, SurfaceFormat.Color, DepthFormat.Depth24);
        GraphicsDevice.SetRenderTarget(target);
        GraphicsDevice.Clear(new Color(42, 48, 54));
        _camera.SetScriptedView(GraphicsDevice, new Vector3(0, 195, 230), new Vector3(0, 50, 20));
        _renderer.Draw(_camera);
        _renderer.ComposeScene();
        _batch.Begin();
        _ground.DrawLabels(_batch, _font, GraphicsDevice.Viewport, _camera, showAll: true);
        _batch.End();
        foreach (var drop in _drops)
        {
            Assert.True(_ground.TryGet(drop.Id, out var position), "resident drop has no floor position");
            Assert.Near(50, position.Y);
            Assert.Near(20, position.Z);
            Assert.True(GroundTraversal.CanReachSurface(new Vector3(0, 50, 20), position, TraversalFloor),
                "visual lift contaminated the pickup destination");
        }

        var clicked = new HashSet<Guid>();
        var away = new Ray(new Vector3(10000, 10000, 10000), Vector3.Up);
        for (int y = 0; y < 540; y += 4)
        {
            for (int x = 0; x < 960; x += 4)
            {
                if (_ground.Pick(new Point(x, y), away, out var id, out _, showAllLabels: true))
                {
                    clicked.Add(id);
                }
            }
        }

        Assert.True(clicked.Count == _drops.Length, "overlapping gold drops cannot be selected individually by label");
        _batch.Begin();
        _ground.DrawLabels(_batch, _font, GraphicsDevice.Viewport, _camera);
        _batch.End();
        for (int y = 0; y < 540; y += 4)
        {
            for (int x = 0; x < 960; x += 4)
            {
                Assert.True(!_ground.Pick(new Point(x, y), away, out _, out _), "hidden label remained clickable after releasing A");
            }
        }

        GraphicsDevice.SetRenderTarget(null);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_output))!);
        using var stream = File.Create(_output);
        target.SaveAsPng(stream, 960, 540);
        Console.WriteLine($"Verified ground elevation, coordinate remapping and {_drops.Length} distinct label click targets: {_output}");
        Exit();
    }

    protected override void UnloadContent()
    {
        _ground.Dispose();
        _batch.Dispose();
        _renderer.Dispose();
        base.UnloadContent();
    }
}
