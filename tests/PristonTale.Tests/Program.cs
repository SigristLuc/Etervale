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

if (args.Length == 2 && args[0] == "--verify-vfx-lifetimes")
{
    return VfxLingerProbe.Run(args[1]);
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

if (args.Length is 4 or 5 && args[0] == "--capture-skills")
{
    var codes = args.Length == 5 ? args[4].Split(',').Select(code => Convert.ToUInt32(code, 16)).ToArray() : null;
    using var probe = new SkillCaptureProbe(args[1], args[2], args[3], codes);
    probe.Run();
    return 0;
}

if (args.Length is 4 or 5 && args[0] == "--capture-monster-skills")
{
    var names = args.Length == 5 ? args[4].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) : null;
    using var probe = new MonsterSkillCaptureProbe(args[1], args[2], args[3], names);
    probe.Run();
    return 0;
}

if (args.Length is 4 or 5 && args[0] == "--capture-vfx")
{
    var names = args.Length == 5 ? args[4].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) : null;
    using var probe = new VfxCaptureProbe(args[1], args[2], args[3], names);
    probe.Run();
    return 0;
}

if (args.Length == 2 && args[0] == "--verify-skill-motions")
{
    SkillMotionProbe.Run(args[1]);
    return 0;
}

if (args.Length == 2 && args[0] == "--verify-player-timing")
{
    PlayerTimingProbe.Run(args[1]);
    return 0;
}


if (args.Length == 4 && args[0] == "--capture-potions")
{
    using var probe = new PotionCaptureProbe(args[1], args[2], args[3]);
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

if (args.Length == 4 && args[0] == "--benchmark-presence")
{
    PresenceLoadBenchmark.Run(args[1].Split(',').Select(int.Parse).ToArray(), args[2].Split(','), int.Parse(args[3]));
    return 0;
}

if (args.Contains("--benchmark-world"))
{
    WorldLoadBenchmark.Run();
    return 0;
}

if (args.Length == 4 && args[0] == "--capture-hud-target")
{
    using var probe = new HudTargetCaptureProbe(args[1], args[2], args[3]);
    probe.Run();
    return 0;
}

if (args.Length == 4 && args[0] == "--capture-shadows")
{
    using var probe = new ShadowCaptureProbe(args[1], args[2], args[3]);
    probe.Run();
    return 0;
}

if (args.Length == 4 && args[0] == "--capture-item-effects")
{
    using var probe = new ItemEffectsCaptureProbe(args[1], args[2], args[3]);
    probe.Run();
    return 0;
}

if (args.Length == 4 && args[0] == "--capture-item-hud")
{
    using var probe = new ItemHudCaptureProbe(args[1], args[2], args[3]);
    probe.Run();
    return 0;
}

if (args.Length is 4 or 5 && args[0] == "--capture-chat")
{
    using var probe = new ChatCaptureProbe(args[1], args[2], args[3], args.Length == 5 ? args[4] : null);
    probe.Run();
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

// Every probe above matches its exact argument count; anything else left here is a mistyped probe, not the suite.
for (var index = 0; index < args.Length; index++)
{
    if (args[index] == "--filter" && index + 1 < args.Length)
    {
        index++;
    }
    else if (args[index] != "--benchmark")
    {
        Console.Error.WriteLine($"Unknown option or wrong argument count: {string.Join(' ', args)} (see docs/testing.md).");
        return 2;
    }
}

var specifications = new List<(string Name, Func<Task> Run)>();
specifications.AddRange(DomainTests.Cases());
specifications.AddRange(ChatRoutingTests.Cases());
specifications.AddRange(GameMasterCommandTests.Cases());
specifications.AddRange(ChatClientTests.Cases());
specifications.AddRange(GameMasterStorageTests.Cases());
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
specifications.AddRange(YahooTests.Cases());
specifications.AddRange(TransientLightTests.Cases());
specifications.AddRange(SpawnGateTests.Cases());
specifications.AddRange(SpawnScheduleTests.Cases());
specifications.AddRange(MonsterMovementTests.Cases());
specifications.AddRange(DeathPenaltyTests.Cases());
specifications.AddRange(WingGateTests.Cases());
specifications.AddRange(HeldItemGlowTests.Cases());
specifications.AddRange(TerrainMarksTests.Cases());
specifications.AddRange(PlayerShotTests.Cases());
specifications.AddRange(MonsterNavigationTests.Cases());
specifications.AddRange(NetworkHardeningTests.Cases());
specifications.AddRange(MultiplayerMovementTests.Cases());
specifications.AddRange(MultiplayerCombatTests.Cases());
specifications.AddRange(MultiplayerPresenceTests.Cases());
specifications.AddRange(MultiplayerSessionTests.Cases());
specifications.AddRange(MultiplayerSocialTests.Cases());
specifications.AddRange(PresencePipelineTests.Cases());
specifications.AddRange(MonsterSkillTests.Cases());
specifications.AddRange(MonsterSkillStatusTests.Cases());
specifications.AddRange(MonsterBossSkillTests.Cases());
specifications.AddRange(MonsterPresentationTests.Cases());
specifications.AddRange(SpatialIndexTests.Cases());
specifications.AddRange(HudInteractionTests.Cases());
specifications.AddRange(ClassSkillDataTests.Cases());
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
specifications.AddRange(PhysiqueRepairTests.Cases());
specifications.AddRange(EquipmentTests.Cases());
specifications.AddRange(RangedEquipmentTests.Cases());
specifications.AddRange(WorldRewardsTests.Cases());
specifications.AddRange(WorldDataRulesTests.Cases());
specifications.AddRange(ItemRulesTests.Cases());
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
specifications.AddRange(NpcServiceTests.Cases());
specifications.AddRange(SkyTests.Cases());
specifications.AddRange(SkillSystemTests.Cases());
specifications.AddRange(SkillPresentationTests.Cases());
specifications.AddRange(SkillLookTests.Cases());
specifications.AddRange(VfxLayerTests.Cases());
specifications.AddRange(VfxImportTests.Cases());
specifications.AddRange(SkillCatalogTests.Cases());
specifications.AddRange(SkillAllyTests.Cases());
specifications.AddRange(SkillDefenseTests.Cases());
specifications.AddRange(SkillAreaTests.Cases());
specifications.AddRange(SkillEnchantTests.Cases());
specifications.AddRange(SkillResurrectionTests.Cases());
specifications.AddRange(SkillControlTests.Cases());
specifications.AddRange(SkillWireTests.Cases());
specifications.AddRange(ProgressionDataTests.Cases());
specifications.AddRange(SkillSummonTests.Cases());
specifications.AddRange(SkillStealthTests.Cases());
specifications.AddRange(SkillChargeTests.Cases());
specifications.AddRange(SkillCompanionTests.Cases());
specifications.AddRange(SpecialItemTests.Cases());
specifications.AddRange(MonsterCrystalTests.Cases());
specifications.AddRange(SummonAllianceTests.Cases());
specifications.AddRange(CastleItemTests.Cases());
specifications.AddRange(EventItemTests.Cases());
specifications.AddRange(PuzzleTests.Cases());
specifications.AddRange(QuestTests.Cases());
specifications.AddRange(QuestServiceTests.Cases());
specifications.AddRange(QuestHudTests.Cases());
specifications.AddRange(AgingTests.Cases());
specifications.AddRange(ServerSettingsTests.Cases());
specifications.AddRange(MixingTests.Cases());
// --filter <text> runs only the cases whose name contains the text.
var filterIndex = Array.IndexOf(args, "--filter");
if (filterIndex >= 0 && filterIndex + 1 < args.Length)
{
    specifications.RemoveAll(spec => !spec.Item1.Contains(args[filterIndex + 1], StringComparison.OrdinalIgnoreCase));
}

var failed = 0;
foreach (var (name, run) in specifications)
{
    Task? task = null;
    try
    {
        task = run();
        await task.WaitAsync(TimeSpan.FromSeconds(15));
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception ex)
    {
        failed++;
        Console.Error.WriteLine($"FAIL {name}: {ex}");
        // A timed-out case keeps running and would share the process with the cases after it.
        if (task is { IsCompleted: false })
        {
            Console.Error.WriteLine("Stopping: the timed-out case is still running.");
            break;
        }
    }
}

Console.WriteLine($"{specifications.Count - failed}/{specifications.Count} passed");
if (args.Contains("--benchmark"))
{
    await DomainTests.BenchmarkAsync();
}

return failed == 0 ? 0 : 1;
