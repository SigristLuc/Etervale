using System.Text;
using Microsoft.Xna.Framework;
using PristonTale.Client.Net.Presence;
using PristonTale.Effects.Effects;
using PristonTale.Effects.Legacy;
using PristonTale.Effects.Vfx;
using PristonTale.Engine3D.IO;
using PristonTale.Gameplay.Effects;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Skills;

namespace PristonTale.Tests;

internal static class SkillPresentationTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("skill effect definitions parse, validate and cover every effect the skill rules reference", DefinitionsAsync);
        yield return ("skill effect assets exist and every legacy layer's original script compiles", AssetsAsync);
        yield return ("vfx system emits, travels, pools instances and keeps its budget", SystemAsync);
        yield return ("legacy particle scripts convert to the native effect format", ImporterAsync);
        yield return ("cooldown and status books follow the server's messages", BooksAsync);
        yield return ("skill sockets offer learned active skills and drop lost ones", BindingsAsync);
        yield return ("floating combat text keeps a bounded ring", FloatingTextAsync);
        yield return ("unknown builtin textures are rejected and a broken authored effect file is skipped", AuthoredFilesAsync);
    }

    private static Task AuthoredFilesAsync()
    {
        const string effect = """
            [{ "name": "vfx.test", "duration": 1, "emitters": [{ "texture": "builtin:glw", "burst": 1, "lifetime": [0.1, 0.2] }] }]
            """;
        var threw = false;
        try
        {
            VfxDefinition.ParseFile(effect);
        }
        catch (InvalidDataException)
        {
            threw = true;
        }

        Assert.True(threw, "a misspelled builtin texture was accepted");
        Assert.True(VfxDefinition.ParseFile(effect.Replace("builtin:glw", "builtin:glow", StringComparison.Ordinal)).Length == 1,
            "a valid builtin texture was refused");

        var directory = Path.Combine(Path.GetTempPath(), $"priston-vfx-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "broken.vfx.json"), "[{ \"name\": ");
            File.WriteAllText(Path.Combine(directory, "typo.vfx.json"), effect);
            var errors = new List<string>();
            var library = new VfxLibrary(_ => null, null!);
            Assert.True(library.LoadDirectory(directory, errors.Add) == 0 && library.Count == 0 && errors.Count == 2,
                "broken authored effect files were not skipped and reported");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }

        return Task.CompletedTask;
    }

    private static VfxDefinition[] Shipped() => SkillPresentation.EmbeddedDefinitions().SelectMany(VfxDefinition.ParseFile).ToArray();

    // Every effect a rule, its modes or its charged resolution names.
    private static IEnumerable<string> Effects(SkillRule rule)
    {
        var rules = new[] { rule }.Concat(rule.Modes).Concat(rule.Charge?.Full is { } full ? [full] : Array.Empty<SkillRule>());
        return rules.SelectMany(static item => new[] { item.CastEffect, item.ImpactEffect, item.ProjectileEffect, item.BuffEffect, item.CompanionEffect });
    }

    private static Task DefinitionsAsync()
    {
        var shipped = Shipped();
        var names = shipped.Select(definition => definition.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.True(names.Count == shipped.Length, "two effect files define the same effect");
        Assert.True(names.Count >= 120 && names.Contains(SkillPresentation.ReviveEffect), "shipped effect library is incomplete");
        foreach (var rule in SkillRuleCatalog.All)
        {
            foreach (var effect in Effects(rule))
            {
                Assert.True(effect.Length == 0 || names.Contains(effect), $"{rule.Name} references missing effect {effect}");
            }
        }

        Assert.ThrowsAsync(() => Task.Run(() => VfxDefinition.ParseFile("""[{ "name": "bad", "duration": 0 }]"""))).Wait();
        Assert.ThrowsAsync(() => Task.Run(() => VfxDefinition.ParseFile(
            """[{ "name": "bad", "emitters": [{ "size": [{ "t": 0.5, "v": [1] }, { "t": 0.2, "v": [2] }] }] }]"""))).Wait();
        return Task.CompletedTask;
    }

    private static Task AssetsAsync()
    {
        var root = FindAssets();
        if (root is null)
        {
            Console.WriteLine("  (skipped: no assets/ directory next to the repository)");
            return Task.CompletedTask;
        }

        var problems = new List<string>();
        foreach (var definition in Shipped())
        {
            foreach (var texture in definition.Emitters.Select(emitter => emitter.Texture).Concat(definition.Trails.Select(trail => trail.Texture)))
            {
                if (!texture.StartsWith(VfxBuiltinTextures.Prefix, StringComparison.Ordinal) && !PristonFileResolver.Exists(Path.Combine(root, texture)))
                {
                    problems.Add($"{definition.Name} texture {texture} is missing");
                }
            }

            foreach (var layer in definition.Legacy)
            {
                try
                {
                    if (LegacyEffectLibrary.ReadParticleSequences(root, layer.Script).Count == 0)
                    {
                        problems.Add($"{definition.Name} legacy script {layer.Script} is empty");
                    }
                }
                catch (Exception ex) when (ex is InvalidDataException or KeyNotFoundException or FormatException or FileNotFoundException or ArgumentException)
                {
                    problems.Add($"{definition.Name} legacy script {layer.Script}: {ex.Message}");
                }
            }
        }

        Assert.True(problems.Count == 0, string.Join("; ", problems));

        foreach (var rule in SkillRuleCatalog.All)
        {
            foreach (var sound in new[] { rule.CastSound, rule.ImpactSound, rule.CompanionSound })
            {
                Assert.True(sound.Length == 0 || PristonFileResolver.Exists(Path.Combine(root, sound)), $"{rule.Name} sound {sound} is missing");
            }
        }

        return Task.CompletedTask;
    }

    private static Task SystemAsync()
    {
        var library = new VfxLibrary(_ => null, null!);
        var burst = library.Register(new VfxDefinition
        {
            Name = "test.burst",
            Duration = 0.5f,
            Emitters = [new VfxEmitterDefinition { Burst = 10, MaxParticles = 10, Lifetime = [0.2f, 0.2f], Speed = [10f, 10f], Spread = 180 }]
        });
        var stream = library.Register(new VfxDefinition
        {
            Name = "test.stream",
            Duration = 1f,
            Emitters = [new VfxEmitterDefinition { Rate = 100, MaxParticles = 200, Lifetime = [0.1f, 0.1f] }],
            Trails = [new VfxTrailDefinition { Lifetime = 0.2f }]
        });
        var anchors = new Anchors();
        var system = new VfxSystem(anchors);
        var instance = system.Spawn(burst, new VfxSpawn(0, 0, Vector3.Zero, 0f))!;
        system.Update(0.01f);
        Assert.True(system.ParticleCount == 10, $"burst emitted {system.ParticleCount}");
        for (var frame = 0; frame < 120; frame++)
        {
            system.Update(1f / 60f);
        }

        Assert.True(system.Instances.Count == 0 && !instance.Alive, "finished effect stayed alive");
        var moving = system.Spawn(stream, new VfxSpawn(0, 0, Vector3.Zero, 0f))!;
        moving.Travel(Vector3.Zero, new Vector3(300, 0, 0), 0.5f);
        var peak = 0;
        for (var frame = 0; frame < 30; frame++)
        {
            system.Update(1f / 60f);
            peak = Math.Max(peak, system.ParticleCount);
        }

        Assert.True(moving.Spawn.Point.X >= 299f && !moving.Travelling, "projectile did not arrive");
        Assert.True(peak is > 5 and <= 200, $"stream particle count {peak}");
        for (var frame = 0; frame < 60; frame++)
        {
            system.Update(1f / 60f);
        }

        Assert.True(system.Instances.Count == 0, "arrived projectile kept emitting");
        for (var index = 0; index < VfxSystem.MaxInstances + 10; index++)
        {
            system.Spawn(burst, new VfxSpawn(0, 0, Vector3.Zero, 0f));
        }

        Assert.True(system.Instances.Count == VfxSystem.MaxInstances, "instance budget exceeded");
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        for (var frame = 0; frame < 20; frame++)
        {
            system.Update(1f / 60f);
        }

        Assert.True(GC.GetAllocatedBytesForCurrentThread() - allocated < 4096, "vfx update allocated per frame");
        return Task.CompletedTask;
    }

    private static Task ImporterAsync()
    {
        const string script = """
            particlesystem "Hit" 1.00 {
              position = XYZ(0,0,0)
              eventsequence "spark" {
                sourceblendmode = BLEND_LAMP
                loops = 1
                numparticles = 12
                gravity = XYZ(0,-100,0)
                emitrate = 40
                emitradius = xyz(5,5,5)
                lifetime = 0.5
                texture = "effect\\particle\\flare.tga"
                initial size = 20
                initial color = rgba(255,200,100,255)
                initial velocity = xyz(random(-50,50),random(20,80),random(-50,50))
                fade so final color = rgba(0,0,0,0)
                fade so final size = 4
              }
            }
            """;
        var sequences = LegacyParticleScript.Read(new StringReader(script));
        var converted = LegacyVfxImporter.Convert("legacy.hit", sequences, 2f);
        converted.Validate();
        var emitter = converted.Emitters.Single();
        Assert.True(emitter.Burst == 12 && emitter.Blend == VfxBlend.Additive && emitter.Shape == VfxShape.Sphere, "burst, blend or shape lost");
        Assert.True(emitter.Size.First().V[0] == 10f && emitter.Size.Last().V[0] == 2f, "size keys not halved like the legacy billboard");
        Assert.True(emitter.Color.Last().V[3] == 0f && emitter.Gravity[1] == -100f && emitter.Spread == 180f, "colour fade, gravity or spread lost");
        var json = System.Text.Json.JsonSerializer.Serialize(new[] { converted }, VfxDefinition.Json);
        Assert.True(VfxDefinition.ParseFile(json).Single().Name == "legacy.hit", "converted definition does not round-trip");
        return Task.CompletedTask;
    }

    private static Task BooksAsync()
    {
        var cooldowns = new SkillCooldownBook();
        cooldowns.Apply(new SkillCastResultInfo(1, 0x08010002, SkillCastStatus.Accepted, 1, 10000, 10000, 0, 0), 1000);
        Assert.True(!cooldowns.Ready(0x08010002, 5000) && Math.Abs(cooldowns.Fraction(0x08010002, 6000) - 0.5f) < 0.001f, "cooldown sweep");
        cooldowns.Apply(new SkillCooldownSnapshot(0, []), 6000);
        Assert.True(cooldowns.Ready(0x08010002, 6000), "snapshot without the skill did not clear it");
        var statuses = new StatusBook();
        var added = 0;
        var removed = 0;
        statuses.Added += (_, _) => added++;
        statuses.Removed += (_, _) => removed++;
        statuses.Apply(new StatusEffectInfo(1, 1, 0x05010002, StatusEffectOp.Add, 1, 2, 0, 0, 30000, 30000, 8), 0);
        statuses.Apply(new StatusEffectInfo(1, 1, 0x05010002, StatusEffectOp.Refresh, 1, 2, 0, 0, 30000, 30000, 8), 5000);
        Assert.True(added == 1 && statuses.For(1).Single().RemainingMs(10000) == 25000, "refresh restarted the timer incorrectly");
        statuses.Apply(new StatusEffectInfo(1, 1, 0x05010002, StatusEffectOp.Remove, 1, 2, 0, 0, 30000, 0, 8), 6000);
        Assert.True(removed == 1 && statuses.For(1).Count == 0, "remove ignored");
        return Task.CompletedTask;
    }

    private static Task BindingsAsync()
    {
        var bindings = new HudSkillBindings();
        var skills = new CharacterClassRegistry().Get(PlayerClass.Magician).Skills;
        bindings.SyncLearned(skills, [new OwnedSkillView(0x08010002, 2), new OwnedSkillView(0x08010001, 1)]);
        Assert.True(bindings.Select(0x08010002, SkillMouseUse.Left) && bindings.Select(0x08010001, SkillMouseUse.Right), "learned skill not selectable");
        Assert.True(!bindings.Select(0x08010001, SkillMouseUse.Left), "right-only skill placed on the left socket");
        Assert.True(bindings.Assign(0, 0x08010002, SkillMouseUse.Left), "short key binding refused");
        bindings.SyncLearned(skills, [new OwnedSkillView(0x08010002, 2)]);
        Assert.True(bindings.RightSkillId == HudSkillBindings.BasicAttackId && bindings.LeftSkillId == 0x08010002, "lost skill kept its socket");
        return Task.CompletedTask;
    }

    private static Task FloatingTextAsync()
    {
        var text = new FloatingCombatText();
        for (var index = 0; index < FloatingCombatText.Capacity * 2; index++)
        {
            text.Add(Vector3.Zero, index, FloatingTextKind.DamageDealt);
        }

        Assert.True(text.Count == FloatingCombatText.Capacity, "floating text grew past its ring");
        text.Update(5f);
        Assert.True(text.Count == 0, "floating text did not expire");
        return Task.CompletedTask;
    }

    private static string? FindAssets()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "assets");
            if (Directory.Exists(Path.Combine(candidate, "Effect")))
            {
                return candidate;
            }
        }

        return null;
    }

    private sealed class Anchors : IVfxAnchors
    {
        public bool TryGetPosition(uint entityId, out Vector3 position)
        {
            position = Vector3.Zero;
            return false;
        }

        public bool TryGetBone(uint entityId, string bone, out Vector3 position)
        {
            position = Vector3.Zero;
            return false;
        }
    }
}
