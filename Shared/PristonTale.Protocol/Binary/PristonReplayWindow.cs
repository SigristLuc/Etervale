namespace PristonTale.Protocol.Binary;

// Encrypted receive check: a counter is rejected if it repeats one of the last 16 accepted counters
// or falls more than 16 behind the highest counter seen. The original compares plain integers, so a direction
// stops accepting packets once its 16-bit counter wraps; this window compares in modulo-65536 order instead.
public sealed class PristonReplayWindow
{
    private const int Size = 16;
    private readonly int[] _recent = new int[Size];
    private int _next;
    private ushort _highest;
    private bool _started;

    public PristonReplayWindow()
    {
        Array.Fill(_recent, -1);
    }

    public bool TryAccept(ushort counter)
    {
        var ahead = (short)(counter - _highest);
        if (_started && ahead < -Size || Array.IndexOf(_recent, counter) >= 0)
        {
            return false;
        }

        _recent[_next] = counter;
        _next = (_next + 1) % Size;
        if (!_started || ahead > 0)
        {
            _highest = counter;
            _started = true;
        }

        return true;
    }
}
