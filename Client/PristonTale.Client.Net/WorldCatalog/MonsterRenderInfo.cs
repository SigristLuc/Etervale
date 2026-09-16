namespace PristonTale.Client.Net.WorldCatalog;

public sealed record MonsterRenderInfo(
    uint TemplateId,
    string ModelInxPath,
    float ModelScale,
    string Name,
    int Level) : ModelRenderInfo(TemplateId, ModelInxPath, ModelScale, Name);
