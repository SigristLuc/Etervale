using Microsoft.Xna.Framework;
using PristonTale.Client.Net.Presence;
using PristonTale.Engine3D.Formats;
using PristonTale.Gameplay.Combat;
using PristonTale.Simulation.Characters;

namespace PristonTale.Gameplay.World.Remote;

// The server owns position, height, facing and motion state; a remote entity only plays its samples back through a
// RemoteSnapshotBuffer. The client decides two things for presentation only: Walk/Run playback speed follows the
// interpolated ground speed, and a Walk/Run state with no ground motion is shown as Stand.
public abstract class RemoteEntityBase
{
    // MonsterLocomotion: 70 u/s per step, one extra step when running. Learned speeds cover faster templates.
    private const float ModelWalkSpeed = 70f;
    private const float ModelRunSpeed = 140f;
    private const float LearnedSpeedLimit = 3f;
    private const float MinLocomotionScale = 0.5f;
    private const float MaxLocomotionScale = 2f;
    private const float StallSpeed = 5f;
    private const double StallDelayMs = 100;
    private const float RateTolerance = 0.01f;
    private const int MaxPendingEvents = 32;

    private readonly RemoteSnapshotBuffer _track;
    private readonly Queue<(double TimeMs, CombatEventInfo Event)> _events = new();
    private readonly ActorMotionController _controller = new();
    private Vector3 _renderPosition;
    private float _renderFacing;
    private MotionCommand _pendingMotion = MotionCommand.None;
    private bool _pendingMotionStartsSound;
    private PristonActorMotion _shownMotion;
    private float _appliedRate = float.NaN;
    private int _lastHp;
    private int _moveSpeed = 1;
    private float _attackRate = 1f;
    private float _planarSpeed;
    private float _learnedWalkSpeed;
    private float _learnedRunSpeed;
    private double _stallMs;
    private bool _stalled;

    protected RemoteEntityBase(RemoteEntityKind kind, double timeMs, Vector3 position, float facing, RemoteMoveState moveState, int hp,
        int soundCode = 0)
    {
        Kind = kind;
        _track = new RemoteSnapshotBuffer(new RemoteSample(timeMs, position, facing, moveState, hp));
        _renderPosition = position;
        _renderFacing = facing;
        _ = _controller.ApplyAuthoritative(ToActorMotion(moveState));
        _shownMotion = _controller.Current;
        _lastHp = hp;
        SoundCode = soundCode;
    }

    public RemoteEntityKind Kind { get; }

    public int SoundCode { get; }

    public bool InVillage { get; set; }

    public event Action<RemoteEntityBase, PristonActorMotion> MotionStarted;

    public Vector3 RenderPosition => _renderPosition;

    public Vector3 DrawnPosition => _renderPosition;

    public float RenderFacing => _renderFacing;

    public Vector3 TargetPosition => _track.Latest.Position;

    public double RenderTimeMs => _track.RenderTimeMs;

    public float PlanarSpeed => _planarSpeed;

    public PristonActorMotion Motion => _controller.Current;

    public PristonActorMotion ShownMotion => _shownMotion;

    public bool IsDead => _controller.IsDead;

    public void SetTarget(double timeMs, Vector3 position, float facing, RemoteMoveState moveState, int hp)
    {
        _ = _track.Add(new RemoteSample(timeMs, position, facing, moveState, hp), _renderPosition);
    }

    public virtual void Update(float dt, double nowMs)
    {
        var elapsedMs = Math.Max(0d, dt * 1000d);
        _track.Advance(elapsedMs);
        var pose = _track.Sample(nowMs);
        ApplyDueTimeline();
        _planarSpeed = pose.PlanarSpeed;
        LearnSpeed(pose);
        UpdateStall(elapsedMs);
        _renderPosition = pose.Position;
        _renderFacing = pose.Facing;
        ApplyWorld(Matrix.CreateRotationY(_renderFacing) * Matrix.CreateTranslation(_renderPosition));

        if (_pendingMotion.Kind != MotionCommandKind.None)
        {
            var command = _pendingMotion;
            var startsSound = _pendingMotionStartsSound;
            _pendingMotion = MotionCommand.None;
            _pendingMotionStartsSound = false;
            ApplyMotionCommand(command);
            _shownMotion = command.Motion;
            _appliedRate = float.NaN;
            if (startsSound)
            {
                MotionStarted?.Invoke(this, command.Motion);
            }
        }

        var rate = RemoteRateFor(_shownMotion);
        if (float.IsNaN(_appliedRate) || MathF.Abs(rate - _appliedRate) > RateTolerance)
        {
            _appliedRate = rate;
            ApplyPlaybackRate(rate);
        }
    }

    protected abstract void ApplyWorld(Matrix world);

    protected abstract void ApplyMotionCommand(MotionCommand command);

    protected abstract void ApplyPlaybackRate(float rate);

    public abstract void Dispose();

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
        _moveSpeed = moveSpeed;
        _attackRate = float.IsFinite(attackRate) && attackRate > 0f ? attackRate : 1f;
    }

    // Combat events share the sample timeline, so a swing restarts the clip the state already started at that time.
    public void ApplyCombatEvent(CombatEventInfo combatEvent, double timeMs)
    {
        if (_events.Count >= MaxPendingEvents)
        {
            ApplyCombatEventNow(_events.Dequeue().Event);
        }

        _events.Enqueue((timeMs, combatEvent));
    }

    private void ApplyDueTimeline()
    {
        while (_track.TryTakeDueState(out var sample))
        {
            ApplyDueEvents(sample.TimeMs, inclusive: false);
            ApplySampleState(sample);
        }

        ApplyDueEvents(_track.RenderTimeMs, inclusive: true);
    }

    private void ApplyDueEvents(double limitMs, bool inclusive)
    {
        while (_events.Count > 0)
        {
            var next = _events.Peek();
            if (next.TimeMs > limitMs || !inclusive && next.TimeMs == limitMs)
            {
                return;
            }

            _ = _events.Dequeue();
            ApplyCombatEventNow(next.Event);
        }
    }

    private void ApplySampleState(in RemoteSample sample)
    {
        var motion = ToActorMotion(sample.MoveState);
        var command = _controller.ApplyAuthoritative(motion);
        if (command.Kind == MotionCommandKind.None
            && motion == PristonActorMotion.Damage
            && _controller.Current == PristonActorMotion.Damage
            && sample.Hp < _lastHp)
        {
            command = _controller.Retrigger(PristonActorMotion.Damage);
        }

        QueueMotion(command, startsSound: true);
        _lastHp = sample.Hp;
    }

    private void ApplyCombatEventNow(CombatEventInfo combatEvent)
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

    private void UpdateStall(double elapsedMs)
    {
        var locomotion = _controller.Current is PristonActorMotion.Walk or PristonActorMotion.Run;
        _stallMs = locomotion && _planarSpeed < StallSpeed ? _stallMs + elapsedMs : 0d;
        var stalled = locomotion && _stallMs >= StallDelayMs;
        if (stalled == _stalled)
        {
            return;
        }

        _stalled = stalled;
        if (locomotion)
        {
            QueueMotion(new MotionCommand(MotionCommandKind.Set, _controller.Current), startsSound: false);
        }
    }

    private void QueueMotion(MotionCommand command, bool startsSound)
    {
        if (command.Kind == MotionCommandKind.None)
        {
            return;
        }

        if (_stalled && command.Motion is PristonActorMotion.Walk or PristonActorMotion.Run)
        {
            command = new MotionCommand(MotionCommandKind.Set, PristonActorMotion.Stand);
        }

        if (command.Kind == MotionCommandKind.Set && command.Motion == _shownMotion)
        {
            _pendingMotion = MotionCommand.None;
            _pendingMotionStartsSound = false;
            return;
        }

        _pendingMotion = command;
        _pendingMotionStartsSound = startsSound;
    }

    private void LearnSpeed(in RemotePose pose)
    {
        if (pose.Kind != RemotePoseKind.Interpolated || !pose.SteadyState || Kind == RemoteEntityKind.Player)
        {
            return;
        }

        if (_shownMotion == PristonActorMotion.Walk)
        {
            _learnedWalkSpeed = Math.Max(_learnedWalkSpeed, Math.Min(pose.PlanarSpeed, ModelWalkSpeed * LearnedSpeedLimit));
        }
        else if (_shownMotion == PristonActorMotion.Run)
        {
            _learnedRunSpeed = Math.Max(_learnedRunSpeed, Math.Min(pose.PlanarSpeed, ModelRunSpeed * LearnedSpeedLimit));
        }
    }

    protected float RemoteRateFor(PristonActorMotion motion)
    {
        return motion switch
        {
            PristonActorMotion.Walk or PristonActorMotion.Run => BaseLocomotionRate(motion) * LocomotionScale(motion),
            PristonActorMotion.Attack or PristonActorMotion.Skill => _attackRate,
            _ => 1f
        };
    }

    // The clip plays at its original rate at the entity's nominal speed and slows or speeds up with the real ground speed.
    private float LocomotionScale(PristonActorMotion motion)
    {
        var reference = ReferenceSpeed(motion);
        return reference > 0f ? Math.Clamp(_planarSpeed / reference, MinLocomotionScale, MaxLocomotionScale) : 1f;
    }

    private float ReferenceSpeed(PristonActorMotion motion)
    {
        var walk = motion == PristonActorMotion.Walk;
        if (Kind == RemoteEntityKind.Player)
        {
            return walk ? Locomotion.WalkUnitsPerSecond(_moveSpeed, InVillage) : Locomotion.RunUnitsPerSecond(_moveSpeed);
        }

        return walk ? Math.Max(ModelWalkSpeed, _learnedWalkSpeed) : Math.Max(ModelRunSpeed, _learnedRunSpeed);
    }

    private float BaseLocomotionRate(PristonActorMotion motion)
    {
        if (Kind != RemoteEntityKind.Player)
        {
            return 1f;
        }

        var translation = Locomotion.BaseTranslation(_moveSpeed);
        return motion == PristonActorMotion.Walk ? AnimationRate.MoveRate(translation) : AnimationRate.MoveRate((translation * 460) >> 8);
    }
}
