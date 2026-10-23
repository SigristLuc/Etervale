using PristonTale.Simulation.Modifiers;
using PristonTale.Simulation.World;
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
using PristonTale.Simulation.Skills;

namespace PristonTale.Application.World;

public sealed partial class FieldInstance : ISpawnSink, IPlayerProximity, IFieldCombatHost, IFieldSkillHost
{
    public const int PlayerNearRadiusSquared = 0x120000;
    public static readonly float PlayerNearRadius = MathF.Sqrt(PlayerNearRadiusSquared);

    private readonly Dictionary<uint, WorldPlayer> _players = new();
    private readonly Dictionary<uint, MonsterInstance> _monsters = new();
    private readonly Dictionary<uint, WorldNpc> _npcs = new();

    // Reused scratch (tick thread only) so Step allocates no per-tick garbage on the hot path.
    private readonly List<MonsterInstance> _liveScratch = new();
    private readonly List<MonsterInstance> _summonScratch = new();
    private readonly List<EntityNeighbour> _neighbourScratch = new();
    private readonly FieldPreyView _prey = new();
    private readonly MonsterOccupancy _occupancy;
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
    private readonly FieldSkills _skills;
    private readonly FieldMonsterSkills _monsterSkills;
    private readonly EntityIdAllocator _ids;
    private readonly CombatRng _rng;
    private readonly MonsterCatalog _catalog;
    private readonly MotionTimingCatalog _motions;
    private double _nowMs;

    public FieldInstance(int fieldId)
        : this(fieldId, FieldSpawnData.Empty, new MonsterCatalog([]), new EntityIdAllocator(), new CombatRng(fieldId), terrain: null)
    {
    }

    public FieldInstance(
        int fieldId, FieldSpawnData spawnData, MonsterCatalog catalog, EntityIdAllocator ids, CombatRng rng,
        MotionTimingCatalog? motions = null, NpcCatalog? npcs = null, int experienceMultiplier = 1, ItemCatalog? items = null,
        FieldTerrain? terrain = null)
    {
        FieldId = fieldId;
        Terrain = terrain ?? FieldTerrain.None;
        GroundLoot = new FieldGroundLoot(items ?? new ItemCatalog([]));
        _ids = ids;
        _rng = rng;
        _catalog = catalog;
        _motions = motions ?? MotionTimingCatalog.Empty;
        _ = PlayerEquipmentTiming.Count;
        _occupancy = new MonsterOccupancy(_neighbourScratch);
        _spawner = new FieldSpawner(spawnData, catalog, rng);
        _brain = new MonsterAiBrain(rng);
        _combat = new FieldCombat(this, rng, experienceMultiplier);
        _skills = new FieldSkills(this, rng);
        _combat.SkillCombat = _skills;
        _monsterSkills = new FieldMonsterSkills(this, rng) { Combat = _combat };
        _combat.MonsterSkills = _monsterSkills;
        Premium = new FieldPremium(fieldId, this);
        _combat.PlayerHitLanded = Premium.RecordHit;
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
    public FieldTerrain Terrain { get; }
    internal FieldPremium Premium { get; }
    internal FieldItemEvents ItemEvents { get; } = new();
    internal FieldStatusBook Statuses { get; } = new();
    internal FieldPresenceFrames Frames { get; } = new();
    private readonly FieldDescriptors _descriptors = new();
    public long NowMs => (long)_nowMs;
    public int PlayerCount => _players.Count;
    public int MonsterCount => _monsters.Count;
    public FieldGroundLoot GroundLoot { get; }

    public IReadOnlyCollection<WorldNpc> Npcs => _npcs.Values;
    public bool TryGetNpc(uint entityId, [MaybeNullWhen(false)] out WorldNpc npc) => _npcs.TryGetValue(entityId, out npc);
    public IEnumerable<uint> PlayerIds => _players.Keys;

    public void Add(WorldPlayer player) => _players[player.EntityId] = player;
    public void Remove(uint entityId)
    {
        if (_players.TryGetValue(entityId, out var leaving))
        {
            LeaveCastleItems(leaving);
        }

        _players.Remove(entityId, out _);
        Statuses.Take(entityId);
        _skills.Forget(entityId);
        Premium.Forget(entityId);
    }

    // Validates a cast on the tick thread and starts its motion; the hits follow in Step.
    public SkillCastOutcome CastSkill(WorldPlayer player, in SkillCastRequest request)
    {
        if (!_players.TryGetValue(player.EntityId, out var present) || !ReferenceEquals(present, player))
        {
            return SkillCastOutcome.Refused(request, SkillCastVerdict.Stale);
        }

        return _skills.Cast(player, request);
    }
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

        if (!_monsters.TryGetValue(targetEntityId, out var monster) || !monster.IsAlive || monster.IsSummon
            || monster.Effects.Has(Simulation.Modifiers.ControlFlags.Charmed) || monster.DuelPlayerId != 0 && monster.DuelPlayerId != attackerEntityId)
        {
            attacker.ClearAttackTarget();
            return false;
        }

        attacker.SetAttackTarget(targetEntityId, sustained);
        return true;
    }

    public long ExpFor(uint playerId) => _combat.ExpFor(playerId);

    // A used item's timed effect on a player of this field; it starts with the next step.
    internal void QueueItemEffect(uint entityId, EffectSpec spec, float durationMs, int rank = 1) => _skills.QueueItemEffect(entityId, spec, durationMs, rank);

    // The field's combat resolution, for scenario tests that drive single monster attacks.
    internal FieldCombat Combat => _combat;

    public void Step(float dt, int? scheduledBossHour = null)
    {
        float dtMs = dt * 1000f;
        _nowMs += dtMs;
        long nowMs = (long)_nowMs;
        _events.Clear();
        _skills.BeginTick();
        ItemEvents.BeginTick();
        _prey.Rebuild(_players.Values);
        _preyCurrent = true;
        foreach (var summon in _monsters.Values)
        {
            if (summon.IsSummon)
            {
                // Wild monsters fight a summon only when it hits them; they never pick it by sight.
                _prey.AddTargetOnly(summon.EntityId, summon.Position, summon.IsAlive);
            }
        }

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

        _preyCurrent = false;
        SnapshotLiveMonsters();
        RebuildCollisionIndex();
        for (int i = 0; i < _liveScratch.Count; i++)
        {
            StepMonster(_liveScratch[i], dtMs);
        }

        _monsterSkills.ApplyChanges();
        StepSummons(dtMs);
        ExpireSoulMonsters();

        // Authoritative NPC movement keeps interaction range and presentation aligned.
        foreach (var npc in _npcs.Values)
        {
            StepNpc(npc, dt);
        }

        foreach (var player in _players.Values)
        {
            if (player.IsDead && HoldsForRestoration(player))
            {
                player.TickDeadHeld(dtMs);
                _collisionIndex.Put(player.EntityId, player.Position);
                continue;
            }

            if (player.IsDead)
            {
                player.TickRespawn(dtMs);
                _collisionIndex.Put(player.EntityId, player.Position);
                continue;
            }

            if (player.TickAction(dtMs, out uint hitTargetId))
            {
                if (_combat.ResolvePlayerSwingHit(player, hitTargetId, nowMs) is { } reward)
                {
                    PlaceLoot(reward, nowMs);
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
            StepPlayer(player, nowMs);
        }

        _skills.Step(dtMs);
        foreach (var reward in _skills.Payouts)
        {
            PlaceLoot(reward, nowMs);
        }

        Premium.Step(_players.Values);

        _combat.TickCorpses(dtMs);
        StepQuests(dtMs, nowMs);
        GroundLoot.Step(nowMs);
        Statuses.Apply(_skills.StatusEvents, nowMs);
        Statuses.Prune(nowMs, _isPresent ??= IsPresent);
    }

    private Func<uint, bool>? _isPresent;

    private bool IsPresent(uint entityId) => _players.ContainsKey(entityId) || _monsters.ContainsKey(entityId) || _npcs.ContainsKey(entityId);

    private void PlaceLoot(KillPayout reward, long nowMs)
    {
        reward = CreditQuests(reward);
        if (_monsters.TryGetValue(reward.MonsterEntityId, out var killed))
        {
            var dropPosition = Terrain.SettleNear(killed.Position, FieldTerrain.DropTolerance);
            GroundLoot.Place(reward, dropPosition, killed.Def.Drops.AllPlayersSeeDrops, nowMs, _rng);
        }
    }

    internal SkillEventView[] SnapshotSkillEvents() => _skills.SnapshotSkillEvents();

    internal StatusEventView[] SnapshotStatusEvents() => _skills.SnapshotStatusEvents();

    bool IFieldSkillHost.TryGetPlayer(uint entityId, [MaybeNullWhen(false)] out WorldPlayer player) => _players.TryGetValue(entityId, out player);

    bool IFieldSkillHost.TryGetMonster(uint entityId, [MaybeNullWhen(false)] out MonsterInstance monster) => _monsters.TryGetValue(entityId, out monster);

    IEnumerable<MonsterInstance> IFieldSkillHost.Monsters => _monsters.Values;

    IEnumerable<WorldPlayer> IFieldSkillHost.Players => _players.Values;

    KillPayout? IFieldSkillHost.ApplyPlayerDamage(WorldPlayer player, MonsterInstance monster, int damage, bool crit, bool forceFlinch, uint sourceEntityId) =>
        _combat.ApplyPlayerDamage(player, monster, damage, crit, NowMs, forceFlinch, sourceEntityId);

    bool IFieldSkillHost.TryFindMonsterDefinition(string name, [MaybeNullWhen(false)] out MonsterDefinition definition) =>
        _catalog.TryFind(name, out definition);

    MonsterInstance IFieldSkillHost.AddSummon(MonsterDefinition definition, Vector3 position, float facing)
    {
        var summon = new MonsterInstance(_ids.NextMonsterId(), definition, Terrain.PlaceOnFloor(position), facing, spawnPointIndex: -1);
        _monsters[summon.EntityId] = summon;
        return summon;
    }

    // Summons move with the same collision as wild monsters; their brain takes its orders from their owner.
    private void StepSummons(float dtMs)
    {
        _summonScratch.Clear();
        foreach (var monster in _monsters.Values)
        {
            if (monster.IsSummon)
            {
                _summonScratch.Add(monster);
            }
        }

        foreach (var summon in _summonScratch)
        {
            if (!summon.IsAlive)
            {
                _skills.StepSummon(summon, TuningFor(summon.Def), dtMs);
                _combat.BeginCorpse(summon.EntityId);
                continue;
            }

            var before = summon.Position;
            var attack = _skills.StepSummon(summon, TuningFor(summon.Def), dtMs);
            if (summon.SwingJustBegan)
            {
                summon.SwingJustBegan = false;
                var tuning = TuningFor(summon.Def);
                _events.Add(new CombatEventView(summon.EntityId, summon.TargetId, CombatEventKindView.SwingBegin,
                    summon.AttackIsSkill ? tuning.SkillDurationMs : tuning.CooldownMs, summon.AttackIsSkill ? tuning.SkillWindupMs : tuning.WindupMs));
            }

            MoveMonster(summon, before);
            if (!summon.IsAlive)
            {
                _combat.BeginCorpse(summon.EntityId);
            }
            else if (attack is { } landed)
            {
                _skills.ResolveSummonHit(summon, landed);
            }
        }
    }

    private void StepPlayer(WorldPlayer player, long nowMs)
    {
        player.ExpireStaleMotion(nowMs);
        _collisionIndex.Put(player.EntityId, player.Position);
    }

    private void StepMonster(MonsterInstance monster, float dtMs)
    {
        MonsterAiTuning tuning = TuningFor(monster.Def);

        Vector3 before = monster.Position;
        MonsterAttackEvent? attack = _brain.Tick(monster, tuning, DuelPrey(monster) ?? _prey, dtMs);

        _monsterSkills.AfterTick(monster, tuning, before, dtMs);
        MoveMonster(monster, before);

        if (attack is { } landed)
        {
            _combat.ResolveMonsterHit(monster, landed);
        }
    }

    // Re-resolve AI movement against the stage and the other monsters only, sliding along blockers as the client's
    // step check does.
    private void MoveMonster(MonsterInstance monster, Vector3 before)
    {
        Vector3 desired = monster.Position;
        float dx = desired.X - before.X;
        float dz = desired.Z - before.Z;
        float distance = MathF.Sqrt((dx * dx) + (dz * dz));
        bool moved = false;
        bool crowded = false;
        if (distance > 0f)
        {
            BuildMonsterNeighbours(monster.EntityId, before, distance);
            var result = MonsterStepping.Step(Terrain, before, MathF.Atan2(dx, dz), distance, _occupancy, out var accepted);
            crowded = result == MonsterStepResult.Crowded;
            monster.Position = accepted;
            moved = accepted.X != before.X || accepted.Z != before.Z;
            _collisionIndex.Put(monster.EntityId, accepted);
        }
        else if (desired != before)
        {
            monster.Position = before;
        }

        monster.MovedThisTick = moved;
        monster.Crowded = crowded;
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
        accepted = Terrain.TryStep(before, accepted, out var grounded) ? grounded : before;
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
        var rate = player.Character.AttackRate;
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

    // Live monsters around a mover, for the server's monster-only overlap test (players are not blockers).
    private void BuildMonsterNeighbours(uint selfId, Vector3 origin, float distance)
    {
        _neighbourScratch.Clear();
        _collisionIndex.Query(origin, distance + EntityCollisionRadii.Monster * 2f, _collisionQuery);
        foreach (var entry in _collisionQuery)
        {
            if (entry.Id != selfId && _monsters.TryGetValue(entry.Id, out var other) && other.IsAlive)
            {
                _neighbourScratch.Add(new EntityNeighbour(other.Position, EntityCollisionRadii.Monster));
            }
        }
    }

    private sealed class MonsterOccupancy(List<EntityNeighbour> neighbours) : IStepOccupancy
    {
        public bool IsOccupied(Vector3 position)
        {
            foreach (var neighbour in neighbours)
            {
                float dx = neighbour.Position.X - position.X;
                float dz = neighbour.Position.Z - position.Z;
                float overlap = neighbour.Radius;
                if (MathF.Abs(neighbour.Position.Y - position.Y) < overlap && (dx * dx) + (dz * dz) < overlap * overlap)
                {
                    return true;
                }
            }

            return false;
        }
    }

    // Wild monsters only: summons have their own step and do not count toward the field's population.
    private void SnapshotLiveMonsters()
    {
        _liveScratch.Clear();
        foreach (var m in _monsters.Values)
        {
            if (!m.IsSummon)
            {
                _liveScratch.Add(m);
            }
        }
    }

    MonsterInstance ISpawnSink.Spawn(MonsterDefinition definition, Vector3 position, int spawnPointIndex, float facing)
    {
        uint id = _ids.NextMonsterId();
        var monster = new MonsterInstance(id, definition, Terrain.PlaceOnFloor(position), facing, spawnPointIndex);
        _monsters[id] = monster;
        _monsterSkills.Prepare(monster);
        return monster;
    }

    bool IFieldCombatHost.TryGetPlayer(uint entityId, [MaybeNullWhen(false)] out WorldPlayer player)
        => _players.TryGetValue(entityId, out player);

    bool IFieldCombatHost.TryGetMonster(uint entityId, [MaybeNullWhen(false)] out MonsterInstance monster)
        => _monsters.TryGetValue(entityId, out monster);

    IEnumerable<MonsterInstance> IFieldCombatHost.Monsters => _monsters.Values;

    IReadOnlyList<PreyCandidate> IFieldCombatHost.NearbyPlayers(Vector3 origin, float radius) => _prey.Nearby(origin, radius);

    void IFieldCombatHost.DespawnMonster(uint entityId) => _monsters.Remove(entityId, out _);

    // Asked for every monster and spawn point each tick: during the spawner pass the prey index (players at the start of
    // the step, which they keep until the players' own step) answers it without visiting every player.
    private bool _preyCurrent;

    bool IPlayerProximity.AnyPlayerNear(Vector3 worldPosition)
    {
        if (!_preyCurrent)
        {
            foreach (var player in _players.Values)
            {
                if (player.DistanceSqTo(worldPosition) < PlayerNearRadiusSquared)
                {
                    return true;
                }
            }

            return false;
        }

        foreach (var prey in _prey.Nearby(worldPosition, PlayerNearRadius))
        {
            float dx = prey.Position.X - worldPosition.X;
            float dz = prey.Position.Z - worldPosition.Z;
            if ((dx * dx) + (dz * dz) < PlayerNearRadiusSquared)
            {
                return true;
            }
        }

        return false;
    }

    public int EntityCount => _players.Count + _monsters.Count + _npcs.Count;

    public EntityView[] BuildViews()
    {
        var views = new EntityView[EntityCount];
        FillViews(views);
        return views;
    }

    // Writes every entity's view with its descriptor revision; views must hold exactly EntityCount entries.
    public void FillViews(EntityView[] views)
    {
        _descriptors.BeginTick();
        var index = 0;
        foreach (var player in _players.Values)
        {
            views[index++] = WithRevision(player.ToView());
        }

        foreach (var monster in _monsters.Values)
        {
            views[index++] = WithRevision(ToMonsterView(monster));
        }

        foreach (var npc in _npcs.Values)
        {
            views[index++] = WithRevision(npc.ToView());
        }

        _descriptors.Prune();
    }

    private EntityView WithRevision(in EntityView view) => view with { Revision = _descriptors.RevisionOf(view) };

    private static EntityView ToMonsterView(MonsterInstance m) => new(
        m.EntityId,
        EntityKindView.Monster,
        isSelf: false,
        m.Position,
        m.Facing,
        ToMoveState(m.PresentedMotion),
        level: m.Def.Combat.Level,
        hp: FieldMonsterSkills.ViewLife(m).Life,
        maxHp: FieldMonsterSkills.ViewLife(m).Max,
        appearance: default,
        templateId: (uint)m.Def.Code,
        moveSpeed: m.Def.Ai.MoveSpeed,
        attackRate: 1f) // monsters never scale their attack advance.
    {
        OwnerId = m.OwnerId,
        ModelScale = m.ModelScale,
        DuelPlayerId = m.DuelPlayerId
    };

    private static MoveStateView ToMoveState(MonsterMotionState motion) => motion switch
    {
        MonsterMotionState.Stand => MoveStateView.Idle,
        MonsterMotionState.Walk => MoveStateView.Walking,
        MonsterMotionState.Run => MoveStateView.Running,
        MonsterMotionState.Attack => MoveStateView.Attack,
        MonsterMotionState.Skill => MoveStateView.Skill,
        MonsterMotionState.Damage => MoveStateView.Damage,
        MonsterMotionState.Dead => MoveStateView.Dead,
        MonsterMotionState.Eat => MoveStateView.Eat,
        MonsterMotionState.Warp => MoveStateView.Warp,
        _ => MoveStateView.Idle,
    };
}
