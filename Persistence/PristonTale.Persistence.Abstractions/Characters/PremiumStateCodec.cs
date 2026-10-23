using System.Text.Json;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items.Usable;

namespace PristonTale.Persistence.Abstractions.Characters;

public sealed record SavedPremiumTimer(PremiumEffectKind Kind, int Value, long DurationSeconds, long ExpiresAtSeconds);

// The character's `premium` column: the running premium timers with their real-clock expiry (Unix seconds) and the
// recent item uses kept for retries.
public sealed record SavedPremium(int Version, SavedPremiumTimer[] Timers, AppliedItemUse[]? Operations = null, int GravityScrolls = 0,
    int BigHeadMode = 0, long BigHeadExpiresAtSeconds = 0)
{
    public static SavedPremium Capture(PlayerCharacter character)
    {
        var timers = character.Premium.Timers
            .Select(static timer => new SavedPremiumTimer(timer.Kind, timer.Value, timer.DurationSeconds, timer.ExpiresAtSeconds))
            .ToArray();
        var premium = character.Premium;
        return new SavedPremium(1, timers, character.ItemUses.Applied.ToArray(), premium.GravityScrolls, premium.BigHeadMode,
            premium.BigHeadExpiresAtSeconds);
    }

    // Expired timers are dropped while loading; an unknown kind or version is corrupt data.
    public static void Restore(PlayerCharacter character, JsonText json, long nowSeconds)
    {
        if (CharacterStateCodec.IsLegacyEmpty(json, JsonValueKind.Object))
        {
            character.Premium.Restore([]);
            character.ItemUses.Restore([]);
            return;
        }

        var state = JsonSerializer.Deserialize<SavedPremium>(json.Value, CharacterStateCodec.Options);
        if (state is null || state.Version != 1 || state.Timers is null || state.Timers.Length > PremiumTimers.MaxTimers)
        {
            throw new InvalidDataException("Unsupported premium-state version or size.");
        }

        character.Premium.Restore(state.Timers
            .Where(timer => timer is not null && timer.ExpiresAtSeconds > nowSeconds)
            .Select(static timer => new PremiumTimer(timer.Kind, timer.Value, timer.DurationSeconds, timer.ExpiresAtSeconds)));
        character.ItemUses.Restore(state.Operations ?? []);
        var bigHead = state.BigHeadExpiresAtSeconds > nowSeconds ? state.BigHeadMode : 0;
        character.Premium.RestoreCounters(state.GravityScrolls, bigHead, state.BigHeadExpiresAtSeconds);
    }
}
