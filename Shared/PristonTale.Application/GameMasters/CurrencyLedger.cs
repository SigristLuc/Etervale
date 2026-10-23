namespace PristonTale.Application.GameMasters;

// Where gold left the economy since the server started, as the game masters' currency report shows it. Mixing, aging
// and force-orb smelting have no service on this server yet, so their totals stay zero until those services record them.
public enum CurrencySink
{
    Mixing = 0,
    Aging = 1,
    Shop = 2,
    LifePotions = 3,
    ManaPotions = 4,
    StaminaPotions = 5,
    ForceOrbs = 6,
    Teleports = 7,
    SkillTraining = 8
}

public sealed class CurrencyLedger
{
    private readonly long[] _totals = new long[Enum.GetValues<CurrencySink>().Length];

    public void Record(CurrencySink sink, long gold)
    {
        if (gold > 0)
        {
            Interlocked.Add(ref _totals[(int)sink], gold);
        }
    }

    public long this[CurrencySink sink] => Interlocked.Read(ref _totals[(int)sink]);

    public long Total
    {
        get
        {
            long total = 0;
            for (var index = 0; index < _totals.Length; index++)
            {
                total += Interlocked.Read(ref _totals[index]);
            }

            return total;
        }
    }

    // A shop purchase counts as potions of its kind when it is a life, mana or stamina potion.
    public static CurrencySink ShopSinkFor(string catalogKey)
    {
        if (catalogKey.StartsWith("PL", StringComparison.OrdinalIgnoreCase))
        {
            return CurrencySink.LifePotions;
        }

        if (catalogKey.StartsWith("PM", StringComparison.OrdinalIgnoreCase))
        {
            return CurrencySink.ManaPotions;
        }

        return catalogKey.StartsWith("PS", StringComparison.OrdinalIgnoreCase) ? CurrencySink.StaminaPotions : CurrencySink.Shop;
    }
}
