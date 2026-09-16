using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Rendering;

namespace PristonTale.Gameplay.World.Remote;

public sealed class RendererActorScene(PristonRenderer renderer) : IActorScene
{
    private readonly PristonRenderer _renderer = renderer;

    public Task PrepareActorResourcesAsync(PristonActor actor) => _renderer.PrepareActorResourcesAsync(actor);

    public void CopyActorPlayback(ActorHandle source, ActorHandle destination) => _renderer.CopyActorPlayback(source, destination);
    public void AttachActor(ActorHandle child, ActorHandle parent, string boneName) => _renderer.AttachActor(child, parent, boneName);
    public ActorHandle AddActor(PristonActorInstance instance) => _renderer.AddActor(instance);
    public void SetActorWorld(ActorHandle handle, Matrix world) => _renderer.SetActorWorld(handle, world);
    public void SetActorColorAdd(ActorHandle handle, Vector3 colorAdd) => _renderer.SetActorColorAdd(handle, colorAdd);
    public void SetActorMotion(ActorHandle handle, PristonActorMotion motion) => _renderer.SetActorMotion(handle, motion);
    public void SetActorMotionIndex(ActorHandle handle, int motionIndex) => _renderer.SetActorMotionIndex(handle, motionIndex);
    public void RestartActorMotion(ActorHandle handle, PristonActorMotion motion) => _renderer.RestartActorMotion(handle, motion);
    public void SetActorPlaybackRate(ActorHandle handle, float rateFactor) => _renderer.SetActorPlaybackRate(handle, rateFactor);
    public void RemoveActor(ActorHandle handle) => _renderer.RemoveActor(handle);
}
