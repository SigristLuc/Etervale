using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Client.Net.Presence;
using PristonTale.Client.Net.WorldCatalog;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.IO;
using PristonTale.Engine3D.Physics;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.World.Remote;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.World;

namespace PristonTale.Tests;

// Characters in Navisko by day and by night, close to the camera: their shadows on the ground and on their own bodies over
// consecutive frames, to see shadow edges, self-shadowed facets and flicker.
internal sealed class ShadowCaptureProbe : Game
{
    private sealed record Shot(string Name, float Hour, int Frames);

    private static readonly Shot[] Shots = [new("day", 14f, 4), new("night", 21.5f, 4)];

    private readonly string _assetRoot;
    private readonly string _output;
    private readonly PresentationSettings _presentation = new();
    private readonly System.Diagnostics.Stopwatch _timeout = System.Diagnostics.Stopwatch.StartNew();
    private PristonRenderer _renderer = null!;
    private PristonSkyLibrary _sky = null!;
    private PristonCamera _camera = null!;
    private RemoteEntityManager _remote = null!;
    private Vector3 _focus;
    private Vector3 _eye;
    private int _shot;
    private int _frame;
    private double _seconds;

    public ShadowCaptureProbe(string assetRoot, string contentRoot, string output)
    {
        _assetRoot = assetRoot;
        _output = output;
        _ = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 960,
            PreferredBackBufferHeight = 540,
            GraphicsProfile = GraphicsProfile.HiDef,
            SynchronizeWithVerticalRetrace = false
        };
        Content = new AbsoluteContentManager(Services, contentRoot);
        IsFixedTimeStep = false;
    }

    protected override void LoadContent()
    {
        Directory.CreateDirectory(_output);
        var field = new WorldCatalog().AllFields.Single(candidate => candidate.Id == "village-1");
        _sky = PristonSkyLibrary.LoadBootstrap(GraphicsDevice, _assetRoot);
        _renderer = new PristonRenderer(GraphicsDevice, Content, presentation: _presentation) { Sky = _sky, DayLengthSeconds = 0 };
        _renderer.SetTimeOfDay(Shots[0].Hour);
        _renderer.SkyProfiles = new PristonSkyProfiles(PristonSkyProfiles.Resolve(field.Backdrop.Day),
            PristonSkyProfiles.Resolve(field.Backdrop.Glow), PristonSkyProfiles.Resolve(field.Backdrop.Night));
        var reader = new PristonSmdReader();
        var stage = reader.ReadStage(Resolve(field.StageSmd));
        var models = field.StageObjectSmds.Select(path => reader.ReadModel(Resolve(path))).ToArray();
        _renderer.PrepareStageTexturesAsync(stage, models).GetAwaiter().GetResult();
        _renderer.AddStage(stage, models);
        var collision = new PristonCollisionWorld(stage);
        var start = field.StartPoints[0];
        _focus = new Vector3(start.X, 0f, -start.Z);
        _focus.Y = collision.TryGetFloorHeight(_focus with { Y = 5000f }, out var floor) ? floor : 0f;
        // Beside the stage lamp nearest the start, so its shadows show at night.
        var lamp = stage.Lights.Where(static light => light.Range > 50f)
            .MinBy(light => Vector2.DistanceSquared(new Vector2(light.Position.X, light.Position.Z), new Vector2(_focus.X, _focus.Z)));
        if (lamp is not null)
        {
            Console.WriteLine($"Lamp at {lamp.Position} range {lamp.Range}");
            _focus = Floor(collision, new Vector3(lamp.Position.X + 60f, lamp.Position.Y, lamp.Position.Z + 40f));
        }

        _eye = _focus + new Vector3(60f, 55f, 110f);
        _camera = new PristonCamera(_eye, Vector3.Normalize(_focus - _eye));

        var loader = new PristonActorLoader();
        var actorScene = new RendererActorScene(_renderer);
        _remote = new RemoteEntityManager(actorScene, new PlayerActorComposer(actorScene, loader), new ModelActorComposer(actorScene, loader),
            new CharacterClassRegistry(), _assetRoot, new WorldRenderCatalog(new Dictionary<uint, MonsterRenderInfo>()), ConsoleClientLog.Instance);
        _remote.Apply(new PresenceUpdate(1,
        [
            new(1, RemoteEntityKind.Player, false, Numeric(_focus), 0.4f, RemoteMoveState.Idle, 64, 64,
                new PlayerAppearanceInfo("Priestess", (int)PlayerClass.Priestess, 3, 0, 0, "", "", 90), 0),
            new(2, RemoteEntityKind.Player, false, Numeric(Floor(collision, _focus + new Vector3(-50f, 0f, -20f))), 2.2f, RemoteMoveState.Idle, 64, 64,
                new PlayerAppearanceInfo("Atalanta", (int)PlayerClass.Atalanta, 3, 1, 1, "", "", 90), 0)
        ], [], [], []));
    }

    protected override void Update(GameTime gameTime)
    {
        if (_timeout.Elapsed > TimeSpan.FromMinutes(3))
        {
            throw new TimeoutException("Shadow capture did not finish.");
        }

        if (_shot >= Shots.Length)
        {
            Exit();
            return;
        }

        const float step = 1f / 60f;
        _seconds += step;
        _renderer.SetTimeOfDay(Shots[_shot].Hour);
        _remote.Update(step);
        _renderer.Update(new GameTime(TimeSpan.FromSeconds(_seconds), TimeSpan.FromSeconds(step)));
    }

    protected override void Draw(GameTime gameTime)
    {
        if (_shot >= Shots.Length || _remote.Count < 2 || _seconds < 3.0)
        {
            GraphicsDevice.Clear(Color.Black);
            return;
        }

        var shot = Shots[_shot];
        using var target = new RenderTarget2D(GraphicsDevice, 960, 540, false, SurfaceFormat.Color, DepthFormat.Depth24Stencil8);
        GraphicsDevice.SetRenderTarget(target);
        _camera.SetScriptedView(GraphicsDevice, _eye, _focus + Vector3.Up * 25f);
        _renderer.Draw(_camera);
        _renderer.ComposeScene();
        GraphicsDevice.SetRenderTarget(null);
        var pixels = new Color[960 * 540];
        target.GetData(pixels);
        for (var index = 0; index < pixels.Length; index++)
        {
            pixels[index].A = 255;
        }

        using var opaque = new Texture2D(GraphicsDevice, 960, 540);
        opaque.SetData(pixels);
        using (var stream = File.Create(Path.Combine(_output, $"{shot.Name}-{_frame}.png")))
        {
            opaque.SaveAsPng(stream, 960, 540);
        }

        Console.WriteLine($"Shadows {shot.Name}-{_frame}: captured");
        if (++_frame >= shot.Frames)
        {
            _frame = 0;
            _shot++;
            _seconds = 0;
        }
    }

    protected override void UnloadContent() => _remote.Dispose();

    private static Vector3 Floor(PristonCollisionWorld collision, Vector3 point) =>
        collision.TryGetFloorHeight(point with { Y = point.Y + 500f }, out var height) ? point with { Y = height } : point;

    private static System.Numerics.Vector3 Numeric(Vector3 value) => new(value.X, value.Y, value.Z);

    private string Resolve(string relative) => PristonFileResolver.Resolve(Path.Combine(_assetRoot, relative.Replace('\\', '/')));
}
