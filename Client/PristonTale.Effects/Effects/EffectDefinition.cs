using Microsoft.Xna.Framework;
using PristonTale.Effects.Particles;

namespace PristonTale.Effects.Effects;

public sealed record EffectDefinition
{
    public required string Name { get; init; }

    public required IReadOnlyList<EmitterDefinition> Emitters { get; init; }

    public IReadOnlyList<Vector3> EmitterOffsets { get; init; } = [];

    public float Duration { get; init; }

    public bool Loop { get; init; }
}
