using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Client.Net.WorldCatalog;

public sealed record NpcRenderInfo(uint TemplateId, string ModelInxPath, float ModelScale, string Name, NpcServices Services, int SoundCode = 0)
    : ModelRenderInfo(TemplateId, ModelInxPath, ModelScale, Name);
