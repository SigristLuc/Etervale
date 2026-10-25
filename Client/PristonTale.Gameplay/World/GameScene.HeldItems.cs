using PristonTale.Client.Net.Presence;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.Effects;
using PristonTale.Gameplay.World.Remote;
using PristonTale.Simulation.Items;

namespace PristonTale.Gameplay.World;

// The glow of every drawn player's held weapon and off-hand item (HeldItemGlow, from the server's description of the
// player): each frame the pulse is painted over the item's skill look, and a weapon with the class-up setting carries
// its circling lights (ClassUpLights) while its holder is alive.
public sealed partial class GameScene
{
    private readonly ClassUpLights _classUpLights = new();
    private HeldItemGlow _ownWeaponGlow = HeldItemGlow.None;
    private HeldItemGlow _ownOffHandGlow = HeldItemGlow.None;
    private Action<ulong, PlayerActorHandles, PlayerAppearanceInfo, bool> _holdRemote;
    private long _heldClockMs;
    private float _heldSeconds;

    // The local player's glows arrive with its own description.
    private void ObserveOwnGlow(PresenceUpdate update)
    {
        if (_remote is null)
        {
            return;
        }

        foreach (var spawn in update.Spawns)
        {
            if (spawn.EntityId == _remote.SelfEntityId && spawn.Player is { } appearance)
            {
                _ownWeaponGlow = appearance.WeaponGlow;
                _ownOffHandGlow = appearance.OffHandGlow;
            }
        }
    }

    private void UpdateHeldItems(float seconds)
    {
        if (_looks is null || _skillFx is null || _remote is null)
        {
            return;
        }

        _heldClockMs = (long)_skillClockMs;
        _heldSeconds = seconds;
        if (_equipmentPresentation is not null && _playerBody is not null)
        {
            Hold(_remote.SelfEntityId, _equipmentPresentation.Handles, _ownWeaponGlow, _ownOffHandGlow, _equipmentPresentation.WeaponKey,
                !_playerController.IsDead);
        }

        _remote.ForEachHolder(_holdRemote ??= (entityId, handles, appearance, shown) =>
            Hold(entityId, handles, appearance.WeaponGlow, appearance.OffHandGlow, appearance.WeaponKey, shown));
        UpdateShots(seconds);
        _looks.EndGlowFrame();
        _classUpLights.EndFrame();
    }

    private void Hold(ulong entityId, PlayerActorHandles handles, in HeldItemGlow weapon, in HeldItemGlow offHand, string weaponKey, bool shown)
    {
        _looks.PaintGlow(handles.Weapon, weapon, _heldClockMs);
        _looks.PaintGlow(handles.OffHand, offHand, _heldClockMs);

        if (shown && weapon.ClassUp && weaponKey.Length > 0 && _renderer.TryGetActorWorld(handles.Weapon, out var world)
            && _renderer.TryGetActorReach(handles.Weapon, out var reach))
        {
            _classUpLights.Hold(_skillFx, entityId, world, reach, weaponKey, EquipmentVisuals.HeldInLeftHand(weaponKey),
                ItemEquipmentMetadata.IsTwoHanded(weaponKey), _heldSeconds);
        }
    }
}
