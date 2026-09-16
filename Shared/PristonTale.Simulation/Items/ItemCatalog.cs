namespace PristonTale.Simulation.Items;

public sealed class ItemCatalog
{
    private readonly Dictionary<uint, ItemDefinition> _byCode;

    public ItemCatalog(IEnumerable<ItemDefinition> definitions)
    {
        _byCode = definitions.ToDictionary(static d => d.Code);
        All = _byCode.Values.ToList();
    }

    public IReadOnlyList<ItemDefinition> All { get; }

    public ItemDefinition Get(uint code) => _byCode[code];

    public bool TryGet(uint code, out ItemDefinition definition) => _byCode.TryGetValue(code, out definition);
}
