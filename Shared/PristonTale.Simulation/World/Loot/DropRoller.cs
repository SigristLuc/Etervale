using PristonTale.Simulation.Combat;

namespace PristonTale.Simulation.World.Loot;

public readonly record struct RolledDrop(DropKind Kind, uint ItemCode, int Amount, bool ToInventory)
{
    public static RolledDrop Gold(int amount) => new(DropKind.Gold, 0, amount, ToInventory: false);
    public static RolledDrop Item(uint code, bool toInventory = false) => new(DropKind.Item, code, 1, toInventory);
}

public static class DropRoller
{
    public static IReadOnlyList<RolledDrop> Roll(
        CombatRng rng,
        DropTable table,
        bool hasEventItem = false,
        uint eventItemCode = 0)
    {
        var drops = new List<RolledDrop>();

        if (hasEventItem && eventItemCode != 0)
            drops.Add(RolledDrop.Item(eventItemCode, toInventory: true));

        // Bonus Weight is a strict per-myriad chance and each entry rolls independently.
        foreach (var bonus in table.BonusEntries)
        {
            if (bonus.Kind == DropKind.Nothing)
                continue;
            if (rng.RollPerMyriad() < bonus.Weight)
                drops.Add(MaterializeEntry(rng, bonus));
        }

        int mainTotal = table.MainWeightTotal;
        if (mainTotal > 0 && table.MaxItemsPerKill > 0)
        {
            for (int i = 0; i < table.MaxItemsPerKill; i++)
            {
                var picked = PickWeightedMain(rng, table, mainTotal);
                if (picked is { Kind: not DropKind.Nothing })
                    drops.Add(MaterializeEntry(rng, picked));
            }
        }

        return drops;
    }

    private static DropEntry PickWeightedMain(CombatRng rng, DropTable table, int mainTotal)
    {
        int roll = rng.Range(0, mainTotal - 1);
        int cursor = 0;
        foreach (var e in table.MainEntries)
        {
            cursor += e.Weight;
            if (roll < cursor)
                return e;
        }
        DropEntry last = null;
        foreach (var e in table.MainEntries)
            last = e;
        return last;
    }

    private static RolledDrop MaterializeEntry(CombatRng rng, DropEntry e) => e.Kind switch
    {
        DropKind.Gold => RolledDrop.Gold(rng.Range(e.GoldMin, e.GoldMax)),
        DropKind.Item => RolledDrop.Item(e.ItemCode),
        _ => default
    };
}
