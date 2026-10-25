using PristonTale.Application.World;
using PristonTale.Simulation.World;

namespace PristonTale.Gameplay.World;

// The wing warp stones (WingGates): stepping on one holds the player on it and opens the wing warp map; walking off it
// closes the map, and the map opens again only after the player left the stone. A trip the server accepts lands with a
// forced FieldWarp, where the return sparkle and the warp sound play.
public sealed partial class GameScene
{
    private FieldWarpGate _wingStone;
    private volatile bool _wingTripLanding;

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

        _wingTripLanding = true;
        var outcome = await _playChannel.TravelWingGateAsync(destination, _presenceCts.Token);
        if (!outcome.Ok)
        {
            _wingTripLanding = false;
        }

        return outcome;
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
