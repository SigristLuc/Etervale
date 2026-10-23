using System.Text.Json;
using PristonTale.Application.Sessions;
using PristonTale.Application.World;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Persistence.Abstractions.Warehouses;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Application.Services;

// Typed NPC services outside the tick: the world validates and prepares, storage is written here, then the world commits.
// Every request holds the character's save gate, so saves and other economic operations never interleave with it.
public sealed class NpcAppService(
    ISessionRegistry sessions,
    ICharacterRepository characters,
    IWarehouseRepository warehouses,
    WorldServer world,
    WorldDataHolder worldData)
{
    public Task<NpcInteraction> InteractAsync(string sessionToken, uint npcId)
    {
        if (!sessions.TryGet(sessionToken, out var session))
        {
            return Task.FromResult(NpcInteraction.Rejected("invalid session"));
        }

        return world.InteractNpcAsync(session, npcId);
    }

    public async Task<NpcServiceReply> RequestAsync(string sessionToken, NpcServiceCommand command, CancellationToken ct = default)
    {
        if (!sessions.TryGet(sessionToken, out var session))
        {
            return Failed(NpcServiceStatus.Rejected, "invalid session");
        }

        if (!await session.SaveGate.WaitAsync(0, ct))
        {
            return Failed(NpcServiceStatus.Busy, "save or character operation pending; retry");
        }

        try
        {
            if (command.Service == NpcServiceKind.Warehouse && command.Action == NpcServiceAction.Open)
            {
                var opened = await OpenWarehouseAsync(session, command, ct);
                if (opened is not null)
                {
                    return opened;
                }
            }

            var prepared = await world.PrepareNpcServiceAsync(session, command);
            if (prepared is null)
            {
                return Failed(NpcServiceStatus.Rejected, "character is not playing");
            }

            if (prepared.Snapshot is null)
            {
                return prepared.Reply;
            }

            // Finish an admitted transaction despite cancellation. Retrying its operation id recovers the outcome.
            var record = prepared.Snapshot.Encode();
            var stored = prepared.Warehouse is null
                ? await characters.UpdateAsync(record, CancellationToken.None)
                : await warehouses.SaveWithCharacterAsync(record, WarehouseStateCodec.Capture(session.AccountId, prepared.Warehouse),
                    prepared.ExpectedWarehouseRevision, CancellationToken.None);
            if (!stored)
            {
                if (prepared.Warehouse is not null)
                {
                    session.Warehouse = null;
                    return Failed(NpcServiceStatus.Conflict, "the warehouse changed elsewhere; open it again");
                }

                return Failed(NpcServiceStatus.StorageFailed, "storage rejected operation; character unchanged");
            }

            await world.CommitNpcServiceAsync(session, prepared);
            return prepared.Reply;
        }
        finally
        {
            session.SaveGate.Release();
        }
    }

    // The warehouse is read from storage on every open, after the NPC and its reach are confirmed.
    private async Task<NpcServiceReply?> OpenWarehouseAsync(PlayerSession session, NpcServiceCommand command, CancellationToken ct)
    {
        var check = await world.CheckNpcServiceAsync(session, command);
        if (!check.Ok)
        {
            return new NpcServiceReply(check);
        }

        try
        {
            var record = await warehouses.FindAsync(session.AccountId, ct);
            session.Warehouse = WarehouseStateCodec.Restore(record, worldData.Current.Items);
            return null;
        }
        catch (Exception exception) when (exception is InvalidDataException or JsonException or ArgumentException)
        {
            session.Warehouse = null;
            return Failed(NpcServiceStatus.StorageFailed, "the saved warehouse cannot be read");
        }
    }

    private static NpcServiceReply Failed(NpcServiceStatus status, string message) => new(NpcServiceOutcome.Failed(status, message));
}
