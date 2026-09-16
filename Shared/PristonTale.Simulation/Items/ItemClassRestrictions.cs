using System.Globalization;
using PristonTale.Simulation.Characters;

namespace PristonTale.Simulation.Items;

public static class ItemClassRestrictions
{
    private const JobBitCode Casters = JobBitCode.Magician | JobBitCode.Priestess | JobBitCode.Shaman;
    private const JobBitCode Fighters = JobBitCode.Mechanician | JobBitCode.Fighter | JobBitCode.Archer
        | JobBitCode.Pikeman | JobBitCode.Atalanta | JobBitCode.Knight | JobBitCode.Assassin;
    private const JobBitCode Female = JobBitCode.Archer | JobBitCode.Atalanta | JobBitCode.Priestess | JobBitCode.Assassin;
    private const JobBitCode All = Casters | Fighters;

    public static uint AllowedJobs(string key)
    {
        bool armor = key.StartsWith("DA1", StringComparison.OrdinalIgnoreCase);
        bool robe = key.StartsWith("DA2", StringComparison.OrdinalIgnoreCase);
        if (key.StartsWith("OM1", StringComparison.OrdinalIgnoreCase))
        {
            return (uint)Casters;
        }

        if (!armor && !robe)
        {
            return 0;
        }

        var allowed = armor ? Fighters : Casters;
        if (int.TryParse(key.AsSpan(3), NumberStyles.None, CultureInfo.InvariantCulture, out int number))
        {
            if (number is 31 or 32 or 35 or 36 or 39 or 40 or 43 or 44 or 51 or 54)
            {
                allowed &= All & ~Female;
            }
            else if (number is 33 or 34 or 37 or 38 or 41 or 42 or 45 or 46 or 52 or 55)
            {
                allowed &= Female;
            }
        }

        return (uint)allowed;
    }
}
