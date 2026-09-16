using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Rendering;

namespace PristonTale.Gameplay.World.Remote;

public interface IModelActorComposer
{
    Task PrepareAsync(string assetRoot, string modelInxPath) => Task.CompletedTask;
    ComposedModelActor Compose(string assetRoot, string modelInxPath, Matrix world, PristonActorMotion motion);
}

public readonly record struct ComposedModelActor(ActorHandle Handle, PristonModelInfo Info);

public sealed class ModelActorComposer(IActorScene scene, PristonActorLoader actorLoader) : IModelActorComposer
{
    private readonly IActorScene _scene = scene;
    private readonly PristonActorLoader _actorLoader = actorLoader;
    private readonly Dictionary<string, Task<PristonActor>> _prepared = new(StringComparer.OrdinalIgnoreCase);

    public Task PrepareAsync(string assetRoot, string modelInxPath)
    {
        var path = AssetPath(assetRoot, modelInxPath);
        if (!_prepared.TryGetValue(path, out var preparation))
        {
            preparation = Task.Run(async () =>
            {
                var actor = _actorLoader.Load(assetRoot, path);
                await _scene.PrepareActorResourcesAsync(actor);
                return actor;
            });
            _prepared.Add(path, preparation);
        }

        return preparation;
    }

    public ComposedModelActor Compose(string assetRoot, string modelInxPath, Matrix world, PristonActorMotion motion)
    {
        var path = AssetPath(assetRoot, modelInxPath);
        if (!_prepared.TryGetValue(path, out var preparation) || !preparation.IsCompletedSuccessfully)
        {
            throw new InvalidOperationException("model actor must be prepared before composition");
        }

        var actor = preparation.Result;
        var handle = _scene.AddActor(new PristonActorInstance(actor, world, motion));
        return new ComposedModelActor(handle, actor.Info);
    }

    private static string AssetPath(string assetRoot, string relativePath)
    {
        var normalized = relativePath.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        return Path.Combine(assetRoot, normalized);
    }
}
