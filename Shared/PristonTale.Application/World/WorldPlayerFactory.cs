using PristonTale.Application.Sessions;
using PristonTale.Persistence.Abstractions.Characters;

namespace PristonTale.Application.World;

public static class WorldPlayerFactory
{
    public static WorldPlayer Create(PlayerSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.Character is null || session.Loaded is null)
            throw new InvalidOperationException("session is not play-ready: call EnterWorld before building a WorldPlayer");

        var appearance = ToAppearance(session.Loaded);
        return new WorldPlayer(
            session.EntityId, session.SpawnPosition, session.Character, session.InVillage, appearance);
    }

    public static PlayerAppearanceView ToAppearance(CharacterRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return new PlayerAppearanceView(
            record.Name,
            record.JobCode,
            record.Tier,
            record.FaceIndex,
            record.HairIndex,
            record.ModelName,
            record.ModelName2);
    }
}
