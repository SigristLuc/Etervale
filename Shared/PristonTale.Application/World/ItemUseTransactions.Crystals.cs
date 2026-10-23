using PristonTale.Simulation.Items;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Simulation.World;

namespace PristonTale.Application.World;

// Monster crystals and soul stones. Billing crystals and soul stones refuse during the three-second delay every crystal
// use starts; the item's level, towns and the quest arena refuse any crystal. Clan pets need a regular clan and castle
// mercenaries the castle-master clan during a siege, neither of which Etervale runs, so both are refused.
internal static partial class ItemUseTransactions
{
    private static ItemUseStatus CheckCrystal(ItemUseContext context, ItemInstance item, FieldDef? here)
    {
        if (!MonsterCrystals.TryGet(item.Definition.CatalogKey, out var crystal))
        {
            return ItemUseStatus.NotUsable;
        }

        var player = context.Player;
        if (crystal.SharedDelay && player.CrystalDelayUntilMs > context.NowMs)
        {
            return ItemUseStatus.Delay;
        }

        if (item.Definition.RequiredLevel > player.Character.Level)
        {
            return ItemUseStatus.LevelTooLow;
        }

        if (here is null || here.State == FieldState.Village || context.Field.FieldId == MonsterCrystals.QuestArenaField)
        {
            return ItemUseStatus.NotHere;
        }

        return crystal.Kind switch
        {
            CrystalKind.ClanPet => ItemUseStatus.ClanOnly,
            CrystalKind.CastleMercenary => ItemUseStatus.CrystalRefused,
            CrystalKind.SoulStone when MonsterCrystals.SoulStoneBlockedFields.Contains(context.Field.FieldId) => ItemUseStatus.NotHere,
            _ => ItemUseStatus.Used
        };
    }

    // The monster the crystal calls; a crystal whose monster the world data lacks is not usable.
    private static ItemUseExtra ExtendCrystal(ItemUseContext context, ItemInstance item)
    {
        MonsterCrystals.TryGet(item.Definition.CatalogKey, out var crystal);
        var key = crystal.Pick(context.Pick, context.CrystalEvent);
        return context.Field.TryFindMonsterKey(key, out _) ? new ItemUseExtra(ItemUseStatus.Used, key) : new ItemUseExtra(ItemUseStatus.NotUsable);
    }
}
