using PristonTale.Simulation.World.Ai;

namespace PristonTale.Simulation.Items.Aging;

// The aging under way on a copy: the count of its counter toward the goal, and the stone that was used (some stones keep
// the level requirement when the level comes).
public sealed record AgingMaturity(int Count, int Goal, AgingStone Stone);

public enum AgingStone : byte
{
    None = 0,
    AgingStone = 1,
    CopperOre = 2,
    ElderCopperOre = 3,
    SuperAgingStone = 4,
    SuperiorCopperOre = 5
}

// What the aging NPC did: the copy was lost, went down one or two levels (it then matures back one), started its next
// level, or gained a level at once and started the next.
public enum AgingResult : byte
{
    Destroyed = 0,
    DownOne = 1,
    DownTwo = 2,
    Success = 3,
    Double = 4
}

// What makes an aging copy mature: its holder's attacks, critical hits or blocks, blows the holder takes, or (the ring)
// seconds worn.
public enum AgingCounter : byte
{
    None = 0,
    Attack = 1,
    Critical = 2,
    Block = 3,
    Hit = 4,
    Time = 5
}

// The aging NPC: a weapon, shield, orb, armour, robe, gloves, boots or armlet (and the ring of family OR2 ordinal 1),
// with the sheltoms its level asks and an optional aging stone, costs (level + 1) x its price / 2. The roll takes the
// copy down or destroys it by its level's chance (lower with an aging stone or the super stones), else it may gain a
// level at once (below +10); copper ores and the super stones turn a destruction into a fall. Every outcome but the loss
// starts the next level: the copy matures when its counter reaches the goal of its level (a fall starts half way).
public static class AgingRules
{
    public const int MaxLevel = 20;

    // Attacks, blocks and blows count only against a foe within this many levels.
    public const int CountLevelSpread = 10;

    private static readonly string[] AgeableFamilies =
        ["WA1", "WC1", "WH1", "WM1", "WP1", "WS1", "WS2", "WT1", "DA1", "DS1", "OM1", "DA2", "DG1", "DB1", "OA2"];

    // Costumes and timed items of ageable families that the NPC refuses.
    private static readonly HashSet<string> Refused = new(StringComparer.OrdinalIgnoreCase)
    {
        "DA131", "DA132", "DA133", "DA134", "DA135", "DA136", "DA137", "DA138", "DA139", "DA140", "DA141", "DA142", "DA143", "DA144", "DA145",
        "DA146", "DA148", "DA154", "DA155",
        "DA231", "DA232", "DA233", "DA234", "DA235", "DA236", "DA237", "DA238", "DA239", "DA240", "DA241", "DA242", "DA243", "DA244", "DA245",
        "DA246", "DA248", "DA254", "DA255",
        "OA231", "OA232", "OA233", "OA234", "DB131", "DB132", "DB133", "DB134", "QT107", "QT108"
    };

    // The sheltoms (1 Lucidy .. 14 Ordo) each level asks, as the window lays them out in four rows of three.
    private static readonly int[][] Sheltoms =
    [
        [3, 3, 4, 4, 5], [3, 3, 4, 4, 5, 5], [3, 3, 4, 4, 5, 5, 6], [3, 3, 4, 4, 5, 5, 6, 6], [3, 3, 4, 4, 5, 5, 6, 6, 7],
        [3, 3, 4, 4, 5, 5, 6, 6, 7, 7], [3, 3, 4, 4, 5, 5, 6, 6, 7, 7, 8], [3, 3, 4, 4, 5, 5, 6, 6, 7, 7, 8, 8],
        [4, 4, 5, 5, 6, 6, 7, 7, 8, 8, 9], [4, 4, 5, 5, 6, 6, 7, 7, 8, 8, 9, 9],
        [5, 5, 6, 6, 7, 7, 8, 8, 9, 9, 10], [5, 5, 6, 6, 7, 7, 8, 8, 9, 9, 10, 10], [6, 6, 7, 7, 8, 8, 9, 9, 10, 10, 11],
        [6, 6, 7, 7, 8, 8, 9, 9, 10, 10, 11, 11], [7, 7, 8, 8, 9, 9, 10, 10, 11, 11, 12], [7, 7, 8, 8, 9, 9, 10, 10, 11, 11, 12, 12],
        [8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 13], [8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 13, 13], [9, 9, 10, 10, 11, 11, 12, 12, 13, 13, 14],
        [9, 9, 10, 10, 11, 11, 12, 12, 13, 13, 14, 14]
    ];

    // Chance in percent (against 0..100) that the aging of each level fails.
    private static readonly int[] FailPlain = [0, 0, 0, 0, 0, 5, 10, 15, 20, 30, 35, 40, 45, 50, 55, 65, 75, 85, 95, 95];
    private static readonly int[] FailAgingStone = [0, 0, 0, 0, 0, 3, 5, 8, 11, 18, 21, 25, 29, 33, 38, 48, 60, 74, 90, 90];
    private static readonly int[] FailSuperStone = [0, 0, 0, 0, 0, 3, 5, 8, 11, 18, 21, 25, 29, 33, 38, 48, 60, 74, 90, 90];
    private static readonly int[] FailSuperiorOre = [0, 0, 0, 0, 0, 3, 7, 11, 14, 22, 26, 31, 35, 40, 45, 55, 67, 79, 93, 93];

    // Chance in percent that a level that did not fail is gained at once (the aging stone doubles it).
    private static readonly int[] DoubleChance = [30, 25, 20, 10, 10, 10, 10, 5, 5, 5, 5, 4, 4, 3, 3, 2, 2, 2, 0, 0];

    // A failure at each level: below the first threshold the copy falls one level, below the second two, else it is lost.
    private static readonly (int One, int Two)[] FailureSplit =
    [
        (101, 101), (101, 101), (101, 101), (101, 101), (101, 101), (101, 101), (90, 101), (80, 101), (70, 85), (60, 80), (50, 70), (40, 60),
        (30, 50), (30, 40), (20, 30), (10, 20), (10, 20), (10, 20), (10, 20), (10, 20), (10, 20)
    ];

    // The goal of each level's counter.
    private static readonly int[] AttackGoal =
        [100, 130, 169, 219, 284, 369, 479, 622, 808, 1049, 1362, 1769, 2297, 2983, 3874, 5031, 6534, 8486, 11021, 14313, 14313];
    private static readonly int[] CriticalGoal =
        [12, 16, 21, 27, 35, 45, 58, 75, 97, 126, 164, 213, 277, 360, 468, 608, 790, 1026, 1332, 1730, 1730];
    private static readonly int[] BlockGoal =
        [15, 19, 25, 32, 42, 55, 71, 92, 119, 155, 201, 261, 339, 440, 571, 742, 964, 1252, 1626, 2112, 2112];
    private static readonly int[] HitGoal =
        [45, 58, 75, 97, 126, 164, 213, 277, 360, 468, 608, 790, 1026, 1332, 1730, 2247, 2918, 3790, 4922, 6392, 6392];

    // The ring that ages counts twenty minutes.
    private const string AgeableRing = "OR201";
    private const int RingGoal = 60 * 20;

    public static string Family(ItemInstance item) => Family(item.Definition.CatalogKey);

    public static string Family(string key) => key.Length >= 3 ? key[..3].ToUpperInvariant() : "";

    public static bool Ageable(ItemInstance item) =>
        Ageable(item.Definition.CatalogKey, item.Definition.Kind, item.Definition.UniqueItem, item.MixSheltom, item.Maturity is not null, item.UpgradeLevel);

    // The same rule from what the owner's client knows of a copy.
    public static bool Ageable(string key, ItemKind kind, int unique, int mixSheltom, bool maturing, int level) =>
        (AgeableFamilies.Contains(Family(key)) || string.Equals(key, AgeableRing, StringComparison.OrdinalIgnoreCase))
        && !Refused.Contains(key) && unique == 0 && kind != ItemKind.Quest && mixSheltom == 0 && !maturing && level < MaxLevel;

    // The sheltom ordinals (1..14) the copy's level asks.
    public static IReadOnlyList<int> SheltomsFor(int level) => Sheltoms[Math.Clamp(level, 0, Sheltoms.Length - 1)];

    // A sheltom's ordinal (1..14), or 0 for anything else (the magic sheltoms do not age).
    public static int SheltomOrdinal(string key) =>
        Family(key) == "OS1" && int.TryParse(key.AsSpan(3), out var ordinal) && ordinal is >= 1 and <= 14 ? ordinal : 0;

    public static AgingStone StoneOf(string key) => key.ToUpperInvariant() switch
    {
        "BI110" => AgingStone.AgingStone,
        "BI111" => AgingStone.CopperOre,
        "BI160" => AgingStone.ElderCopperOre,
        "BI161" => AgingStone.SuperAgingStone,
        "BI185" => AgingStone.SuperiorCopperOre,
        _ => AgingStone.None
    };

    public static long Price(ItemInstance item) => Price(item.UpgradeLevel, item.Roll.Price);

    public static long Price(int level, long price) => (level + 1) * price / 2;

    // True when the sheltoms are exactly those the level asks.
    public static bool SheltomsMatch(int level, IEnumerable<int> ordinals)
    {
        var wanted = SheltomsFor(level).Order().ToArray();
        var given = ordinals.Order().ToArray();
        return wanted.SequenceEqual(given);
    }

    // The outcome of one aging of a copy at its level with the stone, with the original's rolls.
    public static AgingResult Roll(int level, AgingStone stone, ISimRng rng)
    {
        var destroy = rng.NextInclusive(0, 100);
        var down = rng.NextInclusive(0, 100);
        var plus = rng.NextInclusive(0, 100);
        var index = Math.Clamp(level, 0, FailPlain.Length - 1);
        var fail = stone switch
        {
            AgingStone.AgingStone => FailAgingStone[index],
            AgingStone.SuperAgingStone => FailSuperStone[index],
            AgingStone.SuperiorCopperOre => FailSuperiorOre[index],
            _ => FailPlain[index]
        };

        var result = AgingResult.Success;
        if (fail > destroy)
        {
            // A failure where the level has no split (up to +5) leaves the aging a success, as the original does.
            var split = FailureSplit[Math.Clamp(level, 0, FailureSplit.Length - 1)];
            if (split.One <= 100)
            {
                result = down < split.One ? AgingResult.DownOne : down < split.Two ? AgingResult.DownTwo : AgingResult.Destroyed;
            }
        }
        else if (stone is not (AgingStone.SuperAgingStone or AgingStone.SuperiorCopperOre) && level < 10
                 && DoubleChance[index] * (stone == AgingStone.AgingStone ? 2 : 1) > plus)
        {
            result = AgingResult.Double;
        }

        if (result == AgingResult.Destroyed && stone is AgingStone.CopperOre or AgingStone.ElderCopperOre or AgingStone.SuperAgingStone
                or AgingStone.SuperiorCopperOre)
        {
            result = rng.NextExclusive(0, 100) < 81 ? AgingResult.DownOne : AgingResult.DownTwo;
        }

        return result;
    }

    // Applies an outcome other than the loss: the level gained at once or lost, then the next level's aging begins.
    public static void Apply(ItemInstance item, AgingResult result, AgingStone stone)
    {
        if (result == AgingResult.Destroyed)
        {
            return;
        }

        if (result == AgingResult.Double)
        {
            LevelUp(item, stone);
        }
        else if (result is AgingResult.DownOne or AgingResult.DownTwo)
        {
            LevelDown(item, (int)result + 1);
        }

        var goal = GoalFor(item);
        item.Maturity = new AgingMaturity(result <= AgingResult.DownTwo ? goal / 2 : 0, goal, stone);
    }

    // The counter that matures a copy of this family.
    public static AgingCounter CounterOf(ItemInstance item) => Family(item) switch
    {
        "WA1" or "WH1" or "WM1" => AgingCounter.Attack,
        "WC1" or "WS2" or "WS1" or "WP1" or "WT1" => AgingCounter.Critical,
        "DS1" => AgingCounter.Block,
        "DA1" or "DA2" or "OM1" or "DB1" or "DG1" or "OA2" => AgingCounter.Hit,
        _ => string.Equals(item.Definition.CatalogKey, AgeableRing, StringComparison.OrdinalIgnoreCase) ? AgingCounter.Time : AgingCounter.None
    };

    // The copy reached its goal and waits for its level.
    public static bool Matured(ItemInstance item) => item.Maturity is { Goal: > 0 } maturity && maturity.Count >= maturity.Goal;

    // Counts events on a worn aging copy up to its goal without changing it otherwise; true when the goal was reached
    // now. The level comes with Mature, in a stored change of the character.
    public static bool Tally(ItemInstance item, int amount = 1)
    {
        if (item.Maturity is not { Goal: > 0 } maturity || maturity.Count >= maturity.Goal || amount <= 0)
        {
            return false;
        }

        var count = (int)Math.Min(maturity.Goal, (long)maturity.Count + amount);
        item.Maturity = maturity with { Count = count };
        return count >= maturity.Goal;
    }

    // A copy that reached its goal gains its level; true when it did.
    public static bool Mature(ItemInstance item)
    {
        if (!Matured(item))
        {
            return false;
        }

        LevelUp(item, item.Maturity.Stone);
        item.Maturity = null;
        return true;
    }

    // Raises a weapon, armour, robe, shield, gloves or boots to a level at once, as made by a game master; other items stay.
    public static void AgeTo(ItemInstance item, int level)
    {
        if (Family(item) is not ("WA1" or "WC1" or "WH1" or "WM1" or "WP1" or "WS1" or "WS2" or "WT1" or "DA1" or "DB1" or "DG1" or "DS1" or "DA2"))
        {
            return;
        }

        while (item.UpgradeLevel < Math.Min(level, MaxLevel))
        {
            LevelUp(item, AgingStone.None);
        }
    }

    // Counts events on an aging copy; true when the copy matured and gained its level.
    public static bool Count(ItemInstance item, int amount = 1)
    {
        if (item.Maturity is not { } maturity)
        {
            return false;
        }

        var count = (int)Math.Min(maturity.Goal, (long)maturity.Count + amount);
        if (maturity.Goal > 0 && count >= maturity.Goal)
        {
            LevelUp(item, maturity.Stone);
            item.Maturity = null;
            return true;
        }

        item.Maturity = maturity with { Count = count };
        return false;
    }

    private static int GoalFor(ItemInstance item)
    {
        var level = Math.Clamp(item.UpgradeLevel, 0, AttackGoal.Length - 1);
        if (string.Equals(item.Definition.CatalogKey, AgeableRing, StringComparison.OrdinalIgnoreCase))
        {
            return RingGoal;
        }

        return Family(item) switch
        {
            "WC1" or "WS2" or "WP1" => CriticalGoal[level],
            "WA1" or "WH1" or "WM1" or "WS1" or "WT1" => AttackGoal[level],
            "DS1" => BlockGoal[level],
            _ => HitGoal[level]
        };
    }

    // One level up: the family's gains, then the level; the level requirement rises on even levels unless the item asks
    // level 100, 102 or 105 or the aging used the elder copper ore or a super stone.
    public static void LevelUp(ItemInstance item, AgingStone stone)
    {
        var level = item.UpgradeLevel;
        var roll = item.Roll;
        var odd = level % 2 == 1;
        int Damage() => 1 + (level >= 9 ? 1 : 0) + (level >= 19 ? 1 : 0);
        float Absorb(float step) => step * (1 + (level >= 9 ? 1 : 0) + (level >= 19 ? 1 : 0));
        var wear = false;
        switch (Family(item))
        {
            case "WA1" or "WH1":
                roll = roll with { DamageMin = roll.DamageMin + Damage(), DamageMax = roll.DamageMax + Damage(), AttackRating = roll.AttackRating + 10 };
                wear = true;
                break;
            case "WC1" or "WP1" or "WS2":
                roll = roll with
                {
                    DamageMin = roll.DamageMin + Damage(), DamageMax = roll.DamageMax + Damage(), AttackRating = roll.AttackRating + 5,
                    CriticalBonus = roll.CriticalBonus + (odd ? 1 : 0)
                };
                break;
            case "WM1":
                roll = roll with
                {
                    DamageMin = roll.DamageMin + Damage(), DamageMax = roll.DamageMax + Damage(), AttackRating = roll.AttackRating + 10,
                    AddMana = roll.AddMana + 10
                };
                break;
            case "WS1":
                roll = roll with { DamageMin = roll.DamageMin + Damage(), DamageMax = roll.DamageMax + Damage(), CriticalBonus = roll.CriticalBonus + (odd ? 1 : 0) };
                wear = true;
                break;
            case "WT1":
                roll = roll with { DamageMin = roll.DamageMin + Damage(), DamageMax = roll.DamageMax + Damage(), CriticalBonus = roll.CriticalBonus + (odd ? 1 : 0) };
                break;
            case "DS1":
                roll = roll with { Block = roll.Block + (odd ? 1 : 0), Absorb = roll.Absorb + Absorb(0.2f) };
                wear = true;
                break;
            case "OM1" or "DB1" or "DG1":
                roll = roll with { Defence = roll.Defence + (int)(roll.Defence / 10f), Absorb = roll.Absorb + Absorb(0.5f) };
                wear = true;
                break;
            case "DA1" or "DA2":
                roll = roll with { Defence = roll.Defence + (int)(roll.Defence / 20f), Absorb = roll.Absorb + Absorb(0.5f) };
                wear = true;
                break;
            case "OA2":
                roll = roll with { Defence = roll.Defence + (int)(roll.Defence / 10f), AttackRating = roll.AttackRating + 5 };
                wear = true;
                break;
        }

        if (wear)
        {
            item.DurabilityMax = Math.Max(0, item.DurabilityMax - (item.DurabilityMax / 100 + 1));
            item.DurabilityCurrent = Math.Min(item.DurabilityCurrent, item.DurabilityMax);
        }

        item.UpgradeLevel = level + 1;
        var fixedLevel = roll.RequiredLevel is 100 or 102 or 105 || stone is AgingStone.ElderCopperOre or AgingStone.SuperAgingStone
            or AgingStone.SuperiorCopperOre;
        if (!fixedLevel && item.UpgradeLevel % 2 == 0)
        {
            roll = roll with { RequiredLevel = roll.RequiredLevel + 1 };
        }

        item.Roll = roll;
    }

    // Falls `steps` levels (2 or 3: the aging that follows matures one back).
    public static void LevelDown(ItemInstance item, int steps)
    {
        var old = item.UpgradeLevel;
        var level = Math.Max(0, old - steps);
        item.UpgradeLevel = level;
        var odd = level % 2 == 1;
        var roll = item.Roll;
        var damage = old < 10 ? steps : steps switch
        {
            1 => 2,
            2 => old == 10 ? 3 : 4,
            _ => old == 10 ? 4 : old == 11 ? 5 : 6
        };
        float Absorb(float step)
        {
            var taken = step * steps;
            var above = old - 9;
            return above <= 0 ? taken : taken + (above == 1 ? step : step * steps);
        }

        int Critical() => 1 + (steps > 2 && odd ? 1 : 0);
        int Defence(int percent)
        {
            var value = roll.Defence;
            for (var index = 0; index < steps; index++)
            {
                value = (int)(100f * value / (100f + percent));
            }

            return value;
        }

        var wear = false;
        switch (Family(item))
        {
            case "WA1" or "WH1":
                roll = roll with { DamageMin = roll.DamageMin - damage, DamageMax = roll.DamageMax - damage, AttackRating = roll.AttackRating - 10 * steps };
                wear = true;
                break;
            case "WC1" or "WP1" or "WS2":
                roll = roll with
                {
                    DamageMin = roll.DamageMin - damage, DamageMax = roll.DamageMax - damage, AttackRating = roll.AttackRating - 5 * steps,
                    CriticalBonus = roll.CriticalBonus - Critical()
                };
                break;
            case "WM1":
                roll = roll with
                {
                    DamageMin = roll.DamageMin - damage, DamageMax = roll.DamageMax - damage, AttackRating = roll.AttackRating - 10 * steps,
                    AddMana = roll.AddMana - 10 * steps
                };
                break;
            case "WS1":
                roll = roll with { DamageMin = roll.DamageMin - damage, DamageMax = roll.DamageMax - damage, CriticalBonus = roll.CriticalBonus - Critical() };
                wear = true;
                break;
            case "WT1":
                roll = roll with { DamageMin = roll.DamageMin - damage, DamageMax = roll.DamageMax - damage, CriticalBonus = roll.CriticalBonus - Critical() };
                break;
            case "DS1":
                roll = roll with { Block = roll.Block - Critical(), Absorb = roll.Absorb - Absorb(0.2f) };
                wear = true;
                break;
            case "OM1" or "DB1" or "DG1":
                roll = roll with { Defence = Defence(10), Absorb = roll.Absorb - Absorb(0.5f) };
                wear = true;
                break;
            case "DA1" or "DA2":
                roll = roll with { Defence = Defence(5), Absorb = roll.Absorb - Absorb(0.5f) };
                wear = true;
                break;
            case "OA2":
                roll = roll with { Defence = Defence(10), AttackRating = roll.AttackRating - 5 * steps };
                wear = true;
                break;
        }

        if (wear)
        {
            item.DurabilityMax += (item.DurabilityMax / 100 + 1) * steps;
        }

        var required = roll.RequiredLevel;
        if (required < 100)
        {
            required--;
        }

        if (steps > 2 && odd)
        {
            required--;
        }

        item.Roll = roll with { RequiredLevel = Math.Max(1, required) };
    }
}
