using PristonTale.Simulation.Modifiers;
using PristonTale.Contracts.Units;

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

// One monster a crystal may call: its authored name and its weight in a weighted draw (0 = drawn uniformly).
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
    public static readonly int[] SoulStoneBlockedFields = [33, 29, 16];

    // A web pet's blow slows its target to half speed for 36 frames of 70 Hz; monsters with Kelvezu's sound are immune.
    public const int WebSoundCode = 0x1120;
    public const int KelvezuSoundCode = 0x1830;
    public const uint WebSlowStatusCode = 0xF0000301;
    public const float WebSlowMs = 36 * GameUnits.TickMs;

    public static EffectSpec WebSlow() =>
        new(WebSlowStatusCode, EffectSourceKind.Item, true, [StatModifier.Percent(StatKind.MoveSpeed, -50)], control: ControlFlags.Slow);

    // The quest arena refuses every crystal.
    public const int QuestArenaField = 32;

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

        Pet("GP101", "Hopy", " 호피 ", " 짱피 ");
        Pet("GP102", "Hobgoblin", " 홉고블린 ", " 와일드 고블린 ");
        Pet("GP103", "Decoy", " 디코이 ", " 맥시멈 디코이 ");
        Pet("GP104", "Bargon", " 바곤 ", " 바우톤 ");
        Pet("GP105", "Head Cutter", " 헤드커터 ", " 베가 드미르 ");
        Pet("GP106", "Figon", " 파이곤 ", " 헬싱 ");
        Pet("GP107", "King Hopy", " 킹호피 ", " 호러피온 ");
        Pet("GP108", "Hulk", " 헐크 ", " 타이푼 ");
        Pet("GP111", "Web", " 웹 ", " 커즈웹 ");
        Pet("GP112", "Dark Specter", " 다크 스펙터 ", " 다크 팬텀 ");
        Pet("GP113", "Iron Guard", " 아이언 가드 ", " 이미티르 가드 ");

        crystals["GP109"] = new MonsterCrystalDefinition("GP109", "Mystic", CrystalKind.Pet,
            [new(" 호피 ", 20), new(" 홉고블린 ", 20), new(" 디코이 ", 15), new(" 바곤 ", 15), new(" 헤드커터 ", 15), new(" 파이곤 ", 10), new(" 킹호피 ", 5)],
            [new(" 짱피 ", 20), new(" 와일드 고블린 ", 20), new(" 맥시멈 디코이 ", 15), new(" 바우톤 ", 15), new(" 베가 드미르 ", 15), new(" 헬싱 ", 10),
                new(" 호러피온 ", 5)],
            false);
        crystals["GP121"] = new MonsterCrystalDefinition("GP121", "Marvel", CrystalKind.Pet,
            [new(" 바곤 ", 20), new(" 헤드커터 ", 20), new(" 파이곤 ", 15), new(" 킹호피 ", 15), new(" 헐크 ", 12), new(" 웹 ", 10), new(" 다크 스펙터 ", 5),
                new(" 아이언 가드 ", 3)],
            [new(" 바우톤 ", 20), new(" 베가 드미르 ", 20), new(" 헬싱 ", 15), new(" 호러피온 ", 15), new(" 타이푼 ", 12), new(" 커즈웹 ", 10), new(" 다크 팬텀 ", 5),
                new(" 이미티르 가드 ", 3)],
            false);

        void Billing(string key, string name, CrystalMonster[] monsters) =>
            crystals[key] = new MonsterCrystalDefinition(key, name, CrystalKind.Pet, monsters, monsters, true);

        Billing("GP117", "Skeleton", [new(" 스켈레톤 아처 "), new(" 스켈레톤 레인저 "), new(" 스켈레톤 워리어 "), new(" 스켈레톤 나이트 ")]);
        Billing("GP118", "Chaos", [new(" 나즈 "), new(" 헤비 고블린 "), new(" 스트라이더 "), new(" 디-머신 ")]);
        Billing("GP119", "Inferna", [new(" 라투 "), new(" 그로테스크 "), new(" 아이언 피스트 "), new(" 프로스트 에이션트 ")]);
        Billing("GP120", "Extreme", [new(" 오미크론 ", 35), new(" 스파이더 트루퍼 ", 30), new(" 마운틴 ", 20), new(" 오메가 ", 15)]);

        crystals["GP110"] = new MonsterCrystalDefinition("GP110", "Guardian Saint", CrystalKind.ClanPet, [new("가디안 세인트")], [new("가디안 세인트")], false);
        crystals["GP122"] = new MonsterCrystalDefinition("GP122", "Guardian Divine", CrystalKind.ClanPet, [new("가디안 디바인")], [new("가디안 디바인")], false);
        crystals["GP114"] = new MonsterCrystalDefinition("GP114", "Ricarten Militia", CrystalKind.CastleMercenary, [new("리카르텐 민병대")], [new("리카르텐 민병대")], false);
        crystals["GP115"] = new MonsterCrystalDefinition("GP115", "Ricarten Guard", CrystalKind.CastleMercenary, [new("리카르텐 경비대")], [new("리카르텐 경비대")], false);
        crystals["GP116"] = new MonsterCrystalDefinition("GP116", "Bless Royal Guard", CrystalKind.CastleMercenary, [new("블레스왕국 경비대")], [new("블레스왕국 경비대")],
            false);

        void Soul(string key, string name, params string[] souls)
        {
            var monsters = souls.Select(static soul => new CrystalMonster($" {soul}의 영혼 ")).ToArray();
            crystals[key] = new MonsterCrystalDefinition(key, name, CrystalKind.SoulStone, monsters, monsters, true);
        }

        Soul("GP201", "Green Soul Stone", "코크리스", "리치", "싸이클롭스", "메핏", "비독", "좀비", "디코이");
        Soul("GP202", "Yellow Soul Stone", "뱀피릭 뱃", "그리븐", "이블 스네일", "크립트", "부마", "바곤", "타이탄");
        Soul("GP203", "Blue Soul Stone", "비블", "슬레이온", "슬러터", "타워 고렘", "머미", "나즈", "위치");
        Soul("GP204", "Magenta Soul Stone", "버서커", "타이푼", "리자드포크", "램페이지", "아벨린", "인큐버스", "새드니스");
        Soul("GP205", "Cyan Soul Stone", "디-머신", "킹호피", "오메가", "데드존", "아이스 고렘", "콜드아이", "굴가르");
        Soul("GP206", "Violet Soul Stone", "뱀피릭 뱃", "그리븐", "이블 스네일", "크립트", "부마", "바곤", "머핀");
        Soul("GP207", "Red Soul Stone", "비블", "슬레이온", "슬러터", "타워 고렘", "머미", "나즈", "슬리버");
        Soul("GP208", "Black Soul Stone", "버서커", "타이푼", "리자드포크", "램페이지", "아벨린", "인큐버스", "메트론");
        Soul("GP209", "Platinum Soul Stone", "디-머신", "킹호피", "오메가", "데드존", "아이스 고렘", "콜드아이", "그로테스크");
        return crystals;
    }
}
