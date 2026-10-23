using PristonTale.Simulation.Items;

namespace PristonTale.Simulation.Quests;

// Item codes the quests hand out, ask for and take back.
public static class QuestItems
{
    public static readonly uint SilverWolverineTrophy = LegacyItemCode.Parse("QT101");
    public static readonly uint BronzeWolverineTrophy = LegacyItemCode.Parse("QT102");
    public static readonly uint GoldenWolverineTrophy = LegacyItemCode.Parse("QT103");
    public static readonly uint RoyalJelly = LegacyItemCode.Parse("QT104");
    public static readonly uint HairTonic = LegacyItemCode.Parse("QT105");
    public static readonly uint Vamp = LegacyItemCode.Parse("QT106");
    public static readonly uint Level90Token = LegacyItemCode.Parse("QT107");
    public static readonly uint KhaliaTear = LegacyItemCode.Parse("QT108");
    public static readonly uint BonusVampOne = LegacyItemCode.Parse("QT109");
    public static readonly uint BonusVampTwo = LegacyItemCode.Parse("QT110");
    public static readonly uint BonusVampThree = LegacyItemCode.Parse("QT111");
    public static readonly uint Recommendation = LegacyItemCode.Parse("QT112");
    public static readonly uint RoyalAmulet = LegacyItemCode.Parse("QT113");
    public static readonly uint Candy = LegacyItemCode.Parse("QT114");
    public static readonly uint Cake = LegacyItemCode.Parse("QT115");
    public static readonly uint LostHammer = LegacyItemCode.Parse("QT116");
    public static readonly uint GlassBottle = LegacyItemCode.Parse("MA101");
    public static readonly uint Honey = LegacyItemCode.Parse("MA201");
    public static readonly uint Oil = LegacyItemCode.Parse("MA202");

    public static readonly uint ArcaneRing = LegacyItemCode.Parse("OR202");
    public static readonly uint EmperorRing = LegacyItemCode.Parse("OR203");
    public static readonly uint FocusRing = LegacyItemCode.Parse("OR204");

    // Royal jelly takes the bottle and nine honey, hair tonic the bottle and seven oil.
    public const int HoneyForJelly = 9;
    public const int OilForTonic = 7;

    private const uint CategoryMask = 0xFFFF0000u;
    private static readonly uint QuestCategory = LegacyItemCode.Parse("QT101") & CategoryMask;
    private static readonly uint BottleCategory = LegacyItemCode.Parse("MA101") & CategoryMask;
    private static readonly uint IngredientCategory = LegacyItemCode.Parse("MA201") & CategoryMask;

    // Quest and ingredient drops go straight into the killer's pack, as the original server put them there; they never lie
    // on the ground.
    public static bool IsQuestDrop(uint code)
    {
        var category = code & CategoryMask;
        return category == QuestCategory || category == BottleCategory || category == IngredientCategory;
    }

    public static bool IsIngredient(uint code) => (code & CategoryMask) == IngredientCategory || (code & CategoryMask) == BottleCategory;
}
