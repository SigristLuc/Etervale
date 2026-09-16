using System.Diagnostics.CodeAnalysis;

namespace PristonTale.Effects.Effects;

public sealed class EffectCatalog
{
    private readonly Dictionary<string, EffectDefinition> _byName = new(StringComparer.Ordinal);

    public int Count => _byName.Count;

    public IReadOnlyCollection<string> Names => _byName.Keys;

    public void Register(EffectDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        _byName[definition.Name] = definition;
    }

    public bool Contains(string name) => _byName.ContainsKey(name);

    public bool TryGet(string name, [NotNullWhen(true)] out EffectDefinition? definition)
        => _byName.TryGetValue(name, out definition);
}
