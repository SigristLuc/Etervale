using PristonTale.Simulation.Modifiers;

namespace PristonTale.Application.World;

// Timed effects of used items (scrolls, force orbs) and of pets (a slowed monster). Item uses commit in the command phase; their effects start with the
// next field step so the status events reach that step's snapshot.
internal sealed partial class FieldSkills
{
    private readonly List<(ulong Entity, EffectSpec Spec, float DurationMs, int Rank)> _pendingItemEffects = new();

    private readonly List<(ulong Entity, uint Code)> _pendingItemEnds = new();

    public void QueueItemEffect(ulong entity, EffectSpec spec, float durationMs, int rank = 1) => _pendingItemEffects.Add((entity, spec, durationMs, rank));

    // Ends an item's effect on a player with the next step (a castle item replaced or left behind).
    public void EndItemEffect(WorldPlayer player, uint code) => _pendingItemEnds.Add((player.EntityId, code));

    private void StartItemEffects()
    {
        foreach (var (entity, code) in _pendingItemEnds)
        {
            if (_host.TryGetPlayer(entity, out var holder))
            {
                RemoveEffect(holder.Character.Effects, entity, code);
            }
        }

        _pendingItemEnds.Clear();
        foreach (var (entity, spec, durationMs, rank) in _pendingItemEffects)
        {
            var effects = _host.TryGetPlayer(entity, out var player) && !player.IsDead ? player.Character.Effects
                : _host.TryGetMonster(entity, out var monster) && monster.IsAlive ? monster.Effects : null;
            if (effects?.Apply(spec, entity, durationMs, entity) is { } change)
            {
                _statusEvents.Add(StatusView(change, rank));
            }
        }

        _pendingItemEffects.Clear();
    }
}
