using System.Numerics;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.World.Ai;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Motion;

namespace PristonTale.Application.World;

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

    private Vector3 _target;
    private bool _hasTarget;
    private bool _running;
    // Attack approaches reuse the last explicit movement mode.
    private bool _runMode;

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

    public bool RunMode => _runMode;

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

    // WorldServer serializes this operation with the tick and other world mutations.
    public void SetTarget(Vector3 target, bool running)
    {
        if (!WorldPosition.IsValid(target) || IsDead)
        {
            return;
        }

        // Moving drops the target lock without interrupting a swing already in progress.
        _attackTargetId = 0;
        _attackSustained = false;
        // Height is owned by the authority; client terrain samples are not trusted placement.
        _target = new Vector3(target.X, Position.Y, target.Z);
        _running = running;
        _runMode = running;
        _hasTarget = true;
    }

    public void SetAttackTarget(uint targetEntityId, bool sustained)
    {
        _attackTargetId = targetEntityId;
        _attackSustained = sustained;
        _hasTarget = false;
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
        _hasTarget = false;
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

        if (_attackTargetId != 0)
        {
            // A chase target belongs to the previous field; a ground destination does not.
            _hasTarget = false;
            ClearAttackTarget();
        }

        _inVillage = inVillage;
        _respawnPoint = respawnPoint;
    }

    public Vector3 IntegrateStep(float dt)
    {
        if (!float.IsFinite(dt) || dt <= 0f || IsDead)
        {
            return Position;
        }

        if (IsBusy)
        {
            // Busy actions own MoveState and freeze movement.
            return Position;
        }

        if (!_hasTarget)
        {
            MoveState = MoveStateView.Idle;
            return Position;
        }

        var moveSpeed = _character.Derived.MoveSpeed;
        var speed = _running
            ? Locomotion.RunUnitsPerSecond(moveSpeed)
            : Locomotion.WalkUnitsPerSecond(moveSpeed, _inVillage);
        var step = DestinationMovement.Advance(
            new Vector2(Position.X, Position.Z),
            new Vector2(_target.X, _target.Z),
            speed,
            dt);
        if (step.Direction != Vector2.Zero)
        {
            Facing = MathF.Atan2(step.Direction.X, step.Direction.Y);
        }

        MoveState = _running ? MoveStateView.Running : MoveStateView.Walking;
        return new Vector3(step.Position.X, Position.Y, step.Position.Y);
    }

    public void CommitStep(Vector3 accepted)
    {
        if (WorldPosition.IsValid(accepted))
        {
            Position = accepted;
            if (_hasTarget && DestinationMovement.HasArrived(
                new Vector2(accepted.X, accepted.Z),
                new Vector2(_target.X, _target.Z)))
            {
                _hasTarget = false;
                if (!IsBusy && !IsDead)
                {
                    MoveState = MoveStateView.Idle;
                }
            }
        }
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
        _hasTarget = false;
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

    public void ApproachAttackTarget(Vector3 targetPos, bool running)
    {
        _target = new Vector3(targetPos.X, Position.Y, targetPos.Z);
        _running = running;
        _hasTarget = true;
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
            _hasTarget = false;
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
        _hasTarget = false;
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
