using PristonTale.Tests;

if (args.Length == 2 && args[0] == "--benchmark-parsing")
{
    BinaryReadBenchmark.Run(args[1]);
    return 0;
}

if (args.Length == 2 && args[0] == "--verify-shaders")
{
    using var probe = new ShaderValidationProbe(args[1]);
    probe.Run();
    return 0;
}

if (args.Length == 2 && args[0] == "--validate-binary-assets")
{
    BinaryAssetValidationProbe.Run(args[1]);
    return 0;
}

if (args.Length is 6 or 10 or 11 or 12 or 14 or 15 && args[0] == "--benchmark-render")
{
    var hasOptions = args.Length >= 10;
    var duration = hasOptions ? double.Parse(args[6], System.Globalization.CultureInfo.InvariantCulture) : 60;
    var repeat = hasOptions ? int.Parse(args[7]) : 5;
    var hour = hasOptions ? int.Parse(args[8]) : 12;
    var orbit = hasOptions && bool.Parse(args[9]);
    var cameraDistance = args.Length >= 11
        ? float.Parse(args[10], System.Globalization.CultureInfo.InvariantCulture)
        : 0f;
    var cameraPitch = args.Length is 12 or 14 or 15
        ? float.Parse(args[11], System.Globalization.CultureInfo.InvariantCulture)
        : float.NaN;
    var focusX = args.Length >= 14
        ? float.Parse(args[12], System.Globalization.CultureInfo.InvariantCulture)
        : float.NaN;
    var focusZ = args.Length >= 14
        ? float.Parse(args[13], System.Globalization.CultureInfo.InvariantCulture)
        : float.NaN;
    var cameraYaw = args.Length == 15
        ? float.Parse(args[14], System.Globalization.CultureInfo.InvariantCulture)
        : 0f;
    using var probe = new RenderBenchmarkProbe(
        args[1],
        args[2],
        args[3],
        args[4],
        args[5],
        duration,
        repeat,
        hour,
        orbit,
        cameraDistance,
        cameraPitch,
        focusX,
        focusZ,
        cameraYaw);
    probe.Run();
    probe.WriteReport();
    return 0;
}

if (args.Length == 4 && args[0] == "--capture-ranged-equipment")
{
    using var probe = new EquipmentCaptureProbe(args[1], args[2], args[3], ranged: true);
    probe.Run();
    return 0;
}

if (args.Length == 4 && args[0] == "--capture-ground-loot")
{
    using var probe = new GroundLootCaptureProbe(args[1], args[2], args[3]);
    probe.Run();
    return 0;
}

if (args.Length == 3 && args[0] == "--verify-world-rewards")
{
    await WorldRewardsProbe.RunAsync(args[1], args[2]);
    return 0;
}

if (args.Length == 3 && args[0] == "--export-equipment-timings")
{
    EquipmentAssetProbe.ExportTimings(args[1], args[2]);
    return 0;
}

if (args.Length == 2 && args[0] == "--verify-equipment")
{
    EquipmentAssetProbe.Run(args[1]);
    return 0;
}

if (args.Length == 4 && args[0] == "--capture-equipment")
{
    using var probe = new EquipmentCaptureProbe(args[1], args[2], args[3]);
    probe.Run();
    return 0;
}

if (args.Length == 4 && args[0] == "--capture-combat-effects")
{
    using var probe = new CombatEffectsCaptureProbe(args[1], args[2], args[3]);
    probe.Run();
    return 0;
}

if (args.Length == 2 && args[0] == "--verify-monster-skills")
{
    MonsterSkillTimingProbe.Run(args[1]);
    return 0;
}

if (args.Length == 2 && args[0] == "--verify-player-timing")
{
    PlayerTimingProbe.Run(args[1]);
    return 0;
}


if (args.Length == 4 && args[0] is "--capture-potions" or "--capture-hulk-hits")
{
    using var probe = new PotionCaptureProbe(args[1], args[2], args[3], args[0] == "--capture-hulk-hits");
    probe.Run();
    return 0;
}


if (args.Length == 4 && args[0] == "--capture-sky")
{
    using var probe = new SkyCaptureProbe(args[1], args[2], args[3]);
    probe.Run();
    return 0;
}


if (args.Length == 2 && args[0] == "--inspect-ricarten-ground")
{
    GroundTraversalTests.InspectRicarten(args[1]);
    return 0;
}

if (args.Length == 5 && args[0] == "--capture-login")
{
    using var probe = new LoginCaptureProbe(args[1], args[2], args[3], args[4]);
    probe.Run();
    return 0;
}

if (args.Length == 5 && args[0] == "--check-look" && args[1] is "charselect" or "actor" or "opening" or "scenery" or "shade")
{
    using var probe = new SceneLookProbe(args[1], args[2], args[3], args[4]);
    probe.Run();
    return 0;
}

if (args.Length == 4 && args[0] == "--capture-npcs")
{
    using var probe = new ActorPresentationProbe(args[1], args[2], args[3]);
    probe.Run();
    return 0;
}

if (args.Length == 4 && args[0] == "--capture-job-heads")
{
    using var probe = new ActorPresentationProbe(args[1], args[2], args[3], jobHeads: true);
    probe.Run();
    return 0;
}


if (args.Length == 3 && args[0] == "--inspect-actor")
{
    ActorAssetProbe.Run(args[1], args[2]);
    return 0;
}

if (args.Contains("--benchmark-world"))
{
    WorldLoadBenchmark.Run();
    return 0;
}

if (args.Length is 4 or 5 && args[0] == "--capture-hud")
{
    using var probe = new HudCaptureProbe(args[1], args[2], args[3], args.Length == 5 ? args[4] : null);
    probe.Run();
    return 0;
}

if (args.Length == 2 && args[0] == "--profile-ricarten")
{
    await FieldLoadBenchmark.RunAsync(args[1]);
    return 0;
}

if (args.Length == 3 && args[0] is "--profile-stream" or "--profile-stream-baseline")
{
    using var probe = new FieldStreamingProbe(args[1], args[2], args[0] == "--profile-stream-baseline");
    probe.Run();
    return 0;
}

var specifications = new List<(string Name, Func<Task> Run)>();
specifications.AddRange(DomainTests.Cases());
specifications.AddRange(ClientTests.Cases());
specifications.AddRange(RemotePlaybackTests.Cases());
specifications.AddRange(RenderTests.Cases());
specifications.AddRange(RenderFidelityTests.Cases());
specifications.AddRange(MaterialAdapterTests.Cases());
specifications.AddRange(GltfExportTests.Cases());
specifications.AddRange(MovementAndStreamingTests.Cases());
specifications.AddRange(GroundTraversalTests.Cases());
specifications.AddRange(TerrainTests.Cases());
specifications.AddRange(CameraTests.Cases());
specifications.AddRange(ProgressionAndOwnershipTests.Cases());
specifications.AddRange(OutboxTests.Cases());
specifications.AddRange(InventoryTransactionTests.Cases());
specifications.AddRange(PotionTests.Cases());
specifications.AddRange(LegacyParticleTests.Cases());
specifications.AddRange(TransientLightTests.Cases());
specifications.AddRange(SpawnGateTests.Cases());
specifications.AddRange(SpawnScheduleTests.Cases());
specifications.AddRange(MonsterMovementTests.Cases());
specifications.AddRange(MonsterNavigationTests.Cases());
specifications.AddRange(NetworkHardeningTests.Cases());
specifications.AddRange(MonsterSkillTests.Cases());
specifications.AddRange(SpatialIndexTests.Cases());
specifications.AddRange(HudInteractionTests.Cases());
specifications.AddRange(FirstTierSkillTests.Cases());
specifications.AddRange(ProgressionTransactionTests.Cases());
specifications.AddRange(FrameTimingTests.Cases());
specifications.AddRange(ClientSettingsTests.Cases());
specifications.AddRange(PhotorealTests.Cases());
specifications.AddRange(BinaryCursorTests.Cases());
specifications.AddRange(BinaryProtocolTests.Cases());
specifications.AddRange(AuthTests.Cases());
specifications.AddRange(ShadowProjectionTests.Cases());
specifications.AddRange(SkinPaletteTests.Cases());
specifications.AddRange(EffectLoadingTests.Cases());
specifications.AddRange(PassiveSkillTests.Cases());
specifications.AddRange(AnimationFallbackTests.Cases());
specifications.AddRange(ModelNormalTests.Cases());
specifications.AddRange(EquipmentTests.Cases());
specifications.AddRange(RangedEquipmentTests.Cases());
specifications.AddRange(WorldRewardsTests.Cases());
specifications.AddRange(NpcPlacementMigrationTests.Cases());
specifications.AddRange(NpcMovementTests.Cases());
specifications.AddRange(GroundLootLabelTests.Cases());
specifications.AddRange(GroundPickupTransactionTests.Cases());
specifications.AddRange(EntitySweepTests.Cases());
specifications.AddRange(CharacterHeadTests.Cases());
specifications.AddRange(CharacterSelectTests.Cases());
specifications.AddRange(FieldAudioTests.Cases());
specifications.AddRange(MonsterCombatTests.Cases());
specifications.AddRange(AttackInputTests.Cases());
specifications.AddRange(MonsterNameTests.Cases());
specifications.AddRange(WorldImportTests.Cases());
specifications.AddRange(SkyTests.Cases());
var failed = 0;
foreach (var (name, run) in specifications)
{
    try
    {
        await run().WaitAsync(TimeSpan.FromSeconds(15));
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception ex)
    {
        failed++;
        Console.Error.WriteLine($"FAIL {name}: {ex}");
    }
}

Console.WriteLine($"{specifications.Count - failed}/{specifications.Count} passed");
if (args.Contains("--benchmark"))
{
    await DomainTests.BenchmarkAsync();
}

return failed == 0 ? 0 : 1;
