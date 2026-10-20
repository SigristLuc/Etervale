using System.Text;
using Microsoft.Xna.Framework;
using PristonTale.Engine3D.IO;
using PristonTale.Gameplay.Audio;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Tests;

internal static class FieldAudioTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("legacy JODO ambience is normalized without changing RIFF audio", WaveHeaderAsync);
        yield return ("field ambience uses the original radius and distance volume curve", ProximityVolumeAsync);
        yield return ("continuous field ambience follows original state code and day night rules", FieldAmbientSelectionAsync);
        yield return ("field catalogue preserves all 72 authored positional ambience sources", FieldCatalogueAsync);
        yield return ("Ricarten cart and windmill ambience preserve their authored sources", RicartenMachineryAmbienceAsync);
        yield return ("NPC sound-bank names preserve the classic 32-bit lookup", NpcSoundCodesAsync);
        yield return ("NPC talk filenames preserve the classic CP949 checksum", NpcTalkSoundCodeAsync);
        yield return ("NPC motion sounds preserve the classic positional attenuation", NpcProximityVolumeAsync);
    }

    private static Task WaveHeaderAsync()
    {
        var legacy = CreateWaveHeader("JODO");
        PristonWaveDecoder.NormalizeHeader(legacy);
        Assert.True(legacy.AsSpan(0, 4).SequenceEqual("RIFF"u8), "legacy WAVE signature was not normalized");

        var riff = CreateWaveHeader("RIFF");
        var expected = riff.ToArray();
        PristonWaveDecoder.NormalizeHeader(riff);
        Assert.True(riff.SequenceEqual(expected), "ordinary RIFF data was modified");

        AssertInvalid(() => PristonWaveDecoder.NormalizeHeader(CreateWaveHeader("NOPE")));
        AssertInvalid(() => PristonWaveDecoder.NormalizeHeader(CreateWaveHeader("RIFF", 5)));
        AssertInvalid(() => PristonWaveDecoder.NormalizeHeader(new byte[11]));
        return Task.CompletedTask;
    }

    private static Task ProximityVolumeAsync()
    {
        var ambient = new FieldAmbient(new FieldVector(10, 20, -30), 20, 7);
        var atSource = FieldAudioController.ProximityVolume(new Vector3(10, 20, 30), ambient);
        Assert.Near(399f / 400f, atSource, 0.0001f);

        var finalAudibleDistance = MathF.Sqrt((20 * 20) + (299 * 1100));
        var finalAudible = FieldAudioController.ProximityVolume(new Vector3(10 + finalAudibleDistance, 20, 30), ambient);
        Assert.Near(201f / 400f, finalAudible, 0.0001f);

        var silentDistance = MathF.Sqrt((20 * 20) + (300 * 1100));
        var silent = FieldAudioController.ProximityVolume(new Vector3(10 + silentDistance, 20, 30), ambient);
        Assert.Near(0f, silent);
        return Task.CompletedTask;
    }

    private static Task FieldCatalogueAsync()
    {
        var catalog = new WorldCatalog();
        var expectedCounts = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["fore-3"] = 1,
            ["fore-2"] = 1,
            ["village-2"] = 2,
            ["dun-1"] = 1,
            ["forever-fall-04"] = 7,
            ["forever-fall-03"] = 2,
            ["forever-fall-02"] = 6,
            ["forever-fall-01"] = 8,
            ["pilai"] = 10,
            ["dun-4"] = 14,
            ["dun-5"] = 9,
            ["dun-6"] = 11
        };

        var total = 0;
        foreach (var field in catalog.AllFields)
        {
            total += field.Ambients.Count;
            if (expectedCounts.TryGetValue(field.Id, out var expected))
            {
                Assert.True(field.Ambients.Count == expected, $"{field.Id} ambience count changed");
            }
            else
            {
                Assert.True(field.Ambients.Count == 0, $"unexpected ambience source in {field.Id}");
            }

            foreach (var ambient in field.Ambients)
            {
                Assert.True(ambient.SoundCode is >= 0 and <= 27, $"{field.Id} has an invalid ambience sound code");
            }
        }

        Assert.True(total == 72, "the original field ambience table is incomplete");

        var forestToPillai = catalog.GetField("forever-fall-01").WarpGates.Single();
        Assert.True(
            forestToPillai.SpecialEffect == 1 && forestToPillai.Y == 559 && forestToPillai.DestinationY == 449,
            "Forever Fall to Pillai special portal is incomplete");
        var pillaiWarps = catalog.GetField("pilai").WarpGates;
        Assert.True(pillaiWarps.Count == 2, "Pillai is missing an original warp gate");
        Assert.True(pillaiWarps.Any(static warp => warp.SpecialEffect == 1 && warp.DestinationFieldId == "forever-fall-01"), "Pillai return effect portal is missing");
        Assert.True(pillaiWarps.Any(static warp => warp.SpecialEffect == 2 && warp.DestinationFieldId == "pilai"), "Pillai town-warp selector gate is missing");
        return Task.CompletedTask;
    }

    private static Task FieldAmbientSelectionAsync()
    {
        var catalog = new WorldCatalog();
        Assert.True(FieldAudioController.SelectFieldAmbientCode(catalog.GetField("village-2"), 12f) == 0, "Ricarten town ambience is wrong");
        Assert.True(FieldAudioController.SelectFieldAmbientCode(catalog.GetField("fore-1"), 3.99f) == 2, "forest night did not continue until 04:00");
        Assert.True(FieldAudioController.SelectFieldAmbientCode(catalog.GetField("fore-1"), 4f) == 1, "forest day did not begin at 04:00");
        Assert.True(FieldAudioController.SelectFieldAmbientCode(catalog.GetField("fore-1"), 23f) == 2, "forest night did not begin at 23:00");
        Assert.True(FieldAudioController.SelectFieldAmbientCode(catalog.GetField("ruin-1"), 12f) == 3, "ruin wind ambience is wrong");
        Assert.True(FieldAudioController.SelectFieldAmbientCode(catalog.GetField("De-1"), 12f) == 4, "desert wind ambience is wrong");
        Assert.True(FieldAudioController.SelectFieldAmbientCode(catalog.GetField("dun-1"), 12f) == 5, "early dungeon ambience is wrong");
        Assert.True(FieldAudioController.SelectFieldAmbientCode(catalog.GetField("dun-4"), 12f) == 6, "temple ambience is wrong");
        Assert.True(FieldAudioController.SelectFieldAmbientCode(catalog.GetField("Tcave"), 12f) == 7, "cave ambience is wrong");
        Assert.True(FieldAudioController.SelectFieldAmbientCode(catalog.GetField("Dcave"), 12f) == 8, "dark sanctuary ambience is wrong");
        Assert.True(FieldAudioController.SelectFieldAmbientCode(catalog.GetField("dun-7"), 12f) == -1, "unsupported late dungeon gained an invented ambience");
        Assert.True(FieldAudioController.SelectFieldAmbientCode(catalog.GetField("iron-1"), 12f) == 9, "iron ambience is wrong");
        Assert.True(FieldAudioController.SelectFieldAmbientCode(catalog.GetField("ice1"), 12f) == 12, "ice wind ambience is wrong");
        Assert.True(FieldAudioController.SelectFieldAmbientCode(catalog.GetField("castle"), 12f) == -1, "castle gained an invented ambience");
        return Task.CompletedTask;
    }

    private static Task NpcSoundCodesAsync()
    {
        Assert.True(LegacyNpcSoundCode.Resolve("WOLVERLIN") == LegacyNpcSoundCode.Wolverine, "Wolverine NPC bank resolved to the earlier monster duplicate");
        Assert.True(LegacyNpcSoundCode.Resolve("fury") == LegacyNpcSoundCode.Fury, "NPC sound lookup became case-sensitive");
        Assert.True(LegacyNpcSoundCode.Resolve("SKILLMASTER") == LegacyNpcSoundCode.SkillMaster, "skill-master NPC bank was not preserved");
        Assert.True(LegacyNpcSoundCode.Resolve("unknown-bank") == 0, "unknown NPC bank invented a sound code");
        return Task.CompletedTask;
    }

    private static Task RicartenMachineryAmbienceAsync()
    {
        var ricarten = new WorldCatalog().GetField("village-2");
        Assert.True(ricarten.Ambients.Count == 2, "Ricarten machinery ambience sources are incomplete");
        Assert.True(ricarten.Ambients[0] == new FieldAmbient(new FieldVector(-1006, 170, -17835), 80, 27), "store-cart ambience differs from the original field table");
        Assert.True(ricarten.Ambients[1] == new FieldAmbient(new FieldVector(2632, 321, -17285), 80, 27), "mechanical-windmill ambience differs from the original field table");

        return Task.CompletedTask;
    }

    private static Task NpcTalkSoundCodeAsync()
    {
        Assert.True(LegacyTalkSound.GetSoundCode("Hello") == 285541489u, "ASCII talk checksum differs from the classic implementation");
        Assert.True(LegacyTalkSound.GetSoundCode("크릉.. 크릉") == 4127925336u, "CP949 Wolverine talk checksum differs from the classic implementation");
        Assert.True(LegacyTalkSound.GetSoundCode("요즘 말이지") == 2534093287u, "CP949 NPC talk checksum differs from the classic implementation");
        return Task.CompletedTask;
    }

    private static Task NpcProximityVolumeAsync()
    {
        var origin = Vector3.Zero;
        Assert.Near(1f, NpcAudioController.ProximityVolume(origin, origin), 0.0001f);

        var finalAudible = NpcAudioController.ProximityVolume(new Vector3(MathF.Sqrt(399f * 1100f), 0f, 0f), origin);
        Assert.Near(1f / 400f, finalAudible, 0.0001f);

        var silent = NpcAudioController.ProximityVolume(new Vector3(MathF.Sqrt(400f * 1100f), 0f, 0f), origin);
        Assert.Near(0f, silent, 0.0001f);
        return Task.CompletedTask;
    }

    private static byte[] CreateWaveHeader(string signature, uint declaredSize = 4)
    {
        var bytes = new byte[12];
        Encoding.ASCII.GetBytes(signature).CopyTo(bytes, 0);
        BitConverter.TryWriteBytes(bytes.AsSpan(4, 4), declaredSize);
        "WAVE"u8.CopyTo(bytes.AsSpan(8, 4));
        return bytes;
    }

    private static void AssertInvalid(Action operation)
    {
        var rejected = false;
        try
        {
            operation();
        }
        catch (InvalidDataException)
        {
            rejected = true;
        }

        Assert.True(rejected, "invalid WAVE container was accepted");
    }
}
