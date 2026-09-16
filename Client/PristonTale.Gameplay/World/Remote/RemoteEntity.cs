using Microsoft.Xna.Framework;
using PristonTale.Client.Net.Presence;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.Combat;

namespace PristonTale.Gameplay.World.Remote;

public sealed class RemoteEntity : RemoteEntityBase
{
    private readonly IActorScene _scene;
    private readonly ActorHandle _body;
    private readonly ActorHandle _head;
    private readonly PlayerActorHandles _handles;

    public RemoteEntity(IActorScene scene, PlayerActorHandles handles, Vector3 position, float facing, RemoteMoveState moveState, int hp)
        : base(RemoteEntityKind.Player, position, facing, moveState, hp)
    {
        _scene = scene;
        _handles = handles;
        _body = handles.Body;
        _head = handles.Head;
    }

    public PlayerActorHandles Handles => _handles;

    protected override void ApplyWorld(Matrix world)
    {
        _scene.SetActorWorld(_body, world);
        _scene.SetActorWorld(_head, world);
    }

    protected override void ApplyMotionCommand(MotionCommand command)
    {
        if (command.Kind == MotionCommandKind.Restart)
        {
            _scene.RestartActorMotion(_body, command.Motion);
            _scene.RestartActorMotion(_head, command.Motion);
        }
        else
        {
            _scene.SetActorMotion(_body, command.Motion);
            _scene.SetActorMotion(_head, command.Motion);
        }

        // Per-state playback rate (approximate — the wire lacks this entity's precise MoveSpeed/AttackSpeed).
        var rate = RemoteRateFor(command.Motion);
        _scene.SetActorPlaybackRate(_body, rate);
        _scene.SetActorPlaybackRate(_head, rate);
    }

    public override void Dispose()
    {
        _handles.Remove(_scene);
    }
}
