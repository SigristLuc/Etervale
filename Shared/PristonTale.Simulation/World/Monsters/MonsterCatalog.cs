namespace PristonTale.Simulation.World.Monsters;

public sealed class MonsterCatalog
{
    private readonly Dictionary<int, MonsterDefinition> _byCode;

    public MonsterCatalog(IEnumerable<MonsterDefinition> definitions)
    {
        _byCode = definitions.ToDictionary(static d => d.Code);
        All = _byCode.Values.ToList();
    }

    public IReadOnlyList<MonsterDefinition> All { get; }

    public MonsterDefinition Get(int code) => _byCode[code];

    public bool TryGet(int code, out MonsterDefinition definition) => _byCode.TryGetValue(code, out definition);
}
