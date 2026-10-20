using PristonTale.Simulation.Items;
using PristonTale.Simulation.World.Loot;
using PristonTale.Simulation.World.Monsters;

namespace PristonTale.WorldImport;

// Korean token -> column/enum maps of the authored world data files. Token strings are exactly the keys the
// original parsers compare against; none are invented. Three file families use them: item definitions (.txt),
// monster (.inf) and NPC (.npc) character definitions, and field spawn tables (.spm).
//
// The item and monster files spell resistances with different Korean tokens, and the monster set is not in
// elemental order. Both map to the canonical eight-element ResistanceBlock order (bionic, earth, fire, ice,
// lighting, poison, water, wind), the order of the item element indices and Combat/Elements.cs.
public static class KeyMaps
{
    // Item definition (.txt) tokens.

    public const string ItemName = "*이름";          // quoted name
    public const string ItemCode = "*코드";           // 5-character item code
    public const string ItemUnique = "*유니크";        // unique flag
    public const string ItemDurability = "*내구력";    // durability min/max
    public const string ItemWeight = "*무게";          // weight
    public const string ItemPrice = "*가격";           // price
    public const string ItemDamage = "*공격력";        // damage min/max pairs
    public const string ItemShootingRange = "*사정거리"; // attack range
    public const string ItemAttackSpeed = "*공격속도";  // attack speed
    public const string ItemAttackRating = "*명중력";  // attack rating min/max
    public const string ItemCritical = "*크리티컬";     // critical chance
    public const string ItemAbsorb = "*흡수력";        // absorption min/max
    public const string ItemDefence = "*방어력";       // defence min/max
    public const string ItemBlock = "*블럭율";         // block rating min/max
    public const string ItemSpeed = "*이동속도";       // move speed min/max
    public const string ItemReqLevel = "*레벨";        // required level
    public const string ItemReqStrength = "*힘";       // required strength
    public const string ItemReqSpirit = "*정신력";     // required spirit
    public const string ItemReqTalent = "*재능";       // required talent
    public const string ItemReqDexterity = "*민첩성";  // required dexterity
    public const string ItemReqHealth = "*건강";       // required health
    public const string ItemLinkedFile = "*연결파일";  // continues in another file

    // Mana/Life/Stamina regen and +add have dual aliases (newer/older naming).
    public static readonly string[] ItemManaRegen = ["*마나재생", "*기력재생"];
    public static readonly string[] ItemLifeRegen = ["*라이프재생", "*생명력재생"];
    public static readonly string[] ItemStaminaRegen = ["*스테미나재생", "*근력재생"];
    public static readonly string[] ItemAddMana = ["*마나추가", "*기력추가"];
    public static readonly string[] ItemAddLife = ["*라이프추가", "*생명력추가"];
    public static readonly string[] ItemAddStamina = ["*스테미나추가", "*근력추가"];

    // Potion restore.
    public static readonly string[] ItemRestoreStamina = ["*스테미너상승", "*근력상승"];
    public static readonly string[] ItemRestoreMana = ["*마나상승", "*기력상승"];
    public static readonly string[] ItemRestoreLife = ["*라이프상승", "*생명력상승"];

    public const string ItemJobMask = "**특화";        // class specialization mask (overwrites)
    public const string ItemJobRandom = "**특화랜덤";   // random class specialization list

    // ITEM resistance tokens, in ResistanceBlock order (bionic..wind).
    public static readonly (string Token, int Element)[] ItemResistances =
    [
        ("*생체", 0),    // BIONIC
        ("*대자연", 1),  // EARTH
        ("*불", 2),      // FIRE
        ("*냉기", 3),    // ICE
        ("*번개", 4),    // LIGHTING
        ("*독", 5),      // POISON
        ("*물", 6),      // WATER
        ("*바람", 7),    // WIND
    ];

    // Monster (.inf) character tokens.

    public const string CharName = "*이름";            // Legacy identity used by spawn references.
    public const string CharDisplayName = "*Name";     // Display label authored in the supplied monster files.
    public const string CharModel = "*모양파일";       // quoted model file
    public const string CharDialog = "*대화";          // dialog line (at most 20)
    public const string CharAttribute = "*속성";       // 적 = enemy, otherwise NPC
    public const string CharSize = "*크기";            // size level
    public static readonly string[] CharSound = ["*소리", "*효과음"]; // sound family code
    public const string MonLevel = "*레벨";            // level
    public const string MonBoss = "*두목";             // marks a boss class
    public const string MonClass = "*계급";            // numeric class
    public const string MonMoveSpeed = "*이동력";      // move speed
    public const string MonAttack = "*공격력";         // attack damage min/max
    public const string MonAttackSpeed = "*공격속도";  // attack speed
    public const string MonAttackRange = "*공격범위";  // attack range
    public const string MonAccuracy = "*명중력";       // attack rating
    public const string MonDefence = "*방어력";        // defence
    public const string MonAbsorb = "*흡수율";         // absorption
    public const string MonBlock = "*블럭율";          // block chance
    public const string MonSight = "*시야";            // sight (raw squared distance)
    public const string MonSkillDamage = "*기술공격력"; // skill damage min/max
    public const string MonSkillDistance = "*기술공격거리"; // skill distance
    public const string MonSkillRange = "*기술공격범위";    // skill area
    public const string MonSkillRating = "*기술공격률";     // skill chance
    public const string MonSkillCurse = "*저주기술";        // skill curse
    public const string MonMoveRange = "*이동범위";    // roaming range
    public const string MonActiveHour = "*활동시간";   // active hours: 낮 = +1, 밤 = -1
    public const string MonGroup = "*조직";            // group size min/max
    public const string MonIq = "*지능";               // intelligence
    public const string MonNature = "*품성";           // good/evil → Nature
    public const string MonBrood = "*몬스터종족";      // brood
    public const string MonExp = "*경험치";            // experience

    // Active-hour value tokens. 낮(day)=+1, 밤(night)=-1, else 0 (any).
    public const string ActiveHourDay = "낮";
    public const string ActiveHourNight = "밤";

    // Nature value literals (English, case-insensitive).
    public static MonsterNature ParseNature(string word) => word.ToLowerInvariant() switch
    {
        "good" => MonsterNature.Good,
        "evil" => MonsterNature.Evil,
        _ => MonsterNature.Natural,    // anything else (Neutral, blank, …) → NATURAL
    };

    // Brood value tokens. (default NORMAL).
    public static MonsterBrood ParseBrood(string word) => word switch
    {
        "언데드" => MonsterBrood.Undead,
        "뮤턴트" => MonsterBrood.Mutant,
        "디먼" => MonsterBrood.Demon,
        "메카닉" => MonsterBrood.Mechanic,
        _ => MonsterBrood.Normal,           // 노멀 / anything else
    };

    // MONSTER resistance tokens — NOT in elemental order; mapped to ResistanceBlock index.
    // (Distinct from the ITEM resistance tokens above!)
    public static readonly (string Token, int Element)[] MonsterResistances =
    [
        ("*생체", 0),    // BIONIC
        ("*물", 6),      // WATER
        ("*번개", 4),    // LIGHTING
        ("*얼음", 3),    // ICE
        ("*바람", 7),    // WIND
        ("*지동력", 1),  // EARTH
        ("*불", 2),      // FIRE
        ("*독", 5),      // POISON
    ];

    // Monster drop tokens.
    public const string MonDropAllSee = "*아이템모두";    // drops visible to everyone
    public const string MonDropMax = "*아이템카운터";     // maximum drop count
    public const string MonDropPlus = "*추가아이템";      // plus drop (percent + 1 code)
    public const string MonDrop = "*아이템";              // main drop line
    public const string DropNone = "없음";                // nothing slot
    public const string DropMoney = "돈";                 // gold slot (min/max follow)

    // NPC (.npc) service tokens.

    public const string NpcSkillMaster = "*스킬수련";
    public const string NpcJobChange = "*직업전환";       // +code
    public const string NpcEventBooth = "*이벤트매표소";
    public const string NpcWarehouse = "*아이템보관";
    public const string NpcItemMix = "*아이템조합";
    public const string NpcSmelting = "*아이템제련";
    public const string NpcManufacture = "*아이템제작";
    public const string NpcItemAlchemy = "*아이템연금";   // item mix mode 200
    public const string NpcItemAging = "*아이템에이징";
    public const string NpcMixtureReset = "*믹스처리셋";
    public const string NpcCollectMoney = "*모금함";
    public const string NpcClan = "*클랜기능";
    public const string NpcGiveMoney = "*기부함";
    public const string NpcTeleport = "*텔레포트";        // +code
    public const string NpcBlessCastle = "*블레스캐슬";   // +code
    public const string NpcQuestCode = "*퀘스트코드";     // code + param
    public const string NpcLinkedFile = "*연결파일";      // reopen

    // Sell lists (each a ≤32 item-code list).
    public const string NpcSellWeapon = "*무기판매";      // → tab 0
    public const string NpcSellDefence = "*방어구판매";   // → tab 1
    public const string NpcSellEtc = "*잡화판매";         // → tab 2

    // Field spawn table (.spm) tokens.
    // Each has a Korean and an ASCII alias (both accepted; ASCII is case-insensitive).

    public static readonly string[] SpmActor = ["*출연자", "*ACTOR"];               // name+weight
    public static readonly string[] SpmBossActor = ["*출연자두목", "*BOSS_ACTOR"];   // boss
    public static readonly string[] SpmMaxConcurrent = ["*최대동시출현수", "*MAX_ACTOR_POS"]; // concurrent limit
    public static readonly string[] SpmInterval = ["*출현간격", "*DELAY"];          // interval+ms
    public static readonly string[] SpmPerWave = ["*출현수", "*MAX_ACTOR"];          // monsters per wave

    // There is no kind token in the data: the category is encoded only by the prefix of the 5-character item
    // code, which is also the primary-category byte of the binary item code. The kind column classifies by the
    // 2-character prefix so it carries the original category.
    public static ItemKind ClassifyKind(string code)
    {
        if (string.IsNullOrEmpty(code))
            return ItemKind.Misc;
        var category = code.Length >= 3 ? code[..3].ToUpperInvariant() : code.ToUpperInvariant();
        if (category == "WS1")
            return ItemKind.Bow;
        if (category == "WS2")
            return ItemKind.Sword;
        if (category == "OA2")
            return ItemKind.Bracelet;
        var p = code.Length >= 2 ? code[..2].ToUpperInvariant() : code.ToUpperInvariant();
        return p switch
        {
            "WA" => ItemKind.Axe,            // Axes
            "WC" => ItemKind.Claw,           // Claws
            "WH" => ItemKind.Hammer,         // Hammer
            "WM" => ItemKind.Staff,          // magic staff
            "WD" => ItemKind.Dagger,
            "WN" => ItemKind.Phantom,
            "WP" => ItemKind.Pole,           // Poles
            "WS" => ItemKind.Sword,          // WS1 bows are handled above; other WS codes are swords
            "WT" => ItemKind.ThrowingWeapon, // Throwing
            "DA" => ItemKind.Armor,          // body armour
            "DB" => ItemKind.Boots,          // boots
            "DG" => ItemKind.Gloves,         // gloves
            "DS" => ItemKind.Shield,         // shields
            "OA" => ItemKind.Amulet,         // OA1 amulets; OA2 armlets are handled above
            "OM" => ItemKind.Amulet,
            "OR" => ItemKind.Ring,           // rings
            "FO" => ItemKind.ForceOrb,       // force orb
            "OS" => ItemKind.Sheltom,        // Sheltom
            "PM" => ItemKind.Potion,         // Mana potion
            "PL" => ItemKind.Potion,         // Life potion
            "PS" => ItemKind.Potion,         // Stamina potion
            "QT" => ItemKind.Quest,          // quest item
            "MA" => ItemKind.Material,       // crafting material
            _ => ItemKind.Misc,
        };
    }

    /// <summary>The eight-element ResistanceBlock order, for documentation/iteration.</summary>
    public static readonly string[] ElementOrder =
        ["bionic", "earth", "fire", "ice", "lighting", "poison", "water", "wind"];

    /// <summary>Drop-kind classification of a main <c>*아이템</c> line keyword.</summary>
    public static DropKind ClassifyDropKeyword(string keyword) => keyword switch
    {
        DropNone => DropKind.Nothing,
        DropMoney => DropKind.Gold,
        _ => DropKind.Item,
    };
}
