using System.Numerics;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.World.Ai;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Motion;

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
}

public sealed class WorldPlayer
{
    public const float MeleeRange = MeleeReach.PlayerBaseRange;

    public float AttackRange => PlayerAttackReach.For(_character);

    private readonly PlayerCharacter _character;
    private bool _inVillage;
    private Vector3 _respawnPoint;

    // The client owns locomotion as in the original TRANSPLAYDATA flow. Reports spend a distance budget that refills at
    // 1.35 x run speed and holds at most two seconds of it plus 32 units, so many small reports in one tick cannot add
    // up to more than one long report could.
    private const float ReportSpeedTolerance = 1.35f;
    private const float ReportDistanceSlack = 32f;
    private const float FirstReportSeconds = 0.5f;
    private const float MaxBudgetSeconds = 2f;
    private const float BusyCorrectionDistance = 192f;
    private const long StaleMotionMs = 1500;
    // Reports may climb this much at once (a long report over stairs); falls of any height are accepted. The client
    // still steps at most GroundTraversal.StepUp (10 units) per frame; only server-moved monsters climb 20.
    private const float ReportClimb = 64f;
    // Every report is checked against walls. One up to this long follows the client's wall slide and may end this far
    // from the reported point (a corner cut between reports); the server then keeps the slid point on its side of the
    // wall. Longer reports may not cross a wall at all.
    private const float WallCheckDistance = 48f;
    private const float CornerCutTolerance = 8f;
    private long _lastReportMs = -1;
    private float _moveBudget;
    private long _budgetMs = -1;

    private uint _attackTargetId;
    private bool _attackSustained;
    private float _respawnMsLeft;

    // A swing hits only when elapsed time crosses its authored event offset.
    private float _swingElapsedMs;
    private float _swingDurationMs;
    private float _swingHitAtMs;
    private bool _swingHitFired;
    private uint _swingTargetId;

    private PotionDefinition? _pendingPotion;
    private float _potionElapsedMs;
    private float _potionDurationMs;
    private float _potionEventMs;
    private bool _potionApplied;

    public bool HasPendingPotion => _pendingPotion is not null || Action == PlayerActionState.Eat;

    public void QueuePotion(PotionDefinition potion)
    {
        if (!IsDead && !HasPendingPotion)
        {
            _pendingPotion = potion;
        }
    }

    public bool BeginPendingPotion(float durationMs, float eventMs)
    {
        if (IsDead || IsBusy || _pendingPotion is null)
        {
            return false;
        }

        Action = PlayerActionState.Eat;
        MoveState = MoveStateView.Eat;
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
        }

        if (_potionElapsedMs >= _potionDurationMs)
        {
            _pendingPotion = null;
            Action = PlayerActionState.Idle;
            MoveState = MoveStateView.Idle;
        }

        return restored;
    }

    private float _flinchMsLeft;

    public WorldPlayer(uint entityId, Vector3 position, PlayerCharacter character, bool inVillage, PlayerAppearanceView appearance)
    {
        if (!WorldPosition.IsValid(position))
        {
            throw new ArgumentOutOfRangeException(nameof(position));
        }

        EntityId = entityId;
        Position = position;
        _character = character;
        _inVillage = inVillage;
        _respawnPoint = position;
        _appearance = appearance;
    }

    public uint EntityId { get; }
    public Vector3 Position { get; private set; }
    public float Facing { get; private set; }
    public MoveStateView MoveState { get; private set; } = MoveStateView.Idle;

    public uint AttackTargetId => _attackTargetId;
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

    private readonly PlayerAppearanceView _appearance;
    public PlayerAppearanceView Appearance => _appearance with
    {
        WeaponKey = _character.GetEquipped(EquipmentSlot.Weapon)?.Definition.CatalogKey ?? "",
        ArmorKey = _character.GetEquipped(EquipmentSlot.Armor)?.Definition.CatalogKey ?? "",
        OffHandKey = _character.GetEquipped(EquipmentSlot.OffHand)?.Definition.CatalogKey ?? "",
        InVillage = _inVillage
    };

    public int Level => _character.Level;
    public int Hp => _character.Life;
    public int MaxHp => _character.MaxLife;

    public PlayerCharacter Character => _character;

    // WorldServer serializes this with the tick. Height stays authoritative; client terrain samples are not trusted.
    public MovementVerdict ReportMovement(Vector3 position, float facing, bool moving, bool running, uint actionTarget, long nowMs,
        FieldTerrain? terrain = null)
    {
        if (!WorldPosition.IsValid(position) || !float.IsFinite(facing) || IsDead)
        {
            return IsDead ? MovementVerdict.Ignored : MovementVerdict.Rejected;
        }

        float dx = position.X - Position.X;
        float dz = position.Z - Position.Z;
        float distance = MathF.Sqrt(dx * dx + dz * dz);
        if (IsBusy)
        {
            // Swings, flinches and potions own the avatar; small drift is tolerated, large drift is corrected.
            return distance > BusyCorrectionDistance ? MovementVerdict.Rejected : MovementVerdict.Ignored;
        }

        if (distance > RefillMoveBudget(nowMs))
        {
            return MovementVerdict.Rejected;
        }

        // The original rsCheckPlayField only reported players off the floor; Etervale rejects the report instead.
        terrain ??= FieldTerrain.None;
        var planar = new Vector3(position.X, Position.Y, position.Z);
        var reached = planar;
        if (distance > WallCheckDistance)
        {
            if (terrain.CrossesWall(Position, planar))
            {
                return MovementVerdict.Rejected;
            }
        }
        else if (distance > 0f)
        {
            reached = terrain.Slide(Position, planar);
            float missX = reached.X - planar.X;
            float missZ = reached.Z - planar.Z;
            if ((missX * missX) + (missZ * missZ) > CornerCutTolerance * CornerCutTolerance)
            {
                return MovementVerdict.Rejected;
            }
        }

        if (!terrain.TryFloorNear(reached, ReportClimb, out var floor))
        {
            return MovementVerdict.Rejected;
        }

        _moveBudget -= distance;

        if (moving && actionTarget == 0 && _attackTargetId != 0)
        {
            // A move without an action target is a ground click, which drops the attack lock.
            ClearAttackTarget();
        }

        _lastReportMs = nowMs;
        Position = new Vector3(reached.X, floor, reached.Z);
        Facing = facing;

        MoveState = !moving ? MoveStateView.Idle : running ? MoveStateView.Running : MoveStateView.Walking;
        return MovementVerdict.Accepted;
    }

    private float RefillMoveBudget(long nowMs)
    {
        float rate = Locomotion.RunUnitsPerSecond(_character.Derived.MoveSpeed) * ReportSpeedTolerance;
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
        if (!IsBusy && !IsDead && MoveState is MoveStateView.Walking or MoveStateView.Running
            && _lastReportMs >= 0 && nowMs - _lastReportMs > StaleMotionMs)
        {
            MoveState = MoveStateView.Idle;
        }
    }

    public void SetAttackTarget(uint targetEntityId, bool sustained)
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
        _pendingPotion = null;
        Action = PlayerActionState.Idle;
        MoveState = MoveStateView.Idle;
    }

    public void EnterField(Vector3 position, bool inVillage, Vector3 respawnPoint)
    {
        if (!WorldPosition.IsValid(respawnPoint))
        {
            throw new ArgumentOutOfRangeException(nameof(respawnPoint));
        }

        Teleport(position);
        _inVillage = inVillage;
        _respawnPoint = respawnPoint;
    }

    public void CrossGate(bool inVillage, Vector3 respawnPoint)
    {
        if (!WorldPosition.IsValid(respawnPoint))
        {
            throw new ArgumentOutOfRangeException(nameof(respawnPoint));
        }

        // A chase target belongs to the previous field.
        ClearAttackTarget();

        _inVillage = inVillage;
        _respawnPoint = respawnPoint;
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

    public void BeginSwing(uint targetId, Vector3 targetPos, float durationMs, float hitAtMs)
    {
        Facing = MathF.Atan2(targetPos.X - Position.X, targetPos.Z - Position.Z);
        Action = PlayerActionState.Swing;
        MoveState = MoveStateView.Attack;
        _swingElapsedMs = 0f;
        _swingDurationMs = MathF.Max(durationMs, 1f);
        _swingHitAtMs = hitAtMs;
        _swingHitFired = false;
        _swingTargetId = targetId;
        _swingAttackSequence = AttackSequence;
    }

    public bool TickAction(float dtMs, out uint hitTargetId)
    {
        hitTargetId = 0;

        switch (Action)
        {
            case PlayerActionState.Flinch:
                _flinchMsLeft -= dtMs;
                if (_flinchMsLeft <= 0f)
                {
                    Action = PlayerActionState.Idle;
                    MoveState = MoveStateView.Idle;
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
                    MoveState = MoveStateView.Idle;
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
        if (amount <= 0 || IsDead)
        {
            return;
        }

        int hp = _character.Life - amount;
        if (hp <= 0)
        {
            _character.Life = 0;
            IsDead = true;
            _pendingPotion = null;
            Action = PlayerActionState.Idle;
            _attackTargetId = 0;
            _attackSustained = false;
            _respawnMsLeft = RespawnDelayMs;
            MoveState = MoveStateView.Dead;
        }
        else
        {
            _character.Life = hp;
            if (Action == PlayerActionState.Idle && amount > 1)
            {
                Action = PlayerActionState.Flinch;
                var clip = PlayerMotionFallback.GetClip(_character.Class.Class, MotionStateCode.Damage);
                _flinchMsLeft = MotionTiming.DurationMs(clip.Frames, MotionTiming.DamageRateFactor);
                MoveState = MoveStateView.Damage;
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

    public bool TickRespawn(float dtMs)
    {
        if (!IsDead)
        {
            return false;
        }

        _respawnMsLeft -= dtMs;
        if (_respawnMsLeft > 0f)
        {
            return false;
        }

        Position = _respawnPoint;
        _character.Life = MaxHp;
        _character.Mana = _character.MaxMana;
        _character.Stamina = _character.MaxStamina;
        IsDead = false;
        _lastReportMs = -1;
        _budgetMs = -1;
        _attackTargetId = 0;
        MoveState = MoveStateView.Idle;
        return true;
    }

    public const float RespawnDelayMs = 5000f;

    internal static (float DurationMs, float HitAtMs) SwingTimingFallback(int attackSpeed)
    {
        float rate = AnimationRate.AttackRate(attackSpeed);
        return (MotionTiming.DurationMs(PlayerMotionFallback.SwingFrames, rate),
                MotionTiming.DurationMs(PlayerMotionFallback.HitFrames, rate));
    }

    public EntityView ToView() => new(
        EntityId, EntityKindView.Player, isSelf: false, Position, Facing, MoveState,
        Level, Hp, MaxHp, Appearance,
        moveSpeed: _character.Derived.MoveSpeed,
        attackRate: AnimationRate.AttackRate(_character.Derived.AttackSpeed),
        attackTargetId: AttackTargetId, attackSequence: AttackSequence);
}
