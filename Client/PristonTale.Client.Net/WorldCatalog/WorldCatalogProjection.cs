using PristonTale.Simulation.World.Monsters;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Client.Net.WorldCatalog;

internal static class WorldCatalogProjection
{
    public static WorldRenderCatalog ToRenderCatalog(MonsterCatalog monsters, NpcCatalog? npcs = null)
    {
        var byTemplate = new Dictionary<uint, MonsterRenderInfo>(monsters.All.Count);
        foreach (var def in monsters.All)
        {
            byTemplate[(uint)def.Code] = new MonsterRenderInfo(
                (uint)def.Code,
                def.ModelInxPath ?? string.Empty,
                def.ModelScale,
                def.Name ?? string.Empty,
                def.Combat.Level);
        }

        var npcTemplates = new Dictionary<uint, NpcRenderInfo>();
        if (npcs is not null)
        {
            foreach (var definition in npcs.All)
            {
                npcTemplates.Add((uint)definition.Code, new NpcRenderInfo((uint)definition.Code, definition.ModelInxPath,
                    definition.ModelScale, definition.Name, definition.Services, definition.SoundCode));
            }
        }

        return new WorldRenderCatalog(byTemplate, npcTemplates);
    }
}
