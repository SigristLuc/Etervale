namespace PristonTale.Client.Net.WorldCatalog;

public sealed class WorldRenderCatalog
{
    public static readonly WorldRenderCatalog Empty = new(new Dictionary<uint, MonsterRenderInfo>());

    public WorldRenderCatalog(IReadOnlyDictionary<uint, MonsterRenderInfo> monsters, IReadOnlyDictionary<uint, NpcRenderInfo>? npcs = null)
    {
        Monsters = monsters;
        Npcs = npcs ?? new Dictionary<uint, NpcRenderInfo>();
    }

    public IReadOnlyDictionary<uint, MonsterRenderInfo> Monsters { get; }
    public IReadOnlyDictionary<uint, NpcRenderInfo> Npcs { get; }
    public NpcRenderInfo? GetNpc(uint templateId) => Npcs.GetValueOrDefault(templateId);

    public MonsterRenderInfo? GetMonster(uint templateId) =>
        Monsters.TryGetValue(templateId, out var info) ? info : null;
}
