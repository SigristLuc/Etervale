namespace PristonTale.Persistence.Abstractions.WorldData;

public interface IWorldDataRepository
{
    Task<IReadOnlyList<ItemTemplateRecord>> GetAllItemTemplatesAsync(CancellationToken ct = default);

    Task<IReadOnlyList<MonsterRecord>> GetAllMonstersAsync(CancellationToken ct = default);

    Task<IReadOnlyList<NpcRecord>> GetAllNpcsAsync(CancellationToken ct = default);

    Task<IReadOnlyList<FieldSpawnRecord>> GetAllFieldSpawnsAsync(CancellationToken ct = default);

    Task<IReadOnlyList<MotionModelRecord>> GetAllMotionModelsAsync(CancellationToken ct = default);

    Task<IReadOnlyList<MotionSlotRecord>> GetAllMotionSlotsAsync(CancellationToken ct = default);
}
