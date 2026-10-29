using PristonTale.Contracts.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.World.Ai;

namespace PristonTale.Simulation.Characters;

// The gifts a new character receives one at a time in its first minutes when the server gives them (the original's
// new-user present): its class's weapon, a body armour, then three Hopy crystals. Etervale gives perfect copies with the
// class's specialization where the item takes it (the original rolled them as any drop). A character created before the
// gifts existed counts as served.
public static class StarterGifts
{
    public const int Count = 5;
    public static readonly TimeSpan FirstDelay = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);

    // The classes the original names get their own weapon; every other class gets one of the three at random.
    private static readonly string[] MeleeWeapons = ["WS201", "WA101", "WH101"];

    // The catalog key of the gift at the index (0 to Count - 1).
    public static string ItemKey(PlayerClass playerClass, int index, ISimRng rng) => index switch
    {
        0 => playerClass switch
        {
            PlayerClass.Archer => "WS101",
            PlayerClass.Atalanta => "WT101",
            PlayerClass.Magician or PlayerClass.Priestess => "WM101",
            _ => MeleeWeapons[rng.NextExclusive(0, MeleeWeapons.Length)]
        },
        1 => playerClass is PlayerClass.Magician or PlayerClass.Priestess ? "DA202" : "DA102",
        _ => "GP101"
    };

    // The perfect copy of the gift at the index; null when the catalog lacks it.
    public static ItemInstance Create(ItemCatalog items, PlayerClass playerClass, int index, ISimRng rng)
    {
        if (!items.TryGet(ItemCodes.Parse(ItemKey(playerClass, index, rng)), out var definition))
        {
            return null;
        }

        var spec = (uint)JobBits.Of(playerClass);
        return ItemRoller.CreatePerfect(definition, 1, ItemRoller.CanTakeSpec(definition, spec) ? spec : 0);
    }

    public static string Message(string itemName) => $"You received {itemName} as a gift.";
}
