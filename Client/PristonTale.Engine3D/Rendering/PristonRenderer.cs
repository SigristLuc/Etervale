using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Rendering.Lighting;
using PristonTale.Engine3D.Rendering.Post;

namespace PristonTale.Engine3D.Rendering;

public sealed class PristonRenderer : IDisposable
{
    private static readonly ConditionalWeakTable<PristonActor, PreparedActorGeometry> PreparedActorGeometries = new();

    // The original game advances stage-object animation by two ticks per millisecond.
    private const float StageObjectFrameRate = 2000f;

    private static readonly bool ShadowDiagnostics =
        Environment.GetEnvironmentVariable("PRISTON_SHADOW_DIAG") == "1";
    // PlaybackRate supplies the rules-layer FrameStep/80 ratio for speed-scaled motions.
    private const float ReferenceTicksPerSecond = 160000f / 33f;
    private const float DamageEatRateFactor = 92f / 80f;
    private const int MinimumShadowMapSize = 1024;
    private const float ShadowTeleportRefreshDistance = 350f;
    private const uint TexFormScroll = 4;
    private const uint TexFormScroll2 = 6;
    private const uint TexFormScroll10 = 14;
    private const uint TexFormScrollSlow1 = 15;
    private const uint TexFormScrollSlow4 = 18;

    private static readonly PristonMaterial FallbackMaterial = new(
        true,
        0,
        0,
        0,
        false,
        Vector3.One,
        0f,
        0,
        PristonMaterialSurfaceFlags.None,
        string.Empty,
        string.Empty,
        [],
        [],
        0,
        0,
        0);

    private readonly GraphicsDevice _graphicsDevice;
    private readonly IClientLog _log;
    private readonly PristonRenderStateCache _stateCache;
    private readonly PristonShaderLibrary _shaders;
    private readonly PristonTextureLoader _textureLoader;
    private readonly PristonMaterialRuntimeFactory _materialFactory;
    private readonly PristonGpuMeshBuilder _meshBuilder;
    private readonly BatchUploader _batchUploader;
    private RenderTarget2D _shadowMap;
    private readonly PresentationSettings _presentation;
    private readonly PristonSceneTargets _sceneTargets;
    private int _multiSampleCount;
    private readonly List<RenderStageHandle> _stages = [];
    // Rebuilt on residency changes so Draw remains allocation-free.
    private readonly List<RenderBatch> _renderBatches = [];
    private readonly List<RenderBatch> _shadowBatches = [];
    private bool _batchListsDirty;
    // Generations prevent recycled actor slots from validating stale handles.
    private readonly List<ActorRuntime> _actors = [];
    private readonly Stack<int> _freeActorSlots = new();
    private readonly Dictionary<PristonActor, ActorGpuResource> _actorGpuResources = new(ReferenceEqualityComparer.Instance);
    private readonly RenderQueueBuilder _renderQueueBuilder = new();
    private readonly RenderQueueBuilder _portraitQueueBuilder = new();
    private readonly List<RenderBatch> _portraitBatches = [];
    private readonly PristonRenderClock _renderClock = new();
    private uint _renderTime => _renderClock.Milliseconds;
    private bool _disposed;
    private readonly PristonDayNightCycle _dayNight = new();
    private const int LightNight = 0x00001;
    private const int LightLens = 0x00002;
    private const int LightDynamic = 0x80000;

    // Stage lamps (Shaders/Lighting/Shading.hlsli): the illuminance at the reference distance (150 units), in units of the
    // authored level over the night reference (PristonPhotometry), falling off with the inverse square; the authored range
    // is where the light is windowed to zero.
    private const float LampLevelScale = 2f;
    private const float LampRangeStretch = 1f;
    // Self-illumination at night: the luminance (cd/m^2) of an emissive albedo of 1; by day it shows at its authored
    // display level instead (EmissionScale is the larger of the two).
    private const float EmissiveLuminance = 40f;
    // Combat and spell lights by day: a modest share of the daylight, several overlap (meteors, fire fields).
    private const float TransientDaylightShare = 0.12f;

    private PristonPointLight[] _overrideLights;
    private float _lampIntensity = 1f;
    private float _lampRangeScale = 1f;
    private float _glowIntensity = 1f;
    private float _glowScale = 1f;
    private readonly List<PristonPointLight> _activeLights = new(MaxActiveLights);
    public Lighting.PristonTransientLights TransientLights { get; } = new();
    private readonly List<PristonPointLight> _tunedCandidates = [];
    private readonly List<PristonPointLight> _frameGlowLights = [];
    private const float EmitterGlowRadius = 175f;
    private readonly FixedRateRefreshScheduler _sunShadowRefresh = new();
    private readonly PristonShadowCascades _cascades = new();
    private ShadowFilterMode _shadowFilter = ShadowFilterMode.Pcss;
    private bool _ambientOcclusion = true;
    private bool _contactShadows = true;
    private bool _fullShadowRefresh = true;
    private int _cascadeMask;
    private Vector3 _shadowLightDirection;
    private PristonScreenOcclusion _screenOcclusion;
    private readonly PristonLampShadows _lampShadows;
    private readonly PristonLightCulling _lightCulling;
    private readonly PristonEnvironmentLighting _environment;
    private readonly PristonScreenReflections _reflections;
    private bool _screenReflections = true;
    private PristonFrameContext _reflectionFrame;
    private PristonCalibrationSpheres _calibration;
    private bool _calibrationSpheres;
    private readonly PristonVolumetricLight _volumetric;
    private bool _volumetricLight = true;
    private bool _imageBasedLighting = true;
    private int _lampShadowRedraw;
    // The lit pass over surfaces the depth pre-pass already wrote: test against their depth, do not write it again.
    private static readonly DepthStencilState PrepassedDepth = new()
    {
        Name = "PristonPrepassedDepth",
        DepthBufferEnable = true,
        DepthBufferWriteEnable = false,
        DepthBufferFunction = CompareFunction.LessEqual
    };
    // Clearing a cascade tile writes far depth whatever the tile held.
    private static readonly DepthStencilState ShadowClearDepth = new()
    {
        Name = "PristonShadowClear",
        DepthBufferEnable = true,
        DepthBufferWriteEnable = true,
        DepthBufferFunction = CompareFunction.Always
    };
    // The depth pre-pass writes depth, linear depth, normals and motion, never the scene colour.
    private static readonly BlendState PrepassBlend = CreatePrepassBlend();
    private Vector3 _sunShadowFocus;
    private bool _sunShadowFocusValid;
    private int _shadowUpdateRate;
    private int _shadowDrawCalls;

    private readonly GlowRenderer _glowRenderer;
    private readonly PristonSkyCapture _skyCapture;
    private Vector3 _towardSun = Vector3.Up;
    private Vector3 _fogSunColour;
    private float _sunElevation;
    private float _lampFactor;
    private PristonFrameExposure _exposure = PristonFrameExposure.Neutral;
    private PristonPostSettings _postSettings = PristonPostSettings.From(new ClientSettings());
    private float _lastDeltaSeconds;
    // Motion vectors: the frame counter the skin palettes are stamped with, and the previous frame's unjittered camera.
    private uint _motionFrame = 1;
    private bool _hasPreviousCamera;
    private Matrix _previousView;
    private Matrix _previousProjection;
    private uint _previousRenderTime;
    private float _previousPreExposure = 1f;
    private PristonTemporalFrame _temporalFrame;
    private PristonExposureInputs _exposureInputs;
    private readonly Dictionary<BlendState, BlendState> _temporalBlendStates = new(ReferenceEqualityComparer.Instance);

    public PristonRenderer(GraphicsDevice graphicsDevice, ContentManager content, IClientLog log = null, PresentationSettings presentation = null)
    {
        _graphicsDevice = graphicsDevice;
        _log = log ?? ConsoleClientLog.Instance;
        _presentation = presentation;
        _stateCache = new PristonRenderStateCache(graphicsDevice);
        _textureLoader = new PristonTextureLoader(graphicsDevice, _log);
        _textureLoader.Initialize();
        _shaders = new PristonShaderLibrary(content, _log, _textureLoader.White);
        _sceneTargets = new PristonSceneTargets(graphicsDevice, content, _log);
        _materialFactory = new PristonMaterialRuntimeFactory(_textureLoader);
        _meshBuilder = new PristonGpuMeshBuilder(graphicsDevice);
        _batchUploader = new BatchUploader(graphicsDevice);
        _glowRenderer = new GlowRenderer(graphicsDevice, content, _log);
        _skyCapture = new PristonSkyCapture(graphicsDevice);
        _lampShadows = new PristonLampShadows(graphicsDevice, _log);
        _environment = new PristonEnvironmentLighting(graphicsDevice, _sceneTargets.Triangle,
            PristonShaderLibrary.LoadRequired(content, PristonEnvironmentLighting.Asset, _log));
        _reflections = new PristonScreenReflections(graphicsDevice, _sceneTargets.Triangle,
            PristonShaderLibrary.LoadRequired(content, PristonScreenReflections.Asset, _log));
        _volumetric = new PristonVolumetricLight(graphicsDevice, _sceneTargets.Triangle,
            PristonShaderLibrary.LoadRequired(content, PristonVolumetricLight.Asset, _log));
        _lightCulling = new PristonLightCulling(graphicsDevice, PristonShaderLibrary.LoadRequired(content, PristonLightCulling.Asset, _log));
        _screenOcclusion = new PristonScreenOcclusion(graphicsDevice, _sceneTargets.Triangle,
            PristonShaderLibrary.LoadRequired(content, PristonScreenOcclusion.Asset, _log));
        ApplyQuality(presentation?.Rendering ?? new ClientSettings());
        if (_presentation is not null)
        {
            _presentation.RenderingChanged += ApplyQuality;
        }
    }

    private void ApplyQuality(ClientSettings settings)
    {
        if (_shadowMap?.Width != settings.ShadowMapSize)
        {
            // Release first so a size change never holds both maps (an 8192² R32F + D24 pair is ~512 MiB).
            _shadowMap?.Dispose();
            _shadowMap = null;
            _shadowMap = CreateShadowTarget(settings.ShadowMapSize);
        }

        _shadowUpdateRate = settings.ShadowUpdateRate;
        _sunShadowRefresh.Invalidate();
        _cascades.Configure(settings.ShadowCascades, _shadowMap.Width);
        _lampShadows.Configure(settings.ShadowsEnabled ? settings.LampShadowCount : 0, settings.LampShadowTileSize);
        _shadowFilter = settings.ShadowFilter;
        _ambientOcclusion = settings.AmbientOcclusion;
        _contactShadows = settings.ContactShadows;
        _imageBasedLighting = settings.ImageBasedLighting;
        _screenReflections = settings.ScreenSpaceReflections;
        _calibrationSpheres = settings.CalibrationSpheres;
        _volumetricLight = settings.VolumetricLight;
        _fullShadowRefresh = true;
        _multiSampleCount = settings.AntiAliasing == AntiAliasingMode.Msaa ? Math.Clamp(settings.MultiSampleCount, 2, 4) : 0;
        ShadowsEnabled = settings.ShadowsEnabled;
        DistanceFog = PristonDistanceFog.For(settings.DistanceFog);
        DebugView = settings.DebugView;
        ExposureCompensation = settings.ExposureCompensation;
        _maxLocalLights = Math.Clamp(settings.LocalLightLimit, 1, MaxActiveLights);
        _postSettings = PristonPostSettings.From(settings);
        _log.Info(
            $"[Rendering] quality={settings.Quality}; sun shadow={_shadowMap.Width} in {_cascades.Count} cascade(s) of {_cascades.TileSize} " +
            $"to {PristonShadowCascades.ShadowDistance:0} units, {settings.ShadowFilter}; ambient occlusion={settings.AmbientOcclusion}; " +
            $"contact shadows={settings.ContactShadows}; lamp shadows={_lampShadows.Lamps} x {settings.LampShadowTileSize}; " +
            $"anti-aliasing={settings.AntiAliasing}" +
            $"{(_multiSampleCount > 0 ? $" {_multiSampleCount}x" : string.Empty)}; shadow update={FormatUpdateRate(_shadowUpdateRate)}; " +
            $"shadows={ShadowsEnabled}; local lights={_maxLocalLights}; distance fog={settings.DistanceFog}; auto exposure={settings.AutoExposure}; " +
            $"compensation={settings.ExposureCompensation:+0.0;-0.0;0} EV; tonemapper={settings.Tonemapper}; bloom={settings.Bloom}; " +
            $"grade={settings.ColorGrading}; sharpening={settings.Sharpening:0.00}; debug view={DebugView}.");
    }

    // A device that cannot allocate the map throws; each failure steps down to the next smaller size, 2048 being the
    // low tier and 1024 the last resort. Shadow depth is stored in R32F rather than a quantized colour channel.
    private RenderTarget2D CreateShadowTarget(int requestedSize)
    {
        for (var size = requestedSize; ; size /= 2)
        {
            try
            {
                // PreserveContents: the cascades refresh at different rates, and binding a DiscardContents target clears it.
                return new RenderTarget2D(_graphicsDevice, size, size, false, SurfaceFormat.Single, DepthFormat.Depth24, 0,
                    RenderTargetUsage.PreserveContents);
            }
            catch (Exception exception) when (size > MinimumShadowMapSize && exception is ArgumentException or InvalidOperationException or OutOfMemoryException)
            {
                _log.Warn($"[Rendering] Sun shadow {size} allocation failed; trying {size / 2}: {exception.Message}");
            }
        }
    }

    public BoundingBox Bounds { get; private set; }
    public BoundingBox StageBounds { get; private set; }
    public PristonRenderStats LastFrameStats { get; private set; }
    public PristonFrameProfiler Profiler { get; } = new();
    public PristonStreamingStats LastStreamingStats { get; private set; } = PristonStreamingStats.Empty;

    public bool ShadowsEnabled { get; set; } = true;

    public PristonDistanceFog DistanceFog { get; set; } = PristonDistanceFog.Atmospheric;

    // Sky captures rendered since the renderer was created; the fog refreshes its sky copy only on sky changes.
    public int SkyCaptureRefreshes => _skyCapture.Refreshes;

    // Development view of the surface shader; Overdraw also switches every scene pass to additive blending.
    public PristonDebugView DebugView { get; set; }

    private int _skyProfile = -1;
    private int _previousSkyProfile = -1;
    private float _skyBlend = 1f;

    public PristonSkyLibrary Sky { get; set; }
    public PristonSkyProfiles SkyProfiles { get; set; } = PristonSkyProfiles.Forest;

    public float TimeOfDay => _dayNight.TimeOfDay;

    public Color SkyColor { get; private set; } = new(0.45f, 0.62f, 0.85f);

    public int StageLightCount => _stages.Sum(static s => s.StageLights.Length);

    public float DayLengthSeconds
    {
        get => _dayNight.DayLengthSeconds;
        set => _dayNight.DayLengthSeconds = value;
    }

    public void SetTimeOfDay(float hours) => _dayNight.SetTime(hours);

    public void AdvanceTimeOfDay(float hours) => _dayNight.Advance(hours);

    public void ConfigureDayNight(DayNightSettings settings) => _dayNight.Configure(settings);

    public void ConfigureFieldLighting(
        float intensity,
        float rangeScale,
        PristonPointLight[] overrideLights,
        float glowIntensity = 1f,
        float glowScale = 1f)
    {
        _lampIntensity = intensity;
        _lampRangeScale = MathF.Max(0.01f, rangeScale);
        _overrideLights = overrideLights is { Length: > 0 } ? overrideLights : null;
        _glowIntensity = MathF.Max(0f, glowIntensity);
        _glowScale = MathF.Max(0f, glowScale);
    }

    public bool Underground { get; set; }
    public Vector3 UndergroundAmbient { get; set; } = new(0.30f, 0.30f, 0.34f);

    public void SetActorMotion(ActorHandle handle, PristonActorMotion motion)
    {
        if (!TryResolveActor(handle, out var runtime))
        {
            return;
        }

        runtime.SetMotion(motion);
        UpdateActor(runtime);
    }

    public void SetActorMotionIndex(ActorHandle handle, int motionIndex)
    {
        if (!TryResolveActor(handle, out var runtime))
        {
            return;
        }

        runtime.ForceMotionIndex(motionIndex);
        UpdateActor(runtime);
    }

    public void RestartActorMotion(ActorHandle handle, PristonActorMotion motion)
    {
        if (!TryResolveActor(handle, out var runtime))
            return;

        runtime.ForceMotion(motion);
        UpdateActor(runtime);
    }

    public void SetActorPlaybackRate(ActorHandle handle, float rateFactor)
    {
        if (!TryResolveActor(handle, out var runtime))
            return;
        runtime.PlaybackRate = MathF.Max(rateFactor, 1e-3f);
    }

    public void SetActorWorld(ActorHandle handle, Matrix world)
    {
        if (!TryResolveActor(handle, out var runtime) || runtime.World == world)
        {
            return;
        }

        runtime.World = world;
        UpdateActor(runtime);
    }

    public void SetActorColorAdd(ActorHandle handle, Vector3 colorAdd)
    {
        if (!TryResolveActor(handle, out var runtime))
        {
            return;
        }

        var clamped = Vector3.Clamp(colorAdd, Vector3.Zero, Vector3.One);
        foreach (var batch in runtime.Batches)
        {
            batch.ActorColorAdd = clamped;
        }
    }

    public bool TryGetActorBounds(ReadOnlySpan<ActorHandle> handles, out BoundingBox bounds)
    {
        var hasBounds = false;
        bounds = default;
        for (var handleIndex = 0; handleIndex < handles.Length; handleIndex++)
        {
            if (!TryResolveActor(handles[handleIndex], out var runtime))
            {
                continue;
            }

            for (var batchIndex = 0; batchIndex < runtime.Batches.Count; batchIndex++)
            {
                var batchBounds = runtime.Batches[batchIndex].Bounds;
                bounds = hasBounds ? BoundingBox.CreateMerged(bounds, batchBounds) : batchBounds;
                hasBounds = true;
            }
        }

        return hasBounds;
    }

    public void DrawActorPortrait(
        ReadOnlySpan<ActorHandle> handles,
        Matrix view,
        Matrix projection,
        Vector3 cameraPosition,
        Vector3 focusPoint)
    {
        _portraitBatches.Clear();
        for (var handleIndex = 0; handleIndex < handles.Length; handleIndex++)
        {
            if (!TryResolveActor(handles[handleIndex], out var runtime))
            {
                continue;
            }

            _portraitBatches.AddRange(runtime.Batches);
        }

        if (_portraitBatches.Count == 0)
        {
            return;
        }

        var lighting = BuildFrameLighting(focusPoint);
        var frame = new PristonFrameContext(
            view,
            projection,
            cameraPosition,
            _renderTime,
            lighting,
            Matrix.Identity,
            _textureLoader.White,
            false,
            _lampFactor,
            Exposure: _exposure.DisplayScale,
            DisplayOutput: true,
            EmissionScale: _exposure.EmissionScale);
        _portraitQueueBuilder.Build(_portraitBatches, [], frame, includeShadowCasters: false);

        _stateCache.BeginFrame();
        _stateCache.InvalidateDeviceState();
        _stateCache.SetDepthStencilState(DepthStencilState.Default);
        _stateCache.SetSamplerState0(PristonSamplerStates.AnisotropicWrap);
        _shaders.ApplyFrame(frame);

        var staticBatches = 0;
        var dynamicBatches = 0;
        var drawCalls = 0;
        var triangles = 0;
        var staticVertices = 0;
        var staticIndices = 0;
        var normalBatches = 0;
        var twoSidedBatches = 0;
        var diagnostics = new PristonMeshDiagnostics();
        DrawQueue(
            _portraitQueueBuilder.Queue,
            ref staticBatches,
            ref dynamicBatches,
            ref drawCalls,
            ref triangles,
            ref staticVertices,
            ref staticIndices,
            ref normalBatches,
            ref twoSidedBatches,
            ref diagnostics,
            intoScene: false);
    }

    public void CopyActorPlayback(ActorHandle source, ActorHandle destination)
    {
        if (!TryResolveActor(source, out var previous) || !TryResolveActor(destination, out var next))
        {
            return;
        }

        next.PlaybackRate = previous.PlaybackRate;
        next.ForceMotion((PristonActorMotion)previous.Motion.State);
        float span = (previous.Motion.EndFrame - previous.Motion.StartFrame) * 160f;
        float phase = span > 0 ? (previous.Frame - previous.Motion.StartFrame * 160f) / span : 0;
        next.SetFrame((next.Motion.StartFrame + phase * (next.Motion.EndFrame - next.Motion.StartFrame)) * 160f);
        UpdateActor(next);
    }

    public void AttachActor(ActorHandle child, ActorHandle parent, string boneName)
    {
        if (!TryResolveActor(child, out var item) || !TryResolveActor(parent, out var owner) || child == parent)
        {
            throw new ArgumentException("Invalid equipment attachment handles.");
        }

        int bone = -1;
        for (int index = 0; index < owner.Instance.Actor.Skeleton.Objects.Count; index++)
        {
            if (owner.Instance.Actor.Skeleton.Objects[index].Name.Equals(boneName, StringComparison.OrdinalIgnoreCase))
            {
                bone = index;
                break;
            }
        }

        if (bone < 0)
        {
            throw new InvalidDataException($"Equipment bone '{boneName}' is missing from '{owner.Instance.Actor.Skeleton.Path}'.");
        }

        item.Parent = parent;
        item.ParentBone = bone;
        item.World = owner.SampleSkeleton()[bone] * owner.World;
        UpdateActor(item);
    }

    private bool TryResolveActor(ActorHandle handle, out ActorRuntime runtime)
    {
        if (handle.Slot >= 0 && handle.Slot < _actors.Count)
        {
            var candidate = _actors[handle.Slot];
            if (candidate.Alive && candidate.Generation == handle.Generation)
            {
                runtime = candidate;
                return true;
            }
        }
        runtime = null;
        return false;
    }

    public Task PrepareStageTexturesAsync(PristonStage stage, IReadOnlyList<PristonModel> models)
    {
        var pending = new List<Task>
        {
            _textureLoader.PrepareTexturesAsync(Path.GetDirectoryName(stage.Path) ?? ".", PristonMaterialUsage.ForStage(stage))
        };
        foreach (var model in models)
        {
            pending.Add(_textureLoader.PrepareTexturesAsync(Path.GetDirectoryName(model.Path) ?? ".", PristonMaterialUsage.ForModel(model)));
        }

        return Task.WhenAll(pending);
    }

    public Task PrepareActorResourcesAsync(PristonActor actor)
    {
        var textures = _textureLoader.PrepareTexturesAsync(
            Path.GetDirectoryName(actor.Mesh.Path) ?? ".",
            PristonMaterialUsage.ForActor(actor));
        var geometry = PreparedActorGeometries.TryGetValue(actor, out _)
            ? Task.CompletedTask
            : Task.Run(() =>
            {
                PreparedActorGeometries.GetValue(actor, PrepareActorGeometry);
            });
        return Task.WhenAll(textures, geometry);
    }

    public void RequirePreparedTextures() => _textureLoader.RequirePreparedTextures = true;

    public RenderStageHandle AddStage(PristonStage stage, IEnumerable<PristonModel> models) =>
        AddStage(stage, models.Select(static m => new PristonSceneryInstance(m, Matrix.Identity)));

    public RenderStageHandle AddStage(PristonStage stage, IEnumerable<PristonSceneryInstance> models)
    {
        var stageDir = Path.GetDirectoryName(stage.Path) ?? ".";

        PristonPointLight[] stageLights, glowLights;
        if (_overrideLights != null)
        {
            stageLights = glowLights = _overrideLights;
            _log.Info($"[PristonRenderer] Field override: {stageLights.Length} hand-authored light(s).");
        }
        else
        {
            (stageLights, glowLights) = BuildStageLights(stage, _log);
        }

        // Keep override arrays immutable when applying per-stage glow tuning.
        glowLights = ApplyGlowTuning(glowLights, _glowIntensity, _glowScale);
        var handle = new RenderStageHandle(
            stage,
            _lampIntensity,
            _lampRangeScale,
            stageLights,
            glowLights,
            _glowIntensity,
            _glowScale);

        var shadowCollector = new StaticShadowCollector();
        BuildStageGeometry(stage, stageDir, handle, shadowCollector);
        var bakedLight = PristonStageBakedLight.For(stage);
        handle.LightField = PristonStageLightField.Build(stage);
        var animatedModels = new List<PreparedAnimatedInstance>();
        foreach (var instance in models)
        {
            var model = instance.Model;
            var modelDir = Path.GetDirectoryName(model.Path) ?? ".";
            if (model.IsAnimated)
            {
                animatedModels.Add(new PreparedAnimatedInstance(
                    model,
                    modelDir,
                    instance.World,
                    PrepareAnimatedModel(model, instance.World, bakedLight)));
            }
            else
            {
                AddModel(model, modelDir, handle, instance.World, shadowCollector, bakedLight);
            }
        }

        AddAnimatedModelGroups(animatedModels, handle);

        AddStaticShadowAggregate(handle, shadowCollector);

        LogAnimatedStageSummary(handle);
        AssignEmitterGlowColors(handle, stage);

        handle.Bounds = UnionBatchBounds(handle.Batches);
        _stages.Add(handle);
        RebuildRenderBatches();
        return handle;
    }

    public IEnumerator<RenderStageHandle> AddStageIncrementally(PristonStage stage, IReadOnlyList<PristonModel> models)
    {
        var (stageLights, glowLights) = _overrideLights is null ? BuildStageLights(stage, _log) : (_overrideLights, _overrideLights);
        glowLights = ApplyGlowTuning(glowLights, _glowIntensity, _glowScale);
        var handle = new RenderStageHandle(
            stage,
            _lampIntensity,
            _lampRangeScale,
            stageLights,
            glowLights,
            _glowIntensity,
            _glowScale);
        var published = false;
        var stats = new PristonStreamingStats(Path.GetFileName(stage.Path));
        using var steps = StageUploadSteps(stage, models, handle).GetEnumerator();
        try
        {
            while (true)
            {
                var started = Stopwatch.GetTimestamp();
                if (!steps.MoveNext())
                {
                    break;
                }

                stats.Record(steps.Current.Step, Stopwatch.GetElapsedTime(started).TotalMilliseconds, steps.Current.Bytes);
                yield return null;
            }

            var publishStarted = Stopwatch.GetTimestamp();
            LogAnimatedStageSummary(handle);
            AssignEmitterGlowColors(handle, stage);
            handle.Bounds = UnionBatchBounds(handle.Batches);
            _stages.Add(handle);
            RebuildRenderBatches();
            stats.Record(StreamingStep.Publish, Stopwatch.GetElapsedTime(publishStarted).TotalMilliseconds);
            LastStreamingStats = stats;
            _log.Info($"[Stream] upload steps {stats.Source}: {stats.Summary()}");
            published = true;
            yield return handle;
        }
        finally
        {
            if (!published)
            {
                handle.DisposeGpu();
            }
        }
    }

    // Each yielded item describes the main-thread work done just before it, so the caller can time every step.
    private IEnumerable<StreamingWork> StageUploadSteps(PristonStage stage, IReadOnlyList<PristonModel> models, RenderStageHandle handle)
    {
        var cpuBuilder = new PristonGpuMeshBuilder(null);
        var shadowCollector = new StaticShadowCollector();
        var bakedLight = PristonStageBakedLight.For(stage);
        var lightField = Task.Run(() => PristonStageLightField.Build(stage));
        var animatedPreparation = Task.Run(() =>
        {
            var prepared = new Dictionary<PristonModel, PreparedAnimatedModel>();
            foreach (var model in models)
            {
                if (model.IsAnimated)
                {
                    prepared.Add(model, PrepareAnimatedModel(model, Matrix.Identity, bakedLight));
                }
            }

            return prepared;
        });
        var stageDirectory = Path.GetDirectoryName(stage.Path) ?? ".";
        foreach (var bytes in _textureLoader.UploadPreparedTextures(stageDirectory, PristonMaterialUsage.ForStage(stage)))
        {
            yield return new StreamingWork(StreamingStep.TextureUpload, bytes);
        }

        foreach (var model in models)
        {
            var directory = Path.GetDirectoryName(model.Path) ?? ".";
            foreach (var bytes in _textureLoader.UploadPreparedTextures(directory, PristonMaterialUsage.ForModel(model)))
            {
                yield return new StreamingWork(StreamingStep.TextureUpload, bytes);
            }
        }

        var groupsTask = Task.Run(() => BuildStageGroups(stage));
        while (!groupsTask.IsCompleted)
        {
            yield return StreamingWork.Wait;
        }

        foreach (var step in AddGroupsIncrementally(
            CompletedResult(groupsTask),
            stage.Materials,
            stageDirectory,
            handle,
            cpuBuilder,
            shadowCollector))
        {
            yield return step;
        }

        var animatedModels = new List<PreparedAnimatedInstance>();
        foreach (var model in models)
        {
            var directory = Path.GetDirectoryName(model.Path) ?? ".";
            if (model.IsAnimated)
            {
                while (!animatedPreparation.IsCompleted)
                {
                    yield return StreamingWork.Wait;
                }

                var prepared = CompletedResult(animatedPreparation)[model];
                animatedModels.Add(new PreparedAnimatedInstance(
                    model,
                    directory,
                    Matrix.Identity,
                    prepared));
            }
            else
            {
                var modelTask = Task.Run(() => WithBakedLight(PristonModelGeometry.BuildGroups(model, Matrix.Identity, Color.White), model.Materials, bakedLight));
                while (!modelTask.IsCompleted)
                {
                    yield return StreamingWork.Wait;
                }

                foreach (var step in AddGroupsIncrementally(
                    CompletedResult(modelTask),
                    model.Materials,
                    directory,
                    handle,
                    cpuBuilder,
                    shadowCollector))
                {
                    yield return step;
                }
            }
        }

        foreach (var step in AddAnimatedModelGroupsIncrementally(animatedModels, handle))
        {
            yield return step;
        }

        while (!lightField.IsCompleted)
        {
            yield return StreamingWork.Wait;
        }

        handle.LightField = CompletedResult(lightField);
        foreach (var step in AddStaticShadowAggregateIncrementally(handle, shadowCollector, cpuBuilder))
        {
            yield return step;
        }
    }

    private readonly record struct StreamingWork(StreamingStep Step, long Bytes)
    {
        public static StreamingWork Wait => new(StreamingStep.PreparationWait, 0);
    }

    private static BoundingBox UnionBatchBounds(IReadOnlyList<RenderBatch> batches)
    {
        if (batches.Count == 0)
        {
            return new BoundingBox(Vector3.Zero, Vector3.Zero);
        }

        var bounds = batches[0].Bounds;
        for (var index = 1; index < batches.Count; index++)
        {
            bounds = BoundingBox.CreateMerged(bounds, batches[index].Bounds);
        }

        return bounds;
    }

    private static T CompletedResult<T>(Task<T> task)
    {
        if (!task.IsCompletedSuccessfully)
        {
            throw new InvalidOperationException("Stage preparation failed.", task.Exception);
        }

        return task.Result;
    }

    private void LogAnimatedStageSummary(RenderStageHandle handle)
    {
        var boneCount = 0;
        var batchCount = 0;
        var shadowAggregateCount = 0;
        foreach (var group in handle.AnimatedGroups)
        {
            boneCount += group.Palette.Count;
            batchCount += group.ColorBatchCount;
            shadowAggregateCount += group.ShadowAggregateCount;
        }

        if (handle.AnimatedModels.Count > 0)
        {
            _log.Info(
                $"[Rendering] stage animation: models={handle.AnimatedModels.Count}; " +
                $"bones={boneCount}; batches={batchCount}; shadow aggregates={shadowAggregateCount}.");
        }
    }

    private IEnumerable<StreamingWork> AddGroupsIncrementally(
        Dictionary<int, List<PristonCornerVertex>> groups,
        IReadOnlyList<PristonMaterial> materials,
        string directory,
        RenderStageHandle handle,
        PristonGpuMeshBuilder cpuBuilder,
        StaticShadowCollector shadowCollector)
    {
        var merged = new Dictionary<StaticMergeKey, (PristonMaterialRuntime Material, List<PristonCornerVertex> Vertices)>();
        var pending = new List<(PristonMaterialRuntime Material, List<PristonCornerVertex> Vertices)>();
        foreach (var (index, vertices) in groups)
        {
            var material = index >= 0 && index < materials.Count ? materials[index] : FallbackMaterial;
            if (vertices.Count == 0 || material.IsInvisible || material.HasNoRenderableTexture)
            {
                continue;
            }

            var runtime = _materialFactory.Create(directory, material);
            if (vertices.Count == 0 || runtime.Invisible || material.HasNoRenderableTexture)
            {
                continue;
            }

            var shadowCoveredByAggregate = CanAggregateShadow(runtime);
            if (shadowCoveredByAggregate)
            {
                shadowCollector.Add(runtime, vertices);
            }

            if (CanMerge(runtime))
            {
                var key = new StaticMergeKey(runtime.Textures[0], runtime.AlphaTest, runtime.TwoSided, runtime.SelfIllum, runtime.BlendState);
                if (!merged.TryGetValue(key, out var entry))
                {
                    entry = (runtime, []);
                    merged.Add(key, entry);
                }

                entry.Vertices.AddRange(vertices);
            }
            else
            {
                pending.Add((runtime, vertices));
            }

            yield return new StreamingWork(StreamingStep.MaterialSetup, 0);
        }

        pending.AddRange(merged.Values);
        var preparation = Task.Run(() =>
        {
            var batches = new List<PreparedStaticBatch>(pending.Count);
            foreach (var (material, vertices) in pending)
            {
                var array = vertices.ToArray();
                var mesh = material.TextureFormState == 0 ? cpuBuilder.PrepareStaticMesh(array) : null;
                var dynamicVertices = mesh is null ? ToDynamicVertices(array) : [];
                batches.Add(new PreparedStaticBatch(material, mesh, dynamicVertices, CanAggregateShadow(material)));
            }

            return batches;
        });
        while (!preparation.IsCompleted)
        {
            yield return StreamingWork.Wait;
        }

        foreach (var prepared in CompletedResult(preparation))
        {
            var uploaded = prepared.Mesh is null ? null : _meshBuilder.Upload(prepared.Mesh);
            handle.Batches.Add(new RenderBatch(
                prepared.Vertices,
                [],
                uploaded,
                prepared.Material,
                ShadowCoveredByAggregate: prepared.ShadowCoveredByAggregate));
            yield return new StreamingWork(StreamingStep.StaticMeshUpload, PristonGpuMeshBuilder.EstimateBytes(prepared.Mesh));
        }
    }

    private sealed record PreparedStaticBatch(
        PristonMaterialRuntime Material,
        PristonGpuMeshBuilder.PreparedMesh Mesh,
        PristonStaticVertex[] Vertices,
        bool ShadowCoveredByAggregate);

    private static bool CanMerge(PristonMaterialRuntime material)
    {
        return material.TextureFormState == 0
            && !material.Transparent
            && !material.Water
            && !material.Blinking
            && !material.Wind
            && !material.HasLayers
            && material.Textures.Length == 1;
    }

    private static bool CanAggregateShadow(PristonMaterialRuntime material)
    {
        return material.CastsShadow
            && !material.AlphaTest
            && !material.Water
            && !material.Wind;
    }

    private void AddStaticShadowAggregate(RenderStageHandle handle, StaticShadowCollector collector)
    {
        if (collector.Count == 0)
        {
            return;
        }

        foreach (var cell in collector.OrderedCells())
        {
            var mesh = _meshBuilder.CreateStaticMesh(cell.ToArray());
            handle.ShadowBatches.Add(new RenderBatch([], [], mesh, collector.Material, ForceCastsShadow: true));
        }
    }

    private IEnumerable<StreamingWork> AddStaticShadowAggregateIncrementally(
        RenderStageHandle handle,
        StaticShadowCollector collector,
        PristonGpuMeshBuilder cpuBuilder)
    {
        if (collector.Count == 0)
        {
            yield break;
        }

        var cells = collector.OrderedCells().Select(static cell => cell.ToArray()).ToArray();
        var preparation = Task.Run(() =>
        {
            var prepared = new PristonGpuMeshBuilder.PreparedMesh[cells.Length];
            for (var index = 0; index < cells.Length; index++)
            {
                prepared[index] = cpuBuilder.PrepareStaticMesh(cells[index]);
            }

            return prepared;
        });
        while (!preparation.IsCompleted)
        {
            yield return StreamingWork.Wait;
        }

        foreach (var prepared in CompletedResult(preparation))
        {
            var mesh = _meshBuilder.Upload(prepared);
            handle.ShadowBatches.Add(new RenderBatch([], [], mesh, collector.Material, ForceCastsShadow: true));
            yield return new StreamingWork(StreamingStep.ShadowAggregateUpload, PristonGpuMeshBuilder.EstimateBytes(prepared));
        }
    }

    private sealed class StaticShadowCollector
    {
        // Spatial aggregation keeps depth submissions low without turning the whole field into one
        // giant caster that defeats the light-frustum cull. A 1024-unit cell gives the 2800-unit sun
        // footprint useful granularity.
        private const float CellSize = 1024f;
        private readonly Dictionary<(int X, int Z), List<PristonCornerVertex>> _cells = [];

        public int Count => _cells.Count;
        public PristonMaterialRuntime Material { get; private set; }

        public void Add(PristonMaterialRuntime material, List<PristonCornerVertex> vertices)
        {
            Material ??= material;
            var source = CollectionsMarshal.AsSpan(vertices);
            for (var index = 0; index + 2 < source.Length; index += 3)
            {
                var center = (source[index].Position + source[index + 1].Position + source[index + 2].Position) / 3f;
                var key = ((int)MathF.Floor(center.X / CellSize), (int)MathF.Floor(center.Z / CellSize));
                ref var cell = ref CollectionsMarshal.GetValueRefOrAddDefault(_cells, key, out var exists);
                if (!exists)
                {
                    // Eight triangles cover the common small-cell case while avoiding a large eager
                    // allocation for sparse edge cells. List<T> grows geometrically if the cell is dense.
                    cell = new List<PristonCornerVertex>(24);
                }

                var destinationIndex = cell.Count;
                cell.EnsureCapacity(destinationIndex + 3);
                CollectionsMarshal.SetCount(cell, destinationIndex + 3);
                source.Slice(index, 3).CopyTo(CollectionsMarshal.AsSpan(cell).Slice(destinationIndex, 3));
            }
        }

        public IEnumerable<List<PristonCornerVertex>> OrderedCells()
        {
            foreach (var cell in _cells.OrderBy(static pair => pair.Key.X).ThenBy(static pair => pair.Key.Z))
            {
                yield return cell.Value;
            }
        }
    }

    public void RemoveStage(RenderStageHandle handle)
    {
        if (handle is null || !_stages.Remove(handle))
            return;
        handle.DisposeGpu();
        RebuildRenderBatches();
    }

    public ActorHandle AddActor(PristonActorInstance instance)
    {
        var runtime = BuildActor(instance);
        int slot, generation;
        if (_freeActorSlots.Count > 0)
        {
            slot = _freeActorSlots.Pop();
            generation = _actors[slot].Generation + 1;
            runtime.Generation = generation;
            _actors[slot] = runtime;
        }
        else
        {
            slot = _actors.Count;
            generation = 0;
            _actors.Add(runtime);
        }

        _batchListsDirty = true;
        return new ActorHandle(slot, generation);
    }

    public void RemoveActor(ActorHandle handle)
    {
        if (!TryResolveActor(handle, out var runtime))
            return;
        ReleaseActorGpuResource(runtime);
        runtime.Alive = false;
        _freeActorSlots.Push(handle.Slot);
        _batchListsDirty = true;
    }

    public void ClearActors()
    {
        foreach (var actor in _actors)
        {
            if (actor.Alive)
            {
                ReleaseActorGpuResource(actor);
            }
        }

        _actors.Clear();
        _freeActorSlots.Clear();
        _batchListsDirty = true;
    }

    private void RemoveAllStages()
    {
        foreach (var stage in _stages)
            stage.DisposeGpu();
        _stages.Clear();
    }

    // Stage residency changes the static casters, so the sun shadow map must be redrawn.
    private void RebuildRenderBatches()
    {
        RebuildBatchLists();
        StageBounds = UnionStageBounds();
        _sunShadowRefresh.Invalidate();
    }

    // Actor spawns and despawns only mark the lists dirty: bursts coalesce into one rebuild before the next draw,
    // and actors reach the shadow maps on their next scheduled refresh instead of forcing one per event.
    private void EnsureBatchLists()
    {
        if (_batchListsDirty)
        {
            RebuildBatchLists();
        }
    }

    private void RebuildBatchLists()
    {
        _renderBatches.Clear();
        _shadowBatches.Clear();
        foreach (var stage in _stages)
        {
            _renderBatches.AddRange(stage.Batches);
            _shadowBatches.AddRange(stage.ShadowBatches);
        }

        foreach (var actor in _actors)
        {
            if (actor.Alive && actor.Instance.VisibleInMainPass)
            {
                _renderBatches.AddRange(actor.Batches);
                _shadowBatches.AddRange(actor.ShadowBatches);
            }
        }

        Bounds = UnionBatchBounds(_renderBatches);
        _batchListsDirty = false;
        BatchListRebuilds++;
    }

    internal int BatchListRebuilds { get; private set; }

    private BoundingBox UnionStageBounds()
    {
        if (_stages.Count == 0)
            return new BoundingBox(Vector3.Zero, Vector3.Zero);
        var box = _stages[0].Source.Bounds;
        for (var i = 1; i < _stages.Count; i++)
            box = BoundingBox.CreateMerged(box, _stages[i].Source.Bounds);
        return box;
    }

    public void Load(PristonStage stage, IEnumerable<PristonModel> models) => Load(stage, models, []);

    public void Load(PristonStage stage, IEnumerable<PristonModel> models, IEnumerable<PristonActorInstance> actors)
    {
        RemoveAllStages();
        ClearActors();
        AddStage(stage, models);
        foreach (var actor in actors)
            AddActor(actor);
    }

    public void Update(GameTime gameTime)
    {
        var dt = MathF.Max(0f, (float)gameTime.ElapsedGameTime.TotalSeconds);
        _lastDeltaSeconds = dt;
        _dayNight.Update(dt);
        TransientLights.Update(MathF.Max(0, dt));
        var profile = SkyProfiles.Select(TimeOfDay);
        if (_skyProfile != profile)
        {
            _previousSkyProfile = _skyProfile;
            _skyProfile = profile;
            _skyBlend = _previousSkyProfile < 0 ? 1f : 0f;
        }

        _skyBlend = MathF.Min(1f, _skyBlend + MathF.Max(0f, dt) / 2f);
        _renderClock.Advance(gameTime.ElapsedGameTime);
        if (UseExternalAnimationClock)
            return;

        Profiler.Begin(RenderSection.Update);
        AdvanceAnimation(dt);
        Profiler.End(RenderSection.Update);
    }

    private void AdvanceAnimation(float dt)
    {
        foreach (var stage in _stages)
        {
            foreach (var model in stage.AnimatedModels)
            {
                model.Advance(dt * StageObjectFrameRate);
            }

            foreach (var group in stage.AnimatedGroups)
            {
                UpdateAnimatedStageGroup(group);
            }
        }
        foreach (var actor in _actors)
        {
            if (!actor.Alive)
                continue;
            actor.Advance(dt);
            if (!actor.Parent.IsValid)
            {
                UpdateActor(actor);
            }
        }

        foreach (var actor in _actors)
        {
            if (actor.Alive && actor.Parent.IsValid && TryResolveActor(actor.Parent, out var parent))
            {
                actor.World = parent.SampleSkeleton()[actor.ParentBone] * parent.World;
                UpdateActor(actor);
            }
        }
    }

    public bool UseExternalAnimationClock { get; set; }

    public void SetSceneAnimationFrame(float ticks)
    {
        foreach (var stage in _stages)
        {
            foreach (var model in stage.AnimatedModels)
            {
                model.SetFrame(ticks);
            }

            foreach (var group in stage.AnimatedGroups)
            {
                UpdateAnimatedStageGroup(group);
            }
        }
        foreach (var actor in _actors)
        {
            if (!actor.Alive)
                continue;
            actor.SetFrame(ticks);
            UpdateActor(actor);
        }
    }

    // Frame passes, in order (each a profiler section): Lighting (CPU light selection), Queue (culling), SunShadow,
    // SkyCapture (the fog's sky copy, when the sky changed), Sky (the dome, then the pass that fills every scene target),
    // Opaque and Masked (with the motion and depth targets of the temporal anti-aliasing), Transparent, Glow. The HDR
    // scene stays bound after Draw so effects can add to it; ComposeScene then runs the post chain (Temporal, Exposure,
    // Bloom, DisplayTransform, PostAntiAliasing) into the caller's target.
    public void Draw(PristonCamera camera)
    {
        var staticBatches = 0;
        var dynamicBatches = 0;
        var drawCalls = 0;
        var triangles = 0;
        var staticVertices = 0;
        var staticIndices = 0;
        var normalBatches = 0;
        var twoSidedBatches = 0;
        var visibleStaticMeshDiagnostics = new PristonMeshDiagnostics();
        var temporal = _postSettings.AntiAliasing == AntiAliasingMode.Taa && DebugView != PristonDebugView.Overdraw;
        // The depth pre-pass pays for itself: the lit pass shades each pixel once and the light tiles get depth bounds.
        var prepass = _multiSampleCount == 0 && DebugView != PristonDebugView.Overdraw;
        // Binding the scene target ends the caller's render pass, so the profiler can reset its timestamps outside one.
        _sceneTargets.Begin(_multiSampleCount, temporal, prepass);
        Profiler.BeginFrame();
        Profiler.Begin(RenderSection.Frame);
        EnsureBatchLists();
        _batchUploader.BeginFrame();
        _shaders.BeginFrame();
        _shadowDrawCalls = 0;
        var skyBatchCount = 0;
        Profiler.Begin(RenderSection.Lighting);
        var lighting = BuildFrameLighting(camera.Target);
        Profiler.End(RenderSection.Lighting);
        var sunShadowsEnabled = ShadowsEnabled && lighting.Sun.DiffuseColor != Vector3.Zero && !Underground;
        InvalidateShadowAfterTeleport(camera.Target);
        // The moon's shadows move with the actors only: half the refresh rate is enough for its soft, dim light.
        var shadowRate = _sunElevation > -0.05f || _shadowUpdateRate == 0 ? _shadowUpdateRate : Math.Max(_shadowUpdateRate / 2, 30);
        var sunShadowUpdated = _sunShadowRefresh.ShouldRefresh(_renderTime, shadowRate, sunShadowsEnabled);
        if (sunShadowUpdated)
        {
            FitCascades(camera, lighting.Sun.Direction);
            _sunShadowFocus = camera.Target;
            _sunShadowFocusValid = true;
        }

        var lightViewProjection = Matrix.Identity;
        _lampShadowRedraw = ShadowsEnabled && _lampFactor > 0.01f ? _lampShadows.Assign(lighting.PointLights, lighting.TransientLightCount) : 0;
        var lampShadows = _lampShadows.Atlas is not null && ShadowsEnabled && _lampFactor > 0.01f
            ? new PristonLampShadowFrame(_lampShadows.Atlas, _lampShadows.Rows, _lampShadows.FaceSize, _lampShadows.Lamps)
            : default;
        var displayScale = _exposure.DisplayScale;
        var skyRadiance = SrgbToLinear((BackgroundColor ?? SkyColor).ToVector3()) / displayScale;
        var projection = camera.Projection;
        var jitterPixels = Vector2.Zero;
        var jitterClip = Vector2.Zero;
        if (temporal)
        {
            jitterPixels = PristonTemporalAntiAliasing.JitterPixels(_motionFrame);
            jitterClip = PristonTemporalAntiAliasing.PixelsToClip(jitterPixels, _sceneTargets.Width, _sceneTargets.Height);
            projection = PristonTemporalAntiAliasing.Jitter(projection, jitterClip);
        }

        var hasPrevious = temporal && _hasPreviousCamera;
        var motion = new PristonMotionFrame(
            hasPrevious ? _previousView * _previousProjection : camera.View * camera.Projection,
            jitterClip,
            hasPrevious ? _previousRenderTime : _renderTime,
            temporal,
            _motionFrame);
        var frame = new PristonFrameContext(
            camera.View,
            projection,
            camera.Position,
            _renderTime,
            lighting,
            lightViewProjection,
            sunShadowsEnabled ? _shadowMap : _textureLoader.White,
            sunShadowsEnabled,
            _lampFactor,
            DistanceFog with { Color = skyRadiance },
            DebugView,
            Exposure: displayScale,
            EmissionScale: _exposure.EmissionScale,
            Motion: motion,
            Shadows: new PristonShadowFrame(_cascades, ShadowFilterOfHour(), null, Vector2.Zero, temporal ? _motionFrame % 8 : 0f),
            LampShadows: lampShadows);
        PristonUnlitEffect.SceneExposure = displayScale;
        _temporalFrame = new PristonTemporalFrame(
            camera.View,
            camera.Projection,
            hasPrevious ? _previousView : camera.View,
            hasPrevious ? _previousProjection : camera.Projection,
            jitterPixels,
            _previousPreExposure > 0f ? _exposure.PreExposure / _previousPreExposure : 1f);
        Profiler.Begin(RenderSection.Queue);
        var culledBatches = _renderQueueBuilder.Build(
            _renderBatches,
            _shadowBatches,
            frame,
            sunShadowUpdated || _lampShadowRedraw != 0);
        Profiler.End(RenderSection.Queue);
        if (sunShadowUpdated)
        {
            Profiler.Begin(RenderSection.SunShadow);
            RenderShadowMap();
            Profiler.End(RenderSection.SunShadow);
        }

        if (_lampShadowRedraw != 0)
        {
            Profiler.Begin(RenderSection.LampShadows);
            RenderLampShadows();
            Profiler.End(RenderSection.LampShadows);
        }

        _stateCache.BeginFrame();
        _stateCache.InvalidateDeviceState();
        var skyVisible = Sky != null && !Underground && DebugView != PristonDebugView.Overdraw;
        var skyPosition = new Vector3(0f, MathF.Min(camera.Position.Y, 150000f / 256f) / 8f, 0f);
        frame = frame with { FogSky = PrepareFogSky(frame, skyVisible, skyPosition) };
        if (_imageBasedLighting && DebugView != PristonDebugView.Overdraw)
        {
            if (_environment.ShouldRefresh(_renderTime))
            {
                Profiler.Begin(RenderSection.Environment);
                RenderEnvironment(frame, skyVisible, skyPosition);
                Profiler.End(RenderSection.Environment);
            }

            frame = frame with { Environment = new PristonEnvironmentFrame(_environment.Atlas, _environment.Sh, _environment.BrdfLut) };
        }
        Profiler.Begin(RenderSection.Sky);
        if (skyVisible)
        {
            _sceneTargets.BeginSky(SkyColor);
            _stateCache.InvalidateDeviceState();
            var skyView = frame.View;
            skyView.Translation = Vector3.Zero;
            skyView.Translation = Vector3.TransformNormal(-skyPosition, skyView);
            var skyProjection = frame.Projection;
            var nearPlane = skyProjection.M43 / skyProjection.M33;
            skyProjection.M33 = -1f;
            skyProjection.M43 = -nearPlane;
            _shaders.ApplyFrame(CreateSkyFrame(frame, skyView, skyProjection, skyPosition));
            _stateCache.SetDepthStencilState(DepthStencilState.None);
            skyBatchCount = DrawSkyLayers(
                SkyProfiles.Select(TimeOfDay),
                ref staticBatches,
                ref dynamicBatches,
                ref drawCalls,
                ref triangles,
                ref staticVertices,
                ref staticIndices,
                ref normalBatches,
                ref twoSidedBatches,
                ref visibleStaticMeshDiagnostics);
        }

        _sceneTargets.Fill(skyVisible, displayScale, skyRadiance);
        Profiler.End(RenderSection.Sky);

        _stateCache.InvalidateDeviceState();
        _stateCache.SetDepthStencilState(DepthStencilState.Default);
        _shaders.ApplyFrame(frame);
        _prepassDrawn = false;
        if (_sceneTargets.Prepass)
        {
            Profiler.Begin(RenderSection.Prepass);
            DrawPrepass(_renderQueueBuilder.Queue);
            Profiler.End(RenderSection.Prepass);
            // Contact shadows of the sun only, as for the shafts.
            var contact = _contactShadows && sunShadowsEnabled && _sunElevation > -0.05f;
            if (_ambientOcclusion || contact)
            {
                Profiler.Begin(RenderSection.AmbientOcclusion);
                var occlusion = _screenOcclusion.Run(
                    _sceneTargets.LinearDepth,
                    _sceneTargets.Normals,
                    frame.View,
                    frame.Projection,
                    -Vector3.Normalize(lighting.Sun.Direction),
                    _ambientOcclusion,
                    contact,
                    frame.Shadows.NoiseOffset);
                Profiler.End(RenderSection.AmbientOcclusion);
                var strength = new Vector2(_ambientOcclusion ? 1f : 0f, contact ? 1f : 0f);
                frame = frame with { Shadows = frame.Shadows with { Occlusion = occlusion, OcclusionStrength = strength } };
                _shaders.ApplyFrame(frame);
            }

            _sceneTargets.Bind();
            _stateCache.InvalidateDeviceState();
            _prepassDrawn = true;
        }

        if (lighting.PointLights.Count > 0)
        {
            Profiler.Begin(RenderSection.LightCulling);
            var near = frame.Projection.M43 / frame.Projection.M33;
            var far = frame.Projection.M43 / (frame.Projection.M33 + 1f);
            var tiles = _lightCulling.Run(lighting.PointLights, frame.View, frame.Projection, near, far, _sceneTargets.Width,
                _sceneTargets.Height, _prepassDrawn ? _sceneTargets.LinearDepth : null, _textureLoader.White);
            Profiler.End(RenderSection.LightCulling);
            frame = frame with { Tiles = tiles };
            _shaders.ApplyFrame(frame);
            _sceneTargets.Bind();
            _stateCache.InvalidateDeviceState();
        }

        _reflectionFrame = frame;
        DrawQueue(
            _renderQueueBuilder.Queue,
            ref staticBatches,
            ref dynamicBatches,
            ref drawCalls,
            ref triangles,
            ref staticVertices,
            ref staticIndices,
            ref normalBatches,
            ref twoSidedBatches,
            ref visibleStaticMeshDiagnostics);
        if (_calibrationSpheres)
        {
            _calibration ??= new PristonCalibrationSpheres(_textureLoader.White);
            _calibration.Place(camera.Target);
            _stateCache.SetBlendState(BlendState.Opaque);
            _stateCache.SetDepthStencilState(DepthStencilState.Default);
            DrawBatch(_calibration.Grey, ref staticBatches, ref dynamicBatches, ref drawCalls, ref triangles, ref staticVertices,
                ref staticIndices, ref normalBatches, ref twoSidedBatches, ref visibleStaticMeshDiagnostics);
            DrawBatch(_calibration.Chrome, ref staticBatches, ref dynamicBatches, ref drawCalls, ref triangles, ref staticVertices,
                ref staticIndices, ref normalBatches, ref twoSidedBatches, ref visibleStaticMeshDiagnostics);
        }

        _frameGlowLights.Clear();
        foreach (var stage in _stages)
        {
            _frameGlowLights.AddRange(stage.GlowLights);
            foreach (var prop in stage.AnimatedModels)
            {
                if (prop.IsGlowEmitter)
                {
                    _frameGlowLights.Add(new PristonPointLight(
                        prop.WorldCenter(),
                        EmitterGlowRadius * stage.GlowScale,
                        prop.GlowColor,
                        stage.GlowIntensity));
                }
            }
        }

        Profiler.Begin(RenderSection.Glow);
        _glowRenderer.Draw(frame, _frameGlowLights, _lampFactor);
        Profiler.End(RenderSection.Glow);
        // Shafts of the sun only: the moon's are too faint to pay for.
        if (_prepassDrawn && _volumetricLight && sunShadowsEnabled && _sunElevation > -0.05f)
        {
            Profiler.Begin(RenderSection.Volumetric);
            _volumetric.Run(
                _sceneTargets.LinearDepth,
                _shadowMap,
                _cascades,
                frame.View,
                frame.Projection,
                camera.Position,
                camera.Target,
                -Vector3.Normalize(lighting.Sun.Direction),
                lighting.Sun.DiffuseColor,
                frame.Shadows.NoiseOffset,
                _sceneTargets.Bind);
            Profiler.End(RenderSection.Volumetric);
            _stateCache.InvalidateDeviceState();
        }
        LastFrameStats = new PristonRenderStats(
            _renderBatches.Count + skyBatchCount,
            _renderQueueBuilder.Queue.Count + skyBatchCount,
            culledBatches,
            staticBatches,
            dynamicBatches,
            drawCalls,
            triangles,
            staticVertices,
            staticIndices,
            normalBatches,
            twoSidedBatches,
            _stateCache.TotalStateChanges + _shaders.TextureChanges,
            _shaders.TextureChanges,
            _stateCache.VertexBufferChanges,
            _stateCache.IndexBufferChanges,
            _batchUploader.UploadedBytes,
            visibleStaticMeshDiagnostics,
            sunShadowUpdated,
            0,
            _shadowDrawCalls);
        var dayMetering = MathHelper.Clamp((_exposure.IncidentEv100 - 4f) / 4f, 0f, 1f);
        _exposureInputs = new PristonExposureInputs(
            _exposure.PreExposure,
            _exposure.IncidentEv100,
            Underground ? 0f : PristonAutoExposure.DayIncidentWeight * dayMetering,
            ExposureCompensation,
            Underground);
        _previousView = camera.View;
        _previousProjection = camera.Projection;
        _previousRenderTime = _renderTime;
        _previousPreExposure = _exposure.PreExposure;
        _hasPreviousCamera = temporal;
        Profiler.End(RenderSection.Frame);
    }

    // Ends the HDR scene: the temporal resolve, the exposure, bloom and the display transform into the target that was
    // bound when Draw began. Call after the effects that draw into the scene, before the UI.
    public void ComposeScene()
    {
        var postFrame = new PristonPostFrame(
            _postSettings,
            _exposureInputs,
            Vector4.Zero,
            default,
            _lastDeltaSeconds,
            DebugView == PristonDebugView.ExposureFalseColour);
        _sceneTargets.Compose(postFrame, _temporalFrame, Profiler);
        _stateCache.InvalidateDeviceState();
        _motionFrame++;
    }

    // Exposure compensation in stops on top of the automatic (or fixed) exposure (ClientSettings.ExposureCompensation).
    public float ExposureCompensation { get; set; }

    // The nominal display scale of the last frame (PristonFrameContext.Exposure).
    public float DisplayScale => _exposure.DisplayScale;

    // Makes the next frame adopt its metered exposure and drop the temporal history (camera cuts, teleports).
    public void ResetTemporalState()
    {
        _sceneTargets.Post.Exposure.Snap();
        _sceneTargets.InvalidateHistory();
        _hasPreviousCamera = false;
    }

    // Whether actors take the authored light of the floor they stand on (PristonStageLightField). A showcase stage (the
    // character selection) lights its characters in full instead.
    public bool ActorFloorLight { get; set; } = true;

    // Background of a scene without a sky (tools and captures); null clears to the sky colour of the hour.
    public Color? BackgroundColor { get; set; }

    // Samples of the HDR scene target as created (0 without MSAA).
    public int EffectiveMultiSampleCount => _sceneTargets.EffectiveMultiSampleCount;

    private static Vector3 SrgbToLinear(Vector3 colour) => new(SrgbToLinear(colour.X), SrgbToLinear(colour.Y), SrgbToLinear(colour.Z));

    private static float SrgbToLinear(float value) =>
        value <= 0.04045f ? value / 12.92f : MathF.Pow((value + 0.055f) / 1.055f, 2.4f);

    private PristonFrameContext CreateSkyFrame(PristonFrameContext frame, Matrix view, Matrix projection, Vector3 skyPosition)
    {
        return frame with
        {
            View = view,
            Projection = projection,
            CameraPosition = skyPosition,
            ShadowsEnabled = false,
            LampFactor = 0f,
            Fog = PristonDistanceFog.Disabled,
            FogSky = new PristonFogSky(_textureLoader.White, false, Vector3.Up, Vector3.Zero)
        };
    }

    private int DrawSkyLayers(
        int currentProfile,
        ref int staticBatches,
        ref int dynamicBatches,
        ref int drawCalls,
        ref int triangles,
        ref int staticVertices,
        ref int staticIndices,
        ref int normalBatches,
        ref int twoSidedBatches,
        ref PristonMeshDiagnostics visibleStaticMeshDiagnostics)
    {
        var count = 0;
        for (var layer = 0; layer < 2; layer++)
        {
            var profile = layer == 0 ? currentProfile : _previousSkyProfile;
            var opacity = layer == 0 ? 1f : 1f - _skyBlend;
            if (profile < 0 || opacity <= 0f)
            {
                continue;
            }

            foreach (var batch in Sky.GetBatches(profile))
            {
                var blend = batch.Material.BlendType <= 1 ? BlendState.NonPremultiplied : batch.Material.BlendState;
                _stateCache.SetBlendState(blend);
                DrawBatch(batch, ref staticBatches, ref dynamicBatches, ref drawCalls, ref triangles,
                    ref staticVertices, ref staticIndices, ref normalBatches, ref twoSidedBatches,
                    ref visibleStaticMeshDiagnostics, opacity, sky: true);
                count++;
            }
        }

        return count;
    }

    // Atmospheric fog takes its colour from a small HDR copy of the composed sky, refreshed here, before the scene target
    // is cleared, because binding another render target may discard it.
    private PristonFogSky PrepareFogSky(PristonFrameContext frame, bool skyVisible, Vector3 skyPosition)
    {
        if (!frame.Fog.Enabled)
        {
            return new PristonFogSky(_textureLoader.White, false, _towardSun, Vector3.Zero);
        }

        Texture2D capture = null;
        if (skyVisible)
        {
            var currentProfile = SkyProfiles.Select(TimeOfDay);
            var transition = _previousSkyProfile >= 0 && _skyBlend < 1f;
            var animated = Sky.IsAnimated(currentProfile) || (transition && Sky.IsAnimated(_previousSkyProfile));
            if (_skyCapture.ShouldRefresh(currentProfile, _previousSkyProfile, _skyBlend, skyPosition.Y, animated, _renderTime))
            {
                Profiler.Begin(RenderSection.SkyCapture);
                RenderSkyCapture(frame, skyPosition, currentProfile);
                Profiler.End(RenderSection.SkyCapture);
            }

            capture = _skyCapture.Texture;
        }

        // The in-scatter follows the sun down to about 7 degrees below the horizon; it is zero at night and underground.
        var elevation = MathHelper.Clamp((_sunElevation + 0.12f) / 0.17f, 0f, 1f);
        var gate = elevation * elevation * (3f - 2f * elevation);
        var inScatter = _fogSunColour * (frame.Fog.InScatter * gate);
        return new PristonFogSky(capture ?? _textureLoader.White, capture is not null, _towardSun, inScatter);
    }

    private void RenderSkyCapture(PristonFrameContext frame, Vector3 skyPosition, int currentProfile)
    {
        var staticBatches = 0;
        var dynamicBatches = 0;
        var drawCalls = 0;
        var triangles = 0;
        var staticVertices = 0;
        var staticIndices = 0;
        var normalBatches = 0;
        var twoSidedBatches = 0;
        var diagnostics = new PristonMeshDiagnostics();
        var projection = PristonSkyCapture.TileProjection();
        _skyCapture.Begin(SkyColor, _renderTime);
        _stateCache.InvalidateDeviceState();
        for (var tile = 0; tile < PristonSkyCapture.TileCount; tile++)
        {
            _skyCapture.BeginTile(tile);
            _shaders.ApplyFrame(CreateSkyFrame(frame, PristonSkyCapture.TileView(tile, skyPosition), projection, skyPosition));
            _stateCache.SetDepthStencilState(DepthStencilState.None);
            DrawSkyLayers(
                currentProfile,
                ref staticBatches,
                ref dynamicBatches,
                ref drawCalls,
                ref triangles,
                ref staticVertices,
                ref staticIndices,
                ref normalBatches,
                ref twoSidedBatches,
                ref diagnostics);
        }

        _skyCapture.End();
    }

    // The dome's six cube faces, then the environment built from them (Lighting/PristonEnvironmentLighting).
    private void RenderEnvironment(PristonFrameContext frame, bool skyVisible, Vector3 skyPosition)
    {
        _environment.BeginFaces(SkyColor);
        if (skyVisible)
        {
            var staticBatches = 0;
            var dynamicBatches = 0;
            var drawCalls = 0;
            var triangles = 0;
            var staticVertices = 0;
            var staticIndices = 0;
            var normalBatches = 0;
            var twoSidedBatches = 0;
            var diagnostics = new PristonMeshDiagnostics();
            var projection = PristonEnvironmentLighting.FaceProjection();
            _stateCache.InvalidateDeviceState();
            for (var face = 0; face < 6; face++)
            {
                _graphicsDevice.Viewport = PristonEnvironmentLighting.FaceViewport(face);
                _shaders.ApplyFrame(CreateSkyFrame(frame, PristonEnvironmentLighting.FaceView(face, skyPosition), projection, skyPosition));
                _stateCache.SetDepthStencilState(DepthStencilState.None);
                DrawSkyLayers(
                    SkyProfiles.Select(TimeOfDay),
                    ref staticBatches,
                    ref dynamicBatches,
                    ref drawCalls,
                    ref triangles,
                    ref staticVertices,
                    ref staticIndices,
                    ref normalBatches,
                    ref twoSidedBatches,
                    ref diagnostics);
            }
        }

        _environment.Build(_renderTime, frame.Exposure, frame.Lighting.AmbientColor, frame.Lighting.GroundAmbientColor);
        _stateCache.InvalidateDeviceState();
    }

    private void InvalidateShadowAfterTeleport(Vector3 focus)
    {
        var thresholdSquared = ShadowTeleportRefreshDistance * ShadowTeleportRefreshDistance;
        if (_sunShadowFocusValid && Vector3.DistanceSquared(focus, _sunShadowFocus) > thresholdSquared)
        {
            _sunShadowRefresh.Invalidate();
            _fullShadowRefresh = true;
        }
    }

    private static string FormatUpdateRate(int rate)
    {
        return rate == 0 ? "every frame" : $"{rate} Hz";
    }

    // The moonlight is dim and diffuse enough that the plain filter serves it; the soft shadows are the sun's.
    private ShadowFilterMode ShadowFilterOfHour() => _sunElevation > -0.05f ? _shadowFilter : ShadowFilterMode.Pcf;

    // Refits the cascades this refresh updates; a new light direction or a teleport refits them all.
    private void FitCascades(PristonCamera camera, Vector3 lightDirection)
    {
        var direction = Vector3.Normalize(lightDirection);
        var full = _fullShadowRefresh || Vector3.Dot(direction, _shadowLightDirection) < 0.99999f;
        _cascadeMask = _cascades.UpdateMask(full);
        for (var cascade = 0; cascade < _cascades.Count; cascade++)
        {
            if ((_cascadeMask & (1 << cascade)) != 0)
            {
                _cascades.Fit(cascade, camera.View, camera.Projection, direction);
            }
        }

        _shadowLightDirection = direction;
        _fullShadowRefresh = false;
    }

    internal static Matrix StabilizeShadowProjection(Matrix view, Matrix projection, int mapSize) =>
        PristonShadowCascades.StabilizeProjection(view, projection, mapSize);

    private static PristonPointLight[] ApplyGlowTuning(PristonPointLight[] lights, float intensity, float scale)
    {
        var tuned = new PristonPointLight[lights.Length];
        for (var i = 0; i < lights.Length; i++)
            tuned[i] = lights[i] with { Intensity = intensity, Radius = lights[i].Radius * scale };
        return tuned;
    }

    private static void AssignEmitterGlowColors(RenderStageHandle handle, PristonStage stage)
    {
        foreach (var prop in handle.AnimatedModels)
        {
            if (!prop.IsGlowEmitter)
                continue;

            var center = prop.WorldCenter();
            var best = float.MaxValue;
            var color = Vector3.One;
            foreach (var light in stage.Lights)
            {
                var d = Vector3.DistanceSquared(light.Position, center);
                if (d < best) { best = d; color = light.Color; }
            }
            prop.GlowColor = color;
        }
    }

    private static (PristonPointLight[] ground, PristonPointLight[] glow) BuildStageLights(PristonStage stage, IClientLog log)
    {
        var ground = new List<PristonPointLight>();
        var glow = new List<PristonPointLight>();
        int nNight = 0, nLens = 0, nDynamic = 0;

        foreach (var l in stage.Lights)
        {
            var radius = l.Range > 1f ? l.Range : 600f;
            var light = new PristonPointLight(l.Position, radius, l.Color, 1f);
            var isLens = (l.Type & LightLens) != 0;
            if ((l.Type & LightNight) != 0) nNight++;
            if (isLens) nLens++;
            if ((l.Type & LightDynamic) != 0) nDynamic++;

            if (isLens) glow.Add(light);
            if (!isLens || (l.Type & LightDynamic) != 0) ground.Add(light);
        }

        // Only LENS lights receive coronas; dungeon flames use their animated emitters.
        var glowLights = glow.ToArray();
        var groundLights = ground.ToArray();

        log.Info(
            $"[PristonRenderer] {stage.Lights.Count} stage light(s) from {Path.GetFileName(stage.Path)}: " +
            $"{groundLights.Length} ground, {glowLights.Length} glow " +
            $"(night={nNight}, lens={nLens}, dynamic={nDynamic}).");
        return (groundLights, glowLights);
    }

    // The frame's light in photometric units (PristonDayNightCycle, Lighting/PristonPhotometry), pre-exposed by the
    // incident reading: a white surface under the metered light comes out near 1 at every hour.
    private PristonLighting BuildFrameLighting(Vector3 focusPoint)
    {
        var sky = _dayNight.Evaluate();
        var sunIlluminance = sky.SunIlluminance;
        var skyIlluminance = sky.SkyIlluminance;
        var lampFactor = sky.LampFactor;
        var skyColor = sky.SkyColor;
        var sunDirection = sky.SunDirection;
        var horizontal = sky.HorizontalIlluminance;
        if (Underground)
        {
            sunIlluminance = Vector3.Zero;
            skyIlluminance = PristonPhotometry.LegacyLevelToIlluminance(UndergroundAmbient);
            lampFactor = 1f;
            skyColor = new Vector3(0.02f, 0.02f, 0.03f);
            horizontal = PristonPhotometry.Luminance(skyIlluminance);
        }

        SkyColor = new Color(skyColor);
        _lampFactor = lampFactor;
        _towardSun = sky.TowardSun;
        _sunElevation = Underground ? -1f : sky.SunElevation;
        _exposure = PristonFrameExposure.ForIlluminance(horizontal, Underground, ExposureCompensation, EmissiveLuminance);
        var radianceScale = _exposure.PreExposure / MathF.PI;
        var sunColor = sunIlluminance * radianceScale;
        var ambient = skyIlluminance * radianceScale;
        var ground = PristonPhotometry.Hue(Vector3.Lerp(sunIlluminance, skyIlluminance, 0.5f))
            * (horizontal * PristonPhotometry.GroundAlbedo * radianceScale);
        _fogSunColour = sunColor;
        var sun = new PristonDirectionalLight(Vector3.Normalize(sunDirection), sunColor);
        // Transient combat lights come first, then the stage lamps nearest the focus, up to the shader's light count.
        _activeLights.Clear();
        TransientLights.Select(focusPoint, _activeLights);
        var transientCount = _activeLights.Count;
        SelectActiveLights(focusPoint, lampFactor);
        // Combat lights are flashes of feedback: a share of the daylight, so they still read at noon.
        var transientScale = MathF.Max(1f, TransientDaylightShare * horizontal / PristonPhotometry.NightReferenceIlluminance);
        for (var index = 0; index < _activeLights.Count; index++)
        {
            var light = _activeLights[index];
            var scale = index < transientCount ? transientScale : LampLevelScale;
            var illuminance = PristonPhotometry.LegacyLevelToIlluminance(light.Color * light.Intensity) * scale;
            _activeLights[index] = light with { Color = illuminance * radianceScale, Intensity = 1f };
        }

        return new PristonLighting(ambient, sun, _activeLights, transientCount, ground);
    }

    private const int MaxActiveLights = 32;
    private int _maxLocalLights = MaxActiveLights;
    // The gameplay colour add (hover and target highlight) becomes a rim of this strength rather than added light.
    private const float SelectionRimScale = 0.6f;

    private void SelectActiveLights(Vector3 focusPoint, float lampFactor)
    {
        if (lampFactor <= 0.01f)
            return;

        _tunedCandidates.Clear();
        foreach (var stage in _stages)
            foreach (var light in stage.StageLights)
                _tunedCandidates.Add(TuneLight(light, stage, lampFactor));

        if (_tunedCandidates.Count == 0)
            return;

        // Transient lights already hold the first slots; the lamps share the rest of the limit.
        var limit = Math.Max(_maxLocalLights - _activeLights.Count, 0);
        if (limit == 0)
            return;

        if (_tunedCandidates.Count <= limit)
        {
            // Nearest first, as the lamp shadows take the first lamps.
            var first = _activeLights.Count;
            _activeLights.AddRange(_tunedCandidates);
            for (var i = first + 1; i < _activeLights.Count; i++)
            {
                var light = _activeLights[i];
                var distance = Vector3.DistanceSquared(light.Position, focusPoint);
                var j = i - 1;
                while (j >= first && Vector3.DistanceSquared(_activeLights[j].Position, focusPoint) > distance)
                {
                    _activeLights[j + 1] = _activeLights[j];
                    j--;
                }

                _activeLights[j + 1] = light;
            }

            return;
        }

        Span<int> best = stackalloc int[limit];
        Span<float> bestDist = stackalloc float[limit];
        for (var i = 0; i < limit; i++) { best[i] = -1; bestDist[i] = float.MaxValue; }

        for (var i = 0; i < _tunedCandidates.Count; i++)
        {
            var d = Vector3.DistanceSquared(_tunedCandidates[i].Position, focusPoint);
            for (var slot = 0; slot < limit; slot++)
            {
                if (d < bestDist[slot])
                {
                    for (var k = limit - 1; k > slot; k--)
                    {
                        bestDist[k] = bestDist[k - 1];
                        best[k] = best[k - 1];
                    }
                    bestDist[slot] = d;
                    best[slot] = i;
                    break;
                }
            }
        }

        for (var i = 0; i < limit; i++)
            if (best[i] >= 0)
                _activeLights.Add(_tunedCandidates[best[i]]);
    }

    private static PristonPointLight TuneLight(PristonPointLight light, RenderStageHandle stage, float lampFactor) =>
        light with
        {
            Radius = light.Radius * stage.LampRangeScale * LampRangeStretch,
            Intensity = light.Intensity * stage.LampIntensity * lampFactor
        };

    // Renders the cascades of this refresh into their atlas tiles; each tile is cleared by a far-depth triangle over its
    // viewport, so the others keep their contents.
    private void RenderShadowMap()
    {
        _graphicsDevice.SetRenderTarget(_shadowMap);
        _graphicsDevice.BlendState = BlendState.Opaque;
        _graphicsDevice.SamplerStates[0] = SamplerState.LinearWrap;
        for (var cascade = 0; cascade < _cascades.Count; cascade++)
        {
            if ((_cascadeMask & (1 << cascade)) == 0)
            {
                continue;
            }

            var tile = _cascades.TileRectangle(cascade);
            _graphicsDevice.Viewport = new Viewport(tile.X, tile.Y, tile.Width, tile.Height);
            _sceneTargets.Triangle.Draw(_shaders.SelectShadowClear(), ShadowClearDepth);
            _graphicsDevice.DepthStencilState = DepthStencilState.Default;
            var viewProjection = _cascades.ViewProjections[cascade];
            _shaders.ApplyShadowFrame(viewProjection, _renderTime);
            DrawShadowQueueItems(_renderQueueBuilder.Queue.ShadowCasters, viewProjection);
        }

        _graphicsDevice.SetRenderTarget(null);
    }

    // Redraws the cube faces of the lamp rows this frame needs (PristonLampShadows.Assign). Only actors and animated props
    // cast: the stage's own occlusion of its lamps is part of the authored lighting (and a lamp's housing would hide it).
    private void RenderLampShadows()
    {
        _graphicsDevice.SetRenderTarget(_lampShadows.Atlas);
        _graphicsDevice.BlendState = BlendState.Opaque;
        _graphicsDevice.SamplerStates[0] = SamplerState.LinearWrap;
        for (var row = 0; row < _lampShadows.Lamps; row++)
        {
            if ((_lampShadowRedraw & (1 << row)) == 0)
            {
                continue;
            }

            var position = _lampShadows.RowPosition(row);
            var radius = _lampShadows.RowRadius(row);
            var casterLight = new Vector4(position, 1f / MathF.Max(radius, 1f));
            for (var face = 0; face < PristonLampShadows.Faces; face++)
            {
                var tile = _lampShadows.FaceRectangle(row, face);
                _graphicsDevice.Viewport = new Viewport(tile.X, tile.Y, tile.Width, tile.Height);
                _sceneTargets.Triangle.Draw(_shaders.SelectShadowClear(), ShadowClearDepth);
                _graphicsDevice.DepthStencilState = DepthStencilState.Default;
                var viewProjection = PristonLampShadows.FaceViewProjection(position, radius, face);
                _shaders.ApplyShadowFrame(viewProjection, _renderTime, casterLight);
                DrawShadowQueueItems(_renderQueueBuilder.Queue.ShadowCasters, viewProjection, skinnedOnly: true);
            }
        }

        _shaders.ApplyShadowFrame(Matrix.Identity, _renderTime);
        _graphicsDevice.SetRenderTarget(null);
    }

    private readonly BoundingFrustum _shadowCasterFrustum = new(Matrix.Identity);

    // skinnedOnly: actors and animated props only (the lamps' cube faces).
    private void DrawShadowQueueItems(IReadOnlyList<PristonRenderQueueItem<RenderBatch>> items, Matrix lightViewProjection, bool skinnedOnly = false)
    {
        _shadowCasterFrustum.Matrix = lightViewProjection;
        // Thin/single-sided casters need both faces in every shadow draw. This state is invariant for
        // the whole queue and the colour pass invalidates its cache immediately after shadow rendering.
        _graphicsDevice.RasterizerState = PristonRasterizerStates.TwoSided;
        for (var i = 0; i < items.Count; i++)
        {
            var batch = items[i].Batch;
            if ((skinnedOnly && !batch.IsSkinned) || !_shadowCasterFrustum.Intersects(batch.Bounds))
            {
                continue;
            }

            _shadowDrawCalls++;

            var casterTexture = batch.Material.AlphaTest ? batch.Material.SelectTexture(_renderTime) : null;
            var casterUvOffset = batch.Material.AlphaTest ? ComputeUvScrollOffset(batch.Material) : Vector2.Zero;
            if (batch.IsSkinned)
            {
                var effect = _shaders.SelectSkinnedShadow(batch.Material, casterTexture, batch.SkinPalette, casterUvOffset);
                effect.CurrentTechnique.Passes[0].Apply();
                _graphicsDevice.SetVertexBuffer(batch.SkinnedBuffer);
                _graphicsDevice.Indices = batch.SkinnedIndexBuffer;
                _graphicsDevice.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, batch.SkinnedPrimitiveCount);
            }
            else
            {
                var effect = _shaders.SelectStaticShadow(batch.Material, casterTexture, ComputeWindParams(batch), casterUvOffset);

                if (batch.Mesh == null)
                {
                    _batchUploader.Ensure(batch);
                }

                effect.CurrentTechnique.Passes[0].Apply();
                if (batch.Mesh != null)
                {
                    _graphicsDevice.SetVertexBuffer(batch.Mesh.VertexBuffer);
                    _graphicsDevice.Indices = batch.Mesh.IndexBuffer;
                    _graphicsDevice.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, batch.Mesh.PrimitiveCount);
                }
                else
                {
                    _graphicsDevice.SetVertexBuffer(batch.DynamicBuffer);
                    _graphicsDevice.DrawPrimitives(PrimitiveType.TriangleList, 0, batch.Vertices.Length / 3);
                }
            }
        }
    }

    private void DrawQueue(
        PristonRenderQueue<RenderBatch> queue,
        ref int staticBatches,
        ref int dynamicBatches,
        ref int drawCalls,
        ref int triangles,
        ref int staticVertices,
        ref int staticIndices,
        ref int normalBatches,
        ref int twoSidedBatches,
        ref PristonMeshDiagnostics visibleStaticMeshDiagnostics,
        bool intoScene = true)
    {
        // The opaque passes also write the motion and depth of the temporal anti-aliasing; blended passes do not.
        if (intoScene && !_prepassDrawn)
        {
            _sceneTargets.BindTemporal();
            _temporalTargetsBound = _sceneTargets.Temporal;
        }

        Profiler.Begin(RenderSection.Opaque);
        DrawQueueItems(
            queue.Opaque,
            ref staticBatches,
            ref dynamicBatches,
            ref drawCalls,
            ref triangles,
            ref staticVertices,
            ref staticIndices,
            ref normalBatches,
            ref twoSidedBatches,
            ref visibleStaticMeshDiagnostics);
        Profiler.End(RenderSection.Opaque);
        Profiler.Begin(RenderSection.Masked);
        DrawQueueItems(
            queue.AlphaTest,
            ref staticBatches,
            ref dynamicBatches,
            ref drawCalls,
            ref triangles,
            ref staticVertices,
            ref staticIndices,
            ref normalBatches,
            ref twoSidedBatches,
            ref visibleStaticMeshDiagnostics);
        Profiler.End(RenderSection.Masked);
        if (_temporalTargetsBound)
        {
            _sceneTargets.Bind();
            _stateCache.InvalidateDeviceState();
            _temporalTargetsBound = false;
        }

        if (intoScene && _prepassDrawn && _screenReflections && _smoothSurfaceVisible)
        {
            Profiler.Begin(RenderSection.Reflections);
            var frame = _reflectionFrame;
            _reflections.Run(
                _sceneTargets.Color,
                _sceneTargets.LinearDepth,
                _sceneTargets.Normals,
                frame.View,
                frame.Projection,
                frame.Environment,
                _textureLoader.White,
                _environment.Sh,
                frame.Shadows.NoiseOffset,
                _sceneTargets.Bind);
            Profiler.End(RenderSection.Reflections);
            _stateCache.InvalidateDeviceState();
        }

        _drawingTransparent = true;
        Profiler.Begin(RenderSection.Transparent);
        DrawQueueItems(
            queue.Transparent,
            ref staticBatches,
            ref dynamicBatches,
            ref drawCalls,
            ref triangles,
            ref staticVertices,
            ref staticIndices,
            ref normalBatches,
            ref twoSidedBatches,
            ref visibleStaticMeshDiagnostics);
        Profiler.End(RenderSection.Transparent);
        _drawingTransparent = false;
    }

    private void DrawQueueItems(
        IReadOnlyList<PristonRenderQueueItem<RenderBatch>> items,
        ref int staticBatches,
        ref int dynamicBatches,
        ref int drawCalls,
        ref int triangles,
        ref int staticVertices,
        ref int staticIndices,
        ref int normalBatches,
        ref int twoSidedBatches,
        ref PristonMeshDiagnostics visibleStaticMeshDiagnostics)
    {
        for (var i = 0; i < items.Count; i++)
        {
            var batch = items[i].Batch;
            if (DebugView == PristonDebugView.Overdraw)
            {
                _stateCache.SetBlendState(BlendState.Additive);
                _stateCache.SetDepthStencilState(DepthStencilState.None);
            }
            else if (_prepassDrawn && !_drawingTransparent && Prepassed(batch))
            {
                _stateCache.SetBlendState(batch.Material.BlendState);
                _stateCache.SetDepthStencilState(PrepassedDepth);
            }
            else
            {
                var writesDepth = batch.Material.WritesDepth;
                _stateCache.SetBlendState(TemporalBlend(batch.Material.BlendState, writesDepth));
                _stateCache.SetDepthStencilState(writesDepth ? DepthStencilState.Default : DepthStencilState.DepthRead);
            }

            DrawBatch(
                batch,
                ref staticBatches,
                ref dynamicBatches,
                ref drawCalls,
                ref triangles,
                ref staticVertices,
                ref staticIndices,
                ref normalBatches,
                ref twoSidedBatches,
                ref visibleStaticMeshDiagnostics);
        }
    }

    // With the temporal targets bound, a surface that is blended or writes no depth leaves the motion and depth of what
    // is behind it: the same blend on the colour, no writes to the other targets.
    private BlendState TemporalBlend(BlendState blend, bool writesDepth)
    {
        if (!_temporalTargetsBound || (writesDepth && ReferenceEquals(blend, BlendState.Opaque)))
        {
            return blend;
        }

        if (_temporalBlendStates.TryGetValue(blend, out var temporal))
        {
            return temporal;
        }

        temporal = new BlendState
        {
            Name = blend.Name + " (colour only)",
            IndependentBlendEnable = true,
            BlendFactor = blend.BlendFactor
        };
        temporal[0].ColorSourceBlend = blend.ColorSourceBlend;
        temporal[0].ColorDestinationBlend = blend.ColorDestinationBlend;
        temporal[0].ColorBlendFunction = blend.ColorBlendFunction;
        temporal[0].AlphaSourceBlend = blend.AlphaSourceBlend;
        temporal[0].AlphaDestinationBlend = blend.AlphaDestinationBlend;
        temporal[0].AlphaBlendFunction = blend.AlphaBlendFunction;
        temporal[0].ColorWriteChannels = blend.ColorWriteChannels;
        for (var target = 1; target < 4; target++)
        {
            temporal[target].ColorWriteChannels = ColorWriteChannels.None;
        }

        _temporalBlendStates.Add(blend, temporal);
        return temporal;
    }

    private bool _temporalTargetsBound;
    private bool _prepassDrawn;
    private bool _drawingTransparent;
    private bool _prepassMode;
    // Whether the pre-pass drew a surface smooth enough for the screen-space reflections (Passes/Reflections.fx reflects
    // smoothness from 0.55; water and ice are this smooth, the armour shine is not): the pass and its copy are skipped
    // otherwise.
    private bool _smoothSurfaceVisible;
    private const float SmoothRoughness = 0.3f;

    // Surfaces of the opaque and alpha-tested queues that write their depth (also the legacy alpha-blended ground, whose
    // blending keeps its depth write) and every cut-out outside the blended queue (foliage, also the late-flagged
    // leaves: PristonMaterialRuntimeFactory.Classify; their depth, motion and occlusion come from the pre-pass's alpha
    // test, and the lit pass blends only the front-most leaf): the depth pre-pass draws them, and the lit pass shades
    // them at that depth with their own blend.
    private static bool Prepassed(RenderBatch batch) =>
        batch.Material.WritesDepth || (batch.Material.AlphaTest && !batch.Material.Transparent);

    private static BlendState CreatePrepassBlend()
    {
        var blend = new BlendState { Name = "PristonPrepass", IndependentBlendEnable = true };
        blend[0].ColorWriteChannels = ColorWriteChannels.None;
        return blend;
    }

    // The depth pre-pass over the opaque and alpha-tested queues (front to back as sorted).
    private void DrawPrepass(PristonRenderQueue<RenderBatch> queue)
    {
        _sceneTargets.BindPrepass();
        _stateCache.InvalidateDeviceState();
        _stateCache.SetBlendState(PrepassBlend);
        _stateCache.SetDepthStencilState(DepthStencilState.Default);
        _prepassMode = true;
        _smoothSurfaceVisible = false;
        var unused = 0;
        var diagnostics = new PristonMeshDiagnostics();
        foreach (var items in new[] { queue.Opaque, queue.AlphaTest })
        {
            for (var i = 0; i < items.Count; i++)
            {
                var batch = items[i].Batch;
                if (!Prepassed(batch))
                {
                    continue;
                }

                _smoothSurfaceVisible |= batch.Material.Description.Roughness < SmoothRoughness;
                DrawBatch(batch, ref unused, ref unused, ref unused, ref unused, ref unused, ref unused, ref unused, ref unused, ref diagnostics);
            }
        }

        _prepassMode = false;
    }

    private void DrawBatch(
        RenderBatch batch,
        ref int staticBatches,
        ref int dynamicBatches,
        ref int drawCalls,
        ref int triangles,
        ref int staticVertices,
        ref int staticIndices,
        ref int normalBatches,
        ref int twoSidedBatches,
        ref PristonMeshDiagnostics visibleStaticMeshDiagnostics,
        float opacity = 1f,
        bool sky = false)
    {
        if (batch.Material.TwoSided)
        {
            twoSidedBatches++;
        }

        _stateCache.SetRasterizerState(batch.Material.TwoSided ? PristonRasterizerStates.TwoSided : PristonRasterizerStates.SingleSided);

        if (batch.IsSkinned)
        {
            // GPU-skinned colour pass. Skinned/character meshes use the opposite winding
            // to the static stage geometry, so back-face culling would erase them. Render
            // them double-sided (as they were before stage culling was enabled); the cost
            // is negligible for low-poly characters/props.
            _stateCache.SetRasterizerState(PristonRasterizerStates.TwoSided);
            var uvScrollOffset = ComputeUvScrollOffset(batch.Material);
            var texture = batch.Material.SelectTexture(_renderTime);
            var effect = _shaders.SelectSkinnedSurface(
                batch.Material,
                texture,
                uvScrollOffset,
                batch.SkinPalette,
                batch.ActorColorAdd * SelectionRimScale,
                LayerOffset(batch.Material),
                _prepassMode,
                batch.FloorLight);
            effect.CurrentTechnique.Passes[0].Apply();
            _stateCache.SetVertexBuffer(batch.SkinnedBuffer);
            _stateCache.SetIndexBuffer(batch.SkinnedIndexBuffer);
            _graphicsDevice.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, batch.SkinnedPrimitiveCount);
            staticBatches++;
            staticVertices += batch.SkinnedVertexCount;
            staticIndices += batch.SkinnedIndexCount;
            normalBatches++;

            drawCalls++;
            triangles += batch.SkinnedPrimitiveCount;
            return;
        }

        // Static / UV-scroll dynamic path.
        var uvScrollOffsetStatic = ComputeUvScrollOffset(batch.Material);
        if (batch.Mesh == null)
        {
            _batchUploader.Ensure(batch);
        }

        var textureStatic = batch.Material.SelectTexture(_renderTime);
        var effectStatic = sky
            ? _shaders.SelectSky(batch.Material, textureStatic, uvScrollOffsetStatic, opacity)
            : _shaders.SelectStaticSurface(
                batch.Material,
                textureStatic,
                uvScrollOffsetStatic,
                ComputeWindParams(batch),
                LayerOffset(batch.Material),
                opacity,
                _prepassMode);

        effectStatic.CurrentTechnique.Passes[0].Apply();
        if (batch.Mesh != null)
        {
            _stateCache.SetVertexBuffer(batch.Mesh.VertexBuffer);
            _stateCache.SetIndexBuffer(batch.Mesh.IndexBuffer);
            _graphicsDevice.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, batch.Mesh.PrimitiveCount);
            staticBatches++;
            staticVertices += batch.Mesh.VertexCount;
            staticIndices += batch.Mesh.IndexCount;
            visibleStaticMeshDiagnostics += batch.Mesh.Diagnostics;
            if (batch.Mesh.HasNormals)
            {
                normalBatches++;
            }
        }
        else
        {
            _stateCache.SetVertexBuffer(batch.DynamicBuffer);
            _graphicsDevice.DrawPrimitives(PrimitiveType.TriangleList, 0, batch.Vertices.Length / 3);
            dynamicBatches++;
            normalBatches++;
        }

        drawCalls++;
        triangles += batch.TriangleCount;
    }

    // Color and depth passes must share this wind anchor to keep shadows aligned.
    private static Vector4 ComputeWindParams(RenderBatch batch)
    {
        if (!batch.Material.Wind)
            return Vector4.Zero;
        var minY = batch.Bounds.Min.Y;
        var invHeight = 1f / MathF.Max(batch.Bounds.Max.Y - minY, 1f);
        return new Vector4(batch.Material.WindAmplitudeX, batch.Material.WindAmplitudeZ, minY, invHeight);
    }

    private Vector2 ComputeUvScrollOffset(PristonMaterialRuntime material)
    {
        // An authored texture form-state scroll code always wins: the authored water and sea surfaces are
        // animated by it (the "slow scroll" codes are deliberately smooth). Decide by the code's presence, not
        // by the computed offset, so the per-wrap zero-crossing never falls through to the water branch and
        // causes a one-frame jump.
        if (material.TextureFormState != 0)
        {
            var uOffset = TextureUOffset(material.TextureFormState);
            return uOffset == 0f ? Vector2.Zero : new Vector2(uOffset, 0f);
        }

        // Water without a form-state code: a gentle diagonal drift, quantized finely (10 bits) so it reads as
        // smooth rather than stepped. An Etervale choice: the authored water uses the form-state codes.
        if (material.Water)
            return new Vector2(
                ((_renderTime >> 6) & 0x3FF) / 1024f,
                ((_renderTime >> 7) & 0x3FF) / 1024f);

        return Vector2.Zero;
    }

    // Scroll of the material's second texture layer, from its own form state.
    private Vector2 LayerOffset(PristonMaterialRuntime material) =>
        material.Layer is { } layer ? new Vector2(TextureUOffset(layer.FormState), 0f) : Vector2.Zero;

    private float TextureUOffset(uint textureFormState)
    {
        if (textureFormState == TexFormScroll)
            return ((_renderTime >> 6) & 0xFF) / 256f;

        if (textureFormState >= TexFormScroll2 && textureFormState <= TexFormScroll10)
            return (((_renderTime >> 6) & 0xFF) / 256f) * (textureFormState - (TexFormScroll2 - 2));

        if (textureFormState >= TexFormScrollSlow1 && textureFormState <= TexFormScrollSlow4)
        {
            var factor = (int)(TexFormScrollSlow4 - textureFormState + 4);
            var mask = 0xFFFFu >> factor;
            if (mask == 0)
                return 0f;
            return ((_renderTime >> 6) & mask) / (float)mask;
        }

        return 0f;
    }

    private void BuildStageGeometry(
        PristonStage stage,
        string assetDirectory,
        RenderStageHandle handle,
        StaticShadowCollector shadowCollector)
    {
        AddGroups(BuildStageGroups(stage), stage.Materials, assetDirectory, handle.Batches, shadowCollector);
    }

    private static Dictionary<int, List<PristonCornerVertex>> BuildStageGroups(PristonStage stage)
    {
        var groups = new Dictionary<int, List<PristonCornerVertex>>();
        foreach (var face in stage.Faces)
        {
            if (!ValidFace(face.A, face.B, face.C, stage.Vertices.Count))
            {
                continue;
            }

            PristonCornerVertex.AddTriangle(
                GetGroup(groups, face.MaterialIndex),
                stage.Vertices[face.A].Position,
                stage.Vertices[face.B].Position,
                stage.Vertices[face.C].Position,
                stage.Vertices[face.A].Color,
                stage.Vertices[face.B].Color,
                stage.Vertices[face.C].Color,
                stage.TexLinks,
                face.TexLinkIndex);
        }

        return groups;
    }

    private void AddModel(
        PristonModel model,
        string assetDirectory,
        RenderStageHandle handle,
        Matrix world,
        StaticShadowCollector shadowCollector,
        PristonStageBakedLight bakedLight)
    {
        AddGroups(
            WithBakedLight(PristonModelGeometry.BuildGroups(model, world, Color.White), model.Materials, bakedLight),
            model.Materials,
            assetDirectory,
            handle.Batches,
            shadowCollector);
    }

    // A scenery model's surfaces take the stage's authored light for their texture (PristonStageBakedLight).
    private static Dictionary<int, List<PristonCornerVertex>> WithBakedLight(
        Dictionary<int, List<PristonCornerVertex>> groups,
        IReadOnlyList<PristonMaterial> materials,
        PristonStageBakedLight bakedLight)
    {
        foreach (var (materialIndex, vertices) in groups)
        {
            var light = bakedLight.For(materials, materialIndex);
            for (var index = 0; index < vertices.Count; index++)
            {
                vertices[index] = vertices[index] with { Color = light };
            }
        }

        return groups;
    }

    private sealed record PreparedSkinnedMesh(
        PristonSkinnedVertex[] Vertices,
        int[] Indices,
        int TriangleCount,
        BoundingBox Bounds);

    private sealed record PreparedAnimatedBatch(PristonMaterial Material, PreparedSkinnedMesh Mesh);
    private sealed record PreparedAnimatedModel(int BoneCount, PreparedAnimatedBatch[] Batches);
    private sealed record PreparedAnimatedInstance(
        PristonModel Model,
        string Directory,
        Matrix World,
        PreparedAnimatedModel Prepared);
    private sealed record PendingAnimatedBatch(
        PristonMaterialRuntime Material,
        List<PreparedSkinnedMesh> Meshes,
        List<AnimatedModelRuntime> Models);
    private sealed record PreparedActorBatch(PristonMaterial Material, PreparedSkinnedMesh Mesh);
    private sealed record PreparedActorGeometry(int[] BoneMap, PreparedActorBatch[] Batches);
    private sealed record ActorGpuBatch(
        PristonMaterialRuntime Material,
        VertexBuffer VertexBuffer,
        IndexBuffer IndexBuffer,
        int PrimitiveCount,
        int VertexCount,
        int IndexCount,
        BoundingBox Bounds,
        bool ShadowCoveredByAggregate = false);

    private PreparedAnimatedModel PrepareAnimatedModel(PristonModel model, Matrix world, PristonStageBakedLight bakedLight)
    {
        var groups = new Dictionary<int, List<ModelTriangleRef>>();
        for (var objectIndex = 0; objectIndex < model.Objects.Count; objectIndex++)
        {
            var obj = model.Objects[objectIndex];
            foreach (var face in obj.Faces)
            {
                if (!ValidFace(face.A, face.B, face.C, obj.Vertices.Count))
                {
                    continue;
                }

                var list = GetModelGroup(groups, face.MaterialIndex);
                list.Add(new ModelTriangleRef(objectIndex, face));
            }
        }

        var boneCount = model.Objects.Count;
        if (boneCount > PristonShaderLibrary.MaxBones)
        {
            _log.Warn($"[PristonRenderer] AnimatedModel '{model.Path}' has {boneCount} bones, clamping to {PristonShaderLibrary.MaxBones}.");
            boneCount = PristonShaderLibrary.MaxBones;
        }

        var bindTransforms = BuildBindPoseTransforms(model);
        for (var i = 0; i < bindTransforms.Length; i++)
        {
            bindTransforms[i] *= world;
        }

        var cornerNormals = PristonModelNormals.Build(model, (objectIndex, _) => bindTransforms[Math.Min(objectIndex, bindTransforms.Length - 1)]);

        var batches = new List<PreparedAnimatedBatch>(groups.Count);
        foreach (var (materialIndex, triangles) in groups.OrderBy(pair => pair.Key))
        {
            var material = materialIndex >= 0 && materialIndex < model.Materials.Count ? model.Materials[materialIndex] : FallbackMaterial;
            if (material.IsInvisible || material.HasNoRenderableTexture)
            {
                continue;
            }

            var mesh = PrepareSkinnedAnimatedMesh(
                model,
                triangles.ToArray(),
                cornerNormals,
                boneCount,
                bakedLight.For(material));
            batches.Add(new PreparedAnimatedBatch(material, mesh));
        }

        return new PreparedAnimatedModel(boneCount, batches.ToArray());
    }

    private void AddAnimatedModelGroups(
        IReadOnlyList<PreparedAnimatedInstance> models,
        RenderStageHandle handle)
    {
        foreach (var step in AddAnimatedModelGroupsIncrementally(models, handle))
        {
        }
    }

    private IEnumerable<StreamingWork> AddAnimatedModelGroupsIncrementally(
        IReadOnlyList<PreparedAnimatedInstance> models,
        RenderStageHandle handle)
    {
        var chunk = new List<PreparedAnimatedInstance>();
        var chunkBoneCount = 0;
        for (var index = 0; index < models.Count; index++)
        {
            var model = models[index];
            if (model.Prepared.Batches.Length == 0)
            {
                continue;
            }

            if (chunk.Count > 0
                && chunkBoneCount + model.Prepared.BoneCount > PristonShaderLibrary.MaxBones)
            {
                foreach (var step in AddAnimatedStageGroupSteps(chunk, chunkBoneCount, handle))
                {
                    yield return step;
                }

                chunk = [];
                chunkBoneCount = 0;
            }

            chunk.Add(model);
            chunkBoneCount += model.Prepared.BoneCount;
        }

        if (chunk.Count > 0)
        {
            foreach (var step in AddAnimatedStageGroupSteps(chunk, chunkBoneCount, handle))
            {
                yield return step;
            }
        }
    }

    private IEnumerable<StreamingWork> AddAnimatedStageGroupSteps(
        IReadOnlyList<PreparedAnimatedInstance> models,
        int boneCount,
        RenderStageHandle handle)
    {
        var group = new AnimatedStageGroupRuntime(boneCount);
        var pending = new List<PendingAnimatedBatch>();
        var merged = new Dictionary<AnimatedMergeKey, PendingAnimatedBatch>();
        var shadowMeshes = new List<PreparedSkinnedMesh>();
        var shadowModels = new List<AnimatedModelRuntime>();
        PristonMaterialRuntime shadowMaterial = null;
        var paletteOffset = 0;
        for (var modelIndex = 0; modelIndex < models.Count; modelIndex++)
        {
            var source = models[modelIndex];
            var runtime = new AnimatedModelRuntime(
                source.Model,
                group.Palette,
                paletteOffset)
            {
                World = source.World
            };
            group.Models.Add(runtime);
            handle.AnimatedModels.Add(runtime);

            foreach (var sourceBatch in source.Prepared.Batches)
            {
                runtime.IncludeBindBounds(sourceBatch.Mesh.Bounds);
                var material = _materialFactory.Create(source.Directory, sourceBatch.Material);
                var mesh = OffsetBoneIndices(sourceBatch.Mesh, paletteOffset);
                if (CanMergeAnimated(material))
                {
                    var key = CreateAnimatedMergeKey(material);
                    if (!merged.TryGetValue(key, out var batch))
                    {
                        batch = new PendingAnimatedBatch(material, [], []);
                        merged.Add(key, batch);
                        pending.Add(batch);
                    }

                    batch.Meshes.Add(mesh);
                    if (!batch.Models.Contains(runtime))
                    {
                        batch.Models.Add(runtime);
                    }
                }
                else
                {
                    pending.Add(new PendingAnimatedBatch(material, [mesh], [runtime]));
                }
            }

            paletteOffset += source.Prepared.BoneCount;
        }

        foreach (var item in pending)
        {
            var shadowCoveredByAggregate = CanAggregateShadow(item.Material);
            if (shadowCoveredByAggregate)
            {
                shadowMaterial ??= item.Material;
                shadowMeshes.AddRange(item.Meshes);
                foreach (var model in item.Models)
                {
                    if (!shadowModels.Contains(model))
                    {
                        shadowModels.Add(model);
                    }
                }
            }

            var mesh = item.Meshes.Count == 1
                ? item.Meshes[0]
                : MergeSkinnedMeshes(item.Meshes);
            var (vb, ib, primCount, vertCount, idxCount, bounds) = UploadSkinnedMesh(mesh);
            var batch = new RenderBatch(
                [],
                [],
                null,
                item.Material,
                ForceCastsShadow: true,
                ShadowCoveredByAggregate: shadowCoveredByAggregate,
                IsSkinned: true,
                SkinnedBuffer: vb,
                SkinnedIndexBuffer: ib,
                SkinnedPrimitiveCount: primCount,
                SkinnedVertexCount: vertCount,
                SkinnedIndexCount: idxCount,
                SkinnedBounds: bounds,
                BoneCount: group.Palette.Count);
            batch.UseSkinPalette(group.Palette);
            group.Batches.Add(new AnimatedStageBatchRuntime(batch, item.Models.ToArray()));
            group.ColorBatchCount++;
            handle.Batches.Add(batch);
            yield return new StreamingWork(StreamingStep.AnimatedMeshUpload, (long)vertCount * PristonSkinnedVertex.VertexDeclaration.VertexStride);
        }

        if (shadowMeshes.Count > 0)
        {
            var mesh = shadowMeshes.Count == 1
                ? shadowMeshes[0]
                : MergeSkinnedMeshes(shadowMeshes);
            var (vb, ib, primitiveCount, vertexCount, indexCount, bounds) = UploadSkinnedMesh(mesh);
            var batch = new RenderBatch(
                [],
                [],
                null,
                shadowMaterial,
                ForceCastsShadow: true,
                IsSkinned: true,
                SkinnedBuffer: vb,
                SkinnedIndexBuffer: ib,
                SkinnedPrimitiveCount: primitiveCount,
                SkinnedVertexCount: vertexCount,
                SkinnedIndexCount: indexCount,
                SkinnedBounds: bounds,
                BoneCount: group.Palette.Count);
            batch.UseSkinPalette(group.Palette);
            group.Batches.Add(new AnimatedStageBatchRuntime(batch, shadowModels.ToArray()));
            group.ShadowAggregateCount++;
            handle.ShadowBatches.Add(batch);
            yield return new StreamingWork(StreamingStep.ShadowAggregateUpload, (long)vertexCount * PristonSkinnedVertex.VertexDeclaration.VertexStride);
        }

        handle.AnimatedGroups.Add(group);
        UpdateAnimatedStageGroup(group);
    }

    private static PreparedSkinnedMesh OffsetBoneIndices(
        PreparedSkinnedMesh mesh,
        int paletteOffset)
    {
        if (paletteOffset == 0)
        {
            return mesh;
        }

        var vertices = (PristonSkinnedVertex[])mesh.Vertices.Clone();
        for (var index = 0; index < vertices.Length; index++)
        {
            vertices[index].BoneIndex += paletteOffset;
        }

        return mesh with { Vertices = vertices };
    }

    private static bool CanMergeAnimated(PristonMaterialRuntime material)
    {
        return material.TextureFormState == 0
            && !material.Transparent
            && !material.HasLayers
            && material.Textures.Length == 1;
    }

    private static AnimatedMergeKey CreateAnimatedMergeKey(PristonMaterialRuntime material)
    {
        return new AnimatedMergeKey(
            material.Textures[0],
            material.AlphaTest,
            material.TwoSided,
            material.SelfIllum,
            material.Blinking,
            material.Water,
            material.Wind,
            material.Reflex,
            material.BlendState);
    }

    private static PreparedSkinnedMesh MergeSkinnedMeshes(IReadOnlyList<PreparedSkinnedMesh> meshes)
    {
        var vertexCount = 0;
        var indexCount = 0;
        var triangleCount = 0;
        var bounds = meshes[0].Bounds;
        for (var index = 0; index < meshes.Count; index++)
        {
            var mesh = meshes[index];
            vertexCount = checked(vertexCount + mesh.Vertices.Length);
            indexCount = checked(indexCount + mesh.Indices.Length);
            triangleCount = checked(triangleCount + mesh.TriangleCount);
            bounds = BoundingBox.CreateMerged(bounds, mesh.Bounds);
        }

        var vertices = new PristonSkinnedVertex[vertexCount];
        var indices = new int[indexCount];
        var vertexOffset = 0;
        var indexOffset = 0;
        for (var meshIndex = 0; meshIndex < meshes.Count; meshIndex++)
        {
            var mesh = meshes[meshIndex];
            mesh.Vertices.AsSpan().CopyTo(vertices.AsSpan(vertexOffset));
            for (var index = 0; index < mesh.Indices.Length; index++)
            {
                indices[indexOffset + index] = checked(mesh.Indices[index] + vertexOffset);
            }

            vertexOffset += mesh.Vertices.Length;
            indexOffset += mesh.Indices.Length;
        }

        return new PreparedSkinnedMesh(vertices, indices, triangleCount, bounds);
    }

    private ActorRuntime BuildActor(PristonActorInstance instance)
    {
        var actor = instance.Actor;
        var runtime = new ActorRuntime(instance);
        var meshDir = Path.GetDirectoryName(actor.Mesh.Path) ?? ".";
        var prepared = PreparedActorGeometries.GetValue(actor, PrepareActorGeometry);
        runtime.SetBoneMap(prepared.BoneMap);
        var resource = AcquireActorGpuResource(actor, meshDir, prepared);
        runtime.GpuResource = resource;

        try
        {
            foreach (var item in resource.Batches)
            {
                var batch = new RenderBatch(
                    [],
                    [],
                    null,
                    item.Material,
                    ForceCastsShadow: true,
                    ShadowCoveredByAggregate: item.ShadowCoveredByAggregate,
                    IsSkinned: true,
                    SkinnedBuffer: item.VertexBuffer,
                    SkinnedIndexBuffer: item.IndexBuffer,
                    SkinnedPrimitiveCount: item.PrimitiveCount,
                    SkinnedVertexCount: item.VertexCount,
                    SkinnedIndexCount: item.IndexCount,
                    SkinnedBounds: item.Bounds,
                    BoneCount: prepared.BoneMap.Length);
                batch.UseSkinPalette(runtime.Palette);
                runtime.AddBatch(batch);
            }

            if (resource.ShadowAggregate is not null)
            {
                var item = resource.ShadowAggregate;
                var shadowBatch = new RenderBatch(
                    [],
                    [],
                    null,
                    item.Material,
                    ForceCastsShadow: true,
                    IsSkinned: true,
                    SkinnedBuffer: item.VertexBuffer,
                    SkinnedIndexBuffer: item.IndexBuffer,
                    SkinnedPrimitiveCount: item.PrimitiveCount,
                    SkinnedVertexCount: item.VertexCount,
                    SkinnedIndexCount: item.IndexCount,
                    SkinnedBounds: item.Bounds,
                    BoneCount: prepared.BoneMap.Length);
                shadowBatch.UseSkinPalette(runtime.Palette);
                runtime.AddShadowBatch(shadowBatch);
            }

            UpdateActor(runtime);
            return runtime;
        }
        catch
        {
            ReleaseActorGpuResource(runtime);
            throw;
        }
    }

    private ActorGpuResource AcquireActorGpuResource(
        PristonActor actor,
        string meshDirectory,
        PreparedActorGeometry prepared)
    {
        if (!_actorGpuResources.TryGetValue(actor, out var resource))
        {
            var materials = new PristonMaterialRuntime[prepared.Batches.Length];
            var aggregateCandidates = new List<PreparedSkinnedMesh>(prepared.Batches.Length);
            PristonMaterialRuntime shadowMaterial = null;
            for (var index = 0; index < prepared.Batches.Length; index++)
            {
                var item = prepared.Batches[index];
                var material = _materialFactory.Create(meshDirectory, item.Material);
                materials[index] = material;
                if (CanAggregateShadow(material))
                {
                    shadowMaterial ??= material;
                    aggregateCandidates.Add(item.Mesh);
                }
            }

            var hasShadowAggregate = aggregateCandidates.Count > 1;
            var batches = new List<ActorGpuBatch>(prepared.Batches.Length);
            ActorGpuBatch shadowAggregate = null;
            try
            {
                for (var index = 0; index < prepared.Batches.Length; index++)
                {
                    var item = prepared.Batches[index];
                    var material = materials[index];
                    var (vertexBuffer, indexBuffer, primitiveCount, vertexCount, indexCount, bounds) = UploadSkinnedMesh(item.Mesh);
                    batches.Add(new ActorGpuBatch(
                        material,
                        vertexBuffer,
                        indexBuffer,
                        primitiveCount,
                        vertexCount,
                        indexCount,
                        bounds,
                        ShadowCoveredByAggregate: hasShadowAggregate && CanAggregateShadow(material)));
                }

                if (hasShadowAggregate)
                {
                    var merged = MergeSkinnedMeshes(aggregateCandidates);
                    var (vertexBuffer, indexBuffer, primitiveCount, vertexCount, indexCount, bounds) = UploadSkinnedMesh(merged);
                    shadowAggregate = new ActorGpuBatch(
                        shadowMaterial,
                        vertexBuffer,
                        indexBuffer,
                        primitiveCount,
                        vertexCount,
                        indexCount,
                        bounds);
                }

                resource = new ActorGpuResource(actor, batches.ToArray(), shadowAggregate);
                _actorGpuResources.Add(actor, resource);
            }
            catch
            {
                DisposeActorGpuBatches(batches);
                DisposeActorGpuBatch(shadowAggregate);
                throw;
            }
        }

        resource.ReferenceCount++;
        return resource;
    }

    private void ReleaseActorGpuResource(ActorRuntime runtime)
    {
        var resource = runtime.GpuResource;
        if (resource is null)
        {
            return;
        }

        runtime.GpuResource = null;
        resource.ReferenceCount--;
        if (resource.ReferenceCount > 0)
        {
            return;
        }

        _actorGpuResources.Remove(resource.Actor);
        DisposeActorGpuBatches(resource.Batches);
        DisposeActorGpuBatch(resource.ShadowAggregate);
    }

    private static void DisposeActorGpuBatches(IEnumerable<ActorGpuBatch> batches)
    {
        foreach (var batch in batches)
        {
            batch.VertexBuffer.Dispose();
            batch.IndexBuffer.Dispose();
        }
    }

    private static void DisposeActorGpuBatch(ActorGpuBatch batch)
    {
        batch?.VertexBuffer.Dispose();
        batch?.IndexBuffer.Dispose();
    }

    private PreparedActorGeometry PrepareActorGeometry(PristonActor actor)
    {
        var groups = new Dictionary<int, List<ActorTriangleRef>>();
        var boneLookup = BuildObjectLookup(actor.Skeleton);
        var meshTransforms = BuildBindPoseTransforms(actor.Mesh);

        // The shared armor mesh contains several variants and LODs; the INX group selects the visible objects.
        var renderFilter = actor.RenderObjectNames.Count > 0
            ? new HashSet<string>(actor.RenderObjectNames, StringComparer.OrdinalIgnoreCase)
            : null;

        for (var objectIndex = 0; objectIndex < actor.Mesh.Objects.Count; objectIndex++)
        {
            var obj = actor.Mesh.Objects[objectIndex];
            if (renderFilter != null && !renderFilter.Contains(obj.Name))
                continue;

            foreach (var face in obj.Faces)
            {
                if (!ValidFace(face.A, face.B, face.C, obj.Vertices.Count))
                    continue;

                var list = GetActorGroup(groups, face.MaterialIndex);
                var a = PristonModelGeometry.ResolvePhysiqueBone(obj, face.A, boneLookup);
                var b = PristonModelGeometry.ResolvePhysiqueBone(obj, face.B, boneLookup);
                var c = PristonModelGeometry.ResolvePhysiqueBone(obj, face.C, boneLookup);
                list.Add(new ActorTriangleRef(
                    objectIndex,
                    face,
                    a,
                    b,
                    c,
                    meshTransforms[objectIndex]));
            }
        }

        // Upload only referenced bones so helper nodes do not exhaust the shader palette.
        var boneMap = BuildActorBoneMap(groups.Values, actor.Skeleton.Objects.Count, actor.Skeleton.Path, _log, out var boneRemap);
        var skeletonPose = BuildBindPoseTransforms(actor.Skeleton);
        var cornerNormals = PristonModelNormals.Build(actor.Mesh, (objectIndex, vertexIndex) =>
        {
            var bone = PristonModelGeometry.ResolvePhysiqueBone(actor.Mesh.Objects[objectIndex], vertexIndex, boneLookup);
            return skeletonPose[Math.Min(EffectiveBone(bone, objectIndex), skeletonPose.Length - 1)];
        });
        var batches = new List<PreparedActorBatch>(groups.Count);
        foreach (var materialIndex in groups.Keys.OrderBy(static k => k))
        {
            var triangleRefs = groups[materialIndex].ToArray();
            var material = materialIndex >= 0 && materialIndex < actor.Mesh.Materials.Count ? actor.Mesh.Materials[materialIndex] : FallbackMaterial;
            if (material.IsInvisible || material.HasNoRenderableTexture)
            {
                continue;
            }

            var mesh = PrepareSkinnedActorMesh(actor.Mesh, triangleRefs, cornerNormals, boneRemap);
            batches.Add(new PreparedActorBatch(material, mesh));
        }

        return new PreparedActorGeometry(boneMap, batches.ToArray());
    }

    private static PreparedSkinnedMesh PrepareSkinnedAnimatedMesh(
        PristonModel model,
        ModelTriangleRef[] triangleRefs,
        Dictionary<PristonModelFace, Vector3[]>[] cornerNormals,
        int boneCount,
        Color light)
    {
        var uniqueVerts = new List<PristonSkinnedVertex>();
        var vertLookup = new Dictionary<SkinnedVertKey, int>();
        var indices = new List<int>(triangleRefs.Length * 3);
        var minPos = new Vector3(float.MaxValue);
        var maxPos = new Vector3(float.MinValue);

        foreach (var tri in triangleRefs)
        {
            var obj = model.Objects[tri.ObjectIndex];
            var face = tri.Face;
            var stages = PristonStageLinks.Resolve(obj.TexLinks, face.TexLinkIndex);
            var boneIdx = Math.Min(tri.ObjectIndex, boneCount - 1);
            var boneF = (float)boneIdx;
            var normals = cornerNormals[tri.ObjectIndex][face];

            // Animated scenery takes the stage's authored light for its texture (PristonStageBakedLight).
            AddSkinnedVertex(uniqueVerts, vertLookup, indices, ref minPos, ref maxPos,
                obj.Vertices[face.A].Position, normals[0], light, stages, 0, boneF);
            AddSkinnedVertex(uniqueVerts, vertLookup, indices, ref minPos, ref maxPos,
                obj.Vertices[face.B].Position, normals[1], light, stages, 1, boneF);
            AddSkinnedVertex(uniqueVerts, vertLookup, indices, ref minPos, ref maxPos,
                obj.Vertices[face.C].Position, normals[2], light, stages, 2, boneF);
        }

        return PrepareSkinnedMesh(uniqueVerts, indices, minPos, maxPos, triangleRefs.Length);
    }


    private static PreparedSkinnedMesh PrepareSkinnedActorMesh(
        PristonModel mesh,
        ActorTriangleRef[] triangleRefs,
        Dictionary<PristonModelFace, Vector3[]>[] cornerNormals,
        Dictionary<int, int> boneRemap)
    {
        var uniqueVerts = new List<PristonSkinnedVertex>();
        var vertLookup = new Dictionary<SkinnedVertKey, int>();
        var indices = new List<int>(triangleRefs.Length * 3);
        var minPos = new Vector3(float.MaxValue);
        var maxPos = new Vector3(float.MinValue);

        foreach (var tri in triangleRefs)
        {
            var obj = mesh.Objects[tri.ObjectIndex];
            var face = tri.Face;
            var stages = PristonStageLinks.Resolve(obj.TexLinks, face.TexLinkIndex);
            var normals = cornerNormals[tri.ObjectIndex][face];

            var boneA = CompactBone(tri.ABoneIndex, tri.ObjectIndex, boneRemap);
            var boneB = CompactBone(tri.BBoneIndex, tri.ObjectIndex, boneRemap);
            var boneC = CompactBone(tri.CBoneIndex, tri.ObjectIndex, boneRemap);

            AddSkinnedVertex(uniqueVerts, vertLookup, indices, ref minPos, ref maxPos,
                obj.Vertices[face.A].Position, normals[0], Color.White, stages, 0, (float)boneA);
            AddSkinnedVertex(uniqueVerts, vertLookup, indices, ref minPos, ref maxPos,
                obj.Vertices[face.B].Position, normals[1], Color.White, stages, 1, (float)boneB);
            AddSkinnedVertex(uniqueVerts, vertLookup, indices, ref minPos, ref maxPos,
                obj.Vertices[face.C].Position, normals[2], Color.White, stages, 2, (float)boneC);
        }

        return PrepareSkinnedMesh(uniqueVerts, indices, minPos, maxPos, triangleRefs.Length);
    }

    private static int EffectiveBone(int physiqueBoneIndex, int objectIndex) =>
        physiqueBoneIndex >= 0 ? physiqueBoneIndex : objectIndex;

    private static int CompactBone(int physiqueBoneIndex, int objectIndex, Dictionary<int, int> boneRemap) =>
        boneRemap.TryGetValue(EffectiveBone(physiqueBoneIndex, objectIndex), out var slot) ? slot : 0;

    private static int[] BuildActorBoneMap(
        IEnumerable<List<ActorTriangleRef>> groups,
        int skeletonBoneCount,
        string skeletonPath,
        IClientLog log,
        out Dictionary<int, int> remap)
    {
        var used = new SortedSet<int>();
        var maxBone = Math.Max(0, skeletonBoneCount - 1);
        foreach (var list in groups)
            foreach (var tri in list)
            {
                used.Add(Math.Min(EffectiveBone(tri.ABoneIndex, tri.ObjectIndex), maxBone));
                used.Add(Math.Min(EffectiveBone(tri.BBoneIndex, tri.ObjectIndex), maxBone));
                used.Add(Math.Min(EffectiveBone(tri.CBoneIndex, tri.ObjectIndex), maxBone));
            }

        var map = used.ToArray();
        if (map.Length > PristonShaderLibrary.MaxBones)
        {
            log.Warn($"[PristonRenderer] Actor skeleton '{skeletonPath}' skins to {map.Length} distinct bones, clamping to {PristonShaderLibrary.MaxBones}.");
            map = map[..PristonShaderLibrary.MaxBones];
        }
        else if (map.Length == 0)
        {
            map = [0];
        }

        remap = new Dictionary<int, int>(map.Length);
        for (var i = 0; i < map.Length; i++)
            remap[map[i]] = i;
        return map;
    }

    private static void AddSkinnedVertex(
        List<PristonSkinnedVertex> uniqueVerts,
        Dictionary<SkinnedVertKey, int> lookup,
        List<int> indices,
        ref Vector3 minPos,
        ref Vector3 maxPos,
        Vector3 position,
        Vector3 normal,
        Color color,
        PristonStageLinks stages,
        int corner,
        float boneIndex)
    {
        var uv0 = stages.Stage0.Corner(corner);
        var uv1 = stages.Stage1.Corner(corner);
        var uv2 = stages.Stage2.Corner(corner);
        var key = new SkinnedVertKey(position, normal, uv0, uv1, uv2, boneIndex);
        if (!lookup.TryGetValue(key, out var idx))
        {
            idx = uniqueVerts.Count;
            uniqueVerts.Add(new PristonSkinnedVertex(position, normal, color, uv0, boneIndex, uv1, uv2));
            lookup.Add(key, idx);
            minPos = Vector3.Min(minPos, position);
            maxPos = Vector3.Max(maxPos, position);
        }
        indices.Add(idx);
    }

    private static PreparedSkinnedMesh PrepareSkinnedMesh(
        List<PristonSkinnedVertex> vertices, List<int> indices, Vector3 minimum, Vector3 maximum, int triangleCount)
    {
        var bounds = vertices.Count > 0 ? new BoundingBox(minimum, maximum) : new BoundingBox(Vector3.Zero, Vector3.Zero);
        return new PreparedSkinnedMesh(vertices.ToArray(), indices.ToArray(), triangleCount, bounds);
    }

    private (VertexBuffer, IndexBuffer, int, int, int, BoundingBox) UploadSkinnedMesh(PreparedSkinnedMesh prepared)
    {
        var vertices = prepared.Vertices;
        var indices = prepared.Indices;
        var vertexBuffer = new VertexBuffer(_graphicsDevice, PristonSkinnedVertex.VertexDeclaration, vertices.Length, BufferUsage.WriteOnly);
        IndexBuffer indexBuffer = null;
        try
        {
            vertexBuffer.SetData(vertices);
            if (vertices.Length <= ushort.MaxValue)
            {
                var shortIndices = new ushort[indices.Length];
                for (var index = 0; index < indices.Length; index++)
                {
                    shortIndices[index] = (ushort)indices[index];
                }

                indexBuffer = new IndexBuffer(_graphicsDevice, IndexElementSize.SixteenBits, indices.Length, BufferUsage.WriteOnly);
                indexBuffer.SetData(shortIndices);
            }
            else
            {
                indexBuffer = new IndexBuffer(_graphicsDevice, IndexElementSize.ThirtyTwoBits, indices.Length, BufferUsage.WriteOnly);
                indexBuffer.SetData(indices);
            }

            return (vertexBuffer, indexBuffer, prepared.TriangleCount, vertices.Length, indices.Length, prepared.Bounds);
        }
        catch
        {
            vertexBuffer.Dispose();
            indexBuffer?.Dispose();
            throw;
        }
    }

    private void UpdateActor(ActorRuntime runtime)
    {
        if (runtime.PaletteIsCurrent())
        {
            return;
        }

        var skeletonTransforms = runtime.Parent.IsValid ? Array.Empty<Matrix>() : runtime.SampleSkeleton();
        var world = runtime.World;

        var palette = runtime.Palette.Matrices;
        var boneMap = runtime.BoneMap;
        var paletteSize = palette.Length;
        PristonActorPalette.Fill(skeletonTransforms, boneMap, world, runtime.Parent.IsValid, palette);

        runtime.Palette.Pack(_motionFrame);
        // Batches share the packed palette. Refresh their WORLD-space bounds from the
        // animated bone positions, so frustum culling follows the actor (otherwise the
        // bind-pose AABB sits at the origin and the actor pops out when the camera moves).
        if (paletteSize > 0)
        {
            var worldBounds = TransformBoundsByPalette(runtime.BindBounds, palette.AsSpan(0, paletteSize));
            ApplyWorldBounds(runtime.Batches, worldBounds);
            ApplyWorldBounds(runtime.ShadowBatches, worldBounds);
            var centre = (worldBounds.Min + worldBounds.Max) * 0.5f;
            var floorLight = FloorLightAt(new Vector3(centre.X, worldBounds.Min.Y, centre.Z));
            foreach (var batch in runtime.Batches)
            {
                batch.FloorLight = floorLight;
            }
        }

        runtime.MarkPaletteCurrent();
    }

    private static Matrix[] BuildBindPoseTransforms(PristonModel model) =>
        new PristonModelAnimator(model).Evaluate(0f);

    private static Dictionary<string, int> BuildObjectLookup(PristonModel model)
    {
        var lookup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < model.Objects.Count; i++)
            if (model.Objects[i].Name.Length > 0 && !lookup.ContainsKey(model.Objects[i].Name))
                lookup.Add(model.Objects[i].Name, i);
        return lookup;
    }

    private static PristonMotionInfo SelectActorMotion(PristonActor actor)
    {
        var info = actor.Info;
        for (var i = PristonFormatConstants.CharacterMotionBase; i < info.Motions.Count; i++)
        {
            var motion = info.Motions[i];
            if (motion.State == 64 && motion.Repeat != 0 && motion.EndFrame > motion.StartFrame)
                return ApplyMotionFrameOffset(actor, motion);
        }

        for (var i = PristonFormatConstants.CharacterMotionBase; i < info.Motions.Count; i++)
        {
            var motion = info.Motions[i];
            if (motion.Repeat != 0 && motion.EndFrame > motion.StartFrame)
                return ApplyMotionFrameOffset(actor, motion);
        }

        for (var i = PristonFormatConstants.CharacterMotionBase; i < info.Motions.Count; i++)
        {
            var motion = info.Motions[i];
            if (motion.EndFrame > motion.StartFrame)
                return ApplyMotionFrameOffset(actor, motion);
        }

        return new PristonMotionInfo(
            0,
            0,
            1,
            [],
            0,
            0,
            0,
            1,
            0,
            0);
    }

    private static PristonMotionInfo SelectActorMotion(PristonActor actor, PristonActorMotion desiredMotion, PristonMotionContext context)
    {
        if (desiredMotion == PristonActorMotion.Any)
        {
            return SelectActorMotion(actor);
        }

        var selected = PristonMotionSelection.Select(actor.Info, desiredMotion, context);
        return selected is null ? SelectActorMotion(actor) : ApplyMotionFrameOffset(actor, selected);
    }

    private static PristonMotionInfo ApplyMotionFrameOffset(PristonActor actor, PristonMotionInfo motion)
    {
        var rangeIndex = motion.MotionFrame - 1;
        if (rangeIndex < 0 || rangeIndex >= actor.Skeleton.FrameRanges.Count)
            return motion;

        var range = actor.Skeleton.FrameRanges[rangeIndex];
        var offset = range.StartFrame / 160;
        if (offset == 0)
            return motion;

        return new PristonMotionInfo(
            motion.State,
            motion.StartFrame + (uint)offset,
            motion.EndFrame + (uint)offset,
            motion.EventFrames,
            motion.ItemCodeCount,
            motion.JobCodeBit,
            motion.MapPosition,
            motion.Repeat,
            motion.KeyCode,
            motion.MotionFrame);
    }

    private void UpdateAnimatedStageGroup(AnimatedStageGroupRuntime group)
    {
        var changed = false;
        foreach (var runtime in group.Models)
        {
            if (runtime.PaletteIsCurrent())
            {
                continue;
            }

            // Most stage objects are authored in world space (World == Identity); an object authored
            // in local space carries an explicit world placement. Each source model retains its own
            // loop length while writing into a shared shader-palette slice.
            var transforms = runtime.Animator.Evaluate(runtime.Frame);
            var world = runtime.World;
            var palette = group.Palette.Matrices;
            for (var index = 0; index < runtime.PaletteCount; index++)
            {
                palette[runtime.PaletteOffset + index] = transforms[index] * world;
            }

            var worldBounds = TransformBoundsByPalette(
                runtime.BindBounds,
                palette.AsSpan(runtime.PaletteOffset, runtime.PaletteCount));
            runtime.SetWorldBounds(worldBounds);
            runtime.MarkPaletteCurrent();
            changed = true;
        }

        if (!changed)
        {
            return;
        }

        group.Palette.Pack(_motionFrame);
        foreach (var batch in group.Batches)
        {
            var bounds = batch.Models[0].WorldBounds;
            for (var index = 1; index < batch.Models.Length; index++)
            {
                bounds = BoundingBox.CreateMerged(bounds, batch.Models[index].WorldBounds);
            }

            batch.Batch.SetWorldBounds(bounds);
        }
    }

    // The stage's authored light on the floor under a point, the same light the floor itself is tinted by; full light
    // where no stage floor lies under it.
    private float FloorLightAt(Vector3 point)
    {
        if (!ActorFloorLight)
        {
            return 1f;
        }

        foreach (var stage in _stages)
        {
            if (stage.LightField is not null && stage.LightField.TryLightAt(point, out var light))
            {
                return light;
            }
        }

        return 1f;
    }

    private static void ApplyWorldBounds(IReadOnlyList<RenderBatch> batches, BoundingBox worldBounds)
    {
        for (var index = 0; index < batches.Count; index++)
        {
            batches[index].SetWorldBounds(worldBounds);
        }
    }

    // World-space AABB of a rigidly-skinned bind-pose box under a bone palette. For each affine bone,
    // transform the centre once and obtain the exact transformed extents from abs(linearMatrix).
    // This is equivalent to transforming all eight corners but removes seven point transforms per bone.
    internal static BoundingBox TransformBoundsByPalette(BoundingBox bind, ReadOnlySpan<Matrix> palette)
    {
        var center = (bind.Min + bind.Max) * 0.5f;
        var extent = (bind.Max - bind.Min) * 0.5f;
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        for (var index = 0; index < palette.Length; index++)
        {
            var bone = palette[index];
            var transformedCenter = Vector3.Transform(center, bone);
            var transformedExtent = new Vector3(
                extent.X * MathF.Abs(bone.M11) + extent.Y * MathF.Abs(bone.M21) + extent.Z * MathF.Abs(bone.M31),
                extent.X * MathF.Abs(bone.M12) + extent.Y * MathF.Abs(bone.M22) + extent.Z * MathF.Abs(bone.M32),
                extent.X * MathF.Abs(bone.M13) + extent.Y * MathF.Abs(bone.M23) + extent.Z * MathF.Abs(bone.M33));
            min = Vector3.Min(min, transformedCenter - transformedExtent);
            max = Vector3.Max(max, transformedCenter + transformedExtent);
        }

        return new BoundingBox(min, max);
    }

    private readonly record struct StaticMergeKey(
        Texture2D Texture,
        bool AlphaTest,
        bool TwoSided,
        bool SelfIllum,
        BlendState Blend);

    private readonly record struct AnimatedMergeKey(
        Texture2D Texture,
        bool AlphaTest,
        bool TwoSided,
        bool SelfIllum,
        bool Blinking,
        bool Water,
        bool Wind,
        bool Reflex,
        BlendState Blend);

    private void AddGroups(
        Dictionary<int, List<PristonCornerVertex>> groups,
        IReadOnlyList<PristonMaterial> materials,
        string assetDirectory,
        List<RenderBatch> target,
        StaticShadowCollector shadowCollector)
    {
        var merged = new Dictionary<StaticMergeKey, (PristonMaterialRuntime Material, List<PristonCornerVertex> Vertices)>();
        foreach (var (materialIndex, vertices) in groups)
        {
            if (vertices.Count == 0)
            {
                continue;
            }

            var material = materialIndex >= 0 && materialIndex < materials.Count ? materials[materialIndex] : FallbackMaterial;
            if (material.IsInvisible || material.HasNoRenderableTexture)
            {
                continue;
            }

            var runtimeMaterial = _materialFactory.Create(assetDirectory, material);
            if (runtimeMaterial.Invisible || material.HasNoRenderableTexture)
            {
                continue;
            }

            var shadowCoveredByAggregate = CanAggregateShadow(runtimeMaterial);
            if (shadowCoveredByAggregate)
            {
                shadowCollector.Add(runtimeMaterial, vertices);
            }

            if (ShadowDiagnostics && !runtimeMaterial.CastsShadow)
            {
                _log.Info($"[ShadowDiag] non-caster tex='{material.TextureName}' opacity='{material.OpacityName}' " +
                    $"blend={material.BlendType} transp={material.Transparency:0.###} " +
                    $"alphaTest={runtimeMaterial.AlphaTest} transparent={runtimeMaterial.Transparent} " +
                    $"selfIllum={runtimeMaterial.SelfIllum} dir='{Path.GetFileName(assetDirectory)}'");
            }

            var mergeable = CanMerge(runtimeMaterial);
            if (mergeable)
            {
                var key = new StaticMergeKey(
                    runtimeMaterial.Textures[0],
                    runtimeMaterial.AlphaTest,
                    runtimeMaterial.TwoSided,
                    runtimeMaterial.SelfIllum,
                    runtimeMaterial.BlendState);
                if (!merged.TryGetValue(key, out var entry))
                {
                    entry = (runtimeMaterial, []);
                    merged.Add(key, entry);
                }

                entry.Vertices.AddRange(vertices);
            }
            else
            {
                var vertexArray = vertices.ToArray();
                var mesh = runtimeMaterial.TextureFormState == 0 ? _meshBuilder.CreateStaticMesh(vertexArray) : null;
                var dynamicVertices = mesh is null ? ToDynamicVertices(vertexArray) : [];
                target.Add(new RenderBatch(
                    dynamicVertices,
                    [],
                    mesh,
                    runtimeMaterial,
                    ShadowCoveredByAggregate: shadowCoveredByAggregate));
            }
        }

        foreach (var (_, entry) in merged)
        {
            var vertexArray = entry.Vertices.ToArray();
            var mesh = _meshBuilder.CreateStaticMesh(vertexArray);
            target.Add(new RenderBatch(
                [],
                [],
                mesh,
                entry.Material,
                ShadowCoveredByAggregate: CanAggregateShadow(entry.Material)));
        }
    }

    // Unwelded per-triangle vertices for UV-form-state materials.
    private static PristonStaticVertex[] ToDynamicVertices(PristonCornerVertex[] vertices)
    {
        var dynamicVertices = new PristonStaticVertex[vertices.Length];
        for (var i = 0; i < vertices.Length; i++)
        {
            var vertex = vertices[i];
            dynamicVertices[i] = new PristonStaticVertex(
                vertex.Position,
                Vector3.Up,
                vertex.Color,
                vertex.Uv0,
                vertex.Uv1,
                vertex.Uv2);
        }

        PristonVertexNormals.RecalculateTriangleNormals(dynamicVertices);
        return dynamicVertices;
    }

    private static List<PristonCornerVertex> GetGroup(Dictionary<int, List<PristonCornerVertex>> groups, int material)
    {
        if (!groups.TryGetValue(material, out var list))
        {
            list = [];
            groups.Add(material, list);
        }

        return list;
    }

    private static List<ModelTriangleRef> GetModelGroup(Dictionary<int, List<ModelTriangleRef>> groups, int material)
    {
        if (!groups.TryGetValue(material, out var list))
        {
            list = [];
            groups.Add(material, list);
        }

        return list;
    }

    private static List<ActorTriangleRef> GetActorGroup(Dictionary<int, List<ActorTriangleRef>> groups, int material)
    {
        if (!groups.TryGetValue(material, out var list))
        {
            list = [];
            groups.Add(material, list);
        }

        return list;
    }

    private static bool ValidFace(int a, int b, int c, int vertexCount) =>
        a >= 0 && b >= 0 && c >= 0 && a < vertexCount && b < vertexCount && c < vertexCount;

    public void Dispose()
    {
        if (_disposed)
            return;

        RemoveAllStages();
        ClearActors();
        if (_presentation is not null)
        {
            _presentation.RenderingChanged -= ApplyQuality;
        }

        _shadowMap.Dispose();
        Profiler.Dispose();
        _glowRenderer.Dispose();
        _skyCapture.Dispose();
        _sceneTargets.Dispose();
        _screenOcclusion.Dispose();
        _lampShadows.Dispose();
        _lightCulling.Dispose();
        _environment.Dispose();
        _reflections.Dispose();
        _volumetric.Dispose();
        _shaders.Dispose();
        _textureLoader.Dispose();
        _disposed = true;
    }

    private sealed class ActorRuntime
    {
        public ActorRuntime(PristonActorInstance instance)
        {
            Instance = instance;
            World = instance.World;
            Animator = new PristonModelAnimator(instance.Actor.Skeleton);
            var defaultCount = Math.Min(Animator.ObjectCount, PristonShaderLibrary.MaxBones);
            BoneMap = new int[defaultCount];
            for (var i = 0; i < defaultCount; i++)
                BoneMap[i] = i;
            Palette = new PristonSkinPalette(defaultCount);
            Motion = SelectActorMotion(instance.Actor, instance.Motion, instance.MotionContext);
            Frame = StartTicks(Motion);
        }

        public PristonActorInstance Instance { get; }
        public PristonModelAnimator Animator { get; }
        private float _sampledFrame = float.NaN;
        private Matrix[] _sampledTransforms;
        private float _paletteFrame = float.NaN;
        private Matrix _paletteWorld;
        private bool _paletteAttached;

        public Matrix[] SampleSkeleton()
        {
            if (_sampledFrame != Frame)
            {
                _sampledTransforms = Animator.Evaluate(Frame);
                _sampledFrame = Frame;
            }

            return _sampledTransforms;
        }
        public PristonSkinPalette Palette { get; private set; }
        public int[] BoneMap { get; private set; }
        public Matrix World { get; set; }
        public ActorHandle Parent { get; set; } = ActorHandle.None;
        public int ParentBone { get; set; }
        public ActorGpuResource GpuResource { get; set; }

        public bool Alive { get; set; } = true;
        public int Generation { get; set; }

        public void SetBoneMap(int[] boneMap)
        {
            BoneMap = boneMap;
            Palette = new PristonSkinPalette(boneMap.Length);
            _paletteFrame = float.NaN;
        }

        public bool PaletteIsCurrent()
        {
            var attached = Parent.IsValid;
            var frame = attached ? 0f : Frame;
            return _paletteFrame == frame && _paletteWorld == World && _paletteAttached == attached;
        }

        public void MarkPaletteCurrent()
        {
            _paletteFrame = Parent.IsValid ? 0f : Frame;
            _paletteWorld = World;
            _paletteAttached = Parent.IsValid;
        }
        public PristonMotionInfo Motion { get; private set; }
        public float Frame { get; private set; }
        public float PlaybackRate { get; set; } = 1f;
        public List<RenderBatch> Batches { get; } = [];
        public List<RenderBatch> ShadowBatches { get; } = [];
        public BoundingBox BindBounds { get; private set; }
        private bool _hasBindBounds;

        public void AddBatch(RenderBatch batch)
        {
            Batches.Add(batch);
            BindBounds = _hasBindBounds
                ? BoundingBox.CreateMerged(BindBounds, batch.SkinnedBounds)
                : batch.SkinnedBounds;
            _hasBindBounds = true;
        }

        public void AddShadowBatch(RenderBatch batch)
        {
            ShadowBatches.Add(batch);
        }

        public void SetMotion(PristonActorMotion motion)
        {
            var resolved = SelectActorMotion(Instance.Actor, motion, Instance.MotionContext);
            if (resolved == Motion)
                return;
            Motion = resolved;
            Frame = StartTicks(Motion);
        }

        public void ForceMotion(PristonActorMotion motion)
        {
            Motion = SelectActorMotion(Instance.Actor, motion, Instance.MotionContext);
            Frame = StartTicks(Motion);
        }

        public void ForceMotionIndex(int motionIndex)
        {
            var info = Instance.Actor.Info;
            var count = Math.Min(info.Motions.Count, checked((int)info.MotionCount));
            if ((uint)motionIndex >= (uint)count)
            {
                throw new ArgumentOutOfRangeException(nameof(motionIndex), motionIndex, $"Motion index must be between 0 and {count - 1}.");
            }

            var motion = info.Motions[motionIndex];
            if (motion.EndFrame <= motion.StartFrame)
            {
                throw new InvalidDataException($"Motion {motionIndex} in '{info.Path}' has an empty frame range.");
            }

            Motion = ApplyMotionFrameOffset(Instance.Actor, motion);
            Frame = StartTicks(Motion);
        }

        public void Advance(float deltaSeconds)
        {
            var start = StartTicks(Motion);
            var end = EndTicks(Motion, Instance.Actor.Skeleton.LastFrame);
            if (end <= start)
            {
                Frame = start;
                return;
            }

            Frame += deltaSeconds * ReferenceTicksPerSecond * RateFactor();
            if (Motion.Repeat != 0)
            {
                var span = end - start;
                while (Frame >= end)
                    Frame = start + (Frame - end) % span;
                if (Frame < start)
                    Frame = start;
            }
            else if (Frame >= end)
            {
                if (ReturnsToStand(Motion))
                {
                    Motion = SelectActorMotion(Instance.Actor, PristonActorMotion.Stand, Instance.MotionContext);
                    Frame = StartTicks(Motion);
                }
                else
                {
                    Frame = end - 1;
                }
            }
        }

        public void SetFrame(float ticks)
        {
            var start = StartTicks(Motion);
            var end = EndTicks(Motion, Instance.Actor.Skeleton.LastFrame);
            Frame = end > start ? MathHelper.Clamp(ticks, start, end - 1) : start;
        }

        private static int StartTicks(PristonMotionInfo motion) => (int)motion.StartFrame * 160;

        private static int EndTicks(PristonMotionInfo motion, int fallback) =>
            motion.EndFrame > motion.StartFrame ? (int)motion.EndFrame * 160 : fallback;

        // Only one-shots driven by gameplay return to Stand; unlisted motions hold their final pose.
        private static bool ReturnsToStand(PristonMotionInfo motion)
        {
            var state = (PristonActorMotion)motion.State;
            return state is
                PristonActorMotion.Attack or
                PristonActorMotion.Damage or
                PristonActorMotion.Skill or
                PristonActorMotion.Eat or
                PristonActorMotion.FallDamage or
                PristonActorMotion.Yahoo or
                PristonActorMotion.Restart or
                PristonActorMotion.Hammer;
        }

        private float RateFactor()
        {
            var state = (PristonActorMotion)Motion.State;
            return state switch
            {
                PristonActorMotion.Walk or
                PristonActorMotion.Run or
                PristonActorMotion.Attack or
                PristonActorMotion.Skill => PlaybackRate,
                PristonActorMotion.Damage or
                PristonActorMotion.Eat => DamageEatRateFactor,
                _ => 1f,
            };
        }
    }

    private sealed class ActorGpuResource(PristonActor actor, ActorGpuBatch[] batches, ActorGpuBatch shadowAggregate)
    {
        public PristonActor Actor { get; } = actor;
        public ActorGpuBatch[] Batches { get; } = batches;
        public ActorGpuBatch ShadowAggregate { get; } = shadowAggregate;
        public int ReferenceCount { get; set; }
    }
}
