using PristonTale.Client.Net.WorldCatalog;
using PristonTale.Protocol.World;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Client.Net.Online;

internal static class WorldCatalogMapping
{
    public static WorldRenderCatalog ToNeutral(WorldCatalogReply reply)
    {
        var monsters = new Dictionary<uint, MonsterRenderInfo>(reply.Monsters.Count);
        foreach (var m in reply.Monsters)
        {
            monsters[m.TemplateId] = ToNeutral(m);
        }

        var npcs = new Dictionary<uint, NpcRenderInfo>(reply.Npcs.Count);
        foreach (var npc in reply.Npcs)
        {
            npcs.Add(
                npc.TemplateId,
                new NpcRenderInfo(
                    npc.TemplateId,
                    npc.ModelInxPath,
                    npc.ModelScale,
                    npc.Name,
                    (NpcServices)npc.Services,
                    npc.SoundCode));
        }

        return new WorldRenderCatalog(monsters, npcs);
    }

    public static MonsterRenderInfo ToNeutral(MonsterRenderDef def) =>
        new(def.TemplateId, def.ModelInxPath, def.ModelScale, def.Name, def.Level);
}
