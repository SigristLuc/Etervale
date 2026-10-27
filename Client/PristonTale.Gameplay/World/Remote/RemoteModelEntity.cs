using Microsoft.Xna.Framework;
using PristonTale.Client.Net.Presence;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.Combat;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.World;

namespace PristonTale.Gameplay.World.Remote;

public sealed class RemoteModelEntity : RemoteEntityBase
{
    private readonly IActorScene _scene;
    private readonly ActorHandle _actor;
    private readonly float _scale;
    private readonly NpcStandMotionCycle _standMotionCycle;

    public RemoteModelEntity(IActorScene scene, ActorHandle actor, float scale, double timeMs, Vector3 position, float facing, MotionState moveState,
        int hp, EntityKind kind = EntityKind.Monster, int soundCode = 0, PristonModelInfo modelInfo = null, ulong entityId = 0)
        : base(kind, timeMs, position, facing, moveState, hp, soundCode)
    {
        _scene = scene;
        _actor = actor;
        _scale = scale <= 0f ? 1f : scale;
        if (kind == EntityKind.Npc && modelInfo is not null)
        {
            _standMotionCycle = new NpcStandMotionCycle(modelInfo, entityId);
        }
    }

    public override void Update(float dt, double nowMs)
    {
        base.Update(dt, nowMs);
        if (_standMotionCycle is null)
        {
            return;
        }

        var update = _standMotionCycle.Update(dt, ShownMotion);
        if (update.MotionChanged)
        {
            _scene.SetActorMotionIndex(_actor, update.MotionIndex);
        }
    }

    protected override void ApplyWorld(Matrix world)
    {
        _scene.SetActorWorld(_actor, Matrix.CreateScale(_scale) * world);
    }

    protected override void ApplyMotionCommand(MotionCommand command)
    {
        if (command.Kind == MotionCommandKind.Restart)
        {
            _scene.RestartActorMotion(_actor, command.Motion);
        }
        else
        {
            _scene.SetActorMotion(_actor, command.Motion);
        }
    }

    protected override void ApplyPlaybackRate(float rate) => _scene.SetActorPlaybackRate(_actor, rate);

    public override int CopyActorHandles(Span<ActorHandle> destination)
    {
        if (!_actor.IsValid || destination.Length == 0)
        {
            return 0;
        }

        destination[0] = _actor;
        return 1;
    }

    protected override void ApplyMotionIndex(int motionIndex) => _scene.SetActorMotionIndex(_actor, motionIndex);

    public ActorHandle Actor => _actor;

    // A monster's level and model size, which select its effect family.
    public int Level { get; init; }

    public float Scale => _scale;

    public override void Dispose() => _scene.RemoveActor(_actor);
}
