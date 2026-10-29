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

    // The definition with this key, else with this display name, in any case. Several monsters share a display name
    // (field variants) and an event form shares its monster's key: the lowest code wins.
    public bool TryFind(string name, out MonsterDefinition definition) => TryFindKey(name, out definition) || TryLowest(name, byKey: false, out definition);

    // The definition with this key (the monster's unique English name), in any case.
    public bool TryFindKey(string key, out MonsterDefinition definition) => TryLowest(key, byKey: true, out definition);

    private bool TryLowest(string text, bool byKey, out MonsterDefinition definition)
    {
        definition = null;
        foreach (var candidate in All)
        {
            if (string.Equals(byKey ? candidate.Key : candidate.Name, text, StringComparison.OrdinalIgnoreCase) && (definition is null || candidate.Code < definition.Code))
            {
                definition = candidate;
            }
        }

        return definition is not null;
    }
}
