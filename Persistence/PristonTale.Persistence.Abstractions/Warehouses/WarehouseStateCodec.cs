using System.Text.Json;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Persistence.Abstractions.Warehouses;

public static class WarehouseStateCodec
{
    public static WarehouseStorage Restore(WarehouseRecord? record, ItemCatalog catalog)
    {
        if (record is null)
        {
            return new WarehouseStorage();
        }

        if (record.Revision < 0 || record.Money < 0 || record.Money > WarehouseStorage.MaxMoney)
        {
            throw new InvalidDataException("Invalid saved warehouse totals.");
        }

        var storage = new WarehouseStorage(record.Revision, record.Money);
        if (CharacterStateCodec.IsLegacyEmpty(record.Items, JsonValueKind.Array))
        {
            return storage;
        }

        var state = JsonSerializer.Deserialize<SavedItems>(record.Items.Value, CharacterStateCodec.Options);
        if (state is null || state.Version != 1 || state.Items is null || state.Items.Length > WarehouseStorage.MaxItems)
        {
            throw new InvalidDataException("Unsupported warehouse state version or size.");
        }

        var identities = new HashSet<Guid>();
        foreach (var saved in state.Items)
        {
            var item = CharacterStateCodec.RestoreItem(saved, catalog, identities);
            if (saved.Slot.HasValue || !storage.Restore(item, saved.Anchor))
            {
                throw new InvalidDataException("Invalid saved warehouse footprint.");
            }
        }

        return storage;
    }

    public static WarehouseRecord Capture(long accountId, WarehouseStorage storage)
    {
        var items = new List<SavedItem>();
        for (var anchor = 0; anchor < storage.Grid.Capacity; anchor++)
        {
            if (storage.Grid[anchor] is { } item)
            {
                items.Add(CharacterStateCodec.CaptureItem(item, anchor, null));
            }
        }

        return new WarehouseRecord(accountId, storage.Money, storage.Revision, CharacterJson.Write(new SavedItems(1, items.ToArray())));
    }
}
