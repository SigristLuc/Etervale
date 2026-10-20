using PristonTale.Simulation.World.Loot;
using PristonTale.Simulation.World.Monsters;

namespace PristonTale.WorldImport.Parsers;

/// <summary>
/// Importer DTO for one monster <c>.inf</c> (<see cref="MonsterInfParser"/>). Mirrors the <c>monsters</c>
/// row plus the drop child rows. Drop item references are kept as RAW 5-char code strings (resolved to
/// item_templates.code by the <see cref="Importer"/>).
/// </summary>
public sealed class ParsedMonster
{
    public required string Name { get; init; }
    public string DisplayName { get; init; } = "";
    public string Model { get; init; } = "";
    public int SoundCode { get; init; }
    public float ModelScale { get; init; } = 1f;
    public int Level { get; init; }
    public int AttackMin { get; init; }
    public int AttackMax { get; init; }
    public int AttackSpeed { get; init; }
    public int AttackRange { get; init; }
    public int Accuracy { get; init; }
    public int Defence { get; init; }
    public int AbsorbPct { get; init; }
    public int BlockPct { get; init; }
    public int Hp { get; init; }
    public int Sight { get; init; }       // RAW reach (NOT squared)
    public int SkillDamageMin { get; init; }
    public int SkillDamageMax { get; init; }
    public int SkillDistance { get; init; }
    public int SkillRange { get; init; }
    public int SkillRatingPct { get; init; }
    public int SkillCurseCode { get; init; }
    public int MoveSpeed { get; init; }
    public int MoveRange { get; init; }
    public int Iq { get; init; }
    public int GroupMin { get; init; }
    public int GroupMax { get; init; }
    public int Exp { get; init; }
    public int ActiveHour { get; init; }  // +1 day / -1 night / 0 any
    public short Nature { get; init; }
    public short Brood { get; init; }
    public short Class { get; init; }
    public int StunPct { get; init; } = 100; // *스턴율/*스턴률 flinch chance, default 100

    public int[] Resistances { get; } = new int[8]; // single value per element, ResistanceBlock order

    // Drop config (1:1) + the weighted main slots + plus-drops.
    public bool AllPlayersSee { get; set; }
    public int MaxItemsPerKill { get; set; }
    public List<ParsedDropMain> DropItems { get; } = [];
    public List<ParsedDropPlus> DropsPlus { get; } = [];
}

/// <summary>One main <c>*아이템</c> weighted slot. Item drops keep one row per code (the source divides the
/// percentage across the code list). Gold uses <see cref="GoldMin"/>/<see cref="GoldMax"/>; nothing/gold
/// carry no item code.</summary>
public sealed record ParsedDropMain(DropKind Kind, int Weight, string? ItemCode, int GoldMin, int GoldMax);

/// <summary>One <c>*추가아이템</c> independently-rolled bonus drop (percent + one item code).</summary>
public sealed record ParsedDropPlus(int Weight, string? ItemCode);
