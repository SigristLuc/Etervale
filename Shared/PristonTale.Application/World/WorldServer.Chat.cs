namespace PristonTale.Application.World;

public sealed partial class WorldServer
{
    // Players closer than the range (squared planar world units) to the speaker. Fields share one coordinate space, so
    // players across a field seam hear each other as they see each other.
    public Task<uint[]> PlayersNearAsync(uint speakerId, float rangeSquared, bool includeSpeaker = false)
    {
        return EnqueueAsync(speakerId, () =>
        {
            if (!_connections.TryGetValue(speakerId, out var speaker))
            {
                return Array.Empty<uint>();
            }

            var origin = speaker.Player.Position;
            var near = new List<uint>();
            foreach (var connection in _connections.Values)
            {
                if (connection.Player.EntityId == speakerId && !includeSpeaker)
                {
                    continue;
                }

                var dx = connection.Player.Position.X - origin.X;
                var dz = connection.Player.Position.Z - origin.Z;
                if (dx * dx + dz * dz < rangeSquared)
                {
                    near.Add(connection.Player.EntityId);
                }
            }

            return near.ToArray();
        });
    }

    // The name of a monster, NPC or player anywhere in the world.
    public Task<string?> EntityNameAsync(uint requesterId, uint entityId)
    {
        return EnqueueAsync(requesterId, () =>
        {
            foreach (var field in _fields.Values)
            {
                if (field.TryGetEntityName(entityId, out var name))
                {
                    return name;
                }
            }

            return (string?)null;
        });
    }
}
