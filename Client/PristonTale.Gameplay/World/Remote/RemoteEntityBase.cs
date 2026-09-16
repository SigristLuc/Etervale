using Microsoft.Xna.Framework;
using PristonTale.Client.Net.Presence;
using PristonTale.Engine3D.Formats;
using PristonTale.Gameplay.Combat;
using PristonTale.Simulation.Characters;

namespace PristonTale.Gameplay.World.Remote;

public delegate bool FloorHeightQuery(float x, float z, out float y);

public abstract class RemoteEntityBase
{
    private Vector3 _renderPosition;
    private Vector3 _drawnPosition;
    private float _renderFacing;
    private Vector3 _targetPosition;
    private float _targetFacing;
    private readonly ActorMotionController _controller = new();
    private MotionCommand _pendingMotion = MotionCommand.None;
    private bool _pendingMotionStartsSound;
    private int _lastHp;
    private int _moveSpeed = 1;
    private float _attackRate = 1f;
    private FloorHeightQuery _sampledFloor;
    private Vector2 _sampledFloorPosition;
    private int _sampledFloorRevision = -1;
    private bool _floorFound;
    private float _floorHeight;

    private const float PositionLerpPerSecond = 12f;
    private const float FacingLerpPerSecond = 12f;
    private const float SnapDistanceSq = 1f;

    protected RemoteEntityBase(RemoteEntityKind kind, Vector3 position, float facing, RemoteMoveState moveState, int hp, int soundCode = 0)
    {
        Kind = kind;
        _renderPosition = _targetPosition = position;
        _drawnPosition = position;
        _renderFacing = _targetFacing = facing;
        _ = _controller.ApplyAuthoritative(ToActorMotion(moveState));
        _lastHp = hp;
        SoundCode = soundCode;
    }

    public RemoteEntityKind Kind { get; }

    public int SoundCode { get; }

    public event Action<RemoteEntityBase, PristonActorMotion> MotionStarted;

    public Vector3 RenderPosition => _renderPosition;

    public Vector3 DrawnPosition => _drawnPosition;

    public Vector3 TargetPosition => _targetPosition;

    public PristonActorMotion Motion => _controller.Current;

    public bool IsDead => _controller.IsDead;

    public void SetTarget(Vector3 position, float facing, RemoteMoveState moveState, int hp)
    {
        _targetPosition = position;
        _targetFacing = facing;

        var motion = ToActorMotion(moveState);
        var command = _controller.ApplyAuthoritative(motion);
        if (command.Kind == MotionCommandKind.None
            && motion == PristonActorMotion.Damage
            && _controller.Current == PristonActorMotion.Damage
            && hp < _lastHp)
        {
            command = _controller.Retrigger(PristonActorMotion.Damage);
        }

        QueueMotion(command, startsSound: true);
        _lastHp = hp;
    }

    public virtual void Update(float dt, FloorHeightQuery floor = null, int floorRevision = 0)
    {
        if (dt > 0f)
        {
            var t = 1f - MathF.Exp(-PositionLerpPerSecond * dt);
            if (Vector3.DistanceSquared(_renderPosition, _targetPosition) <= SnapDistanceSq)
            {
                _renderPosition = _targetPosition;
            }
            else
            {
                _renderPosition = Vector3.Lerp(_renderPosition, _targetPosition, t);
            }

            var tf = 1f - MathF.Exp(-FacingLerpPerSecond * dt);
            _renderFacing = LerpAngle(_renderFacing, _targetFacing, tf);
        }

        var renderY = _renderPosition.Y;
        // Fixed NPCs preserve authored SPC elevation; moving entities follow resident terrain.
        if (floor is not null && !(Kind == RemoteEntityKind.Npc && _renderPosition.Y != 0))
        {
            var planar = new Vector2(_renderPosition.X, _renderPosition.Z);
            if (_sampledFloor != floor || _sampledFloorPosition != planar || _sampledFloorRevision != floorRevision)
            {
                _floorFound = floor(planar.X, planar.Y, out _floorHeight);
                _sampledFloor = floor;
                _sampledFloorPosition = planar;
                _sampledFloorRevision = floorRevision;
            }

            if (_floorFound)
            {
                renderY = _floorHeight;
            }
        }

        var pose = new Vector3(_renderPosition.X, renderY, _renderPosition.Z);
        _drawnPosition = pose;
        var world = Matrix.CreateRotationY(_renderFacing) * Matrix.CreateTranslation(pose);
        ApplyWorld(world);

        if (_pendingMotion.Kind != MotionCommandKind.None)
        {
            var startedMotion = _pendingMotion.Motion;
            var startsSound = _pendingMotionStartsSound;
            ApplyMotionCommand(_pendingMotion);
            _pendingMotion = MotionCommand.None;
            _pendingMotionStartsSound = false;
            if (startsSound)
            {
                MotionStarted?.Invoke(this, startedMotion);
            }
        }
    }

    protected abstract void ApplyWorld(Matrix world);

    protected abstract void ApplyMotionCommand(MotionCommand command);

    public abstract void Dispose();

    private static float LerpAngle(float from, float to, float t)
    {
        var delta = MathHelper.WrapAngle(to - from);
        return from + delta * t;
    }

    public static PristonActorMotion ToActorMotion(RemoteMoveState moveState) => moveState switch
    {
        RemoteMoveState.Walking => PristonActorMotion.Walk,
        RemoteMoveState.Running => PristonActorMotion.Run,
        RemoteMoveState.Attack => PristonActorMotion.Attack,
        RemoteMoveState.Damage => PristonActorMotion.Damage,
        RemoteMoveState.Dead => PristonActorMotion.Dead,
        RemoteMoveState.Skill => PristonActorMotion.Skill,
        RemoteMoveState.Eat => PristonActorMotion.Eat,
        _ => PristonActorMotion.Stand,
    };

    public void SetRates(int moveSpeed, float attackRate)
    {
        var validRate = float.IsFinite(attackRate) && attackRate > 0f ? attackRate : 1f;
        if (_moveSpeed == moveSpeed && _attackRate == validRate)
        {
            return;
        }

        _moveSpeed = moveSpeed;
        _attackRate = validRate;
        if (_pendingMotion.Kind == MotionCommandKind.None)
        {
            _pendingMotion = new MotionCommand(MotionCommandKind.Set, _controller.Current);
            _pendingMotionStartsSound = false;
        }
    }

    public void ApplyCombatEvent(CombatEventInfo combatEvent)
    {
        if (combatEvent.Kind == RemoteCombatEventKind.PotionBegin)
        {
            QueueMotion(_controller.Retrigger(PristonActorMotion.Eat), startsSound: true);
        }
        else if (combatEvent.Kind == RemoteCombatEventKind.SwingBegin && Motion is PristonActorMotion.Attack or PristonActorMotion.Skill)
        {
            QueueMotion(_controller.Retrigger(Motion), startsSound: true);
        }
        else if (combatEvent.Kind == RemoteCombatEventKind.Hit && !combatEvent.Killed && Motion == PristonActorMotion.Damage)
        {
            QueueMotion(_controller.Retrigger(PristonActorMotion.Damage), startsSound: true);
        }
    }

    private void QueueMotion(MotionCommand command, bool startsSound)
    {
        if (command.Kind == MotionCommandKind.None)
        {
            return;
        }

        _pendingMotion = command;
        _pendingMotionStartsSound = startsSound;
    }

    protected float RemoteRateFor(PristonActorMotion motion)
    {
        if (Kind != RemoteEntityKind.Player && motion is PristonActorMotion.Walk or PristonActorMotion.Run)
        {
            return 1f;
        }

        return motion switch
        {
            PristonActorMotion.Walk => AnimationRate.MoveRate(Locomotion.BaseTranslation(_moveSpeed)),
            PristonActorMotion.Run => AnimationRate.MoveRate((Locomotion.BaseTranslation(_moveSpeed) * 460) >> 8),
            PristonActorMotion.Attack or PristonActorMotion.Skill => _attackRate,
            _ => 1f
        };
    }
}
