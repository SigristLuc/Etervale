namespace PristonTale.Persistence.Abstractions.WorldData;

public sealed record FieldSpawnRecord(
    int FieldCode,
    string AseBase,
    int MaxConcurrentMonsters,
    int MaxPerSpawnPoint,
    int SpawnInterval,
    int PerPointCooldownMs)
{
    public IReadOnlyList<FieldSpawnPointRecord> SpawnPoints { get; init; } = [];

    public IReadOnlyList<FieldMonsterSpawnRecord> MonsterSpawns { get; init; } = [];

    public IReadOnlyList<FieldBossSpawnRecord> BossSpawns { get; init; } = [];

    public IReadOnlyList<FieldFixedNpcRecord> FixedNpcs { get; init; } = [];
}

public sealed record FieldSpawnPointRecord(
    long Id,
    int FieldCode,
    int PointIndex,
    int State,
    int X,
    int Z);

public sealed record FieldMonsterSpawnRecord(
    long Id,
    int FieldCode,
    string MonsterName,
    int Weight);

public sealed record FieldBossSpawnRecord(
    long Id,
    int FieldCode,
    string BossMonsterName,
    string? EscortMonsterName,
    int EscortCount,
    string SpawnHours);

public sealed record FieldFixedNpcRecord(
    long Id,
    int FieldCode,
    string NpcName,
    int X,
    int Z,
    int Angle,
    int Y = 0);
