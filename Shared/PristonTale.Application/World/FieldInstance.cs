using PristonTale.Simulation.Items;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Physics;
using PristonTale.Simulation.World.Ai;
using PristonTale.Simulation.World.Fields;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Simulation.World.Motion;
using PristonTale.Simulation.World.Spawning;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Application.World;

public sealed class FieldInstance : ISpawnSink, IPlayerProximity, IFieldCombatHost
{
    public const int PlayerNearRadiusSquared = 0x120000;
    public static readonly float PlayerNearRadius = MathF.Sqrt(PlayerNearRadiusSquared);

    private readonly Dictionary<uint, WorldPlayer> _players = new();
    private readonly Dictionary<uint, MonsterInstance> _monsters = new();
    private readonly Dictionary<uint, WorldNpc> _npcs = new();

    // Reused scratch (tick thread only) so Step allocates no per-tick garbage on the hot path.
    private readonly List<MonsterInstance> _liveScratch = new();
    private readonly List<EntityNeighbour> _neighbourScratch = new();
    private readonly FieldPreyView _prey = new();
    private readonly SpatialIndex _collisionIndex = new();
    private readonly List<SpatialEntry> _collisionQuery = new();
    private readonly Dictionary<int, MonsterAiTuning> _tuningByCode = new();
    // Null timing entries use the fallback clips and are cached as misses.
    private readonly Dictionary<int, MotionTimingSet?> _playerTimingByJob = new();
    // Snapshots copy this tick-owned list before asynchronous publication.
    private readonly List<CombatEventView> _events = new();

    private static readonly CharacterClassRegistry ClassRegistry = new();

    private readonly FieldSpawner _spawner;
    private readonly MonsterAiBrain _brain;
    private readonly FieldCombat _combat;
    private readonly EntityIdAllocator _ids;
    private readonly CombatRng _rng;
    private readonly MotionTimingCatalog _motions;
    private double _nowMs;

    public FieldInstance(int fieldId)
        : this(fieldId, FieldSpawnData.Empty, new MonsterCatalog([]), new EntityIdAllocator(), new CombatRng(fieldId))
    {
    }

    public FieldInstance(
        int fieldId, FieldSpawnData spawnData, MonsterCatalog catalog, EntityIdAllocator ids, CombatRng rng,
        MotionTimingCatalog? motions = null, NpcCatalog? npcs = null, int experienceMultiplier = 1, ItemCatalog? items = null)
    {
        FieldId = fieldId;
        GroundLoot = new FieldGroundLoot(items ?? new ItemCatalog([]));
        _ids = ids;
        _rng = rng;
        _motions = motions ?? MotionTimingCatalog.Empty;
        _ = PlayerEquipmentTiming.Count;
        _spawner = new FieldSpawner(spawnData, catalog, rng);
        _brain = new MonsterAiBrain(rng);
        _combat = new FieldCombat(this, rng, experienceMultiplier);
        foreach (var placement in spawnData.FixedNpcs)
        {
            if (npcs is null || !npcs.TryGet(placement.NpcCode, out var definition))
            {
                throw new InvalidDataException($"Field {fieldId} references missing NPC {placement.NpcCode}.");
            }

            _motions.TryGet(definition.ModelInxPath, out var motionTimings);
            var npc = WorldNpc.Place(_ids.NextMonsterId(), definition, placement, motionTimings);
            _npcs.Add(npc.EntityId, npc);
        }
    }

    public int FieldId { get; }
    public int PlayerCount => _players.Count;
    public int MonsterCount => _monsters.Count;
    public FieldGroundLoot GroundLoot { get; }

    public IReadOnlyCollection<WorldNpc> Npcs => _npcs.Values;
    public bool TryGetNpc(uint entityId, [MaybeNullWhen(false)] out WorldNpc npc) => _npcs.TryGetValue(entityId, out npc);
    public IEnumerable<uint> PlayerIds => _players.Keys;

    public void Add(WorldPlayer player) => _players[player.EntityId] = player;
    public void Remove(uint entityId) => _players.Remove(entityId, out _);
    public bool TryGet(uint entityId, [MaybeNullWhen(false)] out WorldPlayer player) => _players.TryGetValue(entityId, out player);

    public bool SetAttackTarget(uint attackerEntityId, uint targetEntityId, bool sustained, ulong sequence = 0)
    {
        if (!_players.TryGetValue(attackerEntityId, out var attacker) || attacker.IsDead)
        {
            return false;
        }

        if (!attacker.AcceptAttackSequence(sequence))
        {
            return false;
        }

        if (!_monsters.TryGetValue(targetEntityId, out var monster) || !monster.IsAlive)
        {
            attacker.ClearAttackTarget();
            return false;
        }

        attacker.SetAttackTarget(targetEntityId, sustained);
        return true;
    }

    public long ExpFor(uint playerId) => _combat.ExpFor(playerId);

    public void Step(float dt, int? scheduledBossHour = null)
    {
        float dtMs = dt * 1000f;
        _nowMs += dtMs;
        long nowMs = (long)_nowMs;
        _events.Clear();
        _prey.Rebuild(_players.Values);
        SnapshotLiveMonsters();
        _spawner.MaintainPopulation(dtMs, _liveScratch, this, nowMs, this);
        IReadOnlyList<MonsterInstance> expired = _spawner.MaintainDespawns(dtMs, _liveScratch, this);
        for (int i = 0; i < expired.Count; i++)
        {
            _monsters.Remove(expired[i].EntityId, out _);
            _combat.ForgetMonster(expired[i].EntityId);
        }

        // The shared world clock emits one pass for all fields, independent of player entry.
        if (scheduledBossHour is int hour)
        {
            _spawner.SpawnScheduledBosses(hour, this, this);
        }
        SnapshotLiveMonsters();
        RebuildCollisionIndex();
        for (int i = 0; i < _liveScratch.Count; i++)
        {
            StepMonster(_liveScratch[i], dtMs);
        }

        // Authoritative NPC movement keeps interaction range and presentation aligned.
        foreach (var npc in _npcs.Values)
        {
            StepNpc(npc, dt);
        }

        foreach (var player in _players.Values)
        {
            if (player.IsDead)
            {
                player.TickRespawn(dtMs);
                _collisionIndex.Put(player.EntityId, player.Position);
                continue;
            }

            if (player.TickAction(dtMs, out uint hitTargetId))
            {
                var payout = _combat.ResolvePlayerSwingHit(player, hitTargetId, nowMs);
                if (payout is { } reward && _monsters.TryGetValue(hitTargetId, out var killed))
                {
                    GroundLoot.Place(reward, killed.Position, killed.Def.Drops.AllPlayersSeeDrops, nowMs);
                }
            }

            var restored = player.TickPotion(dtMs, _rng);
            if (restored != PotionResource.None)
            {
                EmitCombatEvent(new CombatEventView(player.EntityId, player.EntityId, CombatEventKindView.PotionApplied, resources: (int)restored));
            }

            if (player.HasPendingPotion && !player.IsBusy)
            {
                var timing = PlayerPotionTiming(player);
                player.BeginPendingPotion(timing.DurationMs, timing.HitAtMs);
                EmitCombatEvent(new CombatEventView(player.EntityId, player.EntityId, CombatEventKindView.PotionBegin,
                    timing.DurationMs, timing.HitAtMs));
            }

            _combat.StepPlayerAttack(player);
            StepPlayer(player, dt);
        }

        _combat.TickCorpses(dtMs);
        GroundLoot.Step(nowMs);
    }

    private void StepPlayer(WorldPlayer player, float dt)
    {
        Vector3 oldPos = player.Position;
        Vector3 desired = player.IntegrateStep(dt);
        if (desired == oldPos)
        {
            player.CommitStep(desired);
            return;
        }

        BuildNeighbours(player.EntityId, oldPos, desired, EntityCollisionRadii.Player);
        Vector3 accepted = EntityCollision.Resolve(oldPos, desired, EntityCollisionRadii.Player, _neighbourScratch);
        player.CommitStep(accepted);
        _collisionIndex.Put(player.EntityId, accepted);
        // Collision hard-blocks the step instead of pushing entities apart.
    }

    private void StepMonster(MonsterInstance monster, float dtMs)
    {
        MonsterAiTuning tuning = TuningFor(monster.Def);

        Vector3 before = monster.Position;
        MonsterAttackEvent? attack = _brain.Tick(monster, tuning, _prey, dtMs);

        if (monster.SwingJustBegan)
        {
            monster.SwingJustBegan = false;
            float durationMs = monster.AttackIsSkill ? tuning.SkillDurationMs : tuning.CooldownMs;
            float hitAtMs = monster.AttackIsSkill ? tuning.SkillWindupMs : tuning.WindupMs;
            _events.Add(new CombatEventView(
                monster.EntityId, monster.TargetId, CombatEventKindView.SwingBegin, durationMs, hitAtMs));
        }

        // Re-resolve AI movement against the authoritative entity roster.
        Vector3 desired = monster.Position;
        if (desired != before)
        {
            BuildNeighbours(monster.EntityId, before, desired, EntityCollisionRadii.Monster);
            Vector3 accepted = EntityCollision.Resolve(before, desired, EntityCollisionRadii.Monster, _neighbourScratch);
            monster.Position = accepted;
            _collisionIndex.Put(monster.EntityId, accepted);
        }

        if (attack is { } landed)
        {
            _combat.ResolveMonsterHit(monster, landed);
        }
    }

    private void StepNpc(WorldNpc npc, float dt)
    {
        var before = npc.Position;
        var desired = npc.IntegrateStep(dt);
        if (desired == before)
        {
            return;
        }

        BuildNeighbours(npc.EntityId, before, desired, EntityCollisionRadii.Npc);
        var accepted = EntityCollision.Resolve(before, desired, EntityCollisionRadii.Npc, _neighbourScratch);
        npc.CommitStep(accepted, accepted == before);
        _collisionIndex.Put(npc.EntityId, accepted);
    }

    public MonsterAiTuning TuningFor(MonsterDefinition def)
    {
        if (!_tuningByCode.TryGetValue(def.Code, out var tuning))
        {
            _motions.TryGet(def.ModelInxPath, out var timing);
            tuning = new MonsterAiTuning(def, timing);
            _tuningByCode[def.Code] = tuning;
        }

        return tuning;
    }

    public (float DurationMs, float HitAtMs) PlayerSwingTiming(WorldPlayer player)
    {
        var rate = AnimationRate.AttackRate(player.Character.Derived.AttackSpeed);
        return PlayerActionTiming(player, MotionStateCode.Attack, rate);
    }

    private (float DurationMs, float HitAtMs) PlayerPotionTiming(WorldPlayer player)
    {
        return PlayerActionTiming(player, MotionStateCode.Eat, MotionTiming.DamageRateFactor);
    }

    private (float DurationMs, float HitAtMs) PlayerActionTiming(WorldPlayer player, int state, float rate)
    {
        var timing = PlayerTimingFor(player.Appearance.JobCode);
        var clip = PlayerMotionFallback.GetClip(player.Character.Class.Class, state);
        if (timing is not null && timing.TryGetClip(state, out var authored))
        {
            clip = authored;
        }

        string weaponKey = player.Character.GetEquipped(EquipmentSlot.Weapon)?.Definition.CatalogKey ?? "";
        if (state == MotionStateCode.Attack && PlayerEquipmentTiming.TryGet(player.Appearance.JobCode, weaponKey, out var equipped))
        {
            clip = equipped;
        }

        var duration = MotionTiming.DurationMs(clip.Frames, rate);
        var eventMs = clip.TryGetEventTicks(0, out var ticks)
            ? MotionTiming.EventOffsetMs(ticks, rate)
            : duration;
        return (duration, eventMs);
    }

    public void EmitCombatEvent(in CombatEventView combatEvent) => _events.Add(combatEvent);

    internal CombatEventView[]? SnapshotEvents() => _events.Count == 0 ? null : _events.ToArray();

    private MotionTimingSet? PlayerTimingFor(int jobCode)
    {
        if (_playerTimingByJob.TryGetValue(jobCode, out var cached))
            return cached;

        MotionTimingSet? set = null;
        if (Enum.IsDefined(typeof(PlayerClass), jobCode))
        {
            var bodyPath = ClassRegistry.Get((PlayerClass)jobCode).Definition.DefaultVisuals.Body.InxPath;
            _motions.TryGet(bodyPath, out set);
        }

        _playerTimingByJob[jobCode] = set;
        return set;
    }

    private void RebuildCollisionIndex()
    {
        _collisionIndex.Clear();
        var order = 0;
        foreach (var player in _players.Values)
        {
            _collisionIndex.Put(player.EntityId, player.Position, order++);
        }

        foreach (var monster in _monsters.Values)
        {
            _collisionIndex.Put(monster.EntityId, monster.Position, order++);
        }

        foreach (var npc in _npcs.Values)
        {
            _collisionIndex.Put(npc.EntityId, npc.Position, order++);
        }
    }

    private void BuildNeighbours(uint selfId, Vector3 before, Vector3 desired, float selfRadius)
    {
        _neighbourScratch.Clear();
        var bounds = EntityCollision.QueryBounds(before, desired, MathF.Max(selfRadius, EntityCollisionRadii.Monster));
        _collisionIndex.Query(bounds.Center, bounds.Radius, _collisionQuery);
        foreach (var entry in _collisionQuery)
        {
            if (entry.Id == selfId)
            {
                continue;
            }

            if (_players.TryGetValue(entry.Id, out var player))
            {
                if (player.Hp > 0)
                {
                    _neighbourScratch.Add(new EntityNeighbour(player.Position, EntityCollisionRadii.Player));
                }
            }
            else if (_monsters.TryGetValue(entry.Id, out var monster) && monster.IsAlive)
            {
                _neighbourScratch.Add(new EntityNeighbour(monster.Position, EntityCollisionRadii.Monster));
            }
            else if (_npcs.TryGetValue(entry.Id, out var npc))
            {
                _neighbourScratch.Add(new EntityNeighbour(npc.Position, EntityCollisionRadii.Npc));
            }
        }
    }

    private void SnapshotLiveMonsters()
    {
        _liveScratch.Clear();
        foreach (var m in _monsters.Values)
            _liveScratch.Add(m);
    }

    MonsterInstance ISpawnSink.Spawn(MonsterDefinition definition, Vector3 position, int spawnPointIndex, float facing)
    {
        uint id = _ids.NextMonsterId();
        var monster = new MonsterInstance(id, definition, position, facing, spawnPointIndex);
        _monsters[id] = monster;
        return monster;
    }

    bool IFieldCombatHost.TryGetPlayer(uint entityId, [MaybeNullWhen(false)] out WorldPlayer player)
        => _players.TryGetValue(entityId, out player);

    bool IFieldCombatHost.TryGetMonster(uint entityId, [MaybeNullWhen(false)] out MonsterInstance monster)
        => _monsters.TryGetValue(entityId, out monster);

    IEnumerable<MonsterInstance> IFieldCombatHost.Monsters => _monsters.Values;

    IReadOnlyList<PreyCandidate> IFieldCombatHost.NearbyPlayers(Vector3 origin, float radius) => _prey.Nearby(origin, radius);

    void IFieldCombatHost.DespawnMonster(uint entityId) => _monsters.Remove(entityId, out _);

    bool IPlayerProximity.AnyPlayerNear(Vector3 worldPosition)
    {
        foreach (var p in _players.Values)
        {
            float dx = p.Position.X - worldPosition.X;
            float dz = p.Position.Z - worldPosition.Z;
            if ((dx * dx) + (dz * dz) < PlayerNearRadiusSquared)
            {
                return true;
            }
        }

        return false;
    }

    public EntityView[] BuildViews()
    {
        var views = new EntityView[_players.Count + _monsters.Count + _npcs.Count];
        var index = 0;
        foreach (var player in _players.Values)
        {
            views[index++] = player.ToView();
        }

        foreach (var monster in _monsters.Values)
        {
            views[index++] = ToMonsterView(monster);
        }

        foreach (var npc in _npcs.Values)
        {
            views[index++] = npc.ToView();
        }

        return views;
    }

    private static EntityView ToMonsterView(MonsterInstance m) => new(
        m.EntityId,
        EntityKindView.Monster,
        isSelf: false,
        m.Position,
        m.Facing,
        ToMoveState(m.Motion),
        level: m.Def.Combat.Level,
        hp: m.Life,
        maxHp: m.Def.Combat.MaxLife,
        appearance: default,
        templateId: (uint)m.Def.Code,
        moveSpeed: m.Def.Ai.MoveSpeed,
        attackRate: 1f); // monsters never scale their attack advance (character.cpp:2862,5031-5037).

    private static MoveStateView ToMoveState(MonsterMotionState motion) => motion switch
    {
        MonsterMotionState.Stand => MoveStateView.Idle,
        MonsterMotionState.Walk => MoveStateView.Walking,
        MonsterMotionState.Run => MoveStateView.Running,
        MonsterMotionState.Attack => MoveStateView.Attack,
        MonsterMotionState.Skill => MoveStateView.Skill,
        MonsterMotionState.Damage => MoveStateView.Damage,
        MonsterMotionState.Dead => MoveStateView.Dead,
        _ => MoveStateView.Idle,
    };
}
