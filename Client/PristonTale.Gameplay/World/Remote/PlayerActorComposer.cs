using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Rendering;
using PristonTale.Simulation.Characters;

namespace PristonTale.Gameplay.World.Remote;

public readonly record struct PlayerActorHandles(ActorHandle Body, ActorHandle Head)
{
    public ActorHandle Weapon { get; init; } = ActorHandle.None;
    public ActorHandle OffHand { get; init; } = ActorHandle.None;

    public void Remove(IActorScene scene)
    {
        if (Weapon.IsValid)
        {
            scene.RemoveActor(Weapon);
        }

        if (OffHand.IsValid)
        {
            scene.RemoveActor(OffHand);
        }

        if (Head.IsValid)
        {
            scene.RemoveActor(Head);
        }

        if (Body.IsValid)
        {
            scene.RemoveActor(Body);
        }
    }
}

public interface IPlayerActorComposer
{
    Task PrepareAsync(string assetRoot, CharacterVisualSet visuals, int faceIndex) => Task.CompletedTask;
    PlayerActorHandles Compose(string assetRoot, CharacterVisualSet visuals, int faceIndex, Matrix world, PristonActorMotion motion);
}

public sealed class PlayerActorComposer(IActorScene scene, PristonActorLoader actorLoader) : IPlayerActorComposer
{
    private readonly IActorScene _scene = scene;
    private readonly PristonActorLoader _actorLoader = actorLoader;
    private readonly Dictionary<(string Root, CharacterVisualSet Visuals, int Face), Task<Prepared>> _prepared = new();

    public Task PrepareAsync(string assetRoot, CharacterVisualSet visuals, int faceIndex)
    {
        var key = (assetRoot, visuals, faceIndex);
        if (!_prepared.TryGetValue(key, out var preparation))
        {
            preparation = Task.Run(async () =>
            {
                var prepared = Load(assetRoot, visuals, faceIndex);
                await _scene.PrepareActorResourcesAsync(prepared.Body);
                await _scene.PrepareActorResourcesAsync(prepared.Head);
                if (prepared.Weapon is not null)
                {
                    await _scene.PrepareActorResourcesAsync(prepared.Weapon);
                }

                if (prepared.OffHand is not null)
                {
                    await _scene.PrepareActorResourcesAsync(prepared.OffHand);
                }

                return prepared;
            });
            _prepared.Add(key, preparation);
        }

        return preparation;
    }

    private Prepared Load(string root, CharacterVisualSet visuals, int face)
    {
        var body = _actorLoader.Load(root, Path.Combine(root, visuals.Body.InxExtensionPath));
        var head = _actorLoader.Load(root, Path.Combine(root, CharacterFace.ResolveHeadPath(visuals, face)));
        var weapon = string.IsNullOrEmpty(visuals.WeaponKey) ? null : _actorLoader.LoadRigid(Path.Combine(root, EquipmentVisuals.MeshPath(visuals.WeaponKey)));
        var offHand = string.IsNullOrEmpty(visuals.OffHandKey) ? null : _actorLoader.LoadRigid(Path.Combine(root, EquipmentVisuals.MeshPath(visuals.OffHandKey)));
        return new Prepared(body, head, weapon, offHand);
    }

    public PlayerActorHandles Compose(string assetRoot, CharacterVisualSet visuals, int faceIndex, Matrix world, PristonActorMotion motion)
    {
        return Compose(assetRoot, visuals, faceIndex, world, motion, visibleInMainPass: true);
    }

    public PlayerActorHandles ComposePortrait(string assetRoot, CharacterVisualSet visuals, int faceIndex, PristonActorMotion motion)
    {
        return Compose(assetRoot, visuals, faceIndex, Matrix.Identity, motion, visibleInMainPass: false);
    }

    private PlayerActorHandles Compose(
        string assetRoot,
        CharacterVisualSet visuals,
        int faceIndex,
        Matrix world,
        PristonActorMotion motion,
        bool visibleInMainPass)
    {
        var prepared = ResolvePrepared(assetRoot, visuals, faceIndex);

        var context = new PristonMotionContext(EquipmentVisuals.MotionIndex(visuals.WeaponKey), visuals.JobMask, visuals.InVillage ? 1 : 2);
        var handles = new PlayerActorHandles(ActorHandle.None, ActorHandle.None);
        try
        {
            handles = handles with
            {
                Body = _scene.AddActor(new PristonActorInstance(prepared.Body, world, motion)
                {
                    MotionContext = context,
                    VisibleInMainPass = visibleInMainPass
                })
            };
            handles = handles with
            {
                Head = _scene.AddActor(new PristonActorInstance(prepared.Head, world, motion)
                {
                    MotionContext = context,
                    VisibleInMainPass = visibleInMainPass
                })
            };
            if (prepared.Weapon is not null)
            {
                handles = handles with
                {
                    Weapon = _scene.AddActor(new PristonActorInstance(prepared.Weapon, world, PristonActorMotion.Stand)
                    {
                        VisibleInMainPass = visibleInMainPass
                    })
                };
                _scene.AttachActor(handles.Weapon, handles.Body, EquipmentVisuals.WeaponBone(visuals.WeaponKey, visuals.InVillage));
            }

            if (prepared.OffHand is not null)
            {
                handles = handles with
                {
                    OffHand = _scene.AddActor(new PristonActorInstance(prepared.OffHand, world, PristonActorMotion.Stand)
                    {
                        VisibleInMainPass = visibleInMainPass
                    })
                };
                _scene.AttachActor(handles.OffHand, handles.Body, "Bip01 L Forearm");
            }

            return handles;
        }
        catch
        {
            handles.Remove(_scene);
            throw;
        }
    }

    public ActorHandle ComposeHead(
        string assetRoot,
        CharacterVisualSet visuals,
        int faceIndex,
        Matrix world,
        PristonActorMotion motion,
        bool visibleInMainPass = true)
    {
        var prepared = ResolvePrepared(assetRoot, visuals, faceIndex);
        var context = new PristonMotionContext(EquipmentVisuals.MotionIndex(visuals.WeaponKey), visuals.JobMask, visuals.InVillage ? 1 : 2);
        return _scene.AddActor(new PristonActorInstance(prepared.Head, world, motion)
        {
            MotionContext = context,
            VisibleInMainPass = visibleInMainPass
        });
    }

    private Prepared ResolvePrepared(string assetRoot, CharacterVisualSet visuals, int faceIndex)
    {
        if (!_prepared.TryGetValue((assetRoot, visuals, faceIndex), out var preparation))
        {
            return Load(assetRoot, visuals, faceIndex);
        }

        if (!preparation.IsCompletedSuccessfully)
        {
            throw new InvalidOperationException("Player actor preparation is incomplete.");
        }

        return preparation.Result;
    }

    private sealed record Prepared(PristonActor Body, PristonActor Head, PristonActor Weapon, PristonActor OffHand);
}
