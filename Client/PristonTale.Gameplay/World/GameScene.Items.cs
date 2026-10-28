using Microsoft.Xna.Framework;
using PristonTale.Client.Net.Presence;
using PristonTale.Gameplay.Effects;
using PristonTale.Gameplay.World.Remote;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Simulation.Physics;

namespace PristonTale.Gameplay.World;

// Item use from the HUD, the premium timers the server sends and the item effects played around players.
public sealed partial class GameScene
{
    private ItemUseEffects _itemEffects;
    private PremiumPetPresentation _pets;
    private ForceOrbPresentation _forceOrbs;

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
            _itemEffects = new ItemUseEffects(_effectLibrary, _assetRoot ?? "", _effects, _renderer.TransientLights, _skillSounds);
        }

        if (_forceOrbs is null && _effects is not null && _skillFx is not null)
        {
            ItemVfx.Register(_skillFx.Library);
            _forceOrbs = new ForceOrbPresentation(_skillFx, _effectLibrary, _assetRoot ?? "", _effects, _renderer?.TransientLights);
        }

        if (_pets is null && _renderer is not null)
        {
            _pets = new PremiumPetPresentation(new RendererActorScene(_renderer), _actorLoader, _assetRoot ?? "", _effectLibrary, _effects,
                _renderer.TransientLights, _skillSounds, _log);
        }
    }

    private void ObserveItems(PresenceUpdate update)
    {
        if (update.PremiumTimers is { } timers)
        {
            PremiumTimers = timers;
        }

        _pets?.Observe(update, ItemAnchor);
        ObserveOwnHead(update);
        _forceOrbs?.Observe(update, ItemAnchor, _skillClockMs);

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
            else if (effect.Effect is ItemEffectKind.Learn or ItemEffectKind.ForceOrb or ItemEffectKind.HeadChange)
            {
                _skillFx?.Spawn(ItemVfx.Learn, effect.Entity, effect.Entity, position);
            }
            else if (effect.Effect == ItemEffectKind.Return)
            {
                // The sparkle stands where the server placed it: the user, the called monster or the revived player.
                _returnEffect?.Start(effect.Position + Vector3.Up * GroundTraversal.VisualOffset);
            }

            _itemEffects?.Play(effect.Effect, effect.Entity, effect.Variant, position, PlayerPosition, _skillClockMs);
        }
    }

    // A hair tint or a big-head potion changes the local player's head when the server describes the player again.
    private void ObserveOwnHead(PresenceUpdate update)
    {
        ObserveOwnGlow(update);
        if (_remote is null || _equipmentPresentation is null || _player is null)
        {
            return;
        }

        foreach (var spawn in update.Spawns)
        {
            if (spawn.EntityId != _remote.SelfEntityId || spawn.Player is not { } appearance)
            {
                continue;
            }

            var playerClass = _player.Class.Class;
            var head = CharacterHead.Apply(_player.Definition.DefaultVisuals, playerClass, appearance.Tier, appearance.FaceIndex, appearance.HairIndex);
            _equipmentPresentation.SetBaseVisuals(CharacterHead.ApplyBigHead(head, playerClass, appearance.BigHead));
        }
    }

    private void UpdateItemEffects(float dt)
    {
        _itemEffects?.Update(_skillClockMs, ItemAnchor, PlayerPosition);
        _pets?.Update(dt, PetOwnerPose, ItemAnchor, PlayerPosition);
        _forceOrbs?.Update(_skillClockMs, ItemAnchor);
    }

    private (Vector3 Position, float Facing)? PetOwnerPose(ulong entityId) =>
        TryGetDrawnPosition(entityId, out var position) && TryGetDrawnFacing(entityId, out var facing) ? (position, facing) : null;

    private Vector3? ItemAnchor(ulong entityId) => TryGetDrawnPosition(entityId, out var position) ? position : null;

    // Where an entity is drawn: the local player's body (its collision position) or a remote entity's drawn position.
    private bool TryGetDrawnPosition(ulong entityId, out Vector3 position)
    {
        if (IsSelf(entityId))
        {
            position = _playerBody.Position;
            return true;
        }

        position = default;
        return _remote is not null && _remote.TryGetDrawnPosition(entityId, out position);
    }

    private bool TryGetDrawnFacing(ulong entityId, out float facing)
    {
        if (IsSelf(entityId))
        {
            facing = PlayerYaw;
            return true;
        }

        facing = 0f;
        return _remote is not null && _remote.TryGetDrawnFacing(entityId, out facing);
    }

    private bool IsSelf(ulong entityId) => _remote is not null && entityId == _remote.SelfEntityId && _playerBody is not null;
}
