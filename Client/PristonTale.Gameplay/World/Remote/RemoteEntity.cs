using Microsoft.Xna.Framework;
using PristonTale.Client.Net.Presence;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.Combat;
using PristonTale.Contracts.Motion;

namespace PristonTale.Gameplay.World.Remote;

public sealed class RemoteEntity : RemoteEntityBase
{
    private readonly IActorScene _scene;
    private readonly ActorHandle _body;
    private readonly ActorHandle _head;
    private readonly PlayerActorHandles _handles;

    public RemoteEntity(IActorScene scene, PlayerActorHandles handles, double timeMs, Vector3 position, float facing, MotionState moveState, int hp)
        : base(RemoteEntityKind.Player, timeMs, position, facing, moveState, hp)
    {
        _scene = scene;
        _handles = handles;
        _body = handles.Body;
        _head = handles.Head;
    }

    public PlayerActorHandles Handles => _handles;

    public override int CopyActorHandles(Span<ActorHandle> destination) => _handles.CopyTo(destination);

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
    }

    protected override void ApplyMotionIndex(int motionIndex)
    {
        // The body's equipment picks the weapon variant of a skill clip; the head follows the same clip.
        var resolved = _scene.ResolveSkillMotion(_body, motionIndex);
        _scene.SetActorMotionIndex(_body, resolved);
        _scene.SetActorMotionIndex(_head, resolved);
    }

    public ActorHandle Body => _body;

    // A released charge: the skill clip jumps ahead and plays the rest at the new rate.
    public void SeekSkill(int framesFromStart, float rate)
    {
        _scene.SeekActorMotion(_body, framesFromStart);
        _scene.SeekActorMotion(_head, framesFromStart);
        ApplyPlaybackRate(rate);
    }

    protected override void ApplyPlaybackRate(float rate)
    {
        _scene.SetActorPlaybackRate(_body, rate);
        _scene.SetActorPlaybackRate(_head, rate);
    }

    public override void Dispose()
    {
        _handles.Remove(_scene);
    }
}
