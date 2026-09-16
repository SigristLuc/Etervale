using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;

namespace PristonTale.Gameplay.World.Remote;

public sealed class GroundLootInteraction(Func<InventoryCommand, Task<InventoryOutcome>> submit, IClientLog log)
{
    private Guid _selected;
    private Task<InventoryOutcome> _pending;
    public bool Busy => _pending is not null;

    public void Select(Guid id)
    {
        if (!Busy)
        {
            _selected = id;
        }
    }

    public void Cancel() => _selected = Guid.Empty;

    public void Update(GroundLootPresentation ground, Vector3 position, Vector2 serverPosition, OwnerState owner)
    {
        if (_pending is not null)
        {
            if (_pending.IsCompleted)
            {
                if (_pending.IsCompletedSuccessfully && !_pending.Result.Ok)
                {
                    log.Info($"[Loot] Pickup rejected: {_pending.Result.Message}");
                }

                if (_pending.IsFaulted)
                {
                    log.Error($"[Loot] Pickup failed: {_pending.Exception?.GetBaseException().Message}");
                }

                _pending = null;
            }

            return;
        }

        if (_selected == Guid.Empty || owner is null)
        {
            return;
        }

        if (owner.Hp <= 0 || !ground.TryGet(_selected, out var target))
        {
            Cancel();
            return;
        }

        var planar = new Vector2(target.X, target.Z);
        if (Vector2.DistanceSquared(new Vector2(position.X, position.Z), planar) > 28 * 28
            || Vector2.DistanceSquared(serverPosition, planar) > 28 * 28)
        {
            return;
        }

        var id = _selected;
        Cancel();
        _pending = submit(new InventoryCommand(Guid.NewGuid(), owner.InventoryRevision, InventoryAction.Pickup, id, 0, 1, default));
        _ = ObserveAsync(_pending);
    }

    private static async Task ObserveAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch
        {
            // Update reports the failure; observing also covers leaving the screen while I/O is pending.
        }
    }
}
