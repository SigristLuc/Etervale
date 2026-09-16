using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Rendering;

namespace PristonTale.Gameplay.World.Remote;

public interface IActorScene
{
    Task PrepareActorResourcesAsync(PristonActor actor) => Task.CompletedTask;
    void CopyActorPlayback(ActorHandle source, ActorHandle destination) { }
    void AttachActor(ActorHandle child, ActorHandle parent, string boneName) => throw new NotSupportedException("Actor attachments are not supported by this scene.");
    ActorHandle AddActor(PristonActorInstance instance);
    void SetActorWorld(ActorHandle handle, Matrix world);
    void SetActorColorAdd(ActorHandle handle, Vector3 colorAdd) { }
    void SetActorMotion(ActorHandle handle, PristonActorMotion motion);
    void SetActorMotionIndex(ActorHandle handle, int motionIndex);
    void RestartActorMotion(ActorHandle handle, PristonActorMotion motion);
    void SetActorPlaybackRate(ActorHandle handle, float rateFactor);
    void RemoveActor(ActorHandle handle);
}
