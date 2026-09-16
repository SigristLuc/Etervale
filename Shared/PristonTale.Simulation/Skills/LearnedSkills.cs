using System.Collections.ObjectModel;
using PristonTale.Simulation.Characters;

namespace PristonTale.Simulation.Skills;

public sealed class LearnedSkills
{
    private readonly Dictionary<uint, int> _ranks = new();
    private readonly IReadOnlyList<Skill> _available;

    public LearnedSkills(PlayerClass characterClass)
    {
        _available = FirstTierSkills.For(characterClass);
        Ranks = new ReadOnlyDictionary<uint, int>(_ranks);
    }

    public IReadOnlyDictionary<uint, int> Ranks { get; }
    public int RankOf(uint code) => _ranks.GetValueOrDefault(code);
    public long Revision { get; private set; }
    public event Action Changed;

    private bool IsAvailable(uint code)
    {
        foreach (var skill in _available)
        {
            if (skill.Code == code)
            {
                return true;
            }
        }

        return false;
    }

    internal void Increase(uint code)
    {
        var rank = RankOf(code);
        if (!IsAvailable(code) || rank >= 10)
        {
            throw new ArgumentOutOfRangeException(nameof(code));
        }

        _ranks[code] = rank + 1;
        Revision++;
        Changed?.Invoke();
    }

    public void Restore(uint code, int rank)
    {
        if (!IsAvailable(code) || rank < 1 || rank > 10 || !_ranks.TryAdd(code, rank))
        {
            throw new ArgumentException("Invalid or duplicate learned skill.");
        }

        Revision++;
        Changed?.Invoke();
    }
}
