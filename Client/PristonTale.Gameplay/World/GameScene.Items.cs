using Microsoft.Xna.Framework;
using PristonTale.Client.Net.Presence;
using PristonTale.Gameplay.Effects;
using PristonTale.Simulation.Items.Usable;

namespace PristonTale.Gameplay.World;

// Item use from the HUD, the premium timers the server sends and the item effects played around players.
public sealed partial class GameScene
{
    private LegacyItemEffects _itemEffects;

    // The local player's running premium timers as last sent by the server.
    public PremiumTimerSnapshot PremiumTimers { get; private set; }

    public Task<ItemUseOutcome> UseItemAsync(ItemUseCommand command)
    {
        return _playChannel is not null ? _playChannel.UseItemAsync(command, _presenceCts.Token)
            : Task.FromResult(ItemUseOutcome.Refused(command.ExpectedRevision, ItemUseStatus.Rejected));
    }

    // Loads the item effects with the scene's effects, at the field load that creates the renderer.
    private void CreateItemEffects()
    {
        if (_itemEffects is null && _effects is not null && _effectLibrary is not null && _renderer is not null)
        {
            _itemEffects = new LegacyItemEffects(_effectLibrary, _assetRoot ?? "", _effects, _renderer.TransientLights, _skillSounds);
        }
    }

    private void ObserveItems(PresenceUpdate update)
    {
        if (update.PremiumTimers is { } timers)
        {
            PremiumTimers = timers;
        }

        foreach (var effect in update.ItemEffects)
        {
            if (ItemAnchor(effect.Entity) is not { } position)
            {
                continue;
            }

            if (effect.Effect == ItemEffectKind.Mastery)
            {
                _skillFx?.Spawn("vfx.agony", effect.Entity, effect.Entity, position);
            }

            _itemEffects?.Play(effect.Effect, effect.Entity, effect.Variant, position, PlayerPosition, _skillClockMs);
        }
    }

    private void UpdateItemEffects() => _itemEffects?.Update(_skillClockMs, ItemAnchor, PlayerPosition);

    private Vector3? ItemAnchor(uint entityId)
    {
        if (_remote is not null && entityId == _remote.SelfEntityId && _playerBody is not null)
        {
            return _playerBody.Position;
        }

        return _remote is not null && _remote.TryGetDrawnPosition(entityId, out var position) ? position : null;
    }
}
