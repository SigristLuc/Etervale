using PristonTale.Persistence.Abstractions.WorldData;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Application.World;

public sealed class WorldDataAppService
{
    private readonly IWorldDataRepository _repository;

    public WorldDataAppService(IWorldDataRepository repository) => _repository = repository;

    public async Task<WorldData> BuildAsync(CancellationToken ct = default)
    {
        var itemRecords = await _repository.GetAllItemTemplatesAsync(ct);
        var monsterRecords = await _repository.GetAllMonstersAsync(ct);
        var npcRecords = await _repository.GetAllNpcsAsync(ct);
        var fieldRecords = await _repository.GetAllFieldSpawnsAsync(ct);
        var motionModels = await _repository.GetAllMotionModelsAsync(ct);
        var motionSlots = await _repository.GetAllMotionSlotsAsync(ct);

        var itemDefs = WorldDataFactory.ToItemDefinitions(itemRecords);
        var itemCodes = itemDefs.ToDictionary(item => item.CatalogKey, item => item.Code, StringComparer.OrdinalIgnoreCase);
        var (monsterDefs, monsterCodeByName) = WorldDataFactory.ToMonsterDefinitions(monsterRecords, itemCodes);
        var (npcDefs, npcCodeByName) = WorldDataFactory.ToNpcDefinitions(npcRecords);

        var fieldSpawns = WorldDataFactory.ToFieldSpawnData(fieldRecords, monsterCodeByName, npcCodeByName);

        var motions = WorldDataFactory.ToMotionTimings(motionModels, motionSlots);

        return new WorldData(
            new ItemCatalog(itemDefs),
            new MonsterCatalog(monsterDefs),
            new NpcCatalog(npcDefs),
            fieldSpawns,
            motions);
    }
}
