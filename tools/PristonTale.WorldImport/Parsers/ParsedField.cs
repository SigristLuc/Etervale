namespace PristonTale.WorldImport.Parsers;

/// <summary>
/// Importer DTO aggregating the three field spawn files for one ASE base: the <c>.spm</c> text config
/// (budget knobs + weighted monster table + boss spawns), the <c>.spp</c> binary spawn points, and the
/// <c>.spc</c> binary fixed NPCs. Monster/NPC references are RAW names (resolved to FK rows by the
/// <see cref="Importer"/>). The <see cref="Importer"/> attaches a <see cref="FieldCode"/> + <see cref="AseBase"/>
/// from the Simulation WorldCatalog by joining on the ASE base name.
/// </summary>
public sealed class ParsedField
{
    public required string AseBase { get; init; }

    // .spm budget knobs. Defaults match the original (10 concurrent monsters, 3 per wave).
    public int MaxConcurrentMonsters { get; set; } = 10;
    public int MaxPerSpawnPoint { get; set; } = 3;
    public int SpawnInterval { get; set; }
    public int PerPointCooldownMs { get; set; }

    public List<(string MonsterName, int Weight)> MonsterSpawns { get; } = [];
    public List<ParsedBoss> Bosses { get; } = [];

    // .spp spawn points: (index, state, x, z).
    public List<(int Index, int State, int X, int Z)> SpawnPoints { get; } = [];

    // .spc fixed NPCs: (npc name, x, z, angle).
    public List<ParsedNpcPlacement> FixedNpcs { get; } = [];
}

public sealed record ParsedNpcPlacement(string NpcName, int X, int Z, int Angle, int Y, string SourceFile);

/// <summary>A <c>*출연자두목</c> boss spawn: master + optional escort/slave + slave count + spawn hours.</summary>
public sealed record ParsedBoss(string MasterName, string? SlaveName, int SlaveCount, IReadOnlyList<int> Hours);
