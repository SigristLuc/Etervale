namespace PristonTale.Simulation.Items;

public sealed class Inventory
{
    public const int DefaultWidth = 12;
    public const int DefaultHeight = 6;
    private readonly ItemInstance[] _slots;
    private readonly uint[] _rows;

    public Inventory(int capacity = DefaultWidth * DefaultHeight)
    {
        if (capacity < 1 || capacity > DefaultWidth * DefaultHeight)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        _slots = new ItemInstance[capacity];
        Width = Math.Min(DefaultWidth, capacity);
        Height = (capacity + Width - 1) / Width;
        _rows = new uint[Height];
    }

    public int Width { get; }
    public int Height { get; }
    public int Capacity => _slots.Length;
    public long Version { get; private set; }
    public ItemInstance this[int index] => _slots[index];
    public event Action Changed;

    public int Find(Guid instanceId)
    {
        for (var index = 0; index < _slots.Length; index++)
        {
            if (_slots[index]?.InstanceId == instanceId)
            {
                return index;
            }
        }

        return -1;
    }

    public bool TryAdd(ItemInstance stack)
    {
        if (stack is null || stack.Count <= 0 || stack.Definition.MaxStack < 1 || Find(stack.InstanceId) >= 0)
        {
            return false;
        }

        var definition = stack.Definition;
        if (!definition.IsStackable && stack.Count != 1)
        {
            return false;
        }

        var remaining = stack.Count;
        foreach (var existing in _slots)
        {
            if (existing is not null && Compatible(existing, stack))
            {
                remaining -= Math.Min(remaining, Math.Max(0, definition.MaxStack - existing.Count));
            }
        }

        Span<uint> plannedRows = stackalloc uint[Height];
        _rows.CopyTo(plannedRows);
        Span<int> anchors = stackalloc int[Capacity];
        var planned = 0;
        while (remaining > 0)
        {
            var anchor = FindSpace(definition, plannedRows);
            if (anchor < 0)
            {
                return false;
            }

            anchors[planned++] = anchor;
            SetFootprint(plannedRows, anchor, definition, true);
            remaining -= Math.Min(remaining, definition.MaxStack);
        }

        remaining = stack.Count;
        foreach (var existing in _slots)
        {
            if (existing is not null && Compatible(existing, stack))
            {
                var amount = Math.Min(remaining, Math.Max(0, definition.MaxStack - existing.Count));
                existing.Count += amount;
                remaining -= amount;
            }
        }

        for (var index = 0; index < planned; index++)
        {
            var amount = Math.Min(remaining, definition.MaxStack);
            var item = stack.Copy(amount, preserveId: index == 0);
            _slots[anchors[index]] = item;
            remaining -= amount;
        }

        plannedRows.CopyTo(_rows);
        NotifyChanged();
        return true;
    }

    public bool TryPlace(ItemInstance item, int anchor)
    {
        if (item is null || item.Count <= 0 || item.Count > item.Definition.MaxStack || Find(item.InstanceId) >= 0
            || !Fits(anchor, item.Definition, _rows))
        {
            return false;
        }

        _slots[anchor] = item.Copy(item.Count, preserveId: true);
        SetFootprint(_rows, anchor, item.Definition, true);
        NotifyChanged();
        return true;
    }

    public bool TryMove(int from, int to)
    {
        if (from < 0 || from >= Capacity || _slots[from] is not { } item)
        {
            return false;
        }

        Span<uint> rows = stackalloc uint[Height];
        _rows.CopyTo(rows);
        SetFootprint(rows, from, item.Definition, false);
        if (!Fits(to, item.Definition, rows))
        {
            return false;
        }

        SetFootprint(rows, to, item.Definition, true);
        _slots[from] = null;
        _slots[to] = item;
        rows.CopyTo(_rows);
        NotifyChanged();
        return true;
    }

    public bool TryExchange(int anchor, ItemInstance replacement)
    {
        if (anchor < 0 || anchor >= Capacity || _slots[anchor] is not { } outgoing)
        {
            return false;
        }

        Span<uint> rows = stackalloc uint[Height];
        _rows.CopyTo(rows);
        SetFootprint(rows, anchor, outgoing.Definition, false);
        var destination = anchor;
        if (replacement is not null)
        {
            if (replacement.Count < 1 || replacement.Count > replacement.Definition.MaxStack || Find(replacement.InstanceId) >= 0)
            {
                return false;
            }

            if (!Fits(destination, replacement.Definition, rows))
            {
                destination = FindSpace(replacement.Definition, rows);
            }

            if (destination < 0)
            {
                return false;
            }

            SetFootprint(rows, destination, replacement.Definition, true);
        }

        _slots[anchor] = null;
        if (replacement is not null)
        {
            _slots[destination] = replacement.Copy(replacement.Count, preserveId: true);
        }

        rows.CopyTo(_rows);
        NotifyChanged();
        return true;
    }

    public bool TrySplit(int from, int to, int amount)
    {
        if (from < 0 || from >= Capacity || _slots[from] is not { } item || !item.Definition.IsStackable
            || amount <= 0 || amount >= item.Count || !Fits(to, item.Definition, _rows))
        {
            return false;
        }

        _slots[to] = item.Copy(amount);
        item.Count -= amount;
        SetFootprint(_rows, to, item.Definition, true);
        NotifyChanged();
        return true;
    }

    public bool TryMerge(int from, int to)
    {
        if (from == to || from < 0 || to < 0 || from >= Capacity || to >= Capacity
            || _slots[from] is not { } source || _slots[to] is not { } destination || !Compatible(source, destination))
        {
            return false;
        }

        var amount = Math.Min(source.Count, destination.Definition.MaxStack - destination.Count);
        if (amount <= 0)
        {
            return false;
        }

        destination.Count += amount;
        source.Count -= amount;
        if (source.Count == 0)
        {
            SetFootprint(_rows, from, source.Definition, false);
            _slots[from] = null;
        }

        NotifyChanged();
        return true;
    }

    public ItemInstance TakeAt(int index)
    {
        if (index < 0 || index >= Capacity || _slots[index] is not { } item)
        {
            return null;
        }

        SetFootprint(_rows, index, item.Definition, false);
        _slots[index] = null;
        NotifyChanged();
        return item;
    }

    public void RemoveAt(int index)
    {
        TakeAt(index);
    }

    public bool ConsumeAt(int index)
    {
        if (index < 0 || index >= Capacity || _slots[index] is not { } item)
        {
            return false;
        }

        if (item.Count == 1)
        {
            TakeAt(index);
        }
        else
        {
            item.Count--;
            NotifyChanged();
        }

        return true;
    }

    private int FindSpace(ItemDefinition definition, ReadOnlySpan<uint> rows)
    {
        for (var index = 0; index < Capacity; index++)
        {
            if (Fits(index, definition, rows))
            {
                return index;
            }
        }

        return -1;
    }

    private bool Fits(int anchor, ItemDefinition definition, ReadOnlySpan<uint> rows)
    {
        if (anchor < 0 || anchor >= Capacity || definition.GridWidth < 1 || definition.GridHeight < 1)
        {
            return false;
        }

        var x = anchor % Width;
        var y = anchor / Width;
        if (definition.GridWidth > Width - x || definition.GridHeight > Height - y
            || (y + definition.GridHeight - 1) * Width + x + definition.GridWidth > Capacity)
        {
            return false;
        }

        var mask = ((1u << definition.GridWidth) - 1u) << x;
        for (var row = y; row < y + definition.GridHeight; row++)
        {
            if ((rows[row] & mask) != 0)
            {
                return false;
            }
        }

        return true;
    }

    private void SetFootprint(Span<uint> rows, int anchor, ItemDefinition definition, bool occupied)
    {
        var x = anchor % Width;
        var y = anchor / Width;
        var mask = ((1u << definition.GridWidth) - 1u) << x;
        for (var row = y; row < y + definition.GridHeight; row++)
        {
            rows[row] = occupied ? rows[row] | mask : rows[row] & ~mask;
        }
    }

    private void NotifyChanged()
    {
        Version++;
        Changed?.Invoke();
    }

    private static bool Compatible(ItemInstance left, ItemInstance right)
    {
        return left.Definition.IsStackable && left.Definition == right.Definition
            && left.UpgradeLevel == right.UpgradeLevel && left.DurabilityCurrent == right.DurabilityCurrent
            && left.DurabilityMax == right.DurabilityMax && left.JobSpecMask == right.JobSpecMask && left.RolledBonus == right.RolledBonus;
    }
}
