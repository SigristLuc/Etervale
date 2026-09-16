using PristonTale.Simulation.Items;
using PristonTale.Simulation.World.Loot;
using PristonTale.Simulation.World.Monsters;

namespace PristonTale.WorldImport;

/// <summary>
/// The Korean-token → column/enum maps, transcribed from the C++ parsers. EVERY entry is traceable to a
/// parser <c>src:line</c> (see the per-region citations). Token strings are read from the operands of the
/// <c>lstrcmp</c>/<c>lstrcmpi</c> calls in <c>fileread.cpp</c> — never invented.
///
/// Three parsers feed this:
/// <list type="bullet">
///   <item><c>DecodeItemInfo</c> (fileread.cpp:3328) — OpenItem <c>.txt</c>.</item>
///   <item><c>smCharDecode</c> (fileread.cpp:4279) — monster <c>.inf</c> and npc <c>.npc</c>.</item>
///   <item><c>DecodeOpenMonster</c> (fileread.cpp:5382) — field <c>.spm</c>.</item>
/// </list>
///
/// NOTE the resistance-token asymmetry: the ITEM parser and the MONSTER parser use <b>different</b> Korean
/// tokens for the same elements, and the monster token set is NOT in elemental order. Both are mapped to
/// the canonical eight-element ResistanceBlock order (bionic, earth, fire, ice, lighting, poison, water,
/// wind — Combat/Elements.cs / sinItem.h:344-351).
/// </summary>
public static class KeyMaps
{
    // ---- Item tokens (DecodeItemInfo, fileread.cpp:3328) ------------------------------------------------

    public const string ItemName = "*이름";          // [VERIFIED fileread.cpp:3354] quoted name
    public const string ItemCode = "*코드";           // [VERIFIED fileread.cpp:3428] 5-char LastCategory key
    public const string ItemUnique = "*유니크";        // [VERIFIED fileread.cpp:3441] UniqueItem
    public const string ItemDurability = "*내구력";    // [VERIFIED fileread.cpp:3479] sDurability[2]
    public const string ItemWeight = "*무게";          // [VERIFIED fileread.cpp:3488] Weight
    public const string ItemPrice = "*가격";           // [VERIFIED fileread.cpp:3495] Price
    public const string ItemDamage = "*공격력";        // [VERIFIED fileread.cpp:3564] sDamage[4]
    public const string ItemShootingRange = "*사정거리"; // [VERIFIED fileread.cpp:3577] Shooting_Range
    public const string ItemAttackSpeed = "*공격속도";  // [VERIFIED fileread.cpp:3583] Attack_Speed
    public const string ItemAttackRating = "*명중력";  // [VERIFIED fileread.cpp:3590] sAttack_Rating[2]
    public const string ItemCritical = "*크리티컬";     // [VERIFIED fileread.cpp:3599] Critical_Hit
    public const string ItemAbsorb = "*흡수력";        // [VERIFIED fileread.cpp:3609] fAbsorb[2]
    public const string ItemDefence = "*방어력";       // [VERIFIED fileread.cpp:3618] sDefence[2]
    public const string ItemBlock = "*블럭율";         // [VERIFIED fileread.cpp:3627] fBlock_Rating[2]
    public const string ItemSpeed = "*이동속도";       // [VERIFIED fileread.cpp:3637] fSpeed[2]
    public const string ItemReqLevel = "*레벨";        // [VERIFIED fileread.cpp:3714] Level
    public const string ItemReqStrength = "*힘";       // [VERIFIED fileread.cpp:3721] Strength
    public const string ItemReqSpirit = "*정신력";     // [VERIFIED fileread.cpp:3728] Spirit
    public const string ItemReqTalent = "*재능";       // [VERIFIED fileread.cpp:3734] Talent
    public const string ItemReqDexterity = "*민첩성";  // [VERIFIED fileread.cpp:3740] Dexterity
    public const string ItemReqHealth = "*건강";       // [VERIFIED fileread.cpp:3746] Health
    public const string ItemLinkedFile = "*연결파일";  // [VERIFIED fileread.cpp:3978] reopen file

    // Mana/Life/Stamina regen and +add have dual aliases (newer/older naming). [VERIFIED 3661-3712].
    public static readonly string[] ItemManaRegen = ["*마나재생", "*기력재생"];        // 3661
    public static readonly string[] ItemLifeRegen = ["*라이프재생", "*생명력재생"];    // 3669
    public static readonly string[] ItemStaminaRegen = ["*스테미나재생", "*근력재생"]; // 3677
    public static readonly string[] ItemAddMana = ["*마나추가", "*기력추가"];          // 3686
    public static readonly string[] ItemAddLife = ["*라이프추가", "*생명력추가"];      // 3694
    public static readonly string[] ItemAddStamina = ["*스테미나추가", "*근력추가"];   // 3702

    // Potion restore. [VERIFIED 3756-3774].
    public static readonly string[] ItemRestoreStamina = ["*스테미너상승", "*근력상승"];  // 3756
    public static readonly string[] ItemRestoreMana = ["*마나상승", "*기력상승"];        // 3765
    public static readonly string[] ItemRestoreLife = ["*라이프상승", "*생명력상승"];    // 3774

    public const string ItemJobMask = "**특화";        // [VERIFIED fileread.cpp:3787] JobCodeMask (overwrite)
    public const string ItemJobRandom = "**특화랜덤";   // [VERIFIED fileread.cpp:3807] dwJobBitCode_Random[]

    // ITEM resistance tokens, in ResistanceBlock order (bionic..wind). [VERIFIED fileread.cpp:3502-3551].
    public static readonly (string Token, int Element)[] ItemResistances =
    [
        ("*생체", 0),    // BIONIC   fileread.cpp:3502
        ("*대자연", 1),  // EARTH    fileread.cpp:3509
        ("*불", 2),      // FIRE     fileread.cpp:3516
        ("*냉기", 3),    // ICE      fileread.cpp:3523
        ("*번개", 4),    // LIGHTING fileread.cpp:3530
        ("*독", 5),      // POISON   fileread.cpp:3537
        ("*물", 6),      // WATER    fileread.cpp:3544
        ("*바람", 7),    // WIND     fileread.cpp:3551
    ];

    // ---- Monster tokens (smCharDecode, fileread.cpp:4279) ----------------------------------------------

    public const string CharName = "*이름";            // Legacy identity used by spawn references (fileread.cpp:4330).
    public const string CharDisplayName = "*Name";     // Display label authored in the supplied monster files.
    public const string CharModel = "*모양파일";       // [VERIFIED fileread.cpp:4440] szModelName (quoted)
    public const string CharDialog = "*대화";          // [VERIFIED fileread.cpp:4453] dialog line (≤20)
    public const string CharAttribute = "*속성";       // [VERIFIED fileread.cpp:4590] 적=enemy/NPC
    public const string CharSize = "*크기";            // [VERIFIED fileread.cpp:4602] SizeLevel
    public static readonly string[] CharSound = ["*소리", "*효과음"]; // [VERIFIED fileread.cpp:4617] dwCharSoundCode
    public const string MonLevel = "*레벨";            // [VERIFIED fileread.cpp:4635] Level
    public const string MonBoss = "*두목";             // [VERIFIED fileread.cpp:4642] wPlayClass[0]=BOSS
    public const string MonClass = "*계급";            // [VERIFIED fileread.cpp:4648] wPlayClass[0]=atoi
    public const string MonMoveSpeed = "*이동력";      // [VERIFIED fileread.cpp:4664] Move_Speed
    public const string MonAttack = "*공격력";         // [VERIFIED fileread.cpp:4671] Attack_Damage[2]
    public const string MonAttackSpeed = "*공격속도";  // [VERIFIED fileread.cpp:4681] Attack_Speed
    public const string MonAttackRange = "*공격범위";  // [VERIFIED fileread.cpp:4691] Shooting_Range
    public const string MonAccuracy = "*명중력";       // [VERIFIED fileread.cpp:4700] Attack_Rating
    public const string MonDefence = "*방어력";        // [VERIFIED fileread.cpp:4708] Defence
    public const string MonAbsorb = "*흡수율";         // [VERIFIED fileread.cpp:4715] Absorption
    public const string MonBlock = "*블럭율";          // [VERIFIED fileread.cpp:4722] Chance_Block
    public const string MonSight = "*시야";            // [VERIFIED fileread.cpp:4795] Sight (RAW, src squares)
    public const string MonSkillDamage = "*기술공격력"; // [VERIFIED fileread.cpp:4827] SkillDamage[2]
    public const string MonSkillDistance = "*기술공격거리"; // [VERIFIED fileread.cpp:4837] SkillDistance
    public const string MonSkillRange = "*기술공격범위";    // [VERIFIED fileread.cpp:4846] SkillRange
    public const string MonSkillRating = "*기술공격률";     // [VERIFIED fileread.cpp:4854] SkillRating
    public const string MonSkillCurse = "*저주기술";        // [VERIFIED fileread.cpp:4862] SkillCurse
    public const string MonMoveRange = "*이동범위";    // [VERIFIED fileread.cpp:4876] MoveRange
    public const string MonActiveHour = "*활동시간";   // [VERIFIED fileread.cpp:4884] ActiveHour 낮=+1/밤=-1
    public const string MonGroup = "*조직";            // [VERIFIED fileread.cpp:4895] GenerateGroup[2]
    public const string MonIq = "*지능";               // [VERIFIED fileread.cpp:4904] IQ
    public const string MonNature = "*품성";           // [VERIFIED fileread.cpp:4925] good/evil → Nature
    public const string MonBrood = "*몬스터종족";      // [VERIFIED fileread.cpp:4989] brood
    public const string MonExp = "*경험치";            // [VERIFIED fileread.cpp:5012] Exp

    // Active-hour value tokens. [VERIFIED fileread.cpp:4884-4892]. 낮(day)=+1, 밤(night)=-1, else 0 (any).
    public const string ActiveHourDay = "낮";
    public const string ActiveHourNight = "밤";

    // Nature value literals (English, case-insensitive). [VERIFIED fileread.cpp:4925-4939].
    public static MonsterNature ParseNature(string word) => word.ToLowerInvariant() switch
    {
        "good" => MonsterNature.Good,
        "evil" => MonsterNature.Evil,
        _ => MonsterNature.Natural,    // anything else (Neutral, blank, …) → NATURAL
    };

    // Brood value tokens. [VERIFIED fileread.cpp:4989-5007] (default NORMAL).
    public static MonsterBrood ParseBrood(string word) => word switch
    {
        "언데드" => MonsterBrood.Undead,    // fileread.cpp:4992
        "뮤턴트" => MonsterBrood.Mutant,    // fileread.cpp:4996
        "디먼" => MonsterBrood.Demon,       // fileread.cpp:5000
        "메카닉" => MonsterBrood.Mechanic,  // fileread.cpp:5004
        _ => MonsterBrood.Normal,           // 노멀 / anything else
    };

    // MONSTER resistance tokens — NOT in elemental order; mapped to ResistanceBlock index.
    // [VERIFIED fileread.cpp:4739-4787]. (Distinct from the ITEM resistance tokens above!)
    public static readonly (string Token, int Element)[] MonsterResistances =
    [
        ("*생체", 0),    // BIONIC   fileread.cpp:4739
        ("*물", 6),      // WATER    fileread.cpp:4745
        ("*번개", 4),    // LIGHTING fileread.cpp:4751
        ("*얼음", 3),    // ICE      fileread.cpp:4758
        ("*바람", 7),    // WIND     fileread.cpp:4764
        ("*지동력", 1),  // EARTH    fileread.cpp:4770
        ("*불", 2),      // FIRE     fileread.cpp:4776
        ("*독", 5),      // POISON   fileread.cpp:4782
    ];

    // Monster drop tokens. [VERIFIED fileread.cpp:5036-5132].
    public const string MonDropAllSee = "*아이템모두";    // 5036 AllSeeItem
    public const string MonDropMax = "*아이템카운터";     // 5041 FallItemMax
    public const string MonDropPlus = "*추가아이템";      // 5047 plus drop (percent + 1 code)
    public const string MonDrop = "*아이템";              // 5064 main drop line
    public const string DropNone = "없음";                // 5071 nothing slot
    public const string DropMoney = "돈";                 // 5085 gold slot (min/max follow)

    // ---- NPC tokens (smCharDecode npc branch, fileread.cpp:5180-5354) ----------------------------------

    public const string NpcSkillMaster = "*스킬수련";     // [VERIFIED fileread.cpp:5180]
    public const string NpcJobChange = "*직업전환";       // [VERIFIED fileread.cpp:5184] +code
    public const string NpcEventBooth = "*이벤트매표소";  // [VERIFIED fileread.cpp:5194]
    public const string NpcWarehouse = "*아이템보관";     // [VERIFIED fileread.cpp:5203]
    public const string NpcItemMix = "*아이템조합";       // [VERIFIED fileread.cpp:5207]
    public const string NpcSmelting = "*아이템제련";      // [VERIFIED fileread.cpp:5212]
    public const string NpcManufacture = "*아이템제작";   // [VERIFIED fileread.cpp:5218]
    public const string NpcItemAlchemy = "*아이템연금";   // [VERIFIED fileread.cpp:5223] ItemMix=200
    public const string NpcItemAging = "*아이템에이징";   // [VERIFIED fileread.cpp:5227]
    public const string NpcMixtureReset = "*믹스처리셋";  // [VERIFIED fileread.cpp:5232]
    public const string NpcCollectMoney = "*모금함";      // [VERIFIED fileread.cpp:5237]
    public const string NpcClan = "*클랜기능";            // [VERIFIED fileread.cpp:5251]
    public const string NpcGiveMoney = "*기부함";         // [VERIFIED fileread.cpp:5289]
    public const string NpcTeleport = "*텔레포트";        // [VERIFIED fileread.cpp:5294] +code
    public const string NpcBlessCastle = "*블레스캐슬";   // [VERIFIED fileread.cpp:5303] +code
    public const string NpcQuestCode = "*퀘스트코드";     // [VERIFIED fileread.cpp:5344] code + param
    public const string NpcLinkedFile = "*연결파일";      // [VERIFIED fileread.cpp:5354] reopen

    // Sell lists (each a ≤32 item-code list). [VERIFIED fileread.cpp:5132/5148/5164].
    public const string NpcSellWeapon = "*무기판매";      // 5132 → tab 0
    public const string NpcSellDefence = "*방어구판매";   // 5148 → tab 1
    public const string NpcSellEtc = "*잡화판매";         // 5164 → tab 2

    // ---- Field .spm tokens (DecodeOpenMonster, fileread.cpp:5382) --------------------------------------
    // Each has a Korean and an ASCII alias (both accepted; ASCII is case-insensitive).

    public static readonly string[] SpmActor = ["*출연자", "*ACTOR"];               // [VERIFIED 5414] name+weight
    public static readonly string[] SpmBossActor = ["*출연자두목", "*BOSS_ACTOR"];   // [VERIFIED 5434] boss
    public static readonly string[] SpmMaxConcurrent = ["*최대동시출현수", "*MAX_ACTOR_POS"]; // [VERIFIED 5469] LimitMax
    public static readonly string[] SpmInterval = ["*출현간격", "*DELAY"];          // [VERIFIED 5475] interval+ms
    public static readonly string[] SpmPerWave = ["*출현수", "*MAX_ACTOR"];          // [VERIFIED 5487] OpenLimit

    // ---- Item-code prefix → ItemKind ------------------------------------------------------------------
    // There is NO *종류/kind token in the data: the item category is entirely encoded by the LastCategory
    // string prefix (the precompiled sItem[].CODE primary-category byte — sinItem.h:103-191). We classify
    // by the 2-char category prefix of the 5-char code so the kind column carries the original category.
    // [VERIFIED sinItem.h:103-191 (category defines), playsub.cpp:758-761 (code-byte extraction)].
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
            "WA" => ItemKind.Axe,            // sinWA1 Axes
            "WC" => ItemKind.Claw,           // sinWC1 Claws
            "WH" => ItemKind.Hammer,         // sinWH1 Hammer
            "WM" => ItemKind.Staff,          // sinWM1 MagStuff (magic staff)
            "WD" => ItemKind.Dagger,         // ITEMTYPE_Dagger
            "WN" => ItemKind.Phantom,        // ITEMTYPE_Phantom
            "WP" => ItemKind.Pole,           // sinWP1 Poles
            "WS" => ItemKind.Sword,          // sinWS1 Shooters / sinWS2 Swords (both 'WS') — defaults Sword
            "WT" => ItemKind.ThrowingWeapon, // sinWT1 Throwing
            "DA" => ItemKind.Armor,          // sinDA1/sinDA2 body armour
            "DB" => ItemKind.Boots,          // sinDB1 boots
            "DG" => ItemKind.Gloves,         // sinDG1 gloves
            "DS" => ItemKind.Shield,         // sinDS1 shields
            "OA" => ItemKind.Amulet,         // sinOA1 Amulet / sinOA2 Armlet → Bracelet handled below
            "OM" => ItemKind.Amulet,         // sinOM1
            "OR" => ItemKind.Ring,           // sinOR1/sinOR2 Ring
            "FO" => ItemKind.ForceOrb,       // sinFO1 force orb
            "OS" => ItemKind.Sheltom,        // sinOS1 Sheltom
            "PM" => ItemKind.Potion,         // sinPM1 Mana potion
            "PL" => ItemKind.Potion,         // sinPL1 Life potion
            "PS" => ItemKind.Potion,         // sinPS1 Stamina potion
            "QT" => ItemKind.Quest,          // sinQT1 quest item
            "MA" => ItemKind.Material,       // sinMA1/MA2 make material
            _ => ItemKind.Misc,
        };
    }

    /// <summary>The eight-element ResistanceBlock order, for documentation/iteration.</summary>
    public static readonly string[] ElementOrder =
        ["bionic", "earth", "fire", "ice", "lighting", "poison", "water", "wind"];

    /// <summary>Drop-kind classification of a main <c>*아이템</c> line keyword. [VERIFIED fileread.cpp:5071-5085].</summary>
    public static DropKind ClassifyDropKeyword(string keyword) => keyword switch
    {
        DropNone => DropKind.Nothing,
        DropMoney => DropKind.Gold,
        _ => DropKind.Item,
    };
}
