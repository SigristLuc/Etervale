using PristonTale.Application.Chat;
using PristonTale.Application.Sessions;
using PristonTale.Application.World;
using PristonTale.Contracts.Social;

namespace PristonTale.Application.Services;

// Party requests outside the tick: each runs on the world, and the lines it answers reach their players through the
// chat. A full command queue drops the request, as it drops a spoken line.
public sealed class PartyAppService(ISessionRegistry sessions, WorldServer world, ChatDirectory chat)
{
    public Task InviteAsync(string sessionToken, ulong targetId) => RunAsync(sessionToken, session => world.InviteToPartyAsync(session, targetId));

    public Task AnswerAsync(string sessionToken, ulong fromId, bool accept) =>
        RunAsync(sessionToken, session => world.AnswerPartyAsync(session, fromId, accept));

    public Task CommandAsync(string sessionToken, PartyCommand command, ulong memberId) =>
        RunAsync(sessionToken, session => world.CommandPartyAsync(session, command, memberId));

    private async Task RunAsync(string sessionToken, Func<PlayerSession, Task<PartyNotice[]>> action)
    {
        if (!sessions.TryGet(sessionToken, out var session))
        {
            return;
        }

        try
        {
            chat.Deliver(await action(session));
        }
        catch (WorldBusyException)
        {
        }
    }
}
