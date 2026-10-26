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

// Characters and a large monster in Navisko in the sun and beside a lamp at night, close to the camera, with the screen
// space shadowing on and off and its occlusion view: shadows on the ground and on the bodies over consecutive frames.
internal sealed class ShadowCaptureProbe : Game
{
    private sealed record Shot(string Name, float Hour, int Frames, bool AtLamp, bool Close, Func<ClientSettings, ClientSettings> Settings,
        bool SunMoves = false);

    private static readonly Shot[] Shots =
    [
        new("day", 14f, 3, false, false, static s => s),
        new("day-close", 14f, 3, false, true, static s => s),
        new("day-close-noao", 14f, 1, false, true, static s => s with { AmbientOcclusion = false }),
        new("day-close-nocontact", 14f, 1, false, true, static s => s with { ContactShadows = false }),
        new("day-close-none", 14f, 1, false, true, static s => s with { AmbientOcclusion = false, ContactShadows = false }),
        new("day-close-occlusion", 14f, 1, false, true, static s => s with { DebugView = PristonDebugView.AmbientOcclusion }),
        new("day-close-noshadow", 14f, 1, false, true, static s => s with { ShadowsEnabled = false }),
        new("night", 21.5f, 3, true, false, static s => s),
        new("morning-moving", 8f, 3, false, false, static s => s, SunMoves: true)
    ];

    // Hours the server's clock advances in one real second.
    private const float HoursPerSecond = 1000f / WorldClock.MinuteMs / 60f;

    private const ulong Priestess = 1;
    private const ulong Atalanta = 2;
    private const ulong Babel = 3;

    private readonly string _assetRoot;
    private readonly string _output;
    private readonly PresentationSettings _presentation = new();
    private readonly System.Diagnostics.Stopwatch _timeout = System.Diagnostics.Stopwatch.StartNew();
    private PristonRenderer _renderer = null!;
    private PristonSkyLibrary _sky = null!;
    private PristonCamera _camera = null!;
    private RemoteEntityManager _remote = null!;
    private PristonCollisionWorld _collision = null!;
    private Vector3 _sunSpot;
    private Vector3 _lampSpot;
    private Vector3 _focus;
    private int _shot = -1;
    private int _frame;
    private double _seconds;
    private double _shotSeconds;
    private Color[]? _previous;

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
        _renderer.SkyProfiles = new PristonSkyProfiles(PristonSkyProfiles.Resolve(field.Backdrop.Day),
            PristonSkyProfiles.Resolve(field.Backdrop.Glow), PristonSkyProfiles.Resolve(field.Backdrop.Night));
        var reader = new PristonSmdReader();
        var stage = reader.ReadStage(Resolve(field.StageSmd));
        var models = field.StageObjectSmds.Select(path => reader.ReadModel(Resolve(path))).ToArray();
        _renderer.PrepareStageTexturesAsync(stage, models).GetAwaiter().GetResult();
        _renderer.AddStage(stage, models);
        _collision = new PristonCollisionWorld(stage);
        var start = field.StartPoints[0];
        _sunSpot = Floor(_collision, new Vector3(start.X, 5000f, -start.Z));
        var lamp = stage.Lights.Where(static light => light.Range > 50f)
            .MinBy(light => Vector2.DistanceSquared(new Vector2(light.Position.X, light.Position.Z), new Vector2(_sunSpot.X, _sunSpot.Z)));
        _lampSpot = lamp is null ? _sunSpot : Floor(_collision, new Vector3(lamp.Position.X + 60f, lamp.Position.Y, lamp.Position.Z + 40f));

        var loader = new PristonActorLoader();
        var actorScene = new RendererActorScene(_renderer);
        var catalog = new WorldRenderCatalog(new Dictionary<uint, MonsterRenderInfo>
        {
            [1] = new(1, "char/monster/Bguardian/Bguardian.inx", 1.2f, "Babel", 104, Simulation.World.Monsters.MonsterBrood.Normal, 8, 25)
        });
        _remote = new RemoteEntityManager(actorScene, new PlayerActorComposer(actorScene, loader), new ModelActorComposer(actorScene, loader),
            new CharacterClassRegistry(), _assetRoot, catalog, ConsoleClientLog.Instance);
        Place(_sunSpot);
    }

    private void Place(Vector3 spot)
    {
        _focus = spot;
        _remote.Apply(new PresenceUpdate(1,
        [
            new(Priestess, RemoteEntityKind.Player, false, Numeric(spot), 0.4f, RemoteMoveState.Idle, 64, 64,
                new PlayerAppearanceInfo("Priestess", (int)PlayerClass.Priestess, 3, 0, 0, "", "", 90), 0),
            new(Atalanta, RemoteEntityKind.Player, false, Numeric(Floor(_collision, spot + new Vector3(-50f, 500f, -20f))), 2.2f, RemoteMoveState.Idle, 64,
                64, new PlayerAppearanceInfo("Atalanta", (int)PlayerClass.Atalanta, 3, 1, 1, "", "", 90), 0),
            new(Babel, RemoteEntityKind.Monster, false, Numeric(Floor(_collision, spot + new Vector3(70f, 500f, -60f))), 3.6f, RemoteMoveState.Idle,
                100, 100, null, 1, Name: "Babel", Level: 104)
        ], [], [], []));
    }

    protected override void Update(GameTime gameTime)
    {
        if (_timeout.Elapsed > TimeSpan.FromMinutes(4))
        {
            throw new TimeoutException("Shadow capture did not finish.");
        }

        if (_shot < 0 || _frame == 0 && _shotSeconds == 0)
        {
            _shot = Math.Max(_shot, 0);
            if (_shot >= Shots.Length)
            {
                Exit();
                return;
            }

            var shot = Shots[_shot];
            _presentation.ApplyRendering(shot.Settings(new ClientSettings()));
            _renderer.SetTimeOfDay(shot.Hour);
            var spot = shot.AtLamp ? _lampSpot : _sunSpot;
            if (spot != _focus)
            {
                Place(spot);
            }
        }

        const float step = 1f / 60f;
        _seconds += step;
        _shotSeconds += step;
        if (_shot < Shots.Length && Shots[_shot].SunMoves)
        {
            _renderer.SetTimeOfDay(Shots[_shot].Hour + (float)_shotSeconds * HoursPerSecond);
        }

        _remote.Update(step);
        _renderer.Update(new GameTime(TimeSpan.FromSeconds(_seconds), TimeSpan.FromSeconds(step)));
    }

    protected override void Draw(GameTime gameTime)
    {
        if (_shot < 0 || _shot >= Shots.Length || _remote.Count < 3 || _shotSeconds < 2.0)
        {
            GraphicsDevice.Clear(Color.Black);
            return;
        }

        var shot = Shots[_shot];
        using var target = new RenderTarget2D(GraphicsDevice, 960, 540, false, SurfaceFormat.Color, DepthFormat.Depth24Stencil8);
        GraphicsDevice.SetRenderTarget(target);
        var eye = shot.Close ? _focus + new Vector3(35f, 45f, 55f) : _focus + new Vector3(80f, 80f, 150f);
        _camera ??= new PristonCamera(eye, Vector3.Forward);
        _camera.SetScriptedView(GraphicsDevice, eye, _focus + Vector3.Up * (shot.Close ? 30f : 25f));
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

        // The change from the previous frame, eight times brighter: what trembles while nothing moves.
        if (_frame > 0 && _previous is not null)
        {
            var changed = 0;
            var difference = new Color[pixels.Length];
            for (var index = 0; index < pixels.Length; index++)
            {
                var delta = Math.Max(Math.Abs(pixels[index].R - _previous[index].R),
                    Math.Max(Math.Abs(pixels[index].G - _previous[index].G), Math.Abs(pixels[index].B - _previous[index].B)));
                changed += delta > 6 ? 1 : 0;
                var shade = (byte)Math.Min(255, delta * 8);
                difference[index] = new Color(shade, shade, shade, (byte)255);
            }

            opaque.SetData(difference);
            using var diffStream = File.Create(Path.Combine(_output, $"{shot.Name}-{_frame}-change.png"));
            opaque.SaveAsPng(diffStream, 960, 540);
            Console.WriteLine($"Shadows {shot.Name}-{_frame}: {changed} pixels changed from the previous frame");
        }

        _previous = pixels;
        Console.WriteLine($"Shadows {shot.Name}-{_frame}: captured");
        if (++_frame >= shot.Frames)
        {
            _previous = null;
            _frame = 0;
            _shotSeconds = 0;
            _shot++;
        }
    }

    protected override void UnloadContent() => _remote.Dispose();

    private static Vector3 Floor(PristonCollisionWorld collision, Vector3 point) =>
        collision.TryGetFloorHeight(point with { Y = point.Y + 500f }, out var height) ? point with { Y = height } : point;

    private static System.Numerics.Vector3 Numeric(Vector3 value) => new(value.X, value.Y, value.Z);

    private string Resolve(string relative) => PristonFileResolver.Resolve(Path.Combine(_assetRoot, relative.Replace('\\', '/')));
}
