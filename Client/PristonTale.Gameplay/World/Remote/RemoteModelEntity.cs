using Microsoft.Xna.Framework;
using PristonTale.Client.Net.Presence;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.Combat;

namespace PristonTale.Gameplay.World.Remote;

public sealed class RemoteModelEntity : RemoteEntityBase
{
    private readonly IActorScene _scene;
    private readonly ActorHandle _actor;
    private readonly float _scale;
    private readonly NpcStandMotionCycle _standMotionCycle;

    public RemoteModelEntity(IActorScene scene, ActorHandle actor, float scale, double timeMs, Vector3 position, float facing, RemoteMoveState moveState,
        int hp, RemoteEntityKind kind = RemoteEntityKind.Monster, int soundCode = 0, PristonModelInfo modelInfo = null, uint entityId = 0)
        : base(kind, timeMs, position, facing, moveState, hp, soundCode)
    {
        _scene = scene;
        _actor = actor;
        _scale = scale <= 0f ? 1f : scale;
        if (kind == RemoteEntityKind.Npc && modelInfo is not null)
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

    public override void Dispose() => _scene.RemoveActor(_actor);
}
