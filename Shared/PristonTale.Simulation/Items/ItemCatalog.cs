namespace PristonTale.Simulation.Items;

public sealed class ItemCatalog
{
    private readonly Dictionary<uint, ItemDefinition> _byCode;
    private readonly Dictionary<uint, ItemDefinition> _questVariants;

    // Quest variants share the code of a regular item but carry their own name and stats (the unique quest marker of the
    // authored data); quests hand them out and saved items name them by their Unique value.
    public ItemCatalog(IEnumerable<ItemDefinition> definitions, IEnumerable<ItemDefinition> questVariants = null)
    {
        _byCode = definitions.ToDictionary(static d => d.Code);
        _questVariants = (questVariants ?? []).ToDictionary(static d => d.Code);
        All = _byCode.Values.ToList();
    }

    public IReadOnlyList<ItemDefinition> All { get; }

    public IReadOnlyCollection<ItemDefinition> QuestVariants => _questVariants.Values;

    public ItemDefinition Get(uint code) => _byCode[code];

    public bool TryGet(uint code, out ItemDefinition definition) => _byCode.TryGetValue(code, out definition);

    public bool TryGetQuestVariant(uint code, out ItemDefinition definition) => _questVariants.TryGetValue(code, out definition);

    // The definition a saved item names: the quest variant for the unique quest marker (the regular one when the code
    // has no variant), the regular one otherwise.
    public bool TryGet(uint code, int unique, out ItemDefinition definition) =>
        unique == ItemDefinition.QuestUnique && TryGetQuestVariant(code, out definition) || TryGet(code, out definition);
}
