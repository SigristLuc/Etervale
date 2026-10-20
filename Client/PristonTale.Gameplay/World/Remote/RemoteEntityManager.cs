using PristonTale.Client.Net.Presence;
using PristonTale.Client.Net.WorldCatalog;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Rendering;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Physics;
using XnaVector3 = Microsoft.Xna.Framework.Vector3;
using NumVector3 = System.Numerics.Vector3;

namespace PristonTale.Gameplay.World.Remote;

public sealed class RemoteEntityManager
{
    private readonly IActorScene _scene;
    private readonly IPlayerActorComposer _playerComposer;
    private readonly IModelActorComposer _monsterComposer;
    private readonly CharacterClassRegistry _classRegistry;
    private readonly string _assetRoot;
    private readonly IClientLog _log;

    private WorldRenderCatalog _catalog;

    private readonly Dictionary<uint, RemoteEntityBase> _entities = new();
    private readonly RemoteCollisionRoster _collisionRoster = new();

    private readonly Dictionary<uint, EntitySpawnInfo> _pendingModels = new();
    private readonly Dictionary<uint, (EntitySpawnInfo Spawn, Task Preparation)> _preparingActors = new();
    private readonly RemoteServerClock _clock = new();

    private uint _selfEntityId;

    private Action<EntitySpawnInfo> _onSelfSpawn;
    private Action<EntityStateInfo> _onSelfState;
    private Action<CombatEventInfo> _onSelfCombatEvent;

    public RemoteEntityManager(
        IActorScene scene,
        IPlayerActorComposer playerComposer,
        IModelActorComposer monsterComposer,
        CharacterClassRegistry classRegistry,
        string assetRoot,
        WorldRenderCatalog catalog = null,
        IClientLog log = null)
    {
        _scene = scene;
        _playerComposer = playerComposer;
        _monsterComposer = monsterComposer;
        _classRegistry = classRegistry;
        _assetRoot = assetRoot;
        _catalog = catalog ?? WorldRenderCatalog.Empty;
        _log = log ?? ConsoleClientLog.Instance;
    }

    public int Count => _entities.Count;

    public uint SelfEntityId => _selfEntityId;

    public event Action<int, PristonActorMotion, XnaVector3> NpcMotionStarted;

    public int PendingMonsterCount => _pendingModels.Count;

    public void SetSelfSink(
        Action<EntitySpawnInfo> onSelfSpawn,
        Action<EntityStateInfo> onSelfState,
        Action<CombatEventInfo> onSelfCombatEvent = null)
    {
        _onSelfSpawn = onSelfSpawn;
        _onSelfState = onSelfState;
        _onSelfCombatEvent = onSelfCombatEvent;
    }

    public void SetCatalog(WorldRenderCatalog catalog)
    {
        if (catalog == null)
        {
            return;
        }

        _catalog = catalog;
        if (_pendingModels.Count == 0)
        {
            return;
        }

        var pending = new List<EntitySpawnInfo>(_pendingModels.Values);
        _pendingModels.Clear();
        foreach (var spawn in pending)
        {
            ApplySpawn(spawn);
        }
    }

    public void Apply(PresenceUpdate update)
    {
        if (update.ServerTimeMs is { } serverTime)
        {
            _clock.Observe(serverTime);
        }

        var eventTime = _clock.Stamp(update.ServerTimeMs);
        foreach (var spawn in update.Spawns)
        {
            _collisionRoster.Spawn(spawn);
            ApplySpawn(spawn);
            if (_entities.TryGetValue(spawn.EntityId, out var entity))
            {
                entity.SetRates(spawn.MoveSpeed, spawn.AttackRate);
            }
        }

        foreach (var state in update.States)
        {
            _collisionRoster.Update(state);
            ApplyState(state);
        }

        foreach (var combatEvent in update.Events)
        {
            var entityId = combatEvent.Kind == RemoteCombatEventKind.SwingBegin ? combatEvent.AttackerId : combatEvent.TargetId;
            if (entityId == _selfEntityId)
            {
                _onSelfCombatEvent?.Invoke(combatEvent);
            }
            else if (_entities.TryGetValue(entityId, out var entity))
            {
                entity.ApplyCombatEvent(combatEvent, eventTime);
            }
        }

        foreach (var removed in update.Removed)
        {
            ApplyRemoved(removed);
        }
    }

    private readonly Dictionary<uint, PlayerAppearanceInfo> _playerAppearances = new();

    private void ApplySpawn(EntitySpawnInfo spawn, bool publish = false)
    {
        if (spawn.IsSelf)
        {
            _selfEntityId = spawn.EntityId;
            _onSelfSpawn?.Invoke(spawn);
            return;
        }

        if (_entities.ContainsKey(spawn.EntityId) && !publish
            && (spawn.Player is null || (_playerAppearances.TryGetValue(spawn.EntityId, out var known) && known == spawn.Player)))
        {
            if (_preparingActors.Remove(spawn.EntityId, out var superseded))
            {
                _ = ObservePreparationAsync(superseded.Preparation);
            }

            return;
        }

        Task preparation = null;
        if (spawn.Kind == RemoteEntityKind.Player && spawn.Player is not null && TryResolveVisuals(spawn.Player.JobCode, out var visuals))
        {
            visuals = ResolvePlayerHead(visuals, spawn.Player);
            preparation = _playerComposer.PrepareAsync(_assetRoot, visuals, spawn.Player.FaceIndex);
        }
        else if (spawn.Kind is RemoteEntityKind.Monster or RemoteEntityKind.Npc)
        {
            var info = ModelInfo(spawn);
            if (info is not null && !string.IsNullOrEmpty(info.ModelInxPath))
            {
                preparation = _monsterComposer.PrepareAsync(_assetRoot, info.ModelInxPath);
            }
        }

        if (preparation is not null && (!publish || !preparation.IsCompletedSuccessfully))
        {
            if (_preparingActors.TryGetValue(spawn.EntityId, out var superseded) && superseded.Preparation != preparation)
            {
                _ = ObservePreparationAsync(superseded.Preparation);
            }

            _preparingActors[spawn.EntityId] = (spawn, preparation);
            return;
        }

        switch (spawn.Kind)
        {
            case RemoteEntityKind.Player:
                ApplyPlayerSpawn(spawn);
                break;
            case RemoteEntityKind.Monster:
            case RemoteEntityKind.Npc:
                ApplyModelSpawn(spawn);
                break;
        }
    }

    private void ApplyPlayerSpawn(EntitySpawnInfo spawn)
    {
        if (spawn.Player is null)
        {
            _log.Warn($"[Remote] player spawn {spawn.EntityId} carried no appearance; skipped.");
            return;
        }

        if (!TryResolveVisuals(spawn.Player.JobCode, out var visuals))
        {
            _log.Warn($"[Remote] spawn {spawn.EntityId}: unknown JobCode {spawn.Player.JobCode}; skipped.");
            return;
        }

        var position = ToXna(spawn.Position);
        var sampleTime = _clock.Stamp(spawn.ServerTimeMs);
        var seedTime = sampleTime;
        var seedFacing = spawn.Facing;
        if (_entities.TryGetValue(spawn.EntityId, out var existingPlayer))
        {
            // An appearance change keeps playing from the drawn pose towards the latest authoritative sample.
            position = existingPlayer.DrawnPosition;
            seedFacing = existingPlayer.RenderFacing;
            seedTime = double.IsFinite(existingPlayer.RenderTimeMs) ? Math.Min(existingPlayer.RenderTimeMs, sampleTime) : sampleTime;
        }

        var world = Microsoft.Xna.Framework.Matrix.CreateRotationY(seedFacing)
                    * Microsoft.Xna.Framework.Matrix.CreateTranslation(position);
        var motion = RemoteEntityBase.ToActorMotion(spawn.MoveState);
        visuals = ResolvePlayerHead(visuals, spawn.Player);
        var handles = new PlayerActorHandles(ActorHandle.None, ActorHandle.None);
        try
        {
            handles = _playerComposer.Compose(_assetRoot, visuals, spawn.Player.FaceIndex, world, motion);
            if (existingPlayer is RemoteEntity player)
            {
                _scene.CopyActorPlayback(player.Handles.Body, handles.Body);
                _scene.CopyActorPlayback(player.Handles.Head, handles.Head);
            }
        }
        catch (Exception exception)
        {
            handles.Remove(_scene);
            _log.Error($"[Remote] player appearance publication failed: {exception.Message}");
            return;
        }

        if (_entities.Remove(spawn.EntityId, out var previous))
        {
            previous.Dispose();
        }

        _playerAppearances[spawn.EntityId] = spawn.Player;
        var replacement = new RemoteEntity(
            _scene,
            handles,
            seedTime,
            position,
            seedFacing,
            spawn.MoveState,
            spawn.Hp)
        {
            InVillage = spawn.Player.InVillage
        };
        replacement.SetRates(spawn.MoveSpeed, spawn.AttackRate);
        replacement.SetTarget(sampleTime, ToXna(spawn.Position), spawn.Facing, spawn.MoveState, spawn.Hp);
        _entities[spawn.EntityId] = replacement;
    }

    private ModelRenderInfo ModelInfo(EntitySpawnInfo spawn)
    {
        if (spawn.Kind == RemoteEntityKind.Monster)
        {
            return _catalog.GetMonster(spawn.TemplateId);
        }

        return _catalog.GetNpc(spawn.TemplateId);
    }

    private void ApplyModelSpawn(EntitySpawnInfo spawn)
    {
        var info = ModelInfo(spawn);
        if (info is null || string.IsNullOrEmpty(info.ModelInxPath))
        {
            _pendingModels[spawn.EntityId] = spawn;
            _log.Info($"[Remote] monster spawn {spawn.EntityId} template {spawn.TemplateId} deferred (catalog not ready / no entry).");
            return;
        }

        var position = ToXna(spawn.Position);
        var world = Microsoft.Xna.Framework.Matrix.CreateScale(info.ModelScale) * Microsoft.Xna.Framework.Matrix.CreateRotationY(spawn.Facing) * Microsoft.Xna.Framework.Matrix.CreateTranslation(position);
        var motion = RemoteEntityBase.ToActorMotion(spawn.MoveState);
        var composed = _monsterComposer.Compose(_assetRoot, info.ModelInxPath, world, motion);
        var monster = new RemoteModelEntity(
            _scene,
            composed.Handle,
            info.ModelScale,
            _clock.Stamp(spawn.ServerTimeMs),
            position,
            spawn.Facing,
            spawn.MoveState,
            spawn.Hp,
            spawn.Kind,
            spawn.Kind == RemoteEntityKind.Npc ? ((NpcRenderInfo)info).SoundCode : 0,
            composed.Info,
            spawn.EntityId);
        monster.SetRates(spawn.MoveSpeed, spawn.AttackRate);
        monster.MotionStarted += OnMotionStarted;
        _entities[spawn.EntityId] = monster;
    }

    private void ApplyState(EntityStateInfo state)
    {
        if (state.EntityId == _selfEntityId && _selfEntityId != 0)
        {
            _onSelfState?.Invoke(state);
            return;
        }

        if (_entities.TryGetValue(state.EntityId, out var entity))
        {
            entity.SetRates(state.MoveSpeed, state.AttackRate);
            entity.SetTarget(_clock.Stamp(state.ServerTimeMs), ToXna(state.Position), state.Facing, state.MoveState, state.Hp);
            if (!_preparingActors.ContainsKey(state.EntityId))
            {
                return;
            }
        }

        if (_preparingActors.TryGetValue(state.EntityId, out var preparing))
        {
            var latest = preparing.Spawn with
            {
                Position = state.Position,
                Facing = state.Facing,
                MoveState = state.MoveState,
                Hp = state.Hp,
                MoveSpeed = state.MoveSpeed,
                AttackRate = state.AttackRate,
                ServerTimeMs = state.ServerTimeMs
            };
            _preparingActors[state.EntityId] = (latest, preparing.Preparation);
            return;
        }

        if (_pendingModels.TryGetValue(state.EntityId, out var pending))
        {
            _pendingModels[state.EntityId] = pending with
            {
                Position = state.Position,
                Facing = state.Facing,
                MoveState = state.MoveState,
                Hp = state.Hp,
                MoveSpeed = state.MoveSpeed,
                AttackRate = state.AttackRate,
                ServerTimeMs = state.ServerTimeMs
            };
            return;
        }

        _log.Info($"[Remote] state for unknown entity {state.EntityId} dropped (spawn not yet seen).");
    }

    private void ApplyRemoved(uint entityId)
    {
        _playerAppearances.Remove(entityId);
        _collisionRoster.Remove(entityId);
        _pendingModels.Remove(entityId);
        if (_preparingActors.Remove(entityId, out var pending))
        {
            _ = ObservePreparationAsync(pending.Preparation);
        }

        if (!_entities.Remove(entityId, out var entity))
        {
            return;
        }

        entity.MotionStarted -= OnMotionStarted;
        entity.Dispose();
    }

    private void OnMotionStarted(RemoteEntityBase entity, PristonActorMotion motion)
    {
        if (entity.Kind == RemoteEntityKind.Npc && entity.SoundCode != 0)
        {
            NpcMotionStarted?.Invoke(entity.SoundCode, motion, entity.DrawnPosition);
        }
    }

    public void Update(float dt)
    {
        _clock.Advance(dt * 1000d);
        uint completed = 0;
        foreach (var (id, pending) in _preparingActors)
        {
            if (!pending.Preparation.IsCompleted)
            {
                continue;
            }

            completed = id;
            if (pending.Preparation.IsCompletedSuccessfully)
            {
                ApplySpawn(pending.Spawn, publish: true);
                if (_entities.TryGetValue(id, out var loaded))
                {
                    loaded.SetRates(pending.Spawn.MoveSpeed, pending.Spawn.AttackRate);
                }
            }
            else
            {
                _log.Error($"[Remote] actor preparation failed: {pending.Preparation.Exception}");
            }

            break;
        }

        if (completed != 0)
        {
            _preparingActors.Remove(completed);
        }

        var now = _clock.NowMs;
        foreach (var entity in _entities.Values)
        {
            entity.Update(dt, now);
        }
    }

    private const float MonsterPickRadius = 60f;
    private const float PlayerPickRadius = 40f;
    private const float NpcPickRadius = 40f;

    private static float PickRadius(RemoteEntityKind kind) => kind switch
    {
        RemoteEntityKind.Monster => MonsterPickRadius,
        RemoteEntityKind.Player => PlayerPickRadius,
        RemoteEntityKind.Npc => NpcPickRadius,
        _ => 0f,
    };

    public bool TryPick(Microsoft.Xna.Framework.Ray ray, uint selfId, out uint entityId, out RemoteEntityKind kind)
    {
        entityId = 0;
        kind = RemoteEntityKind.Unspecified;
        var bestDistance = float.MaxValue;
        foreach (var (id, entity) in _entities)
        {
            if (id == selfId)
                continue;
            if (entity.IsDead)
                continue;
            var radius = PickRadius(entity.Kind);
            if (radius <= 0f)
                continue;
            var hit = CursorPicking.IntersectSphere(ray, entity.DrawnPosition, radius);
            if (hit is null)
                continue;
            if (hit.Value < bestDistance)
            {
                bestDistance = hit.Value;
                entityId = id;
                kind = entity.Kind;
            }
        }
        return entityId != 0;
    }

    public bool TryGetDrawnPosition(uint entityId, out XnaVector3 position)
    {
        if (_entities.TryGetValue(entityId, out var entity))
        {
            position = entity.DrawnPosition;
            return true;
        }

        position = default;
        return false;
    }

    public bool TryGetNpcAudioSource(uint entityId, out int soundCode, out XnaVector3 position)
    {
        if (_entities.TryGetValue(entityId, out var entity) && entity.Kind == RemoteEntityKind.Npc)
        {
            soundCode = entity.SoundCode;
            position = entity.DrawnPosition;
            return true;
        }

        soundCode = 0;
        position = default;
        return false;
    }

    public bool TryGetTarget(uint entityId, out XnaVector3 position, out bool isDead)
    {
        if (_entities.TryGetValue(entityId, out var entity))
        {
            position = entity.RenderPosition;
            isDead = entity.IsDead;
            return true;
        }
        position = default;
        isDead = false;
        return false;
    }

    public void CollectNeighbours(uint excludeId, List<EntityNeighbour> into) => _collisionRoster.Collect(excludeId, into);

    public void Clear()
    {
        foreach (var entity in _entities.Values)
        {
            entity.MotionStarted -= OnMotionStarted;
            entity.Dispose();
        }

        _entities.Clear();
        _playerAppearances.Clear();
        _collisionRoster.Clear();
        _pendingModels.Clear();
        foreach (var pending in _preparingActors.Values)
        {
            _ = ObservePreparationAsync(pending.Preparation);
        }

        _preparingActors.Clear();
    }

    private async Task ObservePreparationAsync(Task preparation)
    {
        try
        {
            await preparation;
        }
        catch (Exception ex)
        {
            _log.Error($"[Remote] abandoned actor preparation failed: {ex}");
        }
    }

    public void Dispose() => Clear();

    private CharacterVisualSet ResolvePlayerHead(CharacterVisualSet defaults, PlayerAppearanceInfo appearance)
    {
        var head = CharacterHead.Apply(defaults, (PlayerClass)appearance.JobCode, appearance.Tier, appearance.FaceIndex, appearance.HairIndex);
        uint jobMask = _classRegistry.Get((PlayerClass)appearance.JobCode).Definition.MotionJobMask;
        return EquipmentVisuals.Apply(head, appearance.ArmorKey, appearance.WeaponKey, appearance.OffHandKey, appearance.InVillage, jobMask);
    }

    private bool TryResolveVisuals(int jobCode, out CharacterVisualSet visuals)
    {
        if (System.Enum.IsDefined(typeof(PlayerClass), jobCode))
        {
            visuals = _classRegistry.Get((PlayerClass)jobCode).Definition.DefaultVisuals;
            return true;
        }
        visuals = null;
        return false;
    }

    private static XnaVector3 ToXna(NumVector3 v) => new(v.X, v.Y, v.Z);
}
