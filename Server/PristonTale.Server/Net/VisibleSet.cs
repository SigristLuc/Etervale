namespace PristonTale.Server.Net;

// The entities one receiver may see in a tick. In a crowd the receiver sees its nearest Capacity entities, like the
// classic server's per-receiver user limit: entities it must not lose (its attack target, whoever attacks it, its
// summons, NPCs) always stay, and an entity it already shows is kept out to 1.15 times the crowd's cut-off distance so
// that the edge of the crowd does not flicker in and out.
internal sealed class VisibleSet(int capacity)
{
    // Squared 1.15: the distance band beyond the cut-off in which already shown entities stay.
    private const float KeepBandSq = 1.3225f;
    private (int Index, float DistanceSq)[] _entries = new (int, float)[256];
    private int _count;

    public int Capacity { get; } = capacity;

    public int Count => _count;

    public void Clear() => _count = 0;

    public void Add(int index, float distanceSq, bool essential)
    {
        if (_count == _entries.Length)
        {
            Array.Resize(ref _entries, _entries.Length * 2);
        }

        _entries[_count++] = (index, essential ? -1f : distanceSq);
    }

    public ReadOnlySpan<(int Index, float DistanceSq)> Entries => _entries.AsSpan(0, _count);

    // Keeps the Capacity nearest entries, plus already shown ones inside the keep band; isShown is asked only for the band.
    public void Trim(Func<int, bool> isShown)
    {
        if (_count <= Capacity)
        {
            return;
        }

        var span = _entries.AsSpan(0, _count);
        var cutoff = Select(span, Capacity - 1);
        var kept = Capacity;
        var band = cutoff * KeepBandSq;
        for (var position = Capacity; position < _count; position++)
        {
            var entry = span[position];
            if (entry.DistanceSq <= band && isShown(entry.Index))
            {
                span[kept++] = entry;
            }
        }

        _count = kept;
    }

    // Partially orders the span so the element at rank k is in place, smaller ones before it; returns its distance.
    private static float Select(Span<(int Index, float DistanceSq)> span, int k)
    {
        var low = 0;
        var high = span.Length - 1;
        while (low < high)
        {
            var pivot = span[(low + high) >>> 1].DistanceSq;
            var left = low;
            var right = high;
            while (left <= right)
            {
                while (span[left].DistanceSq < pivot)
                {
                    left++;
                }

                while (span[right].DistanceSq > pivot)
                {
                    right--;
                }

                if (left <= right)
                {
                    (span[left], span[right]) = (span[right], span[left]);
                    left++;
                    right--;
                }
            }

            if (k <= right)
            {
                high = right;
            }
            else if (k >= left)
            {
                low = left;
            }
            else
            {
                break;
            }
        }

        return span[k].DistanceSq;
    }
}
