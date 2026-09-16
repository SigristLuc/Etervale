using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.Formats;

namespace PristonTale.Engine3D.Rendering;

public sealed class PristonRenderer : IDisposable
{
    private static readonly ConditionalWeakTable<PristonActor, PreparedActorGeometry> PreparedActorGeometries = new();

    // Changing the shadow-map size requires matching bias changes in PristonEffectPipeline.
    private int ShadowMapSize => _shadowMap.Width;
    // FieldRuntime.cpp advances stage-object animation by two ticks per millisecond.
    private const float StageObjectFrameRate = 2000f;

    private static readonly bool ShadowDiagnostics =
        Environment.GetEnvironmentVariable("PRISTON_SHADOW_DIAG") == "1";
    // PlaybackRate supplies the rules-layer FrameStep/80 ratio for speed-scaled motions.
    private const float ReferenceTicksPerSecond = 160000f / 33f;
    private const float DamageEatRateFactor = 92f / 80f;
    private const float ShadowFrustumRadius = 1400f;
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
    private readonly PristonEffectPipeline _effectPipeline;
    private readonly PristonTextureLoader _textureLoader;
    private readonly PristonMaterialRuntimeFactory _materialFactory;
    private readonly PristonGpuMeshBuilder _meshBuilder;
    private readonly BatchUploader _batchUploader;
    private RenderTarget2D _shadowMap;
    private readonly PresentationSettings _presentation;
    private readonly Action<Matrix, Matrix> _drawLampCasters;
    private readonly List<RenderStageHandle> _stages = [];
    // Rebuilt on residency changes so Draw remains allocation-free.
    private readonly List<RenderBatch> _renderBatches = [];
    private readonly List<RenderBatch> _shadowBatches = [];
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

    private PristonPointLight[] _overrideLights;
    private float _lampIntensity = 4f;
    private float _lampRangeScale = 1f;
    private float _glowIntensity = 1f;
    private float _glowScale = 1f;
    private readonly List<PristonPointLight> _activeLights = new(MaxActiveLights);
    private readonly List<PristonPointLight> _activeLampLights = new(MaxActiveLights);
    public Lighting.PristonTransientLights TransientLights { get; } = new();
    private readonly List<PristonPointLight> _tunedCandidates = [];
    private readonly List<PristonPointLight> _frameGlowLights = [];
    private const float EmitterGlowRadius = 175f;
    private Matrix _lightView = Matrix.Identity;
    private Matrix _lightProjection = Matrix.Identity;
    private Matrix _cachedSunLightViewProjection = Matrix.Identity;
    private readonly FixedRateRefreshScheduler _sunShadowRefresh = new();
    private readonly FixedRateRefreshScheduler _lampShadowRefresh = new();
    private Vector3 _sunShadowFocus;
    private Vector3 _lampShadowFocus;
    private bool _sunShadowFocusValid;
    private bool _lampShadowFocusValid;
    private int _shadowUpdateRate;
    private int _shadowDrawCalls;

    private readonly GlowRenderer _glowRenderer;
    private LampShadowSystem _lampShadows;
    private float _lampFactor;

    public PristonRenderer(GraphicsDevice graphicsDevice, ContentManager content, IClientLog log = null, PresentationSettings presentation = null)
    {
        _graphicsDevice = graphicsDevice;
        _log = log ?? ConsoleClientLog.Instance;
        _presentation = presentation;
        _drawLampCasters = DrawLampCasters;
        _stateCache = new PristonRenderStateCache(graphicsDevice);
        _effectPipeline = new PristonEffectPipeline(content, _log);
        _textureLoader = new PristonTextureLoader(graphicsDevice, _log);
        _textureLoader.Initialize();
        _materialFactory = new PristonMaterialRuntimeFactory(_textureLoader);
        _meshBuilder = new PristonGpuMeshBuilder(graphicsDevice);
        _batchUploader = new BatchUploader(graphicsDevice);
        // Shadow depth is stored in R32F rather than a quantized color channel.
        _glowRenderer = new GlowRenderer(graphicsDevice);
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
            var size = settings.ShadowMapSize;
            while (true)
            {
                try
                {
                    var replacement = new RenderTarget2D(_graphicsDevice, size, size, false, SurfaceFormat.Single, DepthFormat.Depth24);
                    _shadowMap?.Dispose();
                    _shadowMap = replacement;
                    break;
                }
                catch (Exception exception) when (size > 1024 && exception is ArgumentException or InvalidOperationException)
                {
                    size /= 2;
                    _log.Warn($"[Rendering] Shadow allocation failed; trying {size}: {exception.Message}");
                }
            }
        }

        if (_lampShadows?.TileSize != settings.LampShadowTileSize)
        {
            var replacement = new LampShadowSystem(_graphicsDevice, settings.LampShadowTileSize);
            _lampShadows?.Dispose();
            _lampShadows = replacement;
        }

        _shadowUpdateRate = settings.ShadowUpdateRate;
        _sunShadowRefresh.Invalidate();
        _lampShadowRefresh.Invalidate();
        ShadowsEnabled = settings.ShadowsEnabled;
        _log.Info(
            $"[Rendering] effective sun shadow={_shadowMap.Width}; lamp tile={_lampShadows.TileSize}; " +
            $"shadow update={FormatUpdateRate(_shadowUpdateRate)}; shadows={ShadowsEnabled}.");
    }

    private void DrawLampCasters(Matrix view, Matrix projection)
    {
        _effectPipeline.ApplyDepthFrame(view, projection, _renderTime);
        DrawShadowQueueItems(_renderQueueBuilder.Queue.ShadowCasters, view * projection);
    }

    private static Texture2D CreateGlowTexture(GraphicsDevice graphicsDevice)
    {
        const int size = 64;
        var data = new Color[size * size];
        var c = (size - 1) * 0.5f;
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var d = new Vector2((x - c) / c, (y - c) / c).Length();
                var a = MathHelper.Clamp(1f - d, 0f, 1f);
                a = a * a;
                data[y * size + x] = new Color(1f, 1f, 1f, a);
            }
        var texture = new Texture2D(graphicsDevice, size, size, false, SurfaceFormat.Color);
        texture.SetData(data);
        return texture;
    }

    public BoundingBox Bounds { get; private set; }
    public BoundingBox StageBounds { get; private set; }
    public PristonRenderStats LastFrameStats { get; private set; }

    public bool ShadowsEnabled { get; set; } = true;

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
            _lampFactor);
        _portraitQueueBuilder.Build(_portraitBatches, [], frame, includeShadowCasters: false);

        _stateCache.BeginFrame();
        _stateCache.InvalidateDeviceState();
        _stateCache.SetDepthStencilState(DepthStencilState.Default);
        _stateCache.SetSamplerState0(PristonSamplerStates.AnisotropicWrap);
        _effectPipeline.ApplyFrame(frame);

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
            ref diagnostics);
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

        _meshBuilder.SetBakeLights(BuildBakeLights(handle));
        var shadowCollector = new StaticShadowCollector();
        BuildStageGeometry(stage, stageDir, handle, shadowCollector);
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
                    PrepareAnimatedModel(model, instance.World, _meshBuilder)));
            }
            else
            {
                AddModel(model, modelDir, handle, instance.World, shadowCollector);
            }
        }

        AddAnimatedModelGroups(animatedModels, handle);

        AddStaticShadowAggregate(handle, shadowCollector);

        LogAnimatedStageSummary(handle);
        AssignEmitterGlowColors(handle, stage);

        handle.Bounds = PristonBinaryUtil.BoundsFrom(handle.Batches.SelectMany(static b => b.BoundsPoints()));
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
        var cpuBuilder = new PristonGpuMeshBuilder(null);
        cpuBuilder.SetBakeLights(BuildBakeLights(handle));
        var shadowCollector = new StaticShadowCollector();
        _log.Info($"[Stream] lamp kernel: {cpuBuilder.LampKernel}.");
        var animatedPreparation = Task.Run(() =>
        {
            var prepared = new Dictionary<PristonModel, PreparedAnimatedModel>();
            foreach (var model in models)
            {
                if (model.IsAnimated)
                {
                    prepared.Add(model, PrepareAnimatedModel(model, Matrix.Identity, cpuBuilder));
                }
            }

            return prepared;
        });
        try
        {
            var stageDirectory = Path.GetDirectoryName(stage.Path) ?? ".";
            foreach (var step in _textureLoader.UploadPreparedTextures(stageDirectory, PristonMaterialUsage.ForStage(stage)))
            {
                yield return null;
            }

            foreach (var model in models)
            {
                var directory = Path.GetDirectoryName(model.Path) ?? ".";
                foreach (var step in _textureLoader.UploadPreparedTextures(directory, PristonMaterialUsage.ForModel(model)))
                {
                    yield return null;
                }
            }

            var groupsTask = Task.Run(() => BuildStageGroups(stage));
            while (!groupsTask.IsCompleted)
            {
                yield return null;
            }

            foreach (var step in AddGroupsIncrementally(
                CompletedResult(groupsTask),
                stage.Materials,
                stageDirectory,
                handle,
                cpuBuilder,
                shadowCollector))
            {
                yield return null;
            }

            var animatedModels = new List<PreparedAnimatedInstance>();
            foreach (var model in models)
            {
                var directory = Path.GetDirectoryName(model.Path) ?? ".";
                if (model.IsAnimated)
                {
                    while (!animatedPreparation.IsCompleted)
                    {
                        yield return null;
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
                    var modelTask = Task.Run(() => PristonModelGeometry.BuildGroups(model, Matrix.Identity, Color.White));
                    while (!modelTask.IsCompleted)
                    {
                        yield return null;
                    }

                    foreach (var step in AddGroupsIncrementally(
                        CompletedResult(modelTask),
                        model.Materials,
                        directory,
                        handle,
                        cpuBuilder,
                        shadowCollector))
                    {
                        yield return null;
                    }
                }
            }

            foreach (var step in AddAnimatedModelGroupsIncrementally(animatedModels, handle))
            {
                yield return null;
            }

            foreach (var step in AddStaticShadowAggregateIncrementally(handle, shadowCollector, cpuBuilder))
            {
                yield return null;
            }

            LogAnimatedStageSummary(handle);
            AssignEmitterGlowColors(handle, stage);
            handle.Bounds = PristonBinaryUtil.BoundsFrom(handle.Batches.SelectMany(static batch => batch.BoundsPoints()));
            _stages.Add(handle);
            RebuildRenderBatches();
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

    private IEnumerable<bool> AddGroupsIncrementally(
        Dictionary<int, List<VertexPositionColorTexture>> groups,
        IReadOnlyList<PristonMaterial> materials,
        string directory,
        RenderStageHandle handle,
        PristonGpuMeshBuilder cpuBuilder,
        StaticShadowCollector shadowCollector)
    {
        var merged = new Dictionary<StaticMergeKey, (PristonMaterialRuntime Material, List<VertexPositionColorTexture> Vertices)>();
        var pending = new List<(PristonMaterialRuntime Material, List<VertexPositionColorTexture> Vertices)>();
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

            yield return false;
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
            yield return false;
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
            yield return false;
        }
    }

    private sealed record PreparedStaticBatch(
        PristonMaterialRuntime Material,
        PristonGpuMeshBuilder.PreparedMesh Mesh,
        PristonDynamicVertex[] Vertices,
        bool ShadowCoveredByAggregate);

    private static bool CanMerge(PristonMaterialRuntime material)
    {
        return material.TextureFormState == 0
            && !material.Transparent
            && !material.Water
            && !material.Blinking
            && !material.Wind
            && !material.HasSecondTexture
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

    private IEnumerable<bool> AddStaticShadowAggregateIncrementally(
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
            yield return false;
        }

        foreach (var prepared in CompletedResult(preparation))
        {
            var mesh = _meshBuilder.Upload(prepared);
            handle.ShadowBatches.Add(new RenderBatch([], [], mesh, collector.Material, ForceCastsShadow: true));
            yield return false;
        }
    }

    private sealed class StaticShadowCollector
    {
        // Spatial aggregation keeps depth submissions low without turning the whole field into one
        // giant caster that defeats the light-frustum cull. A 1024-unit cell gives the 2800-unit sun
        // footprint useful granularity and normally leaves a lamp frustum intersecting only a few cells.
        private const float CellSize = 1024f;
        private readonly Dictionary<(int X, int Z), List<VertexPositionColorTexture>> _cells = [];

        public int Count => _cells.Count;
        public PristonMaterialRuntime Material { get; private set; }

        public void Add(PristonMaterialRuntime material, List<VertexPositionColorTexture> vertices)
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
                    cell = new List<VertexPositionColorTexture>(24);
                }

                var destinationIndex = cell.Count;
                cell.EnsureCapacity(destinationIndex + 3);
                CollectionsMarshal.SetCount(cell, destinationIndex + 3);
                source.Slice(index, 3).CopyTo(CollectionsMarshal.AsSpan(cell).Slice(destinationIndex, 3));
            }
        }

        public IEnumerable<List<VertexPositionColorTexture>> OrderedCells()
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
        RebuildRenderBatches();
        return new ActorHandle(slot, generation);
    }

    public void RemoveActor(ActorHandle handle)
    {
        if (!TryResolveActor(handle, out var runtime))
            return;
        ReleaseActorGpuResource(runtime);
        runtime.Alive = false;
        _freeActorSlots.Push(handle.Slot);
        RebuildRenderBatches();
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
        RebuildRenderBatches();
    }

    private void RemoveAllStages()
    {
        foreach (var stage in _stages)
            stage.DisposeGpu();
        _stages.Clear();
    }

    private void RebuildRenderBatches()
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

        Bounds = PristonBinaryUtil.BoundsFrom(_renderBatches.SelectMany(static b => b.BoundsPoints()));
        StageBounds = UnionStageBounds();
        _sunShadowRefresh.Invalidate();
        _lampShadowRefresh.Invalidate();
    }

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
        _batchUploader.BeginFrame();
        _shadowDrawCalls = 0;
        var skyBatchCount = 0;
        var lighting = BuildFrameLighting(camera.Target);
        // Skip the unused sun pass when its shader contribution is zero.
        var sunShadowsEnabled = ShadowsEnabled && _lampFactor < 1f && lighting.Sun.DiffuseColor != Vector3.Zero;
        InvalidateShadowAfterTeleport(camera.Target);
        var sunShadowUpdated = _sunShadowRefresh.ShouldRefresh(_renderTime, _shadowUpdateRate, sunShadowsEnabled);
        if (sunShadowUpdated)
        {
            _cachedSunLightViewProjection = CreateLightViewProjection(lighting, camera.Target);
            _sunShadowFocus = camera.Target;
            _sunShadowFocusValid = true;
        }

        var lightViewProjection = sunShadowsEnabled ? _cachedSunLightViewProjection : Matrix.Identity;
        var lampShadowsEnabled = ShadowsEnabled && _lampFactor > 0.05f && _activeLampLights.Count > 0;
        var lampShadowsUpdated = _lampShadowRefresh.ShouldRefresh(_renderTime, _shadowUpdateRate, lampShadowsEnabled);
        var frame = new PristonFrameContext(
            camera.View,
            camera.Projection,
            camera.Position,
            _renderTime,
            lighting,
            lightViewProjection,
            sunShadowsEnabled ? _shadowMap : _textureLoader.White,
            sunShadowsEnabled,
            _lampFactor);
        var culledBatches = _renderQueueBuilder.Build(
            _renderBatches,
            _shadowBatches,
            frame,
            sunShadowUpdated || lampShadowsUpdated);
        if (sunShadowUpdated)
        {
            RenderShadowMap();
        }

        var lampShadowMapsUpdated = 0;
        if (lampShadowsUpdated)
        {
            _lampShadows.Select(_activeLampLights, camera.Target, _lampFactor);
            _lampShadows.Render(_graphicsDevice, _drawLampCasters);
            lampShadowMapsUpdated = _lampShadows.Count;
            _lampShadowFocus = camera.Target;
            _lampShadowFocusValid = true;
        }
        else if (!lampShadowsEnabled)
        {
            _lampShadows.Clear();
            _lampShadowFocusValid = false;
        }
        _stateCache.BeginFrame();
        _stateCache.InvalidateDeviceState();
        if (Sky != null && !Underground)
        {
            var skyPosition = new Vector3(0f, MathF.Min(camera.Position.Y, 150000f / 256f) / 8f, 0f);
            var skyView = frame.View;
            skyView.Translation = Vector3.Zero;
            skyView.Translation = Vector3.TransformNormal(-skyPosition, skyView);
            var skyProjection = frame.Projection;
            var nearPlane = skyProjection.M43 / skyProjection.M33;
            skyProjection.M33 = -1f;
            skyProjection.M43 = -nearPlane;
            var skyFrame = frame with
            {
                View = skyView,
                Projection = skyProjection,
                CameraPosition = skyPosition,
                Lighting = new PristonLighting(Vector3.One, new PristonDirectionalLight(Vector3.Down, Vector3.Zero), []),
                ShadowsEnabled = false,
                LampFactor = 0f
            };
            _effectPipeline.ApplyFrame(skyFrame);
            _stateCache.SetDepthStencilState(DepthStencilState.None);
            _stateCache.SetSamplerState0(PristonSamplerStates.AnisotropicWrap);
            var currentProfile = SkyProfiles.Select(TimeOfDay);
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
                        ref visibleStaticMeshDiagnostics, opacity);
                    skyBatchCount++;
                }
            }
        }

        _stateCache.SetDepthStencilState(DepthStencilState.Default);
        _stateCache.SetSamplerState0(PristonSamplerStates.AnisotropicWrap);
        _effectPipeline.ApplyFrame(frame);
        _effectPipeline.ApplyLampShadows(_lampShadows);
        _stateCache.SetDepthStencilState(DepthStencilState.Default);
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

        _glowRenderer.Draw(frame, _frameGlowLights, _lampFactor);
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
            _stateCache.TotalStateChanges,
            _stateCache.TextureChanges,
            _stateCache.VertexBufferChanges,
            _stateCache.IndexBufferChanges,
            _batchUploader.UploadedBytes,
            visibleStaticMeshDiagnostics,
            sunShadowUpdated,
            lampShadowMapsUpdated,
            _shadowDrawCalls);
    }

    private void InvalidateShadowAfterTeleport(Vector3 focus)
    {
        var thresholdSquared = ShadowTeleportRefreshDistance * ShadowTeleportRefreshDistance;
        if (_sunShadowFocusValid && Vector3.DistanceSquared(focus, _sunShadowFocus) > thresholdSquared)
        {
            _sunShadowRefresh.Invalidate();
        }

        if (_lampShadowFocusValid && Vector3.DistanceSquared(focus, _lampShadowFocus) > thresholdSquared)
        {
            _lampShadowRefresh.Invalidate();
        }
    }

    private static string FormatUpdateRate(int rate)
    {
        return rate == 0 ? "every frame" : $"{rate} Hz";
    }

    private Matrix CreateLightViewProjection(PristonLighting lighting, Vector3 focusPoint)
    {
        var radius = ShadowFrustumRadius;
        var center = new Vector3(
            ClampShadowCenter(focusPoint.X, Bounds.Min.X, Bounds.Max.X, radius),
            focusPoint.Y,
            ClampShadowCenter(focusPoint.Z, Bounds.Min.Z, Bounds.Max.Z, radius));
        var lightDirection = Vector3.Normalize(lighting.Sun.Direction);
        var lightPosition = center - lightDirection * radius * 1.8f;
        var up = MathF.Abs(Vector3.Dot(lightDirection, Vector3.Up)) > 0.95f ? Vector3.Forward : Vector3.Up;
        var view = Matrix.CreateLookAt(lightPosition, center, up);
        var orthoWidth = radius * 2.0f;
        var projection = Matrix.CreateOrthographic(orthoWidth, orthoWidth, 10f, radius * 4.0f);

        projection = StabilizeShadowProjection(view, projection, ShadowMapSize);

        _lightView = view;
        _lightProjection = projection;
        return view * projection;
    }

    internal static Matrix StabilizeShadowProjection(Matrix view, Matrix projection, int mapSize)
    {
        // Snap a fixed world anchor, not the moving frustum centre: that centre always maps
        // to clip-space zero and cannot reveal the sub-texel motion we need to cancel.
        var originClip = Vector4.Transform(Vector4.UnitW, view * projection);
        var texelX = (originClip.X * 0.5f + 0.5f) * mapSize;
        var texelY = (originClip.Y * 0.5f + 0.5f) * mapSize;
        var clipPerTexel = 2f / mapSize;
        projection.M41 += (MathF.Round(texelX) - texelX) * clipPerTexel;
        projection.M42 += (MathF.Round(texelY) - texelY) * clipPerTexel;
        return projection;
    }

    private static float ClampShadowCenter(float value, float min, float max, float radius)
    {
        var lower = min + radius;
        var upper = max - radius;
        return lower <= upper ? MathHelper.Clamp(value, lower, upper) : (min + max) * 0.5f;
    }

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

    private static PristonGpuMeshBuilder.BakeLight[] BuildBakeLights(RenderStageHandle stage)
    {
        var result = new PristonGpuMeshBuilder.BakeLight[stage.StageLights.Length];
        for (var i = 0; i < stage.StageLights.Length; i++)
        {
            var l = stage.StageLights[i];
            result[i] = new PristonGpuMeshBuilder.BakeLight(
                l.Position,
                l.Color * (l.Intensity * stage.LampIntensity),
                l.Radius * stage.LampRangeScale);
        }
        return result;
    }

    private PristonLighting BuildFrameLighting(Vector3 focusPoint)
    {
        var sky = _dayNight.Evaluate();
        var ambient = sky.AmbientColor;
        var sunColor = sky.SunColor;
        var lampFactor = sky.LampFactor;
        var skyColor = sky.SkyColor;
        if (Underground)
        {
            sunColor = Vector3.Zero;
            ambient = UndergroundAmbient;
            lampFactor = 1f;
            skyColor = new Vector3(0.02f, 0.02f, 0.03f);
        }
        SkyColor = new Color(skyColor);
        _lampFactor = lampFactor;
        var sun = new PristonDirectionalLight(Vector3.Normalize(sky.SunDirection), sunColor);
        SelectActiveLights(focusPoint, lampFactor);
        _activeLampLights.Clear();
        _activeLampLights.AddRange(_activeLights);
        TransientLights.Select(focusPoint, _activeLights);
        int transientCount = _activeLights.Count;
        for (int index = 0; index < _activeLampLights.Count && _activeLights.Count < MaxActiveLights; index++)
        {
            _activeLights.Add(_activeLampLights[index]);
        }

        return new PristonLighting(ambient, sun, _activeLights, transientCount);
    }

    private const int MaxActiveLights = 16;

    private void SelectActiveLights(Vector3 focusPoint, float lampFactor)
    {
        _activeLights.Clear();
        if (lampFactor <= 0.01f)
            return;

        _tunedCandidates.Clear();
        foreach (var stage in _stages)
            foreach (var light in stage.StageLights)
                _tunedCandidates.Add(TuneLight(light, stage, lampFactor));

        if (_tunedCandidates.Count == 0)
            return;

        if (_tunedCandidates.Count <= MaxActiveLights)
        {
            _activeLights.AddRange(_tunedCandidates);
            return;
        }

        Span<int> best = stackalloc int[MaxActiveLights];
        Span<float> bestDist = stackalloc float[MaxActiveLights];
        for (var i = 0; i < MaxActiveLights; i++) { best[i] = -1; bestDist[i] = float.MaxValue; }

        for (var i = 0; i < _tunedCandidates.Count; i++)
        {
            var d = Vector3.DistanceSquared(_tunedCandidates[i].Position, focusPoint);
            for (var slot = 0; slot < MaxActiveLights; slot++)
            {
                if (d < bestDist[slot])
                {
                    for (var k = MaxActiveLights - 1; k > slot; k--)
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

        for (var i = 0; i < MaxActiveLights; i++)
            if (best[i] >= 0)
                _activeLights.Add(_tunedCandidates[best[i]]);
    }

    private static PristonPointLight TuneLight(PristonPointLight light, RenderStageHandle stage, float lampFactor) =>
        light with
        {
            Radius = light.Radius * stage.LampRangeScale,
            Intensity = light.Intensity * stage.LampIntensity * lampFactor
        };

    private void RenderShadowMap()
    {
        _graphicsDevice.SetRenderTarget(_shadowMap);
        _graphicsDevice.Clear(Color.White);
        _graphicsDevice.DepthStencilState = DepthStencilState.Default;
        _graphicsDevice.BlendState = BlendState.Opaque;
        _graphicsDevice.SamplerStates[0] = SamplerState.LinearWrap;
        _effectPipeline.ApplyDepthFrame(_lightView, _lightProjection, _renderTime);
        DrawShadowQueueItems(_renderQueueBuilder.Queue.ShadowCasters, _lightView * _lightProjection);
        _graphicsDevice.SetRenderTarget(null);
    }

    private readonly BoundingFrustum _shadowCasterFrustum = new(Matrix.Identity);

    private void DrawShadowQueueItems(IReadOnlyList<PristonRenderQueueItem<RenderBatch>> items, Matrix lightViewProjection)
    {
        _shadowCasterFrustum.Matrix = lightViewProjection;
        // Thin/single-sided casters need both faces in every shadow draw. This state is invariant for
        // the whole queue and the colour pass invalidates its cache immediately after shadow rendering.
        _graphicsDevice.RasterizerState = PristonRasterizerStates.TwoSided;
        for (var i = 0; i < items.Count; i++)
        {
            var batch = items[i].Batch;
            if (!_shadowCasterFrustum.Intersects(batch.Bounds))
            {
                continue;
            }

            _shadowDrawCalls++;

            if (batch.IsSkinned)
            {
                Effect effect;
                if (batch.Material.AlphaTest)
                {
                    var texture = batch.Material.SelectTexture(_renderTime);
                    var uvScrollOffset = ComputeUvScrollOffset(batch.Material);
                    effect = _effectPipeline.SelectSkinnedAlphaTestDepthEffect(texture, batch.SkinPalette, uvScrollOffset);
                }
                else
                {
                    effect = _effectPipeline.SelectSkinnedOpaqueDepthEffect(batch.SkinPalette);
                }

                effect.CurrentTechnique.Passes[0].Apply();
                _graphicsDevice.SetVertexBuffer(batch.SkinnedBuffer);
                _graphicsDevice.Indices = batch.SkinnedIndexBuffer;
                _graphicsDevice.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, batch.SkinnedPrimitiveCount);
            }
            else
            {
                var windParams = ComputeWindParams(batch);
                Effect effect;
                if (batch.Material.AlphaTest)
                {
                    var texture = batch.Material.SelectTexture(_renderTime);
                    var uvScrollOffset = ComputeUvScrollOffset(batch.Material);
                    effect = _effectPipeline.SelectAlphaTestDepthEffect(batch.Material, texture, windParams, uvScrollOffset);
                }
                else
                {
                    effect = _effectPipeline.SelectOpaqueDepthEffect(batch.Material, windParams);
                }

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
        ref PristonMeshDiagnostics visibleStaticMeshDiagnostics)
    {
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
            _stateCache.SetBlendState(batch.Material.BlendState);
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
        float opacity = 1f)
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
            var effect = _effectPipeline.SelectSkinnedEffect(
                batch.Material,
                texture,
                uvScrollOffset,
                batch.SkinPalette,
                batch.ActorColorAdd);
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
        var uvScrollOffset2 = ComputeSecondUvScrollOffset(batch.Material);
        var effectStatic = _effectPipeline.SelectEffect(
            batch.Material,
            textureStatic,
            uvScrollOffsetStatic,
            ComputeWindParams(batch),
            uvScrollOffset2,
            opacity);

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
        // An authored BitmapFormState scroll code ALWAYS wins: this is how the original animates
        // water/sea surfaces (the "slow scroll" codes are deliberately smooth). Decide by the code's
        // presence, not by the computed offset, so the per-wrap zero-crossing never falls through to
        // the water branch and causes a one-frame jump.
        if (material.TextureFormState != 0)
        {
            var uOffset = TextureUOffset(material.TextureFormState);
            return uOffset == 0f ? Vector2.Zero : new Vector2(uOffset, 0f);
        }

        // Water (eMaterialType 0x200) WITHOUT a form-state code: gentle diagonal drift. Fine 10-bit
        // quantization so it reads as smooth, not stepped. Tunable enhancement (no ground-truth formula
        // exists — the EU release has no renderer and PTClassic drives water via the form-state codes).
        if (material.Water)
            return new Vector2(
                ((_renderTime >> 6) & 0x3FF) / 1024f,
                ((_renderTime >> 7) & 0x3FF) / 1024f);

        return Vector2.Zero;
    }

    private Vector2 ComputeSecondUvScrollOffset(PristonMaterialRuntime material)
    {
        if (!material.HasSecondTexture || material.SecondTextureFormState == 0)
            return Vector2.Zero;
        var uOffset = TextureUOffset(material.SecondTextureFormState);
        return uOffset == 0f ? Vector2.Zero : new Vector2(uOffset, 0f);
    }

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

    private static Dictionary<int, List<VertexPositionColorTexture>> BuildStageGroups(PristonStage stage)
    {
        var groups = new Dictionary<int, List<VertexPositionColorTexture>>();
        foreach (var face in stage.Faces)
        {
            if (!ValidFace(face.A, face.B, face.C, stage.Vertices.Count))
            {
                continue;
            }

            var tex = face.TexLinkIndex >= 0 && face.TexLinkIndex < stage.TexLinks.Count ? stage.TexLinks[face.TexLinkIndex] : PristonTexLink.Empty;
            var list = GetGroup(groups, face.MaterialIndex);
            AddTriangle(
                list,
                stage.Vertices[face.A].Position,
                stage.Vertices[face.B].Position,
                stage.Vertices[face.C].Position,
                stage.Vertices[face.A].Color,
                stage.Vertices[face.B].Color,
                stage.Vertices[face.C].Color,
                tex);
        }

        return groups;
    }

    private void AddModel(
        PristonModel model,
        string assetDirectory,
        RenderStageHandle handle,
        Matrix world,
        StaticShadowCollector shadowCollector)
    {
        AddGroups(
            PristonModelGeometry.BuildGroups(model, world, Color.White),
            model.Materials,
            assetDirectory,
            handle.Batches,
            shadowCollector);
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

    private PreparedAnimatedModel PrepareAnimatedModel(PristonModel model, Matrix world, PristonGpuMeshBuilder cpuBuilder)
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
        if (boneCount > PristonEffectPipeline.MaxBones)
        {
            _log.Warn($"[PristonRenderer] AnimatedModel '{model.Path}' has {boneCount} bones, clamping to {PristonEffectPipeline.MaxBones}.");
            boneCount = PristonEffectPipeline.MaxBones;
        }

        var bindNormals = BuildSmoothNormals(model);
        var bindTransforms = BuildBindPoseTransforms(model);
        for (var i = 0; i < bindTransforms.Length; i++)
        {
            bindTransforms[i] *= world;
        }

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
                bindNormals,
                bindTransforms,
                boneCount,
                cpuBuilder);
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

    private IEnumerable<bool> AddAnimatedModelGroupsIncrementally(
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
                && chunkBoneCount + model.Prepared.BoneCount > PristonEffectPipeline.MaxBones)
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

    private IEnumerable<bool> AddAnimatedStageGroupSteps(
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
            yield return false;
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
            yield return false;
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
            && !material.HasSecondTexture
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
        var bindNormals = BuildSmoothNormals(actor.Mesh);
        var batches = new List<PreparedActorBatch>(groups.Count);
        foreach (var materialIndex in groups.Keys.OrderBy(static k => k))
        {
            var triangleRefs = groups[materialIndex].ToArray();
            var material = materialIndex >= 0 && materialIndex < actor.Mesh.Materials.Count ? actor.Mesh.Materials[materialIndex] : FallbackMaterial;
            if (material.IsInvisible || material.HasNoRenderableTexture)
            {
                continue;
            }

            var mesh = PrepareSkinnedActorMesh(actor.Mesh, triangleRefs, bindNormals, boneRemap);
            batches.Add(new PreparedActorBatch(material, mesh));
        }

        return new PreparedActorGeometry(boneMap, batches.ToArray());
    }

    private static Vector3[][] BuildSmoothNormals(PristonModel model)
    {
        var result = new Vector3[model.Objects.Count][];
        for (var oi = 0; oi < model.Objects.Count; oi++)
        {
            var obj = model.Objects[oi];
            var normals = new Vector3[obj.Vertices.Count];

            var needsFallback = false;
            for (var vi = 0; vi < normals.Length; vi++)
            {
                var authored = obj.Vertices[vi].Normal;
                if (authored.LengthSquared() > 0.000001f)
                    normals[vi] = authored;
                else
                    needsFallback = true;
            }

            if (needsFallback)
            {
                var averaged = new Vector3[obj.Vertices.Count];
                foreach (var face in obj.Faces)
                {
                    if (!ValidFace(face.A, face.B, face.C, obj.Vertices.Count))
                        continue;
                    var pa = obj.Vertices[face.A].Position;
                    var pb = obj.Vertices[face.B].Position;
                    var pc = obj.Vertices[face.C].Position;
                    var n = Vector3.Cross(pb - pa, pc - pa);
                    if (n.LengthSquared() < 0.000001f)
                        continue;
                    n.Normalize();
                    averaged[face.A] += n;
                    averaged[face.B] += n;
                    averaged[face.C] += n;
                }

                for (var vi = 0; vi < normals.Length; vi++)
                {
                    if (normals[vi].LengthSquared() > 0.000001f)
                        continue;
                    normals[vi] = averaged[vi].LengthSquared() < 0.000001f ? Vector3.Up : Vector3.Normalize(averaged[vi]);
                }
            }

            result[oi] = normals;
        }
        return result;
    }

    private static PreparedSkinnedMesh PrepareSkinnedAnimatedMesh(
        PristonModel model,
        ModelTriangleRef[] triangleRefs,
        Vector3[][] bindNormals,
        Matrix[] bindTransforms,
        int boneCount,
        PristonGpuMeshBuilder cpuBuilder)
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
            var tex = face.TexLinkIndex >= 0 && face.TexLinkIndex < obj.TexLinks.Count ? obj.TexLinks[face.TexLinkIndex] : PristonTexLink.Empty;
            var boneIdx = Math.Min(tri.ObjectIndex, boneCount - 1);
            var boneF = (float)boneIdx;
            var objNormals = bindNormals[tri.ObjectIndex];
            var bind = bindTransforms[tri.ObjectIndex];

            // Bake the lamp light at each vertex's fixed (bind-pose) world position into COLOR.
            // The skinned shader adds this as lamp lighting (× LampFactor), so animated scenery is
            // lit consistently like the static world — no dependency on the dynamic point lights.
            var cA = BakeAnimatedLamp(obj.Vertices[face.A].Position, objNormals[face.A], bind, cpuBuilder);
            var cB = BakeAnimatedLamp(obj.Vertices[face.B].Position, objNormals[face.B], bind, cpuBuilder);
            var cC = BakeAnimatedLamp(obj.Vertices[face.C].Position, objNormals[face.C], bind, cpuBuilder);

            AddSkinnedVertex(uniqueVerts, vertLookup, indices, ref minPos, ref maxPos,
                obj.Vertices[face.A].Position, objNormals[face.A], cA, tex.A, boneF);
            AddSkinnedVertex(uniqueVerts, vertLookup, indices, ref minPos, ref maxPos,
                obj.Vertices[face.B].Position, objNormals[face.B], cB, tex.B, boneF);
            AddSkinnedVertex(uniqueVerts, vertLookup, indices, ref minPos, ref maxPos,
                obj.Vertices[face.C].Position, objNormals[face.C], cC, tex.C, boneF);
        }

        return PrepareSkinnedMesh(uniqueVerts, indices, minPos, maxPos, triangleRefs.Length);
    }

    // Lamp colour baked at an animated prop's bind-pose world position/normal.
    private static Color BakeAnimatedLamp(Vector3 localPosition, Vector3 localNormal, Matrix bind, PristonGpuMeshBuilder cpuBuilder)
    {
        var worldPos = Vector3.Transform(localPosition, bind);
        var worldNormal = Vector3.TransformNormal(localNormal, bind);
        if (worldNormal.LengthSquared() > 1e-8f)
            worldNormal.Normalize();
        return cpuBuilder.BakeLamp(worldPos, worldNormal);
    }

    private static PreparedSkinnedMesh PrepareSkinnedActorMesh(
        PristonModel mesh,
        ActorTriangleRef[] triangleRefs,
        Vector3[][] bindNormals,
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
            var tex = face.TexLinkIndex >= 0 && face.TexLinkIndex < obj.TexLinks.Count ? obj.TexLinks[face.TexLinkIndex] : PristonTexLink.Empty;
            var objNormals = bindNormals[tri.ObjectIndex];

            var boneA = CompactBone(tri.ABoneIndex, tri.ObjectIndex, boneRemap);
            var boneB = CompactBone(tri.BBoneIndex, tri.ObjectIndex, boneRemap);
            var boneC = CompactBone(tri.CBoneIndex, tri.ObjectIndex, boneRemap);

            AddSkinnedVertex(uniqueVerts, vertLookup, indices, ref minPos, ref maxPos,
                obj.Vertices[face.A].Position, objNormals[face.A], Color.Black, tex.A, (float)boneA);
            AddSkinnedVertex(uniqueVerts, vertLookup, indices, ref minPos, ref maxPos,
                obj.Vertices[face.B].Position, objNormals[face.B], Color.Black, tex.B, (float)boneB);
            AddSkinnedVertex(uniqueVerts, vertLookup, indices, ref minPos, ref maxPos,
                obj.Vertices[face.C].Position, objNormals[face.C], Color.Black, tex.C, (float)boneC);
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
        if (map.Length > PristonEffectPipeline.MaxBones)
        {
            log.Warn($"[PristonRenderer] Actor skeleton '{skeletonPath}' skins to {map.Length} distinct bones, clamping to {PristonEffectPipeline.MaxBones}.");
            map = map[..PristonEffectPipeline.MaxBones];
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
        Vector2 texCoord,
        float boneIndex)
    {
        var key = new SkinnedVertKey(position, normal, texCoord, boneIndex);
        if (!lookup.TryGetValue(key, out var idx))
        {
            idx = uniqueVerts.Count;
            uniqueVerts.Add(new PristonSkinnedVertex(position, normal, color, texCoord, boneIndex));
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

        runtime.Palette.Pack();
        // Batches share the packed palette. Refresh their WORLD-space bounds from the
        // animated bone positions, so frustum culling follows the actor (otherwise the
        // bind-pose AABB sits at the origin and the actor pops out when the camera moves).
        if (paletteSize > 0)
        {
            var worldBounds = TransformBoundsByPalette(runtime.BindBounds, palette.AsSpan(0, paletteSize));
            ApplyWorldBounds(runtime.Batches, worldBounds);
            ApplyWorldBounds(runtime.ShadowBatches, worldBounds);
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

        group.Palette.Pack();
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
        Dictionary<int, List<VertexPositionColorTexture>> groups,
        IReadOnlyList<PristonMaterial> materials,
        string assetDirectory,
        List<RenderBatch> target,
        StaticShadowCollector shadowCollector)
    {
        var merged = new Dictionary<StaticMergeKey, (PristonMaterialRuntime Material, List<VertexPositionColorTexture> Vertices)>();
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

    private static PristonDynamicVertex[] ToDynamicVertices(VertexPositionColorTexture[] vertices)
    {
        var dynamicVertices = new PristonDynamicVertex[vertices.Length];
        for (var i = 0; i < vertices.Length; i++)
        {
            var vertex = vertices[i];
            dynamicVertices[i] = new PristonDynamicVertex(
                vertex.Position,
                Vector3.Up,
                vertex.Color,
                vertex.TextureCoordinate);
        }

        PristonVertexNormals.RecalculateTriangleNormals(dynamicVertices);
        return dynamicVertices;
    }

    private static List<VertexPositionColorTexture> GetGroup(Dictionary<int, List<VertexPositionColorTexture>> groups, int material)
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

    private static void AddTriangle(
        List<VertexPositionColorTexture> vertices,
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Color ca,
        Color cb,
        Color cc,
            PristonTexLink texLink)
    {
        var ta = texLink.A;
        var tb = texLink.B;
        var tc = texLink.C;
        vertices.Add(new VertexPositionColorTexture(a, ca, ta));
        vertices.Add(new VertexPositionColorTexture(b, cb, tb));
        vertices.Add(new VertexPositionColorTexture(c, cc, tc));
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
        _glowRenderer.Dispose();
        _lampShadows.Dispose();
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
            var defaultCount = Math.Min(Animator.ObjectCount, PristonEffectPipeline.MaxBones);
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

            Motion = motion;
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
