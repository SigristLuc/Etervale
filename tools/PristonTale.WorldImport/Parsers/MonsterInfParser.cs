using PristonTale.Simulation.World.Loot;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.WorldImport.Parsers;

/// <summary>
/// Parses one monster <c>.inf</c> → <see cref="ParsedMonster"/> with the original character-definition
/// rules for monsters. Tokens via <see cref="KeyMaps"/>. The monster resistance
/// tokens are DISTINCT from the item ones and are NOT in elemental order (mapped through
/// <see cref="KeyMaps.MonsterResistances"/>). <c>*시야</c> sight is stored RAW (the original squares it at
/// runtime; the schema/Simulation expect the raw reach).
///
/// The name is preserved VERBATIM (including the original's leading/trailing spaces inside the quotes,
/// e.g. <c>" 산타고블린 "</c>) because field <c>.spm</c> spawn tables cross-reference monsters by this exact
/// string — trimming would break the join; quoted strings are copied verbatim between the quotes.
/// The separate <c>*Name</c> label supplied by these files is trimmed into <c>DisplayName</c> for the HUD.
/// </summary>
public sealed class MonsterInfParser
{
    public ParsedMonster Parse(string path)
    {
        var lines = SourceTextReader.ReadChain(path);

        string name = "", model = "";
        int soundCode = 0;
        float modelScale = 1f;
        var displayName = "";
        int level = 0, atkMin = 0, atkMax = 0, atkSpeed = 0, atkRange = 0, accuracy = 0;
        int defence = 0, absorb = 0, block = 0, hp = 0, sight = 0;
        int skDmgMin = 0, skDmgMax = 0, skDist = 0, skRange = 0, skRating = 0, skCurse = 0;
        int moveSpeed = 0, moveRange = 0, iq = 0, groupMin = 0, groupMax = 0, exp = 0, activeHour = 0;
        int stun = 100; // Default flinch chance.
        short nature = (short)MonsterNature.Natural;
        short brood = (short)MonsterBrood.Normal;
        short klass = (short)MonsterClass.Normal;
        var res = new int[8];

        bool allSee = false;
        int maxItems = 0;
        var dropItems = new List<ParsedDropMain>();
        var dropsPlus = new List<ParsedDropPlus>();

        foreach (var sourceLine in lines)
        {
            var line = sourceLine.Text;
            var key = Tokenizer.LeadingKey(line);
            if (key.Length == 0)
            {
                continue;
            }
            var t = new Tokenizer(line);
            t.NextWord();

            if (key.Equals(KeyMaps.CharDisplayName, StringComparison.OrdinalIgnoreCase))
            {
                displayName = t.NextValueOrQuoted().Trim();
                continue;
            }

            // Monster resistances (token set distinct from item resistances).
            var resHit = false;
            foreach (var (token, element) in KeyMaps.MonsterResistances)
            {
                if (key == token)
                {
                    res[element] = t.NextInt();
                    resHit = true;
                    break;
                }
            }
            if (resHit)
            {
                continue;
            }

            switch (key)
            {
                case KeyMaps.CharName: name = t.NextValueOrQuoted(); break;
                case KeyMaps.CharModel: model = t.NextValueOrQuoted(); break;
                case KeyMaps.CharSize:
                    t.NextWord();
                    break;
                case "*모델크기":
                    modelScale = t.NextFloat();
                    break;
                case "*효과음":
                case "*소리":
                    soundCode = LegacyNpcSoundCode.Resolve(t.NextWord());
                    break;
                case KeyMaps.MonLevel: level = t.NextInt(); break;
                case KeyMaps.MonBoss: klass = (short)MonsterClass.Boss; break;     // no value
                case KeyMaps.MonClass: klass = (short)t.NextInt(); break;
                case KeyMaps.MonMoveSpeed:
                case "*이동속도":
                    moveSpeed = (int)t.NextFloat();
                    break;
                case KeyMaps.MonAttack: atkMin = t.NextInt(); atkMax = t.NextInt(); break;
                case KeyMaps.MonAttackSpeed: atkSpeed = (int)t.NextFloat(); break;
                case KeyMaps.MonAttackRange: atkRange = t.NextInt(); break;
                case KeyMaps.MonAccuracy: accuracy = t.NextInt(); break;
                case KeyMaps.MonDefence: defence = t.NextInt(); break;
                case KeyMaps.MonAbsorb: absorb = t.NextInt(); break;
                case KeyMaps.MonBlock: block = t.NextInt(); break;
                case KeyMaps.MonSight: sight = t.NextInt(); break;                 // RAW (squared later)
                case "*생명력": case "*라이프": hp = t.NextInt(); break;            // HP, dual alias
                case KeyMaps.MonSkillDamage: skDmgMin = t.NextInt(); skDmgMax = t.NextInt(); break;
                case KeyMaps.MonSkillDistance: skDist = t.NextInt(); break;
                case KeyMaps.MonSkillRange: skRange = t.NextInt(); break;
                case KeyMaps.MonSkillRating: skRating = t.NextInt(); break;
                case KeyMaps.MonSkillCurse: skCurse = t.NextInt(); break;
                case KeyMaps.MonMoveRange: moveRange = (int)t.NextFloat(); break;
                case KeyMaps.MonIq: iq = t.NextInt(); break;
                case KeyMaps.MonGroup: groupMin = t.NextInt(); groupMax = t.NextInt(); break;
                case KeyMaps.MonExp: exp = t.NextInt(); break;
                case KeyMaps.MonActiveHour: activeHour = ParseActiveHour(t.NextWord()); break;
                case KeyMaps.MonNature: nature = (short)KeyMaps.ParseNature(t.NextWord()); break;
                case KeyMaps.MonBrood: brood = (short)KeyMaps.ParseBrood(t.NextWord()); break;
                // Flinch chance; BOTH spellings accepted.
                case "*스턴율": case "*스턴률": stun = t.NextInt(); break;

                case KeyMaps.MonDropAllSee: allSee = true; break;
                case KeyMaps.MonDropMax: maxItems = t.NextInt(); break;
                case KeyMaps.MonDropPlus:
                    {
                        var weight = t.NextInt();
                        var codeWord = t.NextWord();
                        dropsPlus.Add(new ParsedDropPlus(weight, codeWord.Length == 0 ? null : codeWord));
                        break;
                    }
                case KeyMaps.MonDrop:
                    ParseDropLine(t, dropItems);
                    break;
            }
        }

        var m = new ParsedMonster
        {
            Name = name,
            DisplayName = displayName,
            Model = model,
            SoundCode = soundCode,
            ModelScale = modelScale,
            Level = level,
            AttackMin = atkMin, AttackMax = atkMax,
            AttackSpeed = atkSpeed, AttackRange = atkRange, Accuracy = accuracy,
            Defence = defence, AbsorbPct = absorb, BlockPct = block, Hp = hp,
            Sight = sight,
            SkillDamageMin = skDmgMin, SkillDamageMax = skDmgMax,
            SkillDistance = skDist, SkillRange = skRange, SkillRatingPct = skRating, SkillCurseCode = skCurse,
            MoveSpeed = moveSpeed, MoveRange = moveRange, Iq = iq,
            GroupMin = groupMin, GroupMax = groupMax, Exp = exp, ActiveHour = activeHour,
            Nature = nature, Brood = brood, Class = klass, StunPct = stun,
            AllPlayersSee = allSee, MaxItemsPerKill = maxItems,
        };
        Array.Copy(res, m.Resistances, 8);
        m.DropItems.AddRange(dropItems);
        m.DropsPlus.AddRange(dropsPlus);
        return m;
    }

    // *아이템 <weight> [없음 | 돈 min max | code1 code2 ...]
    // For an item-code list the original divides the percentage across the codes and appends one slot each.
    private static void ParseDropLine(Tokenizer t, List<ParsedDropMain> dropItems)
    {
        var weight = t.NextInt();
        var keyword = t.NextWord();
        if (keyword.Length == 0)
            return;

        var kind = KeyMaps.ClassifyDropKeyword(keyword);
        switch (kind)
        {
            case DropKind.Nothing:
                dropItems.Add(new ParsedDropMain(DropKind.Nothing, weight, null, 0, 0));
                break;
            case DropKind.Gold:
                {
                    var min = t.NextInt();
                    var max = t.NextInt();
                    dropItems.Add(new ParsedDropMain(DropKind.Gold, weight, null, min, max));
                    break;
                }
            default:
                {
                    // keyword was actually the first item code; collect it + the rest.
                    var codes = new List<string> { keyword };
                    codes.AddRange(t.RemainingWords());
                    var per = codes.Count > 0 ? weight / codes.Count : weight;
                    foreach (var c in codes)
                        dropItems.Add(new ParsedDropMain(DropKind.Item, per, c, 0, 0));
                    break;
                }
        }
    }

    // 낮 → +1 (day), 밤 → -1 (night), else 0 (any; e.g. 제한없음 "no limit").
    private static int ParseActiveHour(string word) => word switch
    {
        KeyMaps.ActiveHourDay => 1,
        KeyMaps.ActiveHourNight => -1,
        _ => 0,
    };
}
