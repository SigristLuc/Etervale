using System.Collections.Generic;

namespace PristonTale.Simulation.World.Motion;

public sealed class MotionTimingSet
{
    private readonly Dictionary<int, MotionClip> _firstByState;
    private readonly Dictionary<int, IReadOnlyList<MotionClip>> _allByState;

    private MotionTimingSet(
        Dictionary<int, MotionClip> firstByState,
        Dictionary<int, IReadOnlyList<MotionClip>> allByState)
    {
        _firstByState = firstByState;
        _allByState = allByState;
    }

    public int StateCount => _firstByState.Count;

    public static MotionTimingSet Build(IEnumerable<KeyValuePair<int, MotionClip>> orderedStateClips)
    {
        var first = new Dictionary<int, MotionClip>();
        var mutable = new Dictionary<int, List<MotionClip>>();
        foreach (var pair in orderedStateClips)
        {
            if (pair.Value.Frames <= 0)
            {
                continue;
            }

            if (!first.ContainsKey(pair.Key))
            {
                first[pair.Key] = pair.Value;
            }

            if (!mutable.TryGetValue(pair.Key, out var clips))
            {
                clips = [];
                mutable.Add(pair.Key, clips);
            }

            clips.Add(pair.Value);
        }

        var all = new Dictionary<int, IReadOnlyList<MotionClip>>(mutable.Count);
        foreach (var (state, clips) in mutable)
        {
            all.Add(state, clips.ToArray());
        }

        return new MotionTimingSet(first, all);
    }

    public bool TryGetClip(int stateCode, out MotionClip clip) => _firstByState.TryGetValue(stateCode, out clip);

    public IReadOnlyList<MotionClip> ClipsForState(int stateCode)
    {
        return _allByState.TryGetValue(stateCode, out var clips) ? clips : Array.Empty<MotionClip>();
    }
}
