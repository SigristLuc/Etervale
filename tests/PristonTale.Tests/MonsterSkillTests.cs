using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using PristonTale.Application.World;
using PristonTale.Client.Net.Online;
using PristonTale.Client.Net.Presence;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.World.Ai;
using PristonTale.Simulation.World.Fields;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Simulation.World.Motion;
using PristonTale.Simulation.World.Spawning;

namespace PristonTale.Tests;

internal static class MonsterSkillTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("monster skill selection requires supported rules and a real skill clip", SelectionAsync);
        yield return ("monster skills preserve spherical and forward square boundaries", ShapesAsync);
        yield return ("ordinary monster skills bypass precision and block while preserving flat absorption", DamageAsync);
        yield return ("monster skill impact is single-use, interruptible and independent of target movement", ImpactAsync);
        yield return ("field emits skill motion and repeated cast timing through presence", FieldAsync);
        yield return ("single-target monster skill cannot hit bystanders or escaping prey", SingleTargetAsync);
    }

    internal static MonsterDefinition Definition(int soundCode = 0x19F0) => new(
        1, "Skill fixture", MonsterFaction.Enemy, MonsterClass.Normal, MonsterBrood.Normal,
        new MonsterCombatStats(1, 0, new MonsterRange(1, 1), 6, 80, 0, 0, 0, 10000, ResistanceBlock.Zero), "fixture.inx")
    {
        SoundCode = soundCode,
        Skill = new MonsterSkillStats(new MonsterRange(30, 30), 100, 0, 100, 0),
        Ai = new MonsterAi(6, MonsterNature.Evil, 1000, 1, 64, MonsterRange.Zero, MonsterActiveHour.Any)
    };

    private static MotionTimingSet Timing(uint impact = 1600) => MotionTimingSet.Build([
        new(MotionStateCode.Attack, new MotionClip(0, 10, 800, 0, 0, 0, false)),
        new(MotionStateCode.Skill, new MotionClip(100, 130, impact, 0, 0, 0, false))
    ]);

    private static Task SelectionAsync()
    {
        var definition = Definition();
        Assert.True(!new MonsterAiTuning(definition).CanCastOffensiveSkill, "missing skill clip fabricated a cast");
        Assert.True(!new MonsterAiTuning(definition with { SoundCode = 0x1600 }, Timing()).CanCastOffensiveSkill, "unported Babel rules were enabled");
        Assert.True(!new MonsterAiTuning(definition with { Skill = definition.Skill with { CurseSkillCode = 101 } }, Timing()).CanCastOffensiveSkill,
            "curse was treated as ordinary damage");
        var tuning = new MonsterAiTuning(definition, Timing());
        Assert.True(tuning.CanCastOffensiveSkill, "verified skill remained unavailable");
        Assert.Near(330, tuning.SkillWindupMs);
        Assert.Near(990, tuning.SkillDurationMs);
        Assert.Near(990, new MonsterAiTuning(definition, Timing(uint.MaxValue)).SkillWindupMs);
        return Task.CompletedTask;
    }

    private static Task ShapesAsync()
    {
        var skill = Definition().Skill;
        Assert.True(MonsterOffensiveSkill.Contains(skill, Vector3.Zero, Vector3.Zero, new Vector3(99, 0, 0)), "sphere interior excluded");
        Assert.True(!MonsterOffensiveSkill.Contains(skill, Vector3.Zero, Vector3.Zero, new Vector3(100, 0, 0)), "sphere boundary became inclusive");
        Assert.True(!MonsterOffensiveSkill.Contains(skill, Vector3.Zero, Vector3.Zero, new Vector3(80, 80, 0)), "sphere ignored height");
        skill = skill with { SkillDistance = 200, SkillRange = 50 };
        var center = MonsterOffensiveSkill.AreaCenter(Vector3.Zero, MathF.PI / 2, skill);
        Assert.Near(200, center.X);
        Assert.True(MonsterOffensiveSkill.Contains(skill, Vector3.Zero, center, new Vector3(249, 0, 49)), "forward square corner excluded");
        Assert.True(!MonsterOffensiveSkill.Contains(skill, Vector3.Zero, center, new Vector3(250, 0, 0)), "forward area boundary became inclusive");
        Assert.True(!MonsterOffensiveSkill.Contains(skill, Vector3.Zero, center, new Vector3(200, 140, 0)), "area crossed vertical gate");
        Assert.True(!MonsterOffensiveSkill.Contains(skill, Vector3.Zero, center, new Vector3(-200, 0, 0)), "forward skill hit behind caster");
        skill = skill with { SkillRange = 0 };
        Assert.True(!MonsterOffensiveSkill.Contains(skill, Vector3.Zero, Vector3.Zero, new Vector3(460, 0, 0)), "single-target skill has unlimited reach");
        Assert.True(MonsterOffensiveSkill.QueryRadius(skill with { SkillRange = int.MaxValue }) < 1087, "query can scan unbounded grid cells");
        return Task.CompletedTask;
    }

    private static Task DamageAsync()
    {
        var defender = new DefenderProfile(int.MaxValue, 7, 100, 200, ResistanceBlock.Zero, 1000, 1000, false);
        var rng = new CombatRng(1);
        for (int index = 0; index < 100; index++)
        {
            var result = CombatResolver.ResolveMonsterSkill(rng, new MonsterRange(30, 30), defender);
            Assert.True(result.Hit && !result.Blocked && !result.Crit && result.Damage == 23, "skill used basic precision/block/critical rules");
        }

        Assert.True(CombatResolver.ResolveMonsterSkill(rng, new MonsterRange(1, 1), defender).Damage == 1, "absorption lost its damage floor");
        return Task.CompletedTask;
    }

    private static Task ImpactAsync()
    {
        var host = new SkillHost();
        var brain = new MonsterAiBrain(new CombatRng(1));
        var combat = new FieldCombat(host, new CombatRng(2));
        var monster = host.Monster;
        brain.Tick(monster, host.Tuning, host.Prey, 60);
        Assert.True(monster.Motion == MonsterMotionState.Skill && monster.Motion.IsBusy(), "skill did not acquire the busy motion");
        var origin = monster.Position;
        monster.TargetId = 2; // A retarget outside the brain must not rewrite an already prepared cast.
        Assert.True(brain.Tick(monster, host.Tuning, host.Prey, 329) is null, "cast hit before its INX event");
        host.Players[0].Teleport(new Vector3(500, 0, 0));
        host.Prey.Rebuild(host.Players);
        var hit = brain.Tick(monster, host.Tuning, host.Prey, 1);
        Assert.True(hit is { IsSkill: true, TargetEntityId: 1 } && monster.Position == origin, "skill drifted or missed its impact event");
        int outsideHp = host.Players[0].Hp;
        int insideHp = host.Players[1].Hp;
        combat.ResolveMonsterHit(monster, hit!.Value);
        Assert.True(host.Players[0].Hp == outsideHp && host.Players[1].Hp < insideHp, "area followed escaping target or missed another player");
        var effect = host.Events.Single(value => value.Kind == CombatEventKindView.MonsterSkillImpact);
        Assert.True(effect.SourceSoundCode == monster.Def.SoundCode && effect.Position == origin && effect.TargetId == 1,
            "skill effect lost its captured origin, family or target");
        int remainingHp = host.Players[1].Hp;
        combat.ResolveMonsterHit(monster, hit.Value);
        Assert.True(host.Players[1].Hp == remainingHp, "replayed impact applied damage twice");
        Assert.True(host.Events.Count(value => value.Kind == CombatEventKindView.MonsterSkillImpact) == 1, "replayed impact duplicated its VFX");

        var interrupted = new SkillHost();
        brain.Tick(interrupted.Monster, interrupted.Tuning, interrupted.Prey, 60);
        MonsterAiBrain.ApplyFlinchOnHit(interrupted.Monster, new CombatRng(1), 2000, false, 700);
        Assert.True(brain.Tick(interrupted.Monster, interrupted.Tuning, interrupted.Prey, 500) is null
            && !interrupted.Monster.SkillImpactPending, "flinched caster still emitted an impact");
        interrupted.Monster.ApplyDamage(10000);
        Assert.True(brain.Tick(interrupted.Monster, interrupted.Tuning, interrupted.Prey, 1000) is null, "dead caster continued skill");
        return Task.CompletedTask;
    }

    private static Task SingleTargetAsync()
    {
        var definition = Definition(0x1840);
        definition = definition with { Skill = definition.Skill with { SkillRange = 0 } };
        foreach (bool escape in new[] { false, true })
        {
            var host = new SkillHost(definition);
            var brain = new MonsterAiBrain(new CombatRng(1));
            var combat = new FieldCombat(host, new CombatRng(2));
            brain.Tick(host.Monster, host.Tuning, host.Prey, 60);
            if (escape)
            {
                host.Players[0].Teleport(new Vector3(500, 0, 0));
            }

            int before = host.Players[0].Hp;
            int neighbourHp = host.Players[1].Hp;
            var hit = brain.Tick(host.Monster, host.Tuning, host.Prey, 330);
            Assert.True(hit.HasValue, "single-target skill did not emit its impact");
            combat.ResolveMonsterHit(host.Monster, hit!.Value);
            Assert.True((host.Players[0].Hp < before) != escape, "single-target skill ignored escape/reach");
            Assert.True(host.Players[1].Hp == neighbourHp, "single-target skill damaged a bystander");
            Assert.True(host.Events.Count(value => value.Kind == CombatEventKindView.MonsterSkillImpact) == 1, "escaping all damage suppressed the skill VFX");
        }

        return Task.CompletedTask;
    }

    private static Task FieldAsync()
    {
        var definition = Definition();
        var timing = Timing();
        var catalog = new MotionTimingCatalog(new Dictionary<string, MotionTimingSet> { ["fixture.inx"] = timing });
        var field = new FieldInstance(3, FieldSpawnData.Empty, new MonsterCatalog([definition]), new EntityIdAllocator(), new CombatRng(1), catalog);
        var player = CreatePlayer(1, new Vector3(50, 0, 0));
        field.Add(player);
        var monster = ((ISpawnSink)field).Spawn(definition, Vector3.Zero, 0, 0);
        monster.TargetId = 1;
        monster.AiState = MonsterAiState.Chase;
        int entries = 0;
        for (uint tick = 1; tick <= 60; tick++)
        {
            field.Step(.05f);
            var events = field.SnapshotEvents() ?? [];
            foreach (var cast in events.Where(value => value.Kind == CombatEventKindView.SwingBegin && value.AttackerId == monster.EntityId))
            {
                entries++;
                Assert.Near(990, cast.DurationMs);
                Assert.Near(330, cast.HitAtMs);
                var snapshot = new WorldSnapshot(tick, field.BuildViews(), events, 3);
                // Snapshots are sampled only at cast starts, so each one is checked as a fresh subscriber would see it.
                var presence = new WireRoundTrip(player.EntityId).PushMerged(snapshot);
                Assert.True(presence.Spawns.Any(entity => entity.EntityId == monster.EntityId && entity.MoveState == RemoteMoveState.Skill)
                    || presence.States.Any(entity => entity.EntityId == monster.EntityId && entity.MoveState == RemoteMoveState.Skill),
                    "presence collapsed skill into basic attack");
            }
        }

        Assert.True(entries >= 3, "repeated casts stopped after the first skill");
        return Task.CompletedTask;
    }

    private static WorldPlayer CreatePlayer(uint id, Vector3 position)
    {
        var character = new CharacterClassRegistry().CreatePlayer(PlayerClass.Fighter, "Skill target", 100);
        return new WorldPlayer(id, position, character, false, default);
    }

    private sealed class SkillHost : IFieldCombatHost
    {
        public WorldPlayer[] Players { get; } = [CreatePlayer(1, new Vector3(50, 0, 0)), CreatePlayer(2, new Vector3(-50, 0, 0))];
        public MonsterInstance Monster { get; }
        public MonsterAiTuning Tuning { get; }
        public FieldPreyView Prey { get; } = new();

        public SkillHost(MonsterDefinition? definition = null)
        {
            definition ??= Definition();
            Monster = new MonsterInstance(3, definition, Vector3.Zero, 0, 0)
            {
                TargetId = 1,
                AiState = MonsterAiState.Chase
            };
            Tuning = new MonsterAiTuning(definition, Timing());
            Prey.Rebuild(Players);
        }

        public IEnumerable<MonsterInstance> Monsters => [Monster];
        public IReadOnlyList<PreyCandidate> NearbyPlayers(Vector3 origin, float radius) => Prey.Nearby(origin, radius);
        public MonsterAiTuning TuningFor(MonsterDefinition definition) => Tuning;
        public (float DurationMs, float HitAtMs) PlayerSwingTiming(WorldPlayer player) => (990, 330);
        public List<CombatEventView> Events { get; } = [];
        public void EmitCombatEvent(in CombatEventView combatEvent)
        {
            Events.Add(combatEvent);
        }
        public void DespawnMonster(uint entityId)
        {
        }

        public bool TryGetPlayer(uint entityId, [MaybeNullWhen(false)] out WorldPlayer player)
        {
            player = Players.FirstOrDefault(value => value.EntityId == entityId);
            return player is not null;
        }

        public bool TryGetMonster(uint entityId, [MaybeNullWhen(false)] out MonsterInstance monster)
        {
            monster = Monster;
            return entityId == monster.EntityId;
        }
    }
}
