using Microsoft.Xna.Framework;
using PristonTale.Application.World;
using PristonTale.Simulation.World;

namespace PristonTale.Gameplay.World;

// The wing warp stones (WingGates): stepping on one holds the player on it and opens the wing warp map; walking off it
// closes the map, and the map opens again only after the player left the stone. As in the original, a chosen trip shows
// the return sparkle and plays the warp sound on the stone, and the trip itself leaves two seconds later.
public sealed partial class GameScene
{
    private const int WingTripDelayMs = 2000;
    private FieldWarpGate _wingStone;

    // The player stepped on a stone of this field; the screen opens the wing warp map.
    public event Action<int> WingGateEntered;

    // The player left the stone; the screen closes the map.
    public event Action WingGateLeft;

    public int FieldLimitLevel(int fieldCode) => _worldCatalog is not null && _worldCatalog.TryGetFieldByCode(fieldCode, out var field) ? field.LimitLevel : 0;

    public async Task<WingGateOutcome> TravelWingGateAsync(int destination)
    {
        if (_playChannel is null)
        {
            return new WingGateOutcome(WingGateStatus.Rejected, destination);
        }

        if (_playerBody is not null)
        {
            _returnEffect?.Start(_playerBody.Position + Vector3.Up * GroundTraversal.VisualOffset);
            _fieldAudio?.PlayPortalWarp();
        }

        await Task.Delay(WingTripDelayMs, _presenceCts.Token);
        return await _playChannel.TravelWingGateAsync(destination, _presenceCts.Token);
    }

    private void UpdateWingStone()
    {
        if (_activeField is null || _playerBody is null)
        {
            return;
        }

        FieldWarpGate standing = null;
        foreach (var warp in _activeField.Definition.WarpGates)
        {
            if (WingGates.IsStone(warp) && OnGate(warp))
            {
                standing = warp;
                break;
            }
        }

        if (standing is null)
        {
            if (_wingStone is not null)
            {
                _wingStone = null;
                WingGateLeft?.Invoke();
            }

            return;
        }

        if (_wingStone is not null || PlayerDead || _player is not null && _player.Level < standing.LimitLevel)
        {
            return;
        }

        _wingStone = standing;
        ClearAttackTarget();
        _lootInteraction?.Cancel();
        StopPredictedMovement();
        HoldAtSpecialWarp(standing);
        _reportDue = true;
        WingGateEntered?.Invoke(_activeField.Definition.FieldCode);
    }
}
