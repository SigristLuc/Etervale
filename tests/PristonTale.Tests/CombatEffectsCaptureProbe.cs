using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Effects;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Physics;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.Effects;

namespace PristonTale.Tests;

internal sealed class CombatEffectsCaptureProbe : Game
{
    private static readonly float[] MeshTimes = [.15f, .35f, 1f, 6f];
    private static readonly float[] ReturnTimes = [.4f, 1.5f, 2.3f, 3.5f, 4f];
    private static readonly int[] MeshCodes = [0x19F0, 0x1840, 0x1890];
    private readonly string _assets;
    private readonly string _output;
    private readonly float[] _times = [0, .3f, .8f, 1.05f, 1.4f, 2.2f];
    private PristonRenderer _renderer = null!;
    private LegacyEffectLibrary _library = null!;
    private EffectsEngine _effects = null!;
    private LegacyMeteorEffects _meteors = null!;
    private LegacyMonsterMeshEffects _meshes = null!;
    private LegacyReturnEffect _return = null!;
    private PristonCamera _camera = null!;
    private RenderTarget2D _target = null!;
    private int _sample;
    private Color[] _baseline = [];
    private bool _sawIllumination;

    public CombatEffectsCaptureProbe(string assets, string content, string output)
    {
        _assets = assets;
        _output = output;
        Content = new AbsoluteContentManager(Services, content);
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
        _target = new RenderTarget2D(GraphicsDevice, 960, 540, false, SurfaceFormat.Color, DepthFormat.Depth24);
        _library = LegacyEffectLibrary.LoadBootstrap(GraphicsDevice, Content, _assets, ConsoleClientLog.Instance);
        _effects = new EffectsEngine(GraphicsDevice, Content, 812);
        Assert.True(Content.Load<Effect>("Effects/StaticSurface").Parameters["LocalLightCount"] is not null,
            "capture requires the rebuilt production shader");
        _renderer = new PristonRenderer(GraphicsDevice, Content)
        {
            ShadowsEnabled = false,
            DayLengthSeconds = 0
        };
        _renderer.SetTimeOfDay(12);
        var material = new PristonMaterial(true, 1, 0, 1, true, Vector3.One, 0, 1,
            PristonMaterialSurfaceFlags.None, "", "", [], [], 0, 0, 0);
        var stage = new PristonStage("effect-fixture.smd", [material],
            [new(new Vector3(-400, 0, -400), new Color(80, 80, 80)), new(new Vector3(-400, 0, 400), new Color(80, 80, 80)),
             new(new Vector3(400, 0, 400), new Color(80, 80, 80)), new(new Vector3(400, 0, -400), new Color(80, 80, 80))],
            [new(0, 1, 2, 0, -1), new(0, 2, 3, 0, -1)], [], [], new BoundingBox(new Vector3(-400, 0, -400), new Vector3(400, 0, 400)));
        _renderer.AddStage(stage, Array.Empty<PristonModel>());
        _meteors = new LegacyMeteorEffects(_library, _effects, _renderer.TransientLights, new PristonCollisionWorld(stage));
        _camera = new PristonCamera(Vector3.Zero, Vector3.Forward);
        _meshes = new LegacyMonsterMeshEffects(_library, _effects, _renderer.TransientLights);
        _return = new LegacyReturnEffect(GraphicsDevice, _library, _renderer.TransientLights);
        for (int index = 0; index < LegacyMonsterMeshEffects.Capacity; index++)
        {
            Assert.True(_meshes.Spawn(0x19F0, Vector3.Zero, 0, Vector3.Zero), "mesh pool rejected a free slot");
        }

        Assert.True(!_meshes.Spawn(0x19F0, Vector3.Zero, 0, Vector3.Zero), "mesh pool exceeded its budget");
        _meshes.Update(1);
        Assert.True(_meshes.Count == 0, "expired meshes occupied pool slots");
        Assert.True(_meshes.Spawn(0x1890, Vector3.Zero, 0, Vector3.Zero), "mesh pool did not recover after expiration");
        _meshes.Update(.01f);
        Assert.True(_meshes.Count == 0, "zero-distance projectile never finished");
        _meshes.Clear();
        _effects.Clear();
    }

    protected override void Draw(GameTime gameTime)
    {
        _meteors.Clear();
        _meshes.Clear();
        _effects.Clear();
        _return.Clear();
        _renderer.TransientLights.Clear();
        bool returning = _sample >= 21;
        bool mesh = _sample >= 9 && !returning;
        bool meteor = _sample >= 3 && !mesh && !returning;
        float time = 0f;
        if (returning)
        {
            time = ReturnTimes[_sample - 21];
        }
        else if (meteor)
        {
            time = _times[_sample - 3];
        }
        else if (mesh)
        {
            time = MeshTimes[(_sample - 9) % MeshTimes.Length];
        }

        int code = 0;
        if (mesh)
        {
            code = MeshCodes[(_sample - 9) / MeshTimes.Length];
        }
        _camera.SetScriptedView(GraphicsDevice, new Vector3(0, 340, 700), new Vector3(0, 180, 0));
        if (returning)
        {
            _camera.SetScriptedView(GraphicsDevice, new Vector3(0, 110, 240), new Vector3(0, 45, 0));
            _return.Start(Vector3.Zero);
            for (float elapsed = 0f; elapsed < time;)
            {
                var step = MathF.Min(1f / 140f, time - elapsed);
                _return.Update(step);
                _renderer.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(step)));
                elapsed += step;
            }

            Assert.True(_return.Active == (time < 4f), "return effect lifetime changed");
        }
        else if (mesh)
        {
            _camera.SetScriptedView(GraphicsDevice, new Vector3(0, 170, 400), new Vector3(0, 45, 0));
            Assert.True(_meshes.Spawn(code, new Vector3(-70, 0, 0), MathHelper.PiOver2, new Vector3(160, 0, 0)), "mesh effect did not spawn");
            for (float elapsed = 0; elapsed < time;)
            {
                float step = MathF.Min(1f / 140, time - elapsed);
                _meshes.Update(step);
                _effects.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(step)));
                elapsed += step;
            }

            if (time >= 6)
            {
                Assert.True(_meshes.Count == 0 && _effects.Instances.Count == 0, "mesh/trail did not terminate");
            }
        }
        else if (meteor)
        {
            Assert.True(_meteors.Spawn(Vector3.Zero), "meteor could not spawn");
            for (float elapsed = 0; elapsed < time;)
            {
                float step = MathF.Min(1f / 140, time - elapsed);
                _renderer.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(step)));
                _meteors.Update(step);
                _effects.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(step)));
                elapsed += step;
            }

            if (time > 1)
            {
                Assert.True(_meteors.Count == 0, "meteor tunneled through the floor");
                Assert.True(_effects.Instances.Any(instance => instance.Definition.Name == "FireBigHit1"), "meteor did not emit its original impact");
            }
        }
        else if (_sample == 1)
        {
            _renderer.TransientLights.Add(new Vector3(0, 22, 0), new Color(100, 255, 100), 100, 1);
        }
        else if (_sample == 2)
        {
            _renderer.TransientLights.Add(new Vector3(0, 22, 0), new Color(100, 255, 100), 100, 1);
            _renderer.TransientLights.Update(2);
        }

        GraphicsDevice.SetRenderTarget(_target);
        GraphicsDevice.Clear(new Color(18, 22, 25));
        _renderer.Draw(_camera);
        _meshes.Draw(_camera.View, _camera.Projection);
        _return.Draw(_camera.View, _camera.Projection, _camera.Position);
        _effects.Draw(_camera.View, _camera.Projection, _camera.Position);
        _renderer.ComposeScene();
        GraphicsDevice.SetRenderTarget(null);
        var pixels = new Color[960 * 540];
        _target.GetData(pixels);
        if (_sample == 0)
        {
            _baseline = pixels;
        }
        else if (_sample == 1)
        {
            _sawIllumination = pixels.Where((pixel, index) => pixel.G > _baseline[index].G + 3).Any();
            Assert.True(_sawIllumination, "daylight static stage ignored the temporary light");
        }
        else if (_sample == 2)
        {
            Assert.True(pixels.SequenceEqual(_baseline), "expired light left stale shader illumination");
        }

        var name = $"light-{_sample}";
        if (returning)
        {
            name = $"return-{time:0.00}";
        }
        else if (mesh)
        {
            name = $"mesh-{code:X}-{time:0.00}";
        }
        else if (meteor)
        {
            name = $"meteor-{time:0.00}";
        }
        string path = Path.Combine(_output, name + ".png");
        using var stream = File.Create(path);
        _target.SaveAsPng(stream, 960, 540);
        Console.WriteLine(path);
        _sample++;
        if (_sample == 26)
        {
            Exit();
        }
    }

    protected override void UnloadContent()
    {
        _meteors.Clear();
        _meshes.Clear();
        _return.Dispose();
        _effects.Dispose();
        _renderer.Dispose();
        _library.Dispose();
        _target.Dispose();
    }
}
