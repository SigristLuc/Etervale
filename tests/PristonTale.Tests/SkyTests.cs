using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Rendering;
using PristonTale.Simulation.World;

namespace PristonTale.Tests;

internal static class SkyTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("HoSky manifest preserves ordered layers, RGBA and Windows path normalization", ManifestAsync);
        yield return ("all configured outdoor fields resolve their original HoSky profiles", ProfilesAsync);
    }

    private static Task ManifestAsync()
    {
        using var reader = new StringReader("""
            StartSky: 33
            label {
            Filename: Effect\\Sky\\day.ase FadeDelay: 1 RGBA: 255 128 64 220
            }
            empty {
            }
            """);
        var profiles = PristonSkyManifest.Parse(reader);
        Assert.True(profiles.Count == 2 && profiles[1].Length == 0, "empty profile shifted numeric identities");
        var layer = profiles[0][0];
        Assert.True(layer.ModelPath == "Effect/Sky/day.smd" && layer.DelayedFade, "compiled path or fade metadata lost");
        Assert.True(layer.Color == new Color(255, 128, 64, 220), "layer tint lost");
        return Task.CompletedTask;
    }

    private static Task ProfilesAsync()
    {
        foreach (var backdrop in new[] { FieldBackdrops.Forest, FieldBackdrops.Ruin1, FieldBackdrops.Ruin2,
            FieldBackdrops.Desert, FieldBackdrops.Fall, FieldBackdrops.Iron1, FieldBackdrops.Iron2, FieldBackdrops.Snow })
        {
            var profiles = new PristonSkyProfiles(PristonSkyProfiles.Resolve(backdrop.Day),
                PristonSkyProfiles.Resolve(backdrop.Glow), PristonSkyProfiles.Resolve(backdrop.Night));
            Assert.True(profiles.Select(12) == profiles.Day && profiles.Select(18) == profiles.Glow
                && profiles.Select(0) == profiles.Night, "sky phase disagrees with the existing 24-hour light cycle");
        }

        Assert.True(PristonSkyProfiles.Opening.Select(12) == 22, "opening lost its 0x80 sky");
        return Task.CompletedTask;
    }
}
