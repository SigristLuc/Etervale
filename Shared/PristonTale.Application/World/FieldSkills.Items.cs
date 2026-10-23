using PristonTale.Simulation.Modifiers;

namespace PristonTale.Application.World;

// Timed effects of used items (scrolls, force orbs). Item uses commit in the command phase; their effects start with the
// next field step so the status events reach that step's snapshot.
internal sealed partial class FieldSkills
{
    private readonly List<(uint Entity, EffectSpec Spec, float DurationMs)> _pendingItemEffects = new();

    public void QueueItemEffect(uint entity, EffectSpec spec, float durationMs) => _pendingItemEffects.Add((entity, spec, durationMs));

    private void StartItemEffects()
    {
        foreach (var (entity, spec, durationMs) in _pendingItemEffects)
        {
            if (_host.TryGetPlayer(entity, out var player) && !player.IsDead
                && player.Character.Effects.Apply(spec, entity, durationMs, entity) is { } change)
            {
                _statusEvents.Add(StatusView(change, 1));
            }
        }

        _pendingItemEffects.Clear();
    }
}
