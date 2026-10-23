using PristonTale.Simulation.Items;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Simulation.World;

namespace PristonTale.Application.World;

// Bless Castle items: scrolls and stones work only in the castle field; a running item of the same code refuses, and a
// castle scroll refuses while the premium scroll of the same effect runs. Restoration is used by its dead owner in a
// castle field after ten seconds down.
internal static partial class ItemUseTransactions
{
    // A dead player in a castle field who carries a Restoration stays down this long so the item can revive him.
    public const float RestorationDelayMs = 10_000f;
    public const float RestorationHoldMs = 12_000f;
    public const float RestorationGuardMs = 5000f;

    private static ItemUseStatus CheckCastleItem(ItemUseContext context, UsableItemRule rule)
    {
        if (!CastleItems.TryGetByIndex(rule.Argument, out var castle))
        {
            return ItemUseStatus.NotUsable;
        }

        if (context.Field.FieldId != CastleItems.CastleFieldId)
        {
            return ItemUseStatus.NotHere;
        }

        var effects = context.Player.Character.Effects;
        if (effects.TryGet(castle.StatusCode, out _))
        {
            return ItemUseStatus.AlreadyRunning;
        }

        return castle.Kind == CastleItemKind.Scroll && effects.TryGet(ItemScrolls.For((ScrollEffectKind)castle.Target).StatusCode, out _)
            ? ItemUseStatus.SameAttribute
            : ItemUseStatus.Used;
    }

    private static ItemUseStatus CheckRestoration(ItemUseContext context, FieldDef? here)
    {
        if (here?.State != FieldState.Castle)
        {
            return ItemUseStatus.NotHere;
        }

        return context.Player.DeadMs >= RestorationDelayMs ? ItemUseStatus.Used : ItemUseStatus.Delay;
    }

    // Whether a dead player waits for his Restoration instead of respawning.
    public static bool HoldsForRestoration(WorldPlayer player, FieldState state) =>
        player.IsDead && state == FieldState.Castle && player.DeadMs < RestorationHoldMs
        && player.Character.Inventory is var inventory && Enumerable.Range(0, inventory.Capacity)
            .Any(anchor => inventory[anchor]?.Definition.CatalogKey.Equals("BC105", StringComparison.OrdinalIgnoreCase) == true);
}
