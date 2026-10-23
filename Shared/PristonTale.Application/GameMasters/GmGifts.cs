using PristonTale.Application.Chat;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.World.Ai;

namespace PristonTale.Application.GameMasters;

// A game master's gift into a player's bag follows the durable path of every inventory change: the player's save gate,
// the copy made and checked on the tick, the character written to storage, then the commit on the tick.
internal static class GmGifts
{
    public static async Task<bool> GiveAsync(GameMasterCommands commands, ChatMember issuer, ChatMember target, Func<ISimRng, ItemInstance> make)
    {
        if (commands.Characters is null)
        {
            return false;
        }

        var session = target.Session;
        if (!await session.SaveGate.WaitAsync(0))
        {
            return false;
        }

        try
        {
            var prepared = await commands.World.PrepareGiftAsync(issuer.EntityId, session, make);
            if (prepared?.Snapshot is null || !prepared.Outcome)
            {
                return false;
            }

            if (!await commands.Characters.UpdateAsync(prepared.Snapshot.Encode(), CancellationToken.None))
            {
                return false;
            }

            return await commands.World.CommitGiftAsync(session, prepared);
        }
        finally
        {
            session.SaveGate.Release();
        }
    }
}
