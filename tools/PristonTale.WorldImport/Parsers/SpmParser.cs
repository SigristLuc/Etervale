namespace PristonTale.WorldImport.Parsers;

/// <summary>
/// Parses one field <c>.spm</c> text file (the monster-spawn config), faithful to <c>DecodeOpenMonster</c>
/// (fileread.cpp:5382). Populates the budget knobs + the weighted monster table + boss spawns of the
/// supplied <see cref="ParsedField"/>. Each token accepts a Korean and an ASCII alias.
///
/// <c>*출현간격</c> (interval) is a tick-mask in the source: <c>OpenInterval=(1&lt;&lt;n); if(&gt;1) --;</c>
/// (so n yields a <c>(1&lt;&lt;n)-1</c> mask), with an optional 2nd word giving a millisecond cooldown
/// (<c>dwIntervalTime = ms*1000</c>). We store the raw shift result and the ms value separately
/// [VERIFIED fileread.cpp:5475-5485].
/// </summary>
public sealed class SpmParser
{
    public void Parse(string path, ParsedField field)
    {
        foreach (var line in Cp949.ReadLines(path))
        {
            var key = Tokenizer.LeadingKey(line);
            if (key.Length == 0)
                continue;
            var t = new Tokenizer(line);
            t.NextWord();

            if (Match(key, KeyMaps.SpmActor))
            {
                var monster = t.NextValueOrQuoted();
                var weight = t.NextInt();
                if (monster.Length > 0)
                    field.MonsterSpawns.Add((monster, weight));
            }
            else if (Match(key, KeyMaps.SpmBossActor))
            {
                var master = t.NextValueOrQuoted();
                var slave = t.NextValueOrQuoted();
                var slaveCount = t.NextInt();
                var hours = new List<int>();
                while (!t.AtEnd && hours.Count < 32)
                {
                    var w = t.NextWord();
                    if (w.Length == 0)
                        break;
                    hours.Add(Tokenizer.Atoi(w));
                }
                if (master.Length > 0)
                    field.Bosses.Add(new ParsedBoss(
                        master, slave.Length == 0 ? null : slave, slaveCount, hours));
            }
            else if (Match(key, KeyMaps.SpmMaxConcurrent))
            {
                field.MaxConcurrentMonsters = t.NextInt();
            }
            else if (Match(key, KeyMaps.SpmInterval))
            {
                var n = t.NextInt();
                var interval = 1 << Math.Clamp(n, 0, 30);
                if (interval > 1)
                    interval--;
                field.SpawnInterval = interval;
                if (!t.AtEnd)
                    field.PerPointCooldownMs = t.NextInt() * 1000;
            }
            else if (Match(key, KeyMaps.SpmPerWave))
            {
                field.MaxPerSpawnPoint = t.NextInt();
            }
        }
    }

    private static bool Match(string key, string[] aliases)
    {
        foreach (var a in aliases)
            if (string.Equals(key, a, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }
}
