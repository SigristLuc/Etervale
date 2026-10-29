using PristonTale.Simulation.Modifiers;
using PristonTale.Contracts.Units;
using PristonTale.Simulation.World;

namespace PristonTale.Simulation.Items.Usable;

// What a monster crystal or soul stone calls. A pet fights for its owner; a clan pet also needs a regular clan; a
// castle mercenary guards the castle-master clan during a siege; a soul stone releases a hostile monster.
public enum CrystalKind : byte
{
    Pet,
    ClanPet,
    CastleMercenary,
    SoulStone
}

// One monster a crystal may call: its key (MonsterDefinition.Key) and its weight in a weighted draw (0 = drawn uniformly).
public readonly record struct CrystalMonster(string Key, int Weight = 0);

// A crystal's monsters outside and during the crystal event; one entry means a fixed monster. SharedDelay crystals
// refuse while the three-second delay of the last crystal runs.
public sealed record MonsterCrystalDefinition(string CatalogKey, string Name, CrystalKind Kind, IReadOnlyList<CrystalMonster> Monsters,
    IReadOnlyList<CrystalMonster> EventMonsters, bool SharedDelay)
{
    // The monster for a roll in 0..9999: a weighted draw over percent weights, else a uniform one.
    public string Pick(int roll, bool crystalEvent)
    {
        var table = crystalEvent ? EventMonsters : Monsters;
        if (table.Count == 1)
        {
            return table[0].Key;
        }

        if (table.Any(static monster => monster.Weight > 0))
        {
            var percent = roll % 100;
            var sum = 0;
            foreach (var monster in table)
            {
                sum += monster.Weight;
                if (percent < sum)
                {
                    return monster.Key;
                }
            }

            return table[^1].Key;
        }

        return table[roll % table.Count].Key;
    }
}

public static class MonsterCrystals
{
    // Every crystal use starts it; billing crystals and soul stones refuse while it runs.
    public const float SharedDelayMs = 3000f;

    // A pet lives four minutes, plus two seconds and three life points per owner level from 1 to 99.
    public const float PetBaseLifetimeMs = 240_000f;
    public const int SoulLifetimeSeconds = 240;

    // Clan pets: at most three of the clan within this distance of the user, one per player.
    public const int ClanPetLimit = 3;
    public const float ClanPetRadius = 1810f;

    // Castle mercenaries: twenty per clan, five per kind, one per player.
    public const int MercenaryClanLimit = 20;
    public const int MercenaryKindLimit = 5;

    // Fields where soul stones do not work besides towns: the castle, the ice field and the office room.
    public static readonly int[] SoulStoneBlockedFields = [FieldCodes.Castle, FieldCodes.InnerIce, FieldCodes.OfficeRoom];

    // A web pet's blow slows its target to half speed for 36 frames of 70 Hz; monsters with Kelvezu's sound are immune.
    public const uint WebSlowStatusCode = 0xF0000301;
    public const float WebSlowMs = 36 * GameUnits.TickMs;

    public static EffectSpec WebSlow() =>
        new(WebSlowStatusCode, EffectSourceKind.Item, true, [StatModifier.Percent(StatKind.MoveSpeed, -50)], control: ControlFlags.Slow);

    // The quest arena refuses every crystal.

    private static readonly Dictionary<string, MonsterCrystalDefinition> Crystals = Build();

    public static IReadOnlyCollection<MonsterCrystalDefinition> All => Crystals.Values;

    public static bool TryGet(string catalogKey, out MonsterCrystalDefinition crystal)
    {
        crystal = null;
        return !string.IsNullOrEmpty(catalogKey) && Crystals.TryGetValue(catalogKey, out crystal);
    }

    public static float PetLifetimeMs(int ownerLevel) => PetBaseLifetimeMs + (ownerLevel is >= 1 and <= 99 ? ownerLevel * 2000f : 0f);

    public static int PetBonusLife(int ownerLevel) => ownerLevel is >= 1 and <= 99 ? ownerLevel * 3 : 0;

    // The crystal event runs from 12:00 to 23:59 server time (mode 1) or all day (mode 2).
    public static bool EventActive(int mode, int hour) => mode == 2 || mode == 1 && hour >= 12;

    private static Dictionary<string, MonsterCrystalDefinition> Build()
    {
        var crystals = new Dictionary<string, MonsterCrystalDefinition>(StringComparer.OrdinalIgnoreCase);
        void Pet(string key, string name, string monster, string eventMonster) =>
            crystals[key] = new MonsterCrystalDefinition(key, name, CrystalKind.Pet, [new(monster)], [new(eventMonster)], false);

        Pet("GP101", "Hopy", "HOPY 2", "Zzangpy 2");
        Pet("GP102", "Hobgoblin", "Hobgoblin 2", "Wild Goblin");
        Pet("GP103", "Decoy", "Decoy 2", "Maximum Decoy");
        Pet("GP104", "Bargon", "Bargon", "Bauton 2");
        Pet("GP105", "Head Cutter", "Head Cutter 2", "Vega Demir");
        Pet("GP106", "Figon", "Figon 2", "HellSing 2");
        Pet("GP107", "King Hopy", "Hopy King 2", "Horropion 2");
        Pet("GP108", "Hulk", "Hulk 2", "Typhoon 2");
        Pet("GP111", "Web", "Web 2", "CurseWeb");
        Pet("GP112", "Dark Specter", "Dark Specter 2", "Dark Phantom");
        Pet("GP113", "Iron Guard", "Iron Guard 2", "Imitir Guard");

        crystals["GP109"] = new MonsterCrystalDefinition("GP109", "Mystic", CrystalKind.Pet,
            [new("HOPY 2", 20), new("Hobgoblin 2", 20), new("Decoy 2", 15), new("Bargon", 15), new("Head Cutter 2", 15), new("Figon 2", 10), new("Hopy King 2", 5)],
            [new("Zzangpy 2", 20), new("Wild Goblin", 20), new("Maximum Decoy", 15), new("Bauton 2", 15), new("Vega Demir", 15), new("HellSing 2", 10),
                new("Horropion 2", 5)],
            false);
        crystals["GP121"] = new MonsterCrystalDefinition("GP121", "Marvel", CrystalKind.Pet,
            [new("Bargon", 20), new("Head Cutter 2", 20), new("Figon 2", 15), new("Hopy King 2", 15), new("Hulk 2", 12), new("Web 2", 10), new("Dark Specter 2", 5),
                new("Iron Guard 2", 3)],
            [new("Bauton 2", 20), new("Vega Demir", 20), new("HellSing 2", 15), new("Horropion 2", 15), new("Typhoon 2", 12), new("CurseWeb", 10), new("Dark Phantom", 5),
                new("Imitir Guard", 3)],
            false);

        void Billing(string key, string name, CrystalMonster[] monsters) =>
            crystals[key] = new MonsterCrystalDefinition(key, name, CrystalKind.Pet, monsters, monsters, true);

        Billing("GP117", "Skeleton", [new("Skeleton Archer 2"), new("Skeleton Ranger 2"), new("Skeleton Warrior 2"), new("Skeleton Knight 2")]);
        Billing("GP118", "Chaos", [new("Naz 2"), new("Heavy Goblin 2"), new("Strider 2"), new("D-Machine 2")]);
        Billing("GP119", "Inferna", [new("Ratoo 2"), new("Grotesque 2"), new("Iron Fist 2"), new("Frost 2")]);
        Billing("GP120", "Extreme", [new("Omicron 2", 35), new("Spidertrooper 2", 30), new("Mountain 2", 20), new("Omega 3", 15)]);

        crystals["GP110"] = new MonsterCrystalDefinition("GP110", "Guardian Saint", CrystalKind.ClanPet, [new("Guardian Saint")], [new("Guardian Saint")], false);
        crystals["GP122"] = new MonsterCrystalDefinition("GP122", "Guardian Divine", CrystalKind.ClanPet, [new("Guardian Divine")], [new("Guardian Divine")], false);
        crystals["GP114"] = new MonsterCrystalDefinition("GP114", "Ricarten Militia", CrystalKind.CastleMercenary, [new("Ricarten Militia")], [new("Ricarten Militia")], false);
        crystals["GP115"] = new MonsterCrystalDefinition("GP115", "Ricarten Guard", CrystalKind.CastleMercenary, [new("Ricarten Guard")], [new("Ricarten Guard")], false);
        crystals["GP116"] = new MonsterCrystalDefinition("GP116", "Bless Royal Guard", CrystalKind.CastleMercenary, [new("Bless Royal Guard")], [new("Bless Royal Guard")],
            false);

        // A soul stone calls the soul of each monster it names (the data has no soul monsters yet, so none appears).
        void Soul(string key, string name, params string[] souls)
        {
            var monsters = souls.Select(static soul => new CrystalMonster($"Soul of {soul}")).ToArray();
            crystals[key] = new MonsterCrystalDefinition(key, name, CrystalKind.SoulStone, monsters, monsters, true);
        }

        Soul("GP201", "Green Soul Stone", "Cokris", "Leech", "Cyclops", "Mephit", "Bee Dog", "Zombi", "Decoy");
        Soul("GP202", "Yellow Soul Stone", "Vampiric Bat", "Greven", "Evil Snail", "Crypt", "Buma", "Bagon", "Titan");
        Soul("GP203", "Blue Soul Stone", "BeEvil", "Slayon", "Slaughter", "Tower Golem", "Mummy", "Naz", "Witch");
        Soul("GP204", "Magenta Soul Stone", "Berserker", "Typhoon", "Lizardfolk", "Rampage", "Avelin", "Incubus", "Sadness");
        Soul("GP205", "Cyan Soul Stone", "D-Machine", "Hopy King", "Omega", "DeadZone", "Icegolem", "Coldeye", "Icegoblin");
        Soul("GP206", "Violet Soul Stone", "Vampiric Bat", "Greven", "Evil Snail", "Crypt", "Buma", "Bagon", "Muffin");
        Soul("GP207", "Red Soul Stone", "BeEvil", "Slayon", "Slaughter", "Tower Golem", "Mummy", "Naz", "Sliver");
        Soul("GP208", "Black Soul Stone", "Berserker", "Typhoon", "Lizardfolk", "Rampage", "Avelin", "Incubus", "Metron");
        Soul("GP209", "Platinum Soul Stone", "D-Machine", "Hopy King", "Omega", "DeadZone", "Icegolem", "Coldeye", "Grotesque");
        return crystals;
    }
}
