using System.Threading.Channels;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using PristonTale.Client.Net;
using PristonTale.Client.Net.Presence;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Physics;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.Combat;
using PristonTale.Gameplay.Audio;
using PristonTale.Gameplay.World.Remote;
using PristonTale.Gameplay.World.Streaming;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Physics;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Motion;
using NumVector3 = System.Numerics.Vector3;

namespace PristonTale.Gameplay.World;

public sealed class GameScene : IDisposable, IFieldContext
{
    private ActorHandle _playerBodyActor = ActorHandle.None;
    private ActorHandle _playerHeadActor = ActorHandle.None;
    private FieldDef _currentField;

    private readonly GraphicsDevice _graphicsDevice;
    private readonly ContentManager _content;
    private readonly IClientLog _log;
    private readonly PristonAssetCache _assetCache = new();
    private readonly PristonSmdReader _reader;
    private readonly PristonActorLoader _actorLoader;
    private readonly WorldCatalog _worldCatalog = new();
    private readonly CharacterClassRegistry _classRegistry = new();
    private PlayerCharacter _player;
    private PristonRenderer _renderer;
    private PlayerActorComposer _playerComposer;
    private GroundLootPresentation _groundLoot;
    private GroundLootInteraction _lootInteraction;
    private FloorHeightQuery _lootFloor;
    private PlayerEquipmentPresentation _equipmentPresentation;
    private ModelActorComposer _monsterComposer;
    private PristonCamera _camera;
    private readonly PristonTale.Gameplay.Effects.LegacyEffectLibrary _effectLibrary;
    private readonly PristonTale.Effects.EffectsEngine _effects;
    private readonly FieldAudioController _fieldAudio;
    private readonly NpcAudioController _npcAudio;
    private PristonTale.Gameplay.Effects.LegacyMeteorEffects _meteors;
    private PristonTale.Gameplay.Effects.LegacyMonsterMeshEffects _monsterMeshEffects;
    private PristonTale.Gameplay.Effects.LegacyReturnEffect _returnEffect;
    private readonly List<LoadedField> _fields = [];
    private readonly FieldCollisionQuery _fieldCollision;
    private readonly List<EntityNeighbour> _entityNeighbours = [];
    private LoadedField _activeField;
    private string _assetRoot;
    private PhysicsBody _playerBody;
    private ActorMotionController _playerController = new();
    private bool _runMode;
    private int _selfHp;
    private int _selfMaxHp;
    private Vector3 _playerForward = Vector3.Forward;
    private MouseState _lastMouseState;
    private Vector2 _moveTarget;
    private bool _hasMoveTarget;

    private int? _serverMoveSpeed;
    private float _serverAttackRate = 1f;
    private Vector2 _positionCorrection;
    private readonly GroundFloorQuery _groundFloorQuery;
    private int? _snapshotField;

    private float MovementSpeed(bool running)
    {
        var moveSpeed = _serverMoveSpeed ?? _player?.Derived.MoveSpeed ?? 1;
        var inVillage = _activeField?.Definition.State == FieldState.Village;
        return running ? Locomotion.RunUnitsPerSecond(moveSpeed) : Locomotion.WalkUnitsPerSecond(moveSpeed, inVillage);
    }

    private const float PlayerRadius = 24f;
    private const float BodyProbeHeight = 30f;
    private const float WallFloorThreshold = 0.25f;
    private const double DoubleClickMs = 350.0;
    private double _lastLeftClickMs = double.NegativeInfinity;
    private bool _moveTargetRun;

    private readonly AttackInputController _attackInput = new();
    private readonly TargetSelection _targetSelection = new();
    private readonly TargetLivenessQuery _targetLiveness;
    private bool _selfAttacking;

    private RemoteEntityManager _remote;
    // Catalog handoff and presence draining keep renderer access on the game thread.
    private volatile PristonTale.Client.Net.WorldCatalog.WorldRenderCatalog _worldRenderCatalog =
        PristonTale.Client.Net.WorldCatalog.WorldRenderCatalog.Empty;
    private volatile PristonTale.Client.Net.WorldCatalog.WorldRenderCatalog _pendingCatalog;
    private IPlayChannel _playChannel;
    private CancellationTokenSource _presenceCts;
    private Task _presenceReader;
    private readonly Channel<PresenceUpdate> _presenceQueue = Channel.CreateBounded<PresenceUpdate>(
        new BoundedChannelOptions(4096)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait
        });
    // TRANSPLAYDATA cadence: the original sends every 64 frames; moving avatars report at 5 Hz for smoother peers.
    private const double MovingReportMs = 200;
    private const double IdleReportMs = 1000;
    private double _reportClockMs;
    private double _lastReportMs = double.NegativeInfinity;
    private bool _reportDue;
    private bool _lastReportMoving;
    private bool _lastReportRunning;
    private uint _lastReportTarget;
    private int _lastSentFieldCode = -1;

    private readonly PristonSkyLibrary _sky;
    private readonly PresentationSettings _presentation;

    public GameScene(GraphicsDevice graphicsDevice, ContentManager content, IClientLog log = null, PristonSkyLibrary sky = null,
        PristonTale.Gameplay.Effects.LegacyEffectLibrary effects = null, PresentationSettings presentation = null, PristonAudioLibrary audio = null)
    {
        _presentation = presentation;
        _sky = sky;
        _effectLibrary = effects;
        if (effects is not null)
        {
            _effects = new PristonTale.Effects.EffectsEngine(graphicsDevice, content);
        }
        if (audio is not null)
        {
            _fieldAudio = new FieldAudioController(audio);
            _npcAudio = new NpcAudioController(audio);
        }
        _groundFloorQuery = TryGetFloorHeightNear;
        _targetLiveness = QueryTargetLiveness;
        _fieldCollision = new FieldCollisionQuery(_fields);
        _graphicsDevice = graphicsDevice;
        _content = content;
        _log = log ?? ConsoleClientLog.Instance;
        _reader = new PristonSmdReader(_assetCache);
        _actorLoader = new PristonActorLoader(_assetCache);
    }

    public string Status { get; private set; } = "Not loaded";

    public PlayerCharacter Player => _player;
    public OwnerState Owner { get; private set; }
    public bool PointerCaptured { get; set; }
    public bool CameraInputEnabled { get; set; } = true;
    public bool RightAttackEnabled { get; set; }
    public uint AttackTargetId => _attackInput.TargetId;
    public uint SelectedTargetId => _targetSelection.FrameId;
    public FieldDef ActiveFieldDefinition => _activeField?.Definition;
    public PristonIntRectangle ActiveMapRectangle => _activeField?.Stage.AuthoredMapRectangle ?? default;
    public Vector3 PlayerPosition => _playerBody?.Position ?? Vector3.Zero;
    public Vector3 PlayerForward => _playerForward;
    public bool RunMode => _runMode;
    public event Action<PresenceUpdate> PresenceObserved;

    public Task<PristonTale.Simulation.Items.InventoryOutcome> ApplyInventoryAsync(PristonTale.Simulation.Items.InventoryCommand command)
    {
        return _playChannel is not null ? _playChannel.ApplyInventoryAsync(command, _presenceCts.Token)
            : Task.FromResult(new PristonTale.Simulation.Items.InventoryOutcome(false, 0, "world connection unavailable"));
    }
    public event Action<uint> NpcSelected;
    private uint _npcTargetId;
    private bool _npcMoveSent;
    private Vector2 _serverPlanarPosition;
    private float _serverCollisionHeight;

    public Task<PristonTale.Simulation.Progression.ProgressionOutcome> ApplyProgressionAsync(PristonTale.Simulation.Progression.ProgressionCommand command)
    {
        return _playChannel is not null ? _playChannel.ApplyProgressionAsync(command, _presenceCts.Token)
            : Task.FromResult(new PristonTale.Simulation.Progression.ProgressionOutcome(false, 0, "world connection unavailable"));
    }

    public Task<PristonTale.Simulation.World.Npcs.NpcInteraction> InteractNpcAsync(uint entityId)
    {
        return _playChannel is not null ? _playChannel.InteractNpcAsync(entityId, _presenceCts.Token)
            : Task.FromResult(PristonTale.Simulation.World.Npcs.NpcInteraction.Rejected("world connection unavailable"));
    }

    public void PlayNpcInteractionAudio(PristonTale.Simulation.World.Npcs.NpcInteraction interaction)
    {
        if (_npcAudio is null || _remote is null || _playerBody is null
            || !_remote.TryGetNpcAudioSource(interaction.EntityId, out var soundCode, out var sourcePosition))
        {
            return;
        }

        _npcAudio.PlayInteraction(interaction, soundCode, sourcePosition, _playerBody.Position);
    }

    public void StopNpcTalk()
    {
        _npcAudio?.StopTalk();
    }

    public PristonRenderStats LastRenderStats => _renderer?.LastFrameStats ?? default;

    public float TimeOfDay => _renderer?.TimeOfDay ?? 0f;
    public Color SkyColor => _renderer?.SkyColor ?? new Color(72, 92, 116);
    public int StageLightCount => _renderer?.StageLightCount ?? 0;

    public float DayLengthSeconds
    {
        get => _renderer?.DayLengthSeconds ?? 0f;
        set { if (_renderer != null) _renderer.DayLengthSeconds = value; }
    }

    public void AdvanceTimeOfDay(float hours) => _renderer?.AdvanceTimeOfDay(hours);
    public void SetTimeOfDay(float hours) => _renderer?.SetTimeOfDay(hours);

    public bool ShadowsEnabled => _renderer?.ShadowsEnabled ?? false;

    public void ToggleShadows()
    {
        if (_renderer != null)
            _renderer.ShadowsEnabled = !_renderer.ShadowsEnabled;
    }

    public PristonDebugView RenderDebugView => _renderer?.DebugView ?? PristonDebugView.Shaded;

    public void CycleRenderDebugView()
    {
        if (_renderer != null)
        {
            var next = ((int)_renderer.DebugView + 1) % Enum.GetValues<PristonDebugView>().Length;
            _renderer.DebugView = (PristonDebugView)next;
        }
    }

    public void LoadEntered(string assetRoot, EnteredCharacter entered)
    {
        if (!_worldCatalog.TryGetFieldByCode(entered.FieldCode, out var field))
            field = _worldCatalog.Ricarten;

        LoadField(assetRoot, field, entered);
    }

    public void LoadField(string assetRoot, FieldDef field, EnteredCharacter entered = null)
    {
        _assetRoot = assetRoot;
        EnsureRenderer();

        _fieldStreamer?.Dispose();
        _fieldStreamer = new FieldResourceStreamer(assetRoot, _reader, _renderer, _log, ConfigureFieldLighting);
        UnloadAllFields();

        var loaded = AddFieldToWorld(assetRoot, field);
        SetActiveField(loaded);

        _player = entered != null
            ? _classRegistry.CreatePlayer(entered.Class, entered.Name, entered.Level)
            : _classRegistry.CreatePlayer(PlayerClass.Mechanician, "Player");
        var d = _player.Derived;
        _log.Info(
            $"[Player] {_player.Name} ({_player.Class.Name}) Lv{_player.Level}  " +
            $"HP {_player.Life}/{d.MaxLife}  MP {_player.Mana}/{d.MaxMana}  SP {_player.Stamina}/{d.MaxStamina}  " +
            $"Dmg {d.AttackDamageMin}-{d.AttackDamageMax}  AR {d.AttackRating}  Def {d.Defence}  " +
            $"Abs {d.Absorption}  Wt {d.MaxWeight}  Spd {d.MoveSpeed}");
        _playerController = new ActorMotionController();
        _runMode = false;
        _selfHp = _player.Life;
        _selfMaxHp = d.MaxLife;
        var spawn = entered != null && (entered.SpawnX != 0f || entered.SpawnZ != 0f)
            ? new Vector2(entered.SpawnX, -entered.SpawnZ)
            : SpawnPlanarPosition(field);
        _playerBody = new PhysicsBody(spawn) { Radius = PlayerRadius };
        UpdateBodyHeight(_playerBody);
        _playerForward = Vector3.Forward;

        _renderer.ClearActors();
        AddPlayerActors(assetRoot, entered?.FaceIndex ?? 0, entered?.Tier ?? 0, entered?.HairIndex ?? 0);

        _camera = new PristonCamera(_playerBody.Position, _playerForward, ToCameraSettings(field.Camera));
        Status = $"Loaded {field.Name}: {loaded.Stage.Vertices.Count} vertices, {loaded.Stage.Faces.Count} faces, " +
                 $"{field.StageObjectSmds.Count} animation SMDs + {_player.Name}";
        ScanStreaming();
    }

    private void EnsureRenderer()
    {
        if (_renderer != null)
            return;
        _renderer = new PristonRenderer(_graphicsDevice, _content, _log, _presentation) { Sky = _sky };
        if (_effects is not null)
        {
            _meteors = new PristonTale.Gameplay.Effects.LegacyMeteorEffects(_effectLibrary, _effects, _renderer.TransientLights, _fieldCollision);
            _monsterMeshEffects = new PristonTale.Gameplay.Effects.LegacyMonsterMeshEffects(_effectLibrary, _effects, _renderer.TransientLights);
            _returnEffect = new PristonTale.Gameplay.Effects.LegacyReturnEffect(_graphicsDevice, _effectLibrary, _renderer.TransientLights);
        }
        var actorScene = new RendererActorScene(_renderer);
        _playerComposer = new PlayerActorComposer(actorScene, _actorLoader);
        _monsterComposer = new ModelActorComposer(actorScene, _actorLoader);
        _groundLoot = new GroundLootPresentation(actorScene, _actorLoader, _assetRoot, _log);
        _lootInteraction = new GroundLootInteraction(ApplyInventoryAsync, _log);
        _lootFloor = LootFloorAt;
    }

    private FieldResourceStreamer _fieldStreamer;

    private void RequestField(FieldDef field)
    {
        if (!IsLoaded(field.Id))
        {
            _fieldStreamer.Request(field);
        }
    }

    private LoadedField AddFieldToWorld(string assetRoot, FieldDef field)
    {
        var existing = _fields.FirstOrDefault(f => f.Definition.Id == field.Id);
        if (existing != null)
            return existing;

        var loaded = LoadFieldResources(assetRoot, field);
        _fields.Add(loaded);
        _groundLoot?.InvalidateFloor();
        return loaded;
    }

    private LoadedField LoadFieldResources(string assetRoot, FieldDef field)
    {
        var stage = _reader.ReadStage(AssetPath(assetRoot, field.StageSmd));
        var animationModels = field.StageObjectSmds
            .Select(path => AssetPath(assetRoot, path))
            .Select(_reader.ReadModel)
            .ToArray();
        var collision = new PristonCollisionWorld(stage);
        ConfigureFieldLighting(field.Lighting);
        var handle = _renderer.AddStage(stage, animationModels);
        return new LoadedField(field, stage, collision, handle);
    }

    private void UnloadAllFields()
    {
        foreach (var field in _fields)
            _renderer.RemoveStage(field.RenderHandle);
        _fields.Clear();
        _groundLoot?.InvalidateFloor();
        _activeField = null;
    }

    private void AddPlayerActors(string assetRoot, int faceIndex, int tier, int hairIndex)
    {
        var visuals = CharacterHead.Apply(_player.Definition.DefaultVisuals, _player.Class.Class, tier, faceIndex, hairIndex);
        var handles = _playerComposer.Compose(
            assetRoot,
            visuals,
            faceIndex,
            PlayerWorld(),
            _playerController.Current);
        _equipmentPresentation = new PlayerEquipmentPresentation(_playerComposer, new RendererActorScene(_renderer), _log,
            assetRoot, visuals, _player.Definition.MotionJobMask, handles);
        _playerBodyActor = handles.Body;
        _playerHeadActor = handles.Head;
    }

    public void SetWorldRenderCatalog(PristonTale.Client.Net.WorldCatalog.WorldRenderCatalog catalog)
    {
        if (catalog == null)
            return;
        _worldRenderCatalog = catalog;
        _pendingCatalog = catalog;
    }

    public void BeginRemotePresence(IPlayChannel channel, EnteredCharacter entered)
    {
        if (channel == null)
        {
            throw new ArgumentNullException(nameof(channel));
        }

        if (_renderer == null)
        {
            throw new InvalidOperationException("BeginRemotePresence requires a loaded field (call LoadEntered first).");
        }

        if (_remote != null)
        {
            return;
        }

        _ = entered;
        _remote = new RemoteEntityManager(
            new RendererActorScene(_renderer),
            _playerComposer,
            _monsterComposer,
            _classRegistry,
            _assetRoot,
            _worldRenderCatalog,
            _log);
        _remote.SetSelfSink(OnSelfSpawn, OnSelfState, OnSelfCombatEvent);
        _remote.NpcMotionStarted += OnNpcMotionStarted;
        _renderer.RequirePreparedTextures();
        _playChannel = channel;
        _lastSentFieldCode = entered.FieldCode;
        _presenceCts = new CancellationTokenSource();
        _presenceReader = Task.Run(() => ReadPresenceAsync(channel, _presenceCts.Token));
    }

    private async Task ReadPresenceAsync(IPlayChannel channel, CancellationToken ct)
    {
        try
        {
            await foreach (var update in channel.ReadAllAsync(ct))
            {
                await _presenceQueue.Writer.WriteAsync(update, ct);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _log.Warn($"[Remote] presence reader ended: {ex.Message}");
        }
    }

    private void DrainPresence(float dt)
    {
        if (_remote == null)
        {
            return;
        }

        var pending = _pendingCatalog;
        if (pending != null)
        {
            _pendingCatalog = null;
            _remote.SetCatalog(pending);
        }

        try
        {
            for (var processed = 0; processed < 64 && _presenceQueue.Reader.TryRead(out var update); processed++)
            {
                _snapshotField = update.FieldId;
                if (update.Owner is not null)
                {
                    Owner = update.Owner;
                }
                var remapped = RemapToWorld(update);
                _remote.Apply(remapped);
                if (remapped.Warp is { } warp)
                {
                    ApplyWarp(warp);
                }

                _groundLoot.Apply(update);
                ObserveEffects(update);
                PresenceObserved?.Invoke(update);
            }

            _remote.Update(dt);
            _groundLoot.Update(_lootFloor);
        }
        catch (Exception ex)
        {
            _log.Error($"[Remote] presence drain failed: {ex}");
        }
    }

    private static PresenceUpdate RemapToWorld(PresenceUpdate u)
    {
        // Transport uses PT-global (x, z); presentation uses (x, -z).
        List<EntitySpawnInfo> spawns = null;
        if (u.Spawns.Count > 0)
        {
            spawns = new List<EntitySpawnInfo>(u.Spawns.Count);
            for (var i = 0; i < u.Spawns.Count; i++)
            {
                var s = u.Spawns[i];
                spawns.Add(s with { Position = FlipZ(s.Position), Facing = FlipFacing(s.Facing) });
            }
        }

        List<EntityStateInfo> states = null;
        if (u.States.Count > 0)
        {
            states = new List<EntityStateInfo>(u.States.Count);
            for (var i = 0; i < u.States.Count; i++)
            {
                var st = u.States[i];
                states.Add(st with { Position = FlipZ(st.Position), Facing = FlipFacing(st.Facing) });
            }
        }

        return new PresenceUpdate(
            u.ServerTick,
            spawns ?? u.Spawns,
            states ?? u.States,
            u.Removed,
            u.Events,
            u.FieldId,
            u.Owner)
        {
            Warp = u.Warp is { } warp ? warp with { Position = FlipZ(warp.Position) } : null,
            ServerTimeMs = u.ServerTimeMs
        };
    }

    private static NumVector3 FlipZ(NumVector3 v) => new(v.X, v.Y, -v.Z);

    private static float FlipFacing(float f) => MathF.Atan2(MathF.Sin(f), -MathF.Cos(f));

    private void TearDownRemotePresence()
    {
        if (_remote is not null)
        {
            _remote.NpcMotionStarted -= OnNpcMotionStarted;
        }

        _ = ClosePresenceAsync(_playChannel, _presenceReader, _presenceCts);
        _remote?.Dispose();
        _remote = null;
        _playChannel = null;
        _presenceReader = null;
        _presenceCts = null;
        _lastSentFieldCode = -1;
        _selfAttacking = false;
        _attackInput.Cancel();
        _targetSelection.Clear();
        _meteors?.Clear();
        _monsterMeshEffects?.Clear();
        _renderer?.TransientLights.Clear();
        _effects?.Clear();
        while (_presenceQueue.Reader.TryRead(out _))
        {
        }
    }

    private async Task ClosePresenceAsync(IPlayChannel channel, Task reader, CancellationTokenSource cancellation)
    {
        try
        {
            if (cancellation is not null)
            {
                await cancellation.CancelAsync();
            }

            if (channel is not null)
            {
                await channel.DisposeAsync();
            }

            if (reader is not null)
            {
                await reader;
            }
        }
        catch (Exception ex)
        {
            _log.Error($"[Remote] asynchronous shutdown failed: {ex}");
        }
        finally
        {
            cancellation?.Dispose();
        }
    }

    private int _streamScanCounter;

    private void UpdateStreaming()
    {
        if (_activeField == null || _assetRoot == null || _playerBody == null)
        {
            return;
        }

        UpdateActiveField();

        if (++_streamScanCounter < FieldStreamingPolicy.ScanIntervalFrames)
        {
            return;
        }

        _streamScanCounter = 0;
        ScanStreaming();
    }

    private void ScanStreaming()
    {
        var active = _activeField.Definition;
        var player = _playerBody.PlanarPosition;
        Vector2? movementTarget = _hasMoveTarget ? _moveTarget : null;
        var movementSpeed = _hasMoveTarget ? MovementSpeed(_moveTargetRun) : 0f;
        var disconnect = FieldStreamingPolicy.RetentionDistance(movementSpeed);
        foreach (var neighbour in _worldCatalog.Neighbours(active))
        {
            if (IsLoaded(neighbour.Id))
            {
                continue;
            }

            if (TryGateSeam(active, neighbour, out var seam)
                && FieldStreamingPolicy.ShouldPrefetch(player, movementTarget, seam, movementSpeed))
            {
                RequestField(neighbour);
            }
        }

        foreach (var warp in active.WarpGates)
        {
            var point = new Vector2(warp.X, -warp.Z);
            if (warp.SpecialEffect != 2
                && FieldStreamingPolicy.ShouldPrefetch(player, movementTarget, point, movementSpeed)
                && _worldCatalog.TryGetField(warp.DestinationFieldId, out var destination))
            {
                RequestField(destination);
            }
        }

        for (var i = _fields.Count - 1; i >= 0; i--)
        {
            var field = _fields[i];
            if (field == _activeField)
            {
                continue;
            }

            var keep = TryGateSeam(active, field.Definition, out var seam) && Vector2.DistanceSquared(player, seam) < disconnect * disconnect;
            foreach (var warp in active.WarpGates)
            {
                if (warp.DestinationFieldId == field.Definition.Id
                    && Vector2.DistanceSquared(player, new Vector2(warp.X, -warp.Z)) < disconnect * disconnect)
                {
                    keep = true;
                }
            }

            if (!keep)
            {
                _log.Info($"[Stream] unload {field.Definition.Id} (walked away from {active.Id}).");
                UnloadField(field);
            }
        }
    }

    private void UpdateActiveField()
    {
        var position = _playerBody.Position;
        LoadedField best = null;
        var bestHeight = float.MinValue;
        foreach (var field in _fields)
        {
            if (field.Collision.TryGetFloorHeight(position, position.Y + GroundTraversal.VisualOffset, GroundTraversal.StepUp, out var height)
                && height > bestHeight)
            {
                bestHeight = height;
                best = field;
            }
        }

        if (best != null)
        {
            SetActiveField(best);
        }
    }

    private void SetActiveField(LoadedField field)
    {
        if (_activeField == field)
            return;
        _activeField = field;
        _currentField = field.Definition;
        _renderer.Underground = field.Definition.Underground;
        if (!field.Definition.Underground)
        {
            var backdrop = field.Definition.Backdrop;
            _renderer.SkyProfiles = new PristonSkyProfiles(
                PristonSkyProfiles.Resolve(backdrop.Day),
                PristonSkyProfiles.Resolve(backdrop.Glow),
                PristonSkyProfiles.Resolve(backdrop.Night));
        }

        var ambient = field.Definition.Lighting.Ambient;
        _renderer.UndergroundAmbient = new Vector3(ambient.R, ambient.G, ambient.B);
        _renderer.ConfigureDayNight(ToDayNightSettings(field.Definition.DayNight));
        _fieldAudio?.SelectField(field.Definition);
        field.Definition.OnLoaded?.Invoke(this);
    }

    private bool IsLoaded(string fieldId) => _fields.Any(f => f.Definition.Id == fieldId);

    private void UnloadField(LoadedField field)
    {
        _renderer.RemoveStage(field.RenderHandle);
        _fields.Remove(field);
        _groundLoot?.InvalidateFloor();
    }

    private static bool TryGateSeam(FieldDef a, FieldDef b, out Vector2 seam)
    {
        foreach (var gate in a.Gates)
        {
            if (gate.DestinationFieldId == b.Id)
            {
                seam = GatePlanar(gate);
                return true;
            }
        }

        foreach (var gate in b.Gates)
        {
            if (gate.DestinationFieldId == a.Id)
            {
                seam = GatePlanar(gate);
                return true;
            }
        }

        seam = default;
        return false;
    }

    private static Vector2 GatePlanar(FieldGate gate) => new(gate.X, -gate.Z);

    private const float WarpCooldownSeconds = 1.5f;
    private const float SpecialWarpDelaySeconds = 2f;
    private float _warpCooldown;
    private FieldWarpGate _pendingWarp;
    private float _pendingWarpDelay;

    private void UpdateWarps(float dtSeconds)
    {
        if (_pendingWarp is not null)
        {
            HoldAtSpecialWarp(_pendingWarp);
            _pendingWarpDelay = MathF.Max(0f, _pendingWarpDelay - dtSeconds);
            if (_pendingWarpDelay <= 0f && Warp(_pendingWarp))
            {
                _pendingWarp = null;
                _warpCooldown = WarpCooldownSeconds;
                _returnEffect?.Clear();
            }

            return;
        }

        if (_warpCooldown > 0f)
        {
            _warpCooldown -= dtSeconds;
            return;
        }

        if (_activeField == null || _playerBody == null)
        {
            return;
        }

        var player = _playerBody.PlanarPosition;
        foreach (var warp in _activeField.Definition.WarpGates)
        {
            if (warp.SpecialEffect == 2 || warp.DestinationFieldId == _activeField.Definition.Id)
            {
                continue;
            }

            if (_player != null && _player.Level < warp.LimitLevel)
            {
                continue;
            }

            if (!_worldCatalog.TryGetField(warp.DestinationFieldId, out _))
            {
                continue;
            }

            var center = new Vector2(warp.X, -warp.Z);
            var radius = MathF.Max(warp.Width, 48f);
            var playerFeet = _playerBody.Height + GroundTraversal.VisualOffset;
            var withinHeight = warp.Y == 0 || MathF.Abs(playerFeet - warp.Y) < warp.Height;
            if (withinHeight && Vector2.DistanceSquared(player, center) <= radius * radius)
            {
                if (warp.SpecialEffect == 1)
                {
                    BeginSpecialWarp(warp);
                    return;
                }

                if (Warp(warp))
                {
                    _warpCooldown = WarpCooldownSeconds;
                }

                return;
            }
        }
    }

    private void BeginSpecialWarp(FieldWarpGate warp)
    {
        _pendingWarp = warp;
        _pendingWarpDelay = SpecialWarpDelaySeconds;
        ClearAttackTarget();
        _lootInteraction?.Cancel();
        StopPredictedMovement();
        HoldAtSpecialWarp(warp);
        _reportDue = true;
        _returnEffect?.Start(_playerBody.Position + Vector3.Up * GroundTraversal.VisualOffset);
        _fieldAudio?.PlayPortalWarp();
        RequestField(_worldCatalog.GetField(warp.DestinationFieldId));
    }

    private void HoldAtSpecialWarp(FieldWarpGate warp)
    {
        _playerBody.PlanarPosition = new Vector2(warp.X, -warp.Z);
        _playerBody.Height = warp.Y - GroundTraversal.VisualOffset;
        ApplyPlayerWorld();
    }

    private bool Warp(FieldWarpGate warp)
    {
        var destDef = _worldCatalog.GetField(warp.DestinationFieldId);
        var dest = _fields.FirstOrDefault(field => field.Definition.Id == destDef.Id);
        if (dest is null)
        {
            RequestField(destDef);
            return false;
        }

        for (var i = _fields.Count - 1; i >= 0; i--)
        {
            if (_fields[i] != dest)
            {
                UnloadField(_fields[i]);
            }
        }

        SetActiveField(dest);
        _hasMoveTarget = false;
        _moveTargetRun = false;
        _playerBody.PlanarPosition = new Vector2(warp.DestinationX, -warp.DestinationZ);
        _playerBody.Height = warp.DestinationY - GroundTraversal.VisualOffset;
        ApplyPlayerWorld();
        _camera = new PristonCamera(_playerBody.Position, _playerForward, ToCameraSettings(destDef.Camera));
        _log.Info($"[Warp] -> {destDef.Id} at ({warp.DestinationX},{warp.DestinationZ}).");
        return true;
    }

    private void ConfigureFieldLighting(FieldLighting lighting)
    {
        if (_renderer == null)
            return;

        PristonPointLight[] overrides = null;
        if (lighting.Lights.Count > 0)
        {
            overrides = new PristonPointLight[lighting.Lights.Count];
            for (var i = 0; i < lighting.Lights.Count; i++)
            {
                var l = lighting.Lights[i];
                overrides[i] = new PristonPointLight(
                    new Vector3(l.X, l.Y, l.Z),
                    l.Range,
                    new Vector3(l.R / 255f, l.G / 255f, l.B / 255f),
                    l.Intensity);
            }
        }

        _renderer.ConfigureFieldLighting(lighting.LampIntensity, lighting.LampRangeScale, overrides, lighting.GlowIntensity, lighting.GlowScale);
    }

    private static PristonCameraSettings ToCameraSettings(FieldCamera c) => new()
    {
        DefaultDistance = c.Distance,
        MinDistance = c.MinDistance,
        MaxDistance = c.MaxDistance,
        DefaultPitch = c.Pitch,
        FieldOfViewDegrees = c.FieldOfViewDegrees,
        TargetHeight = c.TargetHeight,
    };

    private static DayNightSettings ToDayNightSettings(FieldDayNight d)
    {
        if (d.Gradient.Count == 0)
            return new DayNightSettings { DayLengthSeconds = d.DayLengthSeconds, StartTimeOfDay = d.StartTimeOfDay };

        var keys = new DayNightKey[d.Gradient.Count];
        for (var i = 0; i < keys.Length; i++)
        {
            var k = d.Gradient[i];
            keys[i] = new DayNightKey(k.Hour,
                new Vector3(k.Sun.R, k.Sun.G, k.Sun.B),
                new Vector3(k.Ambient.R, k.Ambient.G, k.Ambient.B),
                new Vector3(k.Sky.R, k.Sky.G, k.Sky.B));
        }
        return new DayNightSettings { Gradient = keys, DayLengthSeconds = d.DayLengthSeconds, StartTimeOfDay = d.StartTimeOfDay };
    }

    private void SetPlayerMotion(PristonActorMotion motion)
    {
        if (_renderer == null || !_playerBodyActor.IsValid)
            return;
        _renderer.SetActorMotion(_playerBodyActor, motion);
        _renderer.SetActorMotion(_playerHeadActor, motion);
        var rate = PlayerRateFor(motion);
        _renderer.SetActorPlaybackRate(_playerBodyActor, rate);
        _renderer.SetActorPlaybackRate(_playerHeadActor, rate);
    }

    private void RestartPlayerMotion(PristonActorMotion motion)
    {
        if (_renderer == null || !_playerBodyActor.IsValid)
            return;
        _renderer.RestartActorMotion(_playerBodyActor, motion);
        _renderer.RestartActorMotion(_playerHeadActor, motion);
        var rate = PlayerRateFor(motion);
        _renderer.SetActorPlaybackRate(_playerBodyActor, rate);
        _renderer.SetActorPlaybackRate(_playerHeadActor, rate);
    }

    private void ApplyPlayerCommand(MotionCommand command)
    {
        if (command.Kind == MotionCommandKind.None)
            return;
        if (command.Kind == MotionCommandKind.Restart)
            RestartPlayerMotion(command.Motion);
        else
            SetPlayerMotion(command.Motion);
    }

    public void DebugForceMotion(PristonActorMotion motion)
        => ApplyPlayerCommand(_playerController.ApplyAuthoritative(motion));

    public int PlayerServerHp => _selfHp;
    public int PlayerServerMaxHp => _selfMaxHp;

    private void OnSelfSpawn(EntitySpawnInfo spawn)
    {
        _selfMaxHp = spawn.MaxHp;
        OnSelfState(new EntityStateInfo(spawn.EntityId, spawn.Position, spawn.Facing, spawn.MoveState,
            spawn.Hp, spawn.MoveSpeed, spawn.AttackRate, spawn.AttackTargetId, spawn.AttackSequence));
    }

    private void OnSelfState(EntityStateInfo state)
    {
        _attackInput.Reconcile(state.AttackSequence, state.AttackTargetId);
        _selfAttacking = state.MoveState == RemoteMoveState.Attack;
        int previousHp = _selfHp;
        _selfHp = state.Hp;
        _serverMoveSpeed = state.MoveSpeed;
        _serverAttackRate = state.AttackRate;
        // The client owns locomotion; server echoes lag behind it, so only WARPFIELD moves the local avatar.
        _serverPlanarPosition = new Vector2(state.Position.X, state.Position.Z);
        _serverCollisionHeight = state.Position.Y;
        var wire = RemoteEntityBase.ToActorMotion(state.MoveState);
        bool wasDead = _playerController.IsDead;
        switch (wire)
        {
            case PristonActorMotion.Eat:
                ApplyPlayerCommand(_playerController.ApplyAuthoritative(PristonActorMotion.Eat));
                break;

            case PristonActorMotion.Attack:
                ApplyPlayerCommand(_playerController.ApplyAuthoritative(PristonActorMotion.Attack));
                break;

            case PristonActorMotion.Damage:
                {
                    var command = _playerController.ApplyAuthoritative(PristonActorMotion.Damage);
                    if (command.Kind == MotionCommandKind.None && state.Hp < previousHp)
                    {
                        command = _playerController.Retrigger(PristonActorMotion.Damage);
                    }

                    ApplyPlayerCommand(command);
                    break;
                }

            case PristonActorMotion.Dead:
                if (!wasDead)
                {
                    ApplyPlayerCommand(_playerController.ApplyAuthoritative(PristonActorMotion.Dead));
                    ClearAttackTarget();
                    _hasMoveTarget = false;
                    _moveTargetRun = false;
                }

                break;
            default:
                if (wasDead)
                {
                    RespawnAt(state.Position);
                }
                else if (_playerController.Current is PristonActorMotion.Damage or PristonActorMotion.Attack or PristonActorMotion.Eat)
                {
                    ApplyPlayerCommand(_playerController.ApplyAuthoritative(PristonActorMotion.Stand));
                }

                break;
        }
    }

    private void ApplyWarp(WarpInfo warp)
    {
        _serverPlanarPosition = new Vector2(warp.Position.X, warp.Position.Z);
        if (_playerBody is null)
        {
            return;
        }

        if (_activeField?.Definition.FieldCode != warp.FieldId && _worldCatalog.TryGetFieldByCode(warp.FieldId, out var field))
        {
            var loaded = _fields.FirstOrDefault(candidate => candidate.Definition.Id == field.Id);
            if (loaded is null)
            {
                RequestField(field);
                return;
            }

            SetActiveField(loaded);
            _lastSentFieldCode = field.FieldCode;
            _hasMoveTarget = false;
        }

        var authoritative = new Vector2(warp.Position.X, warp.Position.Z);
        var difference = authoritative - _playerBody.PlanarPosition;
        if (warp.Forced || difference.LengthSquared() > 200f * 200f)
        {
            _log.Info($"[Movement] correction {difference.Length():F1} units; field={warp.FieldId}, forced={warp.Forced}.");
            StopPredictedMovement();
            _playerBody.PlanarPosition = authoritative;
            _positionCorrection = Vector2.Zero;
            TrackBodyHeight(_playerBody);
            ApplyPlayerWorld();
            _camera?.SnapFollow(_playerBody.Position, _fieldCollision);
            _reportDue = true;
        }
    }

    private void ApplyPositionCorrection(float dt)
    {
        if (_playerBody is null || _positionCorrection.LengthSquared() <= 0.0001f)
        {
            return;
        }

        var step = _positionCorrection * (1f - MathF.Exp(-12f * dt));
        _playerBody.PlanarPosition += step;
        _positionCorrection -= step;
        TrackBodyHeight(_playerBody);
        ApplyPlayerWorld();
    }

    private void OnSelfCombatEvent(CombatEventInfo combatEvent)
    {
        if (combatEvent.Kind == RemoteCombatEventKind.PotionBegin)
        {
            ApplyPlayerCommand(_playerController.Retrigger(PristonActorMotion.Eat));
        }
        else if (combatEvent.Kind == RemoteCombatEventKind.SwingBegin && !_playerController.LocomotionFrozen)
        {
            _selfAttacking = true;
            ApplyPlayerCommand(_playerController.Retrigger(PristonActorMotion.Attack));
        }
        else if (combatEvent.Kind == RemoteCombatEventKind.Hit && !combatEvent.Killed && _playerController.Current == PristonActorMotion.Damage)
        {
            ApplyPlayerCommand(_playerController.Retrigger(PristonActorMotion.Damage));
        }
    }

    private void RespawnAt(NumVector3 worldPosition)
    {
        _hasMoveTarget = false;
        _playerBody.PlanarPosition = new Vector2(worldPosition.X, worldPosition.Z);
        UpdateBodyHeight(_playerBody);
        ApplyPlayerWorld();
        _camera?.SnapFollow(_playerBody.Position, _fieldCollision);
        ApplyPlayerCommand(_playerController.ApplyAuthoritative(PristonActorMotion.Restart));
    }

    private float PlayerRateFor(PristonActorMotion motion)
    {
        if (_player == null)
        {
            return 1f;
        }

        var d = _player.Derived;
        return motion switch
        {
            PristonActorMotion.Walk => AnimationRate.MoveRate(Locomotion.BaseTranslation(_serverMoveSpeed ?? d.MoveSpeed)),
            PristonActorMotion.Run => AnimationRate.MoveRate((Locomotion.BaseTranslation(_serverMoveSpeed ?? d.MoveSpeed) * 460) >> 8),
            PristonActorMotion.Attack or PristonActorMotion.Skill => _serverMoveSpeed.HasValue ? _serverAttackRate : AnimationRate.AttackRate(d.AttackSpeed),
            _ => 1f,
        };
    }

    void IFieldContext.SetDayLength(float seconds) => DayLengthSeconds = seconds;

    public void Update(GameTime gameTime)
    {
        _npcAudio?.Update();
        if (_equipmentPresentation?.Update(Owner, _activeField?.Definition.State == FieldState.Village, PlayerWorld(), _playerController.Current) == true)
        {
            _playerBodyActor = _equipmentPresentation.Handles.Body;
            _playerHeadActor = _equipmentPresentation.Handles.Head;
        }
        _meteors?.Update((float)gameTime.ElapsedGameTime.TotalSeconds);
        _monsterMeshEffects?.Update((float)gameTime.ElapsedGameTime.TotalSeconds);
        _returnEffect?.Update((float)gameTime.ElapsedGameTime.TotalSeconds);
        _effects?.Update(gameTime);
        var frameDt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        if (_playerBody is not null)
        {
            _fieldAudio?.Update(_playerBody.Position, TimeOfDay, frameDt);
        }
        var completedField = _fieldStreamer?.Update();
        if (completedField is not null)
        {
            _fields.Add(completedField);
            _groundLoot?.InvalidateFloor();
        }
        DrainPresence(frameDt);
        ApplyPositionCorrection(frameDt);
        UpdatePlayerInput(gameTime);
        UpdateTargetSelection(gameTime);
        UpdateWarps((float)gameTime.ElapsedGameTime.TotalSeconds);
        UpdateStreaming();
        if (_camera != null && _activeField != null)
        {
            _camera.Update(gameTime, _graphicsDevice, _playerBody.Position, _playerForward, _fieldCollision, PointerCaptured, CameraInputEnabled);
        }

        if (_renderer != null)
        {
            _renderer.Update(gameTime);
        }

        var dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        UpdateMovementReport(dt);
        _currentField?.OnUpdate?.Invoke(this, dt);
    }

    private void OnNpcMotionStarted(int soundCode, PristonActorMotion motion, Vector3 sourcePosition)
    {
        if (_npcAudio is not null && _playerBody is not null)
        {
            _npcAudio.PlayMotion(soundCode, motion, sourcePosition, _playerBody.Position);
        }
    }

    private void UpdateMovementReport(float dt)
    {
        _reportClockMs += dt * 1000.0;
        if (_playChannel == null || _activeField == null || _playerBody == null)
        {
            return;
        }

        var current = _playerController.Current;
        var moving = current is PristonActorMotion.Walk or PristonActorMotion.Run;
        var running = current == PristonActorMotion.Run;
        var target = _attackInput.TargetId;
        var fieldCode = _activeField.Definition.FieldCode;
        var elapsed = _reportClockMs - _lastReportMs;
        var changed = moving != _lastReportMoving || running != _lastReportRunning || target != _lastReportTarget || fieldCode != _lastSentFieldCode;
        if (!_reportDue && !changed && elapsed < (moving ? MovingReportMs : IdleReportMs))
        {
            return;
        }

        _reportDue = false;
        _lastReportMs = _reportClockMs;
        _lastReportMoving = moving;
        _lastReportRunning = running;
        _lastReportTarget = target;
        _lastSentFieldCode = fieldCode;
        // Transport is PT-global (x, z); presentation uses (x, -z).
        var planar = _playerBody.PlanarPosition;
        var position = new NumVector3(planar.X, _playerBody.Height, -planar.Y);
        var facing = MathF.Atan2(_playerForward.X, -_playerForward.Z);
        SendUpstream(_playChannel.SendMovementAsync(position, facing, moving, running, target, fieldCode, _presenceCts.Token), "movement");
    }

    private void SendUpstream(Task send, string what)
    {
        // Network sends must never block the game loop.
        _ = send.ContinueWith(
            t => _log.Warn($"[Remote] {what} send failed: {t.Exception?.GetBaseException().Message}"),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void ObserveEffects(PresenceUpdate update)
    {
        if (_effects is null)
        {
            return;
        }

        foreach (var effect in update.Events)
        {
            if (effect.Kind == RemoteCombatEventKind.MonsterSkillImpact)
            {
                ObserveMonsterSkillEffect(effect);
                continue;
            }

            if (effect.Kind != RemoteCombatEventKind.PotionApplied || _effects.Instances.Count >= 128)
            {
                continue;
            }

            Vector3 position;
            if (effect.TargetId == _remote.SelfEntityId && _playerBody is not null)
            {
                position = _playerBody.Position;
            }
            else if (!_remote.TryGetDrawnPosition(effect.TargetId, out position))
            {
                continue;
            }

            position.Y += 48f;
            for (var resource = 0; resource < 3 && _effects.Instances.Count < 128; resource++)
            {
                if ((effect.Resources & (1 << resource)) != 0)
                {
                    _effects.Spawn(_effectLibrary.Potion(resource), position);
                }
            }
        }
    }

    private void ObserveMonsterSkillEffect(CombatEventInfo effect)
    {
        if (!_remote.TryGetDrawnPosition(effect.AttackerId, out var caster))
        {
            return;
        }

        var origin = new Vector3(effect.Position.X, caster.Y, -effect.Position.Z);
        if (effect.SourceSoundCode is 0x18A0 or 0x1890)
        {
            Vector3 target;
            if (effect.TargetId == _remote.SelfEntityId && _playerBody is not null)
            {
                target = _playerBody.Position;
            }
            else if (!_remote.TryGetDrawnPosition(effect.TargetId, out target))
            {
                return;
            }

            if (effect.SourceSoundCode == 0x18A0)
            {
                _meteors.Spawn(target);
            }
            else
            {
                _monsterMeshEffects.Spawn(effect.SourceSoundCode, origin, effect.Facing, target);
            }
            return;
        }

        if (effect.SourceSoundCode is 0x19F0 or 0x1840)
        {
            _monsterMeshEffects.Spawn(effect.SourceSoundCode, origin, effect.Facing, Vector3.Zero);
            return;
        }

        if (effect.SourceSoundCode != 0x18E0 || _effects.Instances.Count >= 128)
        {
            return;
        }

        var position = new Vector3(
            effect.Position.X + MathF.Sin(effect.Facing) * 68f,
            caster.Y + 22f,
            -effect.Position.Z - MathF.Cos(effect.Facing) * 68f);
        _effects.Spawn(_effectLibrary.HulkHit(Random.Shared.Next(3)), position);
        _renderer.TransientLights.Add(position, new Color(100, 255, 100), 100, 1);
    }

    public void Draw()
    {
        if (_renderer != null && _camera != null)
        {
            _renderer.Draw(_camera);
            _monsterMeshEffects?.Draw(_camera.View, _camera.Projection);
            _returnEffect?.Draw(_camera.View, _camera.Projection, _camera.Position);
            _effects?.Draw(_camera.View, _camera.Projection, _camera.Position);
            _renderer.ComposeScene();
        }
    }

    private static string AssetPath(string assetRoot, string relativePath)
    {
        var normalized = relativePath.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        return Path.Combine(assetRoot, normalized);
    }

    private static Vector2 SpawnPlanarPosition(FieldDef field)
    {
        var point = field.StartPoints.Count > 0 ? field.StartPoints[0] : field.Center;
        return new Vector2(point.X, -point.Z);
    }

    private void UpdatePlayerInput(GameTime gameTime)
    {
        if (_renderer == null || _activeField == null || _camera == null || _playerBody == null)
        {
            return;
        }

        var mouse = Mouse.GetState();
        if (_pendingWarp is not null)
        {
            _lastMouseState = mouse;
            return;
        }

        if (_playerController.IsDead)
        {
            _lastMouseState = mouse;
            return;
        }

        if (!PointerCaptured && RightAttackEnabled && mouse.RightButton == ButtonState.Pressed
            && _lastMouseState.RightButton == ButtonState.Released && TryPickAttackTarget(mouse, rightButton: true))
        {
            _hasMoveTarget = false;
        }

        if (!PointerCaptured && mouse.LeftButton == ButtonState.Pressed && _lastMouseState.LeftButton == ButtonState.Released)
        {
            var nowMs = gameTime.TotalGameTime.TotalMilliseconds;
            var doubleClickRun = (nowMs - _lastLeftClickMs) <= DoubleClickMs;
            _lastLeftClickMs = nowMs;
            var kb = Keyboard.GetState();
            var shiftRun = kb.IsKeyDown(Keys.LeftShift) || kb.IsKeyDown(Keys.RightShift);
            if (TryPickLoot(mouse))
            {
            }
            else if (TryPickAttackTarget(mouse))
            {
                _hasMoveTarget = false;
            }
            else if (SetMoveTarget(mouse))
            {
                ClearAttackTarget();
                _lootInteraction.Cancel();
                _moveTargetRun = doubleClickRun || shiftRun;
                _runMode = _moveTargetRun;
                _reportDue = true;
            }
        }

        if (_attackInput.Release(mouse.LeftButton == ButtonState.Pressed, mouse.RightButton == ButtonState.Pressed) is { } release)
        {
            SendAttackIntent(release);
        }

        UpdateNpcApproach();
        _lootInteraction?.Update(_groundLoot, _playerBody.Position, _serverPlanarPosition, Owner);
        UpdateAttackPresentation(gameTime);
        if (_hasMoveTarget && !_playerController.LocomotionFrozen && !_selfAttacking)
        {
            var current = _playerBody.PlanarPosition;
            var keyboard = Keyboard.GetState();
            var running = _moveTargetRun || keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift);
            if (running != _runMode)
            {
                _runMode = running;
                _reportDue = true;
            }

            var step = DestinationMovement.Advance(
                new System.Numerics.Vector2(current.X, current.Y),
                new System.Numerics.Vector2(_moveTarget.X, _moveTarget.Y),
                MovementSpeed(running),
                (float)gameTime.ElapsedGameTime.TotalSeconds);
            var proposed = new Vector2(step.Position.X, step.Position.Y);
            var slid = MoveWithCollision(current, proposed - current, _playerBody.Height + BodyProbeHeight);
            CommitGroundMovement(ResolveEntityCollision(current, slid));
            if (step.Direction != System.Numerics.Vector2.Zero)
            {
                _playerForward = new Vector3(step.Direction.X, 0f, step.Direction.Y);
            }

            ApplyPlayerWorld();
            var arrived = DestinationMovement.HasArrived(
                new System.Numerics.Vector2(_playerBody.PlanarPosition.X, _playerBody.PlanarPosition.Y),
                new System.Numerics.Vector2(_moveTarget.X, _moveTarget.Y));
            if (arrived)
            {
                _hasMoveTarget = false;
                _moveTargetRun = false;
                _positionCorrection = Vector2.Zero;
                ApplyPlayerCommand(_playerController.ApplyLocomotion(PristonActorMotion.Stand));
            }
            else
            {
                var motion = running ? PristonActorMotion.Run : PristonActorMotion.Walk;
                ApplyPlayerCommand(_playerController.ApplyLocomotion(motion));
            }
        }

        _lastMouseState = mouse;
    }

    public void DrawGroundLabels(SpriteBatch batch, SpriteFont font)
    {
        if (_groundLoot is null)
        {
            return;
        }

        bool showAll = !PointerCaptured && Keyboard.GetState().IsKeyDown(Keys.A);
        Guid hovered = Guid.Empty;
        var mouse = Mouse.GetState();
        if (!PointerCaptured && TryCursorRay(mouse, out var ray))
        {
            _groundLoot.Pick(mouse.Position, ray, out hovered, out _, showAll);
        }

        _groundLoot.DrawLabels(batch, font, _graphicsDevice.Viewport, _camera, hovered, showAll);
    }

    private bool LootFloorAt(float x, float z, out float height) =>
        TryGetFloorHeightNear(new Vector3(x, 0, z), _playerBody.Height + GroundTraversal.VisualOffset, out height);

    private bool TryPickLoot(MouseState mouse)
    {
        if (_groundLoot is null || _lootInteraction.Busy || !TryCursorRay(mouse, out var ray)
            || !_groundLoot.Pick(mouse.Position, ray, out var id, out var position, Keyboard.GetState().IsKeyDown(Keys.A)))
        {
            return false;
        }

        var feet = _playerBody.Position + Vector3.Up * GroundTraversal.VisualOffset;
        if (!GroundTraversal.CanReachSurface(feet, position, _groundFloorQuery))
        {
            return true;
        }

        ClearAttackTarget();
        _npcTargetId = 0;
        _lootInteraction.Select(id);
        _moveTarget = new Vector2(position.X, position.Z);
        _moveTargetRun = _runMode;
        _hasMoveTarget = true;
        _reportDue = true;
        return true;
    }

    private bool SetMoveTarget(MouseState mouse)
    {
        if (!TryCursorRay(mouse, out var ray))
        {
            return false;
        }

        if (!TryRaycastFloor(ray, out var point))
        {
            return false;
        }

        var feet = _playerBody.Position + Vector3.Up * GroundTraversal.VisualOffset;
        if (!GroundTraversal.CanReachSurface(feet, point, _groundFloorQuery))
        {
            return false;
        }

        _moveTarget = new Vector2(point.X, point.Z);
        _hasMoveTarget = true;
        return true;
    }

    private bool TryCursorRay(MouseState mouse, out Ray ray)
    {
        return CursorPicking.TryCreateRay(
            _graphicsDevice.Viewport,
            _camera.Projection,
            _camera.View,
            _camera.Position,
            mouse.Position,
            out ray);
    }

    private bool TryPickAttackTarget(MouseState mouse, bool rightButton = false)
    {
        if (_remote == null)
        {
            return false;
        }
        if (!TryCursorRay(mouse, out var ray))
        {
            return false;
        }
        if (!_remote.TryPick(ray, _remote.SelfEntityId, out var entityId, out var kind))
        {
            return false;
        }
        if (kind == RemoteEntityKind.Npc)
        {
            _lootInteraction.Cancel();
            ClearAttackTarget();
            _npcTargetId = entityId;
            _npcMoveSent = false;
            _targetSelection.Trace(entityId);
            StopPredictedMovement();
            return true;
        }
        if (kind != RemoteEntityKind.Monster)
        {
            return false;
        }

        _npcTargetId = 0;
        _lootInteraction.Cancel();
        _targetSelection.Trace(entityId);

        var kb = Keyboard.GetState();
        var automatic = kb.IsKeyDown(Keys.LeftShift) || kb.IsKeyDown(Keys.RightShift);
        SendAttackIntent(_attackInput.Press(entityId, automatic, rightButton));
        return true;
    }

    private void SendAttackIntent(AttackRequest request)
    {
        if (_playChannel is null)
        {
            return;
        }

        SendUpstream(_playChannel.SendAttackAsync(request.TargetId, request.Sustained, _presenceCts.Token, request.Sequence), "attack");
    }

    // Like the original client's attack cancel, this drops the trace but keeps the target frame open.
    private void ClearAttackTarget()
    {
        _attackInput.Cancel();
        _targetSelection.ClearTrace();
        _npcTargetId = 0;
        _npcMoveSent = false;
    }

    // Hover picking runs in Update so Draw stays free of scene queries.
    private void UpdateTargetSelection(GameTime gameTime)
    {
        uint hovered = 0;
        if (_remote is not null && _camera is not null && !PointerCaptured && CameraInputEnabled && TryCursorRay(Mouse.GetState(), out var ray)
            && _remote.TryPick(ray, _remote.SelfEntityId, out var entityId, out _))
        {
            hovered = entityId;
        }

        _targetSelection.Hover(hovered);
        _targetSelection.Update(gameTime.TotalGameTime.TotalMilliseconds, _targetLiveness);
    }

    private TargetLiveness QueryTargetLiveness(uint entityId)
    {
        if (_remote is null || !_remote.TryGetTarget(entityId, out _, out var dead))
        {
            return TargetLiveness.Missing;
        }

        return dead ? TargetLiveness.Dead : TargetLiveness.Alive;
    }

    private void StopPredictedMovement()
    {
        _hasMoveTarget = false;
        _moveTargetRun = false;
        _positionCorrection = Vector2.Zero;
        ApplyPlayerCommand(_playerController.ApplyLocomotion(PristonActorMotion.Stand));
    }

    private void UpdateNpcApproach()
    {
        if (_npcTargetId == 0 || _remote is null)
        {
            return;
        }

        if (_playerController.IsDead || !_remote.TryGetTarget(_npcTargetId, out var position, out _))
        {
            ClearAttackTarget();
            StopPredictedMovement();
            return;
        }

        var target = new Vector2(position.X, position.Z);
        var decision = NpcApproach.Evaluate(
            _playerBody.PlanarPosition,
            _serverPlanarPosition,
            target,
            _npcMoveSent);
        if (decision.StopLocally)
        {
            StopPredictedMovement();
        }

        if (decision.SendStop)
        {
            _npcMoveSent = true;
            _reportDue = true;
        }

        if (decision.OpenDialog)
        {
            var entityId = _npcTargetId;
            ClearAttackTarget();
            NpcSelected?.Invoke(entityId);
            return;
        }

        if (decision.BeginApproach)
        {
            _moveTarget = decision.Destination;
            _moveTargetRun = _runMode;
            _hasMoveTarget = true;
            _npcMoveSent = true;
            _reportDue = true;
        }
    }

    private void UpdateAttackPresentation(GameTime gameTime)
    {
        if (_attackInput.TargetId == 0 || _remote == null)
        {
            return;
        }

        if (_playerController.LocomotionFrozen || _selfAttacking)
        {
            return;
        }

        if (!_remote.TryGetTarget(_attackInput.TargetId, out var targetPos, out var targetDead) || targetDead)
        {
            ClearAttackTarget();
            if (_playerController.Current == PristonActorMotion.Attack)
            {
                ApplyPlayerCommand(_playerController.ApplyLocomotion(PristonActorMotion.Stand));
            }

            return;
        }

        var current = _playerBody.PlanarPosition;
        var targetPlanar = new Vector2(targetPos.X, targetPos.Z);
        var decision = AttackPresentation.Decide(current, targetPlanar, Owner?.AttackRange ?? AttackPresentation.MeleeRange);
        if (decision.ShouldAttack)
        {
            if (decision.Facing.LengthSquared() > 1e-6f)
            {
                _playerForward = new Vector3(decision.Facing.X, 0f, decision.Facing.Y);
            }

            ApplyPlayerWorld();
            ApplyPlayerCommand(_playerController.ApplyLocomotion(PristonActorMotion.Stand));

            return;
        }

        var elapsed = (float)gameTime.ElapsedGameTime.TotalSeconds;
        var dir = decision.ApproachDirection;
        var speed = MovementSpeed(_runMode);
        var step = speed * elapsed;
        var probeY = _playerBody.Height + BodyProbeHeight;
        var slid = MoveWithCollision(current, dir * step, probeY);
        CommitGroundMovement(ResolveEntityCollision(current, slid));
        _playerForward = new Vector3(dir.X, 0f, dir.Y);
        ApplyPlayerWorld();
        ApplyPlayerCommand(_playerController.ApplyLocomotion(_runMode ? PristonActorMotion.Run : PristonActorMotion.Walk));
    }

    private void UpdateBodyHeight(PhysicsBody body)
    {
        if (TryGetFloorHeight(body.Position, out var height))
        {
            body.Height = height - GroundTraversal.VisualOffset;
        }
    }

    private void TrackBodyHeight(PhysicsBody body)
    {
        if (TryGetFloorHeightNear(body.Position, body.Height + GroundTraversal.VisualOffset, out var height))
        {
            body.Height = height - GroundTraversal.VisualOffset;
        }
    }

    private void CommitGroundMovement(Vector2 destination)
    {
        var feet = _playerBody.Position + Vector3.Up * GroundTraversal.VisualOffset;
        GroundTraversal.TryFollow(feet, destination, _groundFloorQuery, out var reached);
        _playerBody.PlanarPosition = new Vector2(reached.X, reached.Z);
        _playerBody.Height = reached.Y - GroundTraversal.VisualOffset;
    }

    private bool TryGetFloorHeightNear(Vector3 position, float referenceHeight, out float height)
    {
        return _fieldCollision.TryGetFloorHeight(position, referenceHeight, GroundTraversal.StepUp, out height);
    }

    private Vector2 ResolveEntityCollision(Vector2 oldPlanar, Vector2 desiredPlanar)
    {
        // Visual terrain heights remain client-only until the authority owns the same geometry.
        if (_remote == null)
        {
            return desiredPlanar;
        }

        _remote.CollectNeighbours(_remote.SelfEntityId, _entityNeighbours);
        if (_entityNeighbours.Count == 0)
        {
            return desiredPlanar;
        }

        var y = _serverCollisionHeight;
        var old3 = new NumVector3(oldPlanar.X, y, oldPlanar.Y);
        var desired3 = new NumVector3(desiredPlanar.X, y, desiredPlanar.Y);
        var accepted = EntityCollision.Resolve(old3, desired3, EntityCollisionRadii.Player, _entityNeighbours);
        return new Vector2(accepted.X, accepted.Z);
    }

    private Vector2 MoveWithCollision(Vector2 from, Vector2 delta, float probeY)
    {
        var pos = from;
        var remaining = delta;
        for (var iter = 0; iter < 3; iter++)
        {
            var len = remaining.Length();
            if (len < 0.0001f)
                break;
            var dir = remaining / len;
            var start = new Vector3(pos.X, probeY, pos.Y);
            var end = new Vector3(pos.X + dir.X * (len + PlayerRadius), probeY, pos.Y + dir.Y * (len + PlayerRadius));
            if (!TraceWallsAcrossFields(start, end, out var hitPoint, out var normal))
            {
                pos += remaining;
                break;
            }
            if (MathF.Abs(normal.Y) >= WallFloorThreshold)
            {
                pos += remaining;
                break;
            }
            var hitPlanar = new Vector2(hitPoint.X, hitPoint.Z);
            var allowed = MathHelper.Clamp(Vector2.Distance(pos, hitPlanar) - PlayerRadius, 0f, len);
            pos += dir * allowed;
            var n = new Vector2(normal.X, normal.Z);
            if (n.LengthSquared() < 1e-6f)
                break;
            n = Vector2.Normalize(n);
            if (Vector2.Dot(n, dir) > 0f)
                n = -n;
            var leftover = remaining - dir * allowed;
            remaining = leftover - n * Vector2.Dot(leftover, n);
        }
        return pos;
    }

    private bool TraceWallsAcrossFields(Vector3 start, Vector3 end, out Vector3 hitPoint, out Vector3 hitNormal)
    {
        return _fieldCollision.TryTraceSegment(start, end, out hitPoint, out hitNormal);
    }

    private bool TryGetFloorHeight(Vector3 position, out float height)
    {
        return _fieldCollision.TryGetFloorHeight(position, float.MaxValue, 0f, out height);
    }

    private bool TryRaycastFloor(Ray ray, out Vector3 point)
    {
        point = default;
        var found = false;
        var bestDistance = float.MaxValue;
        foreach (var field in _fields)
        {
            if (field.Collision.TryRaycastFloor(ray, out var p))
            {
                var distance = Vector3.DistanceSquared(ray.Position, p);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    point = p;
                    found = true;
                }
            }
        }
        return found;
    }

    private void ApplyPlayerWorld()
    {
        if (!_playerBodyActor.IsValid)
            return;
        var world = PlayerWorld();
        _renderer.SetActorWorld(_playerBodyActor, world);
        _renderer.SetActorWorld(_playerHeadActor, world);
    }

    private Matrix PlayerWorld()
    {
        var yaw = MathF.Atan2(_playerForward.X, _playerForward.Z);
        return Matrix.CreateRotationY(yaw) * Matrix.CreateTranslation(GroundTraversal.ToVisualPosition(_playerBody.Position));
    }

    public void Dispose()
    {
        _fieldStreamer?.Dispose();
        TearDownRemotePresence();
        _equipmentPresentation?.Stop();
        _groundLoot?.Dispose();
        _returnEffect?.Dispose();
        _renderer?.Dispose();
        _meteors?.Clear();
        _monsterMeshEffects?.Clear();
        _effects?.Dispose();
        _fieldAudio?.Dispose();
        _npcAudio?.Dispose();
    }

    private sealed class PhysicsBody
    {
        public PhysicsBody(Vector2 planarPosition)
        {
            PlanarPosition = planarPosition;
        }

        public Vector2 PlanarPosition { get; set; }
        public float Height { get; set; }
        public float Radius { get; init; }
        public Vector3 Position => new(PlanarPosition.X, Height, PlanarPosition.Y);
    }
}
