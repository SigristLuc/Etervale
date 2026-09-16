using Microsoft.Xna.Framework;
using PristonTale.Effects.Legacy;
using PristonTale.Effects.Particles;

namespace PristonTale.Tests;

internal static class LegacyParticleTests
{
    private const string Script = """
        particlesystem "Attack" 1.00 {
          position = XYZ(0,0,0)
          eventsequence "fixture" {
            sourceblendmode = BLEND_LAMP
            loops = 1
            numparticles = 2
            gravity = XYZ(0,0,0)
            emitrate = 10
            emitradius = xyz(0,0,0)
            lifetime = 2
            texture = "effect\\particle\flare.tga"
            initial size = 20
            initial sizeext = 100
            initial PartAngle = xyz(0,0,0)
            initial color = rgba(100,200,0,255)
            initial velocity = xyz(10,0,20)
            at 1 velocity = xyz(-10,0,-20)
            fade so at 0.5 color = rgba(200,100,0,255)
            fade so final color = rgba(0,0,0,0)
            fade so final size = 60
            fade so final PartAngle = xyz(0,0,1024)
          }
        }
        """;

    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("legacy particle scripts preserve events, units, blend and rectangular billboards", TimelineAsync);
        yield return ("legacy particle emission is finite and stable across frame rates and hitches", EmissionAsync);
        yield return ("legacy particle compiler rejects unsupported or unbounded scripts", ValidationAsync);
        yield return ("legacy particle gravity, delay, loops, plane and oriented offsets match their contracts", ExtendedAsync);
        yield return ("legacy particle random channels remain stable without frame allocations", RandomAsync);
    }

    private static Task ExtendedAsync()
    {
        var sequence = Read(Script.Replace("loops = 1", "loops = 3\n            delay = 0.25\n            particletype = TYPE_TWO")
            .Replace("BLEND_LAMP", "BLEND_INVSHADOW").Replace("gravity = XYZ(0,0,0)", "gravity = XYZ(0,-20,0)"));
        Assert.True(sequence.TotalCount == 6 && sequence.GroundPlane
            && sequence.Blend == PristonTale.Effects.Rendering.ParticleBlendMode.InverseShadow, "extended directives lost");
        var particle = new Particle { Age = .5f };
        var rotation = PristonTale.Gameplay.Effects.LegacyMonsterMeshEffects.DirectionRotation(Vector3.Right);
        sequence.Initialize(ref particle, Vector3.Zero, 12u, rotation);
        sequence.Evaluate(ref particle, rotation);
        Assert.Near(-2.5f, particle.Position.Y);
        Assert.Near(10f, particle.Position.X);
        foreach (var direction in new[] { Vector3.Right, Vector3.Left, Vector3.Up, Vector3.Down, Vector3.Forward, Vector3.Backward })
        {
            var basis = PristonTale.Gameplay.Effects.LegacyMonsterMeshEffects.DirectionRotation(direction);
            var actual = Vector3.TransformNormal(Vector3.Forward, basis);
            Assert.Near(0, Vector3.Distance(actual, direction), .0001f);
            Assert.Near(1, basis.Right.Length(), .0001f);
        }
        var definition = new EmitterDefinition { MaxParticles = 6, LegacySequence = sequence };
        var emitter = new ParticleEmitter(definition, Vector3.Zero, 12);
        emitter.Update(.3f, Vector3.Zero);
        Assert.True(emitter.AliveCount == 0, "delay was ignored");
        emitter.Update(.6f, Vector3.Zero);
        Assert.True(emitter.AliveCount == 6, "finite loops were truncated to pool size");
        emitter.Update(10, Vector3.Zero);
        Assert.True(!emitter.IsAlive, "looped sequence leaked after a hitch");
        return Task.CompletedTask;
    }

    private static LegacyParticleSequence Read(string script = Script)
    {
        using var reader = new StringReader(script);
        return LegacyParticleScript.Read(reader).Single();
    }

    private static Task TimelineAsync()
    {
        var sequence = Read();
        Assert.True(sequence.Texture == "effect/particle/flare.tga", "legacy slashes were not normalized");
        var particle = new Particle { Age = 1.5f };
        sequence.Initialize(ref particle, new Vector3(5, 7, 9), new Random(1));
        sequence.Evaluate(ref particle);
        Assert.Near(25, particle.Size);
        Assert.Near(50, particle.Height);
        Assert.Near(10, particle.Position.X);
        Assert.Near(7, particle.Position.Y);
        Assert.Near(-1, particle.Position.Z);
        Assert.Near(MathHelper.Pi * .375f, particle.Rotation);
        Assert.True(particle.Color.R is >= 66 and <= 67 && particle.Color.A is >= 84 and <= 85, "intermediate color key was ignored");
        return Task.CompletedTask;
    }

    private static Task EmissionAsync()
    {
        var definition = new EmitterDefinition { MaxParticles = 2, LegacySequence = Read() };
        var oneStep = new ParticleEmitter(definition, Vector3.Zero, 123);
        var manySteps = new ParticleEmitter(definition, Vector3.Zero, 123);
        Assert.True(oneStep.AliveCount == 0, "script converted to immediate burst");
        oneStep.Update(.5f, Vector3.Zero);
        for (int index = 0; index < 50; index++)
        {
            manySteps.Update(.01f, Vector3.Zero);
        }

        Assert.True(oneStep.AliveCount == 2 && manySteps.AliveCount == 2, "finite emission count changed with FPS");
        var one = oneStep.GetRenderView().Particles;
        var many = manySteps.GetRenderView().Particles;
        for (int index = 0; index < one.Length; index++)
        {
            Assert.Near(one[index].Position.X, many[index].Position.X, .0001f);
            Assert.Near(one[index].Size, many[index].Size, .0001f);
        }

        oneStep.Update(10, Vector3.Zero);
        Assert.True(!oneStep.IsAlive && oneStep.AliveCount == 0, "finite sequence did not expire after a hitch");
        var stopped = new ParticleEmitter(definition, Vector3.Zero, 123);
        stopped.Stop();
        stopped.Update(1, Vector3.Zero);
        Assert.True(!stopped.IsAlive && stopped.AliveCount == 0, "stopped script still emitted");
        return Task.CompletedTask;
    }

    private static Task ValidationAsync()
    {
        foreach (string invalid in new[]
        {
            Script.Replace("BLEND_LAMP", "BLEND_SHADOW", StringComparison.Ordinal),
            Script.Replace("numparticles = 2", "numparticles = 1000000", StringComparison.Ordinal),
            Script.Replace("emitrate = 10", "emitrate = NaN", StringComparison.Ordinal),
            Script.Replace("loops = 1", "loops = 0", StringComparison.Ordinal),
            Script.Replace("initial size = 20", "initial mesh = 20", StringComparison.Ordinal),
            Script.Replace("initial size = 20", "initial size = random(30,20)", StringComparison.Ordinal),
            Script.Replace("at 1 velocity", "fade so at 1 velocity", StringComparison.Ordinal),
            Script.Replace("initial PartAngle = xyz(0,0,0)", "initial PartAngle = xyz(1,0,0)", StringComparison.Ordinal),
            Script[..^1]
        })
        {
            bool rejected = false;
            try
            {
                Read(invalid);
            }
            catch (InvalidDataException)
            {
                rejected = true;
            }

            Assert.True(rejected, "unsupported particle content was silently accepted");
        }

        return Task.CompletedTask;
    }

    private static Task RandomAsync()
    {
        var sequence = Read(Script.Replace("initial velocity = xyz(10,0,20)",
            "initial velocity = xyz(random(-200,200),random(-20,20),random(-20,20))", StringComparison.Ordinal));
        var particle = new Particle { Age = .4f };
        sequence.Initialize(ref particle, Vector3.Zero, new Random(3));
        sequence.Evaluate(ref particle);
        var expected = particle.Position;
        for (int index = 0; index < 100; index++)
        {
            sequence.Evaluate(ref particle);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 1000; index++)
        {
            sequence.Evaluate(ref particle);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated == 0 && particle.Position == expected, "script re-evaluation allocated or re-rolled randomness");
        Assert.True(MathF.Abs(particle.Position.X) <= 80 && MathF.Abs(particle.Position.Z) <= 8, "axis ranges became spherical velocity");
        return Task.CompletedTask;
    }
}
