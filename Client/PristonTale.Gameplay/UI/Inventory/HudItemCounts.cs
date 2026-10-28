using Microsoft.Xna.Framework;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Simulation.Characters;

namespace PristonTale.Gameplay.UI.Inventory;

// The stack counts drawn at an item's bottom-right corner (the bag, the belt, the warehouse), built once per revision of
// the items they label so drawing allocates nothing. A single item shows no count.
public sealed class HudItemCounts
{
    private readonly Dictionary<Guid, string> _labels = new();
    private long _revision = long.MinValue;

    public void Refresh(long revision, IReadOnlyList<OwnedItemView> items)
    {
        if (_revision == revision)
        {
            return;
        }

        _revision = revision;
        _labels.Clear();
        for (var index = 0; index < items.Count; index++)
        {
            if (items[index].Count > 1)
            {
                _labels[items[index].InstanceId] = items[index].Count.ToString();
            }
        }
    }

    public string Label(OwnedItemView item) => _labels.GetValueOrDefault(item.InstanceId, "");

    // The item's icon in its cells, its count over the corner.
    public void Draw(HudPainter painter, HudItemIcons icons, OwnedItemView item, Rectangle bounds, Color tint)
    {
        icons.Draw(painter, item.CatalogKey, bounds, tint);
        var label = Label(item);
        if (label.Length > 0)
        {
            painter.Text(label, bounds.Right - painter.Measure(label) - 1, bounds.Bottom - 13);
        }
    }
}
