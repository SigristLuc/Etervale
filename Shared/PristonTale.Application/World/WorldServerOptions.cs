using PristonTale.Simulation.Items.Aging;
using PristonTale.Simulation.World.Loot;

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

    public int ExperienceEventPercent { get; init; }

    public AgingSettings Aging { get; init; } = AgingSettings.Default;

    public Simulation.Items.Mixing.MixingSettings Mixing { get; init; } = Simulation.Items.Mixing.MixingSettings.Default;

    // Game minutes added to the game clock, to start the server at another hour of the day.
    public int ClockOffsetMinutes { get; init; }

    public int MaxPendingCommandsPerPlayer { get; init; } = DefaultMaxPendingCommandsPerPlayer;

    // The crystal event: 0 off, 1 from 12:00 to 23:59 server time, 2 all day; crystals then call their event monsters.
    public int CrystalEvent { get; init; }

    // Item events that replace drop rolls with event items (rates per 10000); all off by default.
    public ItemEventRates ItemEvents { get; init; } = ItemEventRates.None;

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

        if (CrystalEvent is < 0 or > 2)
        {
            throw new InvalidOperationException("World:CrystalEvent must be 0, 1 or 2.");
        }

        if (ExperienceEventPercent < 0 || Aging is null || Aging.PricePercent < 0 || Aging.FailurePercent < 0 || Aging.DoublePercent < 0)
        {
            throw new InvalidOperationException("Experience event and aging percentages must not be negative.");
        }

        return this;
    }
}

// The authoritative command queue refused a command; the caller drops it like a lost packet.
public sealed class WorldBusyException(string message) : InvalidOperationException(message);

// A commit found the character or a reservation changed after its operation was stored: storage is ahead of memory, so
// the session must reload from it. An error, never a refusal.
public sealed class CommitInvariantException(string message) : Exception(message);
