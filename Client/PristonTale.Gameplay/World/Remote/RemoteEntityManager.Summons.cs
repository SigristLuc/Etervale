using PristonTale.Client.Net.Presence;

namespace PristonTale.Gameplay.World.Remote;

// The name plates of monsters fighting for players (skill summons and crystal pets): their name, their life for the
// owner's own, and the owner's name for everyone else's.
public sealed partial class RemoteEntityManager
{
    private readonly Dictionary<ulong, SummonPlate> _summonPlates = new();

    public sealed class SummonPlate
    {
        public string Name { get; init; } = "";
        public int Life { get; set; }
        public int MaxLife { get; init; }
    }

    public IEnumerable<(ulong EntityId, ulong OwnerId, SummonPlate Plate)> SummonPlates =>
        _summonPlates.Select(pair => (pair.Key, _summonOwners.GetValueOrDefault(pair.Key), pair.Value));

    // The name of a player in view, or an empty string.
    public string PlayerName(ulong entityId) => _playerAppearances.TryGetValue(entityId, out var appearance) ? appearance.Name : "";

    private void TrackSummonPlate(EntitySpawnInfo spawn, string name)
    {
        _summonPlates[spawn.EntityId] = new SummonPlate { Name = string.IsNullOrEmpty(spawn.Name) ? name : spawn.Name, Life = spawn.Life, MaxLife = spawn.MaxLife };
    }

    private void UpdateSummonPlate(ulong entityId, int life)
    {
        if (_summonPlates.TryGetValue(entityId, out var plate))
        {
            plate.Life = life;
        }
    }
}
