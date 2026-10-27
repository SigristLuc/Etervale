using System.Numerics;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Skills;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Simulation.World.Ai;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Social;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Motion;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.World;

namespace PristonTale.Application.World;

public enum MovementVerdict
{
    Accepted,
    Ignored,
    Rejected
}

public enum PlayerActionState
{
    Idle = 0,

    Swing = 1,

    Flinch = 2,

    Eat = 3,

    Cast = 4,

    Yahoo = 5,
}

public sealed class WorldPlayer
{
    public const float MeleeRange = MeleeReach.PlayerBaseRange;

    public float AttackRange => PlayerAttackReach.For(_character);

    // A game master may dash (Shift held while running); its movement budget allows that speed.
    public int GameMasterLevel { get; init; }

    private readonly PlayerCharacter _character;
    private bool _inVillage;

    // The client owns locomotion and reports it with MovementReport. Reports spend a distance budget that refills at
    // 1.35 x run speed and holds at most two seconds of it plus 32 units, so many small reports in one tick cannot add
    // up to more than one long report could.
    private const float ReportSpeedTolerance = 1.35f;
    private const float ReportDistanceSlack = 32f;
    private const float FirstReportSeconds = 0.5f;
    private const float MaxBudgetSeconds = 2f;
    private const float BusyCorrectionDistance = 192f;
    private const long StaleMotionMs = 1500;
    // How far above the server's height a floor still counts as the one the player stands on where a report starts; falls
    // of any height are accepted.
    private const float ReportClimb = 64f;
    // The most each 8-unit step of a report may climb: the client steps up at most GroundTraversal.StepUp (10 units)
    // every 4 units, so 20, with room for the two sides sampling the ground at different points. Allowing ReportClimb at
    // every step let one report scale 64 units per step, up any cliff without a wall face.
    private const float StepClimb = 24f;
    // Every report is walked in pieces no longer than this, each following the client's wall slide from the floor the
    // last piece stood on, so the rising ground of a ramp is not taken for a wall. A piece may end this far from its
    // point on the reported path (a corner cut between reports); the server then keeps the slid point on its side of
    // the wall.
    private const float WallCheckDistance = 48f;
    private const float CornerCutTolerance = 8f;
    private long _lastReportMs = -1;
    private float _moveBudget;
    private long _budgetMs = -1;

    private ulong _attackTargetId;
    private bool _attackSustained;

    // A swing hits only when elapsed time crosses its authored event offset.
    private float _swingElapsedMs;
    private float _swingDurationMs;
    private float _swingHitAtMs;
    private bool _swingHitFired;
    private ulong _swingTargetId;

    private PotionDefinition? _pendingPotion;
    private float _potionElapsedMs;
    private float _potionDurationMs;
    private float _potionEventMs;
    private bool _potionApplied;
    private bool _potionCheers;

    public bool HasPendingPotion => _pendingPotion is not null || Action == PlayerActionState.Eat;

    // A queued cheer starts once the motion in progress ends (FieldInstance gives it the clip's length).
    private bool _yahooPending;
    private float _yahooMsLeft;
    private long _yahooKeyUntilMs = long.MinValue;

    public bool HasPendingYahoo => _yahooPending;

    // Quest work waiting for the next upkeep, notices and dialogs for the owner, and duel bookkeeping.
    public PlayerQuestState QuestState { get; } = new();

    internal long AgingCountedAtMs { get; set; } = -1;
    internal float AgingWornMs { get; set; }
    internal long AgingCheckAtMs { get; set; }

    // A potion that cheers (the tiger capsule) ends its eating motion at the event frame and cheers instead.
    public void QueuePotion(PotionDefinition potion, bool cheers = false)
    {
        if (!IsDead && !HasPendingPotion)
        {
            _pendingPotion = potion;
            _potionCheers = cheers;
        }
    }

    public void QueueYahoo()
    {
        if (!IsDead)
        {
            _yahooPending = true;
        }
    }

    // A yahoo word in the chat (key false) opens the Space window and cheers; Space (key true) cheers inside the window
    // for its stamina. Either one cheers only from standing, walking or running.
    public bool AskYahoo(bool key, long nowMs)
    {
        if (!key)
        {
            _yahooKeyUntilMs = nowMs + YahooRules.KeyWindowMs;
        }
        else if (nowMs >= _yahooKeyUntilMs)
        {
            return false;
        }

        if (IsDead || IsBusy || _yahooPending || MoveState is not (MotionState.Stand or MotionState.Walk or MotionState.Run))
        {
            return false;
        }

        if (key)
        {
            var cost = YahooRules.KeyStaminaCost(_character.MaxStamina);
            if (_character.Stamina <= cost)
            {
                return false;
            }

            _character.Stamina -= cost;
        }

        _yahooPending = true;
        return true;
    }

    public bool BeginPendingYahoo(float durationMs)
    {
        if (IsDead || IsBusy || !_yahooPending)
        {
            return false;
        }

        _yahooPending = false;
        Action = PlayerActionState.Yahoo;
        MoveState = MotionState.Yahoo;
        _yahooMsLeft = MathF.Max(1f, durationMs);
        return true;
    }

    public bool BeginPendingPotion(float durationMs, float eventMs)
    {
        if (IsDead || IsBusy || _pendingPotion is null)
        {
            return false;
        }

        Action = PlayerActionState.Eat;
        MoveState = MotionState.Eat;
        _potionElapsedMs = 0f;
        _potionDurationMs = MathF.Max(1f, durationMs);
        _potionEventMs = Math.Clamp(eventMs, 0f, _potionDurationMs);
        _potionApplied = false;
        return true;
    }

    public PotionResource TickPotion(float dtMs, ISimRng random)
    {
        if (Action != PlayerActionState.Eat || IsDead || _pendingPotion is null)
        {
            return PotionResource.None;
        }

        _potionElapsedMs += dtMs;
        var restored = PotionResource.None;
        if (!_potionApplied && _potionElapsedMs >= _potionEventMs)
        {
            _potionApplied = true;
            restored = _pendingPotion.Restore(_character, random);
            if (_potionCheers)
            {
                _potionElapsedMs = _potionDurationMs;
                _yahooPending = true;
            }
        }

        if (_potionElapsedMs >= _potionDurationMs)
        {
            _pendingPotion = null;
            Action = PlayerActionState.Idle;
            MoveState = MotionState.Stand;
        }

        return restored;
    }

    private float _flinchMsLeft;

    // The length of the damage motion: the class fallback until the field sets the body's authored clip (FieldInstance.Add).
    public float FlinchMs { get; set; }

    // A skill motion owns the avatar like a swing; FieldSkills fires its hits from the elapsed time.
    private float _castElapsedMs;
    private float _castDurationMs;

    public PlayerSkillCooldowns Cooldowns { get; } = new();
    public RegenerationState Regeneration { get; } = new();
    public float CastElapsedMs => _castElapsedMs;
    public bool CastCompleted { get; private set; }
    public bool IsCasting => Action == PlayerActionState.Cast;

    // Casts of each skill this session; every fifth adds proficiency to the skill's saved use counter.
    public Dictionary<uint, int> SkillCasts { get; } = new();

    // The monster this player last attacked or cast at (its familiar and passive summon fight it).
    public ulong CombatTargetId { get; set; }

    // A released charge ends the cast motion earlier.
    public void ShortenCast(float durationMs)
    {
        if (IsCasting)
        {
            _castDurationMs = MathF.Max(_castElapsedMs, MathF.Max(1f, durationMs));
        }
    }

    public void BeginCast(Vector3 facingTarget, float durationMs)
    {
        if (facingTarget != Position)
        {
            Facing = MathF.Atan2(facingTarget.X - Position.X, facingTarget.Z - Position.Z);
        }

        Action = PlayerActionState.Cast;
        MoveState = MotionState.Skill;
        CastCompleted = false;
        _castElapsedMs = 0f;
        _castDurationMs = MathF.Max(1f, durationMs);
        ClearAttackTarget();
    }

    public RegenerationPosture Posture => IsDead ? RegenerationPosture.Acting : MoveState switch
    {
        MotionState.Stand => RegenerationPosture.Standing,
        MotionState.Walk => RegenerationPosture.Walking,
        MotionState.Run => RegenerationPosture.Running,
        _ => RegenerationPosture.Acting
    };

    public WorldPlayer(ulong entityId, Vector3 position, PlayerCharacter character, bool inVillage, PlayerAppearanceView appearance)
    {
        if (!WorldPosition.IsValid(position))
        {
            throw new ArgumentOutOfRangeException(nameof(position));
        }

        EntityId = entityId;
        Position = position;
        _character = character;
        FlinchMs = MotionTiming.DurationMs(PlayerMotionFallback.GetClip(character.Class.Class, MotionCode.Damage).Frames, MotionTiming.DamageRateFactor);
        _inVillage = inVillage;
        _appearance = appearance;
        _cueLevel = character.Level;
        _cueSkillRevision = character.Skills.Revision;
        _cueRanks = TrainedRanks();
    }

    private int _cueLevel;
    private long _cueSkillRevision;
    private int _cueRanks;

    // Whether the player gained a level or trained a skill rank since the last call, for the effects everyone near sees.
    public void TakeProgressCues(out bool leveledUp, out bool learned)
    {
        leveledUp = _character.Level > _cueLevel;
        _cueLevel = _character.Level;
        learned = false;
        if (_character.Skills.Revision != _cueSkillRevision)
        {
            _cueSkillRevision = _character.Skills.Revision;
            var ranks = TrainedRanks();
            learned = ranks > _cueRanks;
            _cueRanks = ranks;
        }
    }

    private int TrainedRanks()
    {
        var total = 0;
        foreach (var rank in _character.Skills.Ranks.Values)
        {
            total += rank;
        }

        return total;
    }

    public ulong EntityId { get; }
    public Vector3 Position { get; private set; }
    public float Facing { get; private set; }
    public MotionState MoveState { get; private set; } = MotionState.Stand;

    public ulong AttackTargetId => _attackTargetId;
    public ulong AttackSequence { get; private set; }
    private ulong _swingAttackSequence;

    public bool AcceptAttackSequence(ulong sequence)
    {
        if (sequence != 0 && sequence < AttackSequence)
        {
            return false;
        }

        AttackSequence = sequence == 0 ? AttackSequence + 1 : sequence;
        return true;
    }

    public bool AttackSustained => _attackSustained;

    public bool IsDead { get; private set; }

    public PlayerActionState Action { get; private set; }

    public bool IsBusy => Action != PlayerActionState.Idle;

    private PlayerAppearanceView _appearance;
    public PlayerAppearanceView Appearance => _appearance with
    {
        WeaponKey = _character.GetEquipped(EquipmentSlot.Weapon)?.Definition.CatalogKey ?? "",
        ArmorKey = _character.GetEquipped(EquipmentSlot.Armor)?.Definition.CatalogKey ?? "",
        OffHandKey = _character.GetEquipped(EquipmentSlot.OffHand)?.Definition.CatalogKey ?? "",
        WeaponGlow = HeldItemGlow.For(_character.GetEquipped(EquipmentSlot.Weapon)),
        OffHandGlow = HeldItemGlow.For(_character.GetEquipped(EquipmentSlot.OffHand)),
        InVillage = _inVillage,
        Tier = _character.Tier,
        HelperPet = PremiumPets.HelperPet(_character.Premium) is { } pet ? pet - PremiumEffectKind.HelpPetTerry + 1 : 0,
        Phoenix = _character.Premium.IsActive(PremiumEffectKind.PhoenixPet),
        BigHead = _character.Premium.BigHeadMode
    };

    // A hair tint changes the head every viewer draws.
    public void ChangeHair(int hairIndex)
    {
        var current = _appearance;
        _appearance = new PlayerAppearanceView(current.Name, current.Class, current.Tier, current.FaceIndex, hairIndex, current.ModelName, current.ModelName2);
    }

    // Damage is ignored while the guard runs (revives, invulnerability items).
    public void Guard(float durationMs) => GuardMsLeft = MathF.Max(GuardMsLeft, durationMs);

    public int Level => _character.Level;
    public int Hp => _character.Life;
    public int MaxHp => _character.MaxLife;

    public PlayerCharacter Character => _character;

    // WorldServer serializes this with the tick. Height stays authoritative; client terrain samples are not trusted.
    public MovementVerdict ReportMovement(Vector3 position, float facing, bool moving, bool running, ulong actionTarget, long nowMs,
        FieldTerrain? terrain = null)
    {
        if (!WorldPosition.IsValid(position) || !float.IsFinite(facing) || IsDead)
        {
            return IsDead ? MovementVerdict.Ignored : MovementVerdict.Rejected;
        }

        float dx = position.X - Position.X;
        float dz = position.Z - Position.Z;
        float distance = MathF.Sqrt(dx * dx + dz * dz);
        if (IsBusy || _character.Effects.Has(Simulation.Modifiers.ControlFlags.Stun))
        {
            // Swings, flinches, potions, cheers and stuns own the avatar; small drift is tolerated, large drift is corrected.
            return distance > BusyCorrectionDistance ? MovementVerdict.Rejected : MovementVerdict.Ignored;
        }

        if (distance > RefillMoveBudget(nowMs))
        {
            return MovementVerdict.Rejected;
        }

        // The classic server only flagged players found off the floor; Etervale rejects the report instead.
        // A player off a ledge falls as the client shows it (Falling) from the last accepted report, so the walls below are
        // checked at the height the player is falling past rather than at the floor it will land on.
        terrain ??= FieldTerrain.None;
        var pieces = Math.Max(1, (int)MathF.Ceiling(distance / WallCheckDistance));
        var fallSeconds = _lastReportMs < 0 ? float.PositiveInfinity : Math.Max(0L, nowMs - _lastReportMs) / 1000f / pieces;
        var reached = Position;
        for (var piece = 1; piece <= pieces; piece++)
        {
            var along = piece / (float)pieces;
            var target = new Vector3(Position.X + (dx * along), reached.Y, Position.Z + (dz * along));
            var slid = terrain.Slide(reached, target);
            float missX = slid.X - target.X;
            float missZ = slid.Z - target.Z;
            if ((missX * missX) + (missZ * missZ) > CornerCutTolerance * CornerCutTolerance
                || !TryFollowFloor(terrain, reached, slid, fallSeconds, piece == pieces, out reached))
            {
                return MovementVerdict.Rejected;
            }
        }

        _moveBudget -= distance;

        if (moving && actionTarget == 0 && _attackTargetId != 0)
        {
            // A move without an action target is a ground click, which drops the attack lock.
            ClearAttackTarget();
        }

        _lastReportMs = nowMs;
        Position = reached;
        Facing = facing;

        // A character over the weight limit only walks, whatever the client reports.
        var runs = running && !CarriedWeight.IsOverweight(_character);
        MoveState = !moving ? MotionState.Stand : runs ? MotionState.Run : MotionState.Walk;
        return MovementVerdict.Accepted;
    }

    // Walks one piece in steps of the snap distance, as the client follows the ground each frame: a floor within the
    // snap distance below is stood on, a deeper one is fallen toward for the piece's share of the time. A falling body
    // may pass over ground with no floor (water, a gap between ledges), but the report must end over a floor.
    private static bool TryFollowFloor(FieldTerrain terrain, Vector3 from, Vector3 to, float seconds, bool last, out Vector3 reached)
    {
        reached = from;
        var length = new Vector2(to.X - from.X, to.Z - from.Z).Length();
        var steps = Math.Max(1, (int)MathF.Ceiling(length / Falling.SnapDistance));
        var stepSeconds = seconds / steps;
        var height = from.Y;
        var airborne = !terrain.TryFloorNear(from, ReportClimb, out var start) || Falling.IsAirborne(from.Y, start);
        for (var step = 1; step <= steps; step++)
        {
            var point = Vector3.Lerp(from, to, step / (float)steps) with { Y = height };
            if (!terrain.TryFloorNear(point, StepClimb, out var floor))
            {
                if (!airborne || (last && step == steps) || !float.IsFinite(stepSeconds))
                {
                    return false;
                }

                height -= Falling.UnitsPerSecond * stepSeconds;
                reached = point with { Y = height };
                continue;
            }

            airborne = Falling.IsAirborne(height, floor);
            height = airborne ? Falling.Step(height, floor, stepSeconds) : floor;
            reached = point with { Y = height };
        }

        return true;
    }

    private float RefillMoveBudget(long nowMs)
    {
        var moveSpeed = _character.Derived.MoveSpeed;
        float rate = (GameMasterLevel > 0 ? Locomotion.GameMasterDashUnitsPerSecond
                : CarriedWeight.IsOverweight(_character) ? Locomotion.WalkUnitsPerSecond(moveSpeed, true) : Locomotion.RunUnitsPerSecond(moveSpeed))
            * ReportSpeedTolerance;
        if (_budgetMs < 0)
        {
            _moveBudget = rate * FirstReportSeconds + ReportDistanceSlack;
        }
        else
        {
            float refill = rate * Math.Max(0L, nowMs - _budgetMs) / 1000f;
            _moveBudget = MathF.Min(rate * MaxBudgetSeconds + ReportDistanceSlack, _moveBudget + refill);
        }

        _budgetMs = nowMs;
        return _moveBudget;
    }

    // A client that stops reporting while moving is shown standing, like the original's 1.5 s motion cut-off.
    public void ExpireStaleMotion(long nowMs)
    {
        if (!IsBusy && !IsDead && MoveState is MotionState.Walk or MotionState.Run
            && _lastReportMs >= 0 && nowMs - _lastReportMs > StaleMotionMs)
        {
            MoveState = MotionState.Stand;
        }
    }

    public void SetAttackTarget(ulong targetEntityId, bool sustained)
    {
        _attackTargetId = targetEntityId;
        _attackSustained = sustained;
    }

    public void ClearAttackTarget()
    {
        _attackTargetId = 0;
        _attackSustained = false;
    }

    public void Teleport(Vector3 position)
    {
        if (!WorldPosition.IsValid(position))
        {
            throw new ArgumentOutOfRangeException(nameof(position));
        }

        Position = position;
        ClearAttackTarget();
        _lastReportMs = -1;
        _budgetMs = -1;
        // A paid potion that has not restored yet is drunk again where the player arrives; one that already restored is done.
        if (Action == PlayerActionState.Eat && _potionApplied)
        {
            _pendingPotion = null;
        }

        _yahooPending = false;
        Action = PlayerActionState.Idle;
        MoveState = MotionState.Stand;
    }

    public void EnterField(Vector3 position, bool inVillage)
    {
        Teleport(position);
        _inVillage = inVillage;
    }

    public void CrossGate(bool inVillage)
    {
        // A chase target belongs to the previous field.
        ClearAttackTarget();

        _inVillage = inVillage;
    }

    public float DistanceSqTo(Vector3 worldPos)
    {
        float dx = worldPos.X - Position.X;
        float dz = worldPos.Z - Position.Z;
        return (dx * dx) + (dz * dz);
    }

    public bool InAttackRange(Vector3 worldPos)
    {
        float range = AttackRange;
        return DistanceSqTo(worldPos) <= range * range && MathF.Abs(worldPos.Y - Position.Y) <= 64f;
    }

    public void BeginSwing(ulong targetId, Vector3 targetPos, float durationMs, float hitAtMs)
    {
        Facing = MathF.Atan2(targetPos.X - Position.X, targetPos.Z - Position.Z);
        Action = PlayerActionState.Swing;
        MoveState = MotionState.Attack;
        _swingElapsedMs = 0f;
        _swingDurationMs = MathF.Max(durationMs, 1f);
        _swingHitAtMs = hitAtMs;
        _swingHitFired = false;
        _swingTargetId = targetId;
        _swingAttackSequence = AttackSequence;
        CombatTargetId = targetId;
    }

    public bool TickAction(float dtMs, out ulong hitTargetId)
    {
        hitTargetId = 0;

        switch (Action)
        {
            case PlayerActionState.Flinch:
                _flinchMsLeft -= dtMs;
                if (_flinchMsLeft <= 0f)
                {
                    Action = PlayerActionState.Idle;
                    MoveState = MotionState.Stand;
                }

                return false;

            case PlayerActionState.Yahoo:
                _yahooMsLeft -= dtMs;
                if (_yahooMsLeft <= 0f)
                {
                    Action = PlayerActionState.Idle;
                    MoveState = MotionState.Stand;
                }

                return false;

            case PlayerActionState.Cast:
                _castElapsedMs += dtMs;
                if (_castElapsedMs >= _castDurationMs)
                {
                    CastCompleted = true;
                    Action = PlayerActionState.Idle;
                    MoveState = MotionState.Stand;
                }

                return false;

            case PlayerActionState.Swing:
                float before = _swingElapsedMs;
                _swingElapsedMs += dtMs;

                bool hit = false;
                if (!_swingHitFired && _swingHitAtMs > before && _swingHitAtMs <= _swingElapsedMs)
                {
                    _swingHitFired = true;
                    hitTargetId = _swingTargetId;
                    hit = true;
                }

                if (_swingElapsedMs >= _swingDurationMs)
                {
                    if (!_swingHitFired)
                    {
                        // Malformed offsets past the clip still land once at completion.
                        _swingHitFired = true;
                        hitTargetId = _swingTargetId;
                        hit = true;
                    }

                    Action = PlayerActionState.Idle;
                    MoveState = MotionState.Stand;
                    if (!_attackSustained && _swingAttackSequence == AttackSequence)
                    {
                        ClearAttackTarget();
                    }
                }

                return hit;

            default:
                return false;
        }
    }

    public void ApplyDamage(int amount)
    {
        if (amount <= 0 || IsDead || GuardMsLeft > 0f)
        {
            return;
        }

        int hp = _character.Life - amount;
        if (hp <= 0)
        {
            _character.Life = 0;
            IsDead = true;
            _pendingPotion = null;
            _yahooPending = false;
            Action = PlayerActionState.Idle;
            _attackTargetId = 0;
            _attackSustained = false;
            DeadMs = 0f;
            MoveState = MotionState.Dead;
        }
        else
        {
            _character.Life = hp;
            if (Action == PlayerActionState.Idle && amount > 1)
            {
                Action = PlayerActionState.Flinch;
                _flinchMsLeft = FlinchMs;
                MoveState = MotionState.Damage;
            }
        }
    }

    public void Heal(int amount)
    {
        if (amount <= 0)
        {
            return;
        }
        _character.Life = Math.Min(MaxHp, _character.Life + amount);
    }

    // Real-clock milliseconds before which billing crystals and soul stones refuse (every crystal use restarts it).
    public long CrystalDelayUntilMs { get; set; }

    // Milliseconds since the player died.
    public float DeadMs { get; private set; }

    // A dead player stays down until it chooses where to stand up (RestartTransactions) or a revive reaches it.
    public void TickDead(float dtMs)
    {
        if (IsDead)
        {
            DeadMs += dtMs;
        }
    }

    // Stands a dead player up where the world placed it (EnterField first), with this much life, guarded for a while.
    // Mana and stamina stay as they were, as the original's restart left them.
    public void Restart(int life, float guardMs)
    {
        Revive(life, guardMs);
        _lastReportMs = -1;
        _budgetMs = -1;
    }

    // Time after a revive in which no damage lands, as the original's restart grace.
    public float GuardMsLeft { get; private set; }

    public void TickGuard(float dtMs) => GuardMsLeft = MathF.Max(0f, GuardMsLeft - dtMs);

    // Brings a dead player back where it fell with this much life (a revive skill), guarded for a while.
    public void Revive(int life, float guardMs)
    {
        if (!IsDead)
        {
            return;
        }

        _character.Life = Math.Clamp(life, 1, MaxHp);
        IsDead = false;
        _attackTargetId = 0;
        MoveState = MotionState.Stand;
        Action = PlayerActionState.Idle;
        GuardMsLeft = MathF.Max(0f, guardMs);
    }

    internal static (float DurationMs, float HitAtMs) SwingTimingFallback(int attackSpeed)
    {
        float rate = AnimationRate.AttackRate(attackSpeed);
        return (MotionTiming.DurationMs(PlayerMotionFallback.SwingFrames, rate),
                MotionTiming.DurationMs(PlayerMotionFallback.HitFrames, rate));
    }

    public EntityView ToView() => new(
        EntityId, EntityKind.Player, isSelf: false, Position, Facing, MoveState,
        Level, Hp, MaxHp, Appearance,
        moveSpeed: _character.Derived.MoveSpeed,
        attackRate: _character.AttackRate,
        attackTargetId: AttackTargetId, attackSequence: AttackSequence)
    {
        Hidden = Hidden
    };

    // A game master who hid is left out of every other player's view and out of monsters' sight.
    public bool Hidden { get; set; }

    // A game master's kill: the player dies at once, whatever guard is running.
    public bool Kill()
    {
        if (IsDead)
        {
            return false;
        }

        GuardMsLeft = 0f;
        ApplyDamage(Math.Max(1, _character.Life));
        return IsDead;
    }
}
