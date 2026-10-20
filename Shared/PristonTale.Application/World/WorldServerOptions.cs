namespace PristonTale.Application.World;

public sealed record WorldServerOptions
{
    // Etervale's server-wide bonus on .inf experience; the original only had level-banded event bonuses on
    // its experience total, so this is a server setting rather than a ported constant.
    public const int DefaultExperienceMultiplier = 5;

    // A client normally has a few commands waiting for the next tick; the cap keeps one connection from filling
    // the queue shared by every player.
    public const int DefaultMaxPendingCommandsPerPlayer = 64;

    public static readonly WorldServerOptions Default = new();

    public int ExperienceMultiplier { get; init; } = DefaultExperienceMultiplier;

    public int MaxPendingCommandsPerPlayer { get; init; } = DefaultMaxPendingCommandsPerPlayer;

    public WorldServerOptions Validated()
    {
        if (ExperienceMultiplier < 1)
        {
            throw new InvalidOperationException("World:ExperienceMultiplier must be at least 1.");
        }

        if (MaxPendingCommandsPerPlayer < 1)
        {
            throw new InvalidOperationException("World:MaxPendingCommandsPerPlayer must be at least 1.");
        }

        return this;
    }
}

// The authoritative command queue refused a command; the caller drops it like a lost packet.
public sealed class WorldBusyException(string message) : InvalidOperationException(message);
