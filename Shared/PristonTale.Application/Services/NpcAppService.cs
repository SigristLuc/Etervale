using System.Text.Json;
using PristonTale.Application.Sessions;
using PristonTale.Application.World;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Persistence.Abstractions.Warehouses;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Application.Services;

// Typed NPC services outside the tick: the world validates and prepares, storage is written here, then the world commits
// (DurableOperations).
public sealed class NpcAppService(
    ISessionRegistry sessions,
    ICharacterRepository characters,
    IWarehouseRepository warehouses,
    WorldServer world,
    WorldDataHolder worldData)
{
    private readonly DurableOperations _durable = new(characters);

    public Task<NpcInteraction> InteractAsync(string sessionToken, ulong npcId)
    {
        if (!sessions.TryGet(sessionToken, out var session))
        {
            return Task.FromResult(NpcInteraction.Rejected("invalid session"));
        }

        return world.InteractNpcAsync(session, npcId);
    }

    public async Task<NpcServiceReply> RequestAsync(string sessionToken, NpcServiceCommand command, CancellationToken ct = default)
    {
        using var lease = await _durable.AdmitAsync(sessions, sessionToken, ct);
        if (lease.Failure is null && command.Service == NpcServiceKind.Warehouse && command.Action == NpcServiceAction.Open
            && await OpenWarehouseAsync(lease.Session, command, ct) is { } opened)
        {
            return opened;
        }

        return await lease.RunAsync(
            session => world.PrepareNpcServiceAsync(session, command),
            world.CommitNpcServiceAsync,
            prepared => prepared.Reply,
            failure => failure switch
            {
                DurableFailure.NoSession => Failed(NpcServiceStatus.Rejected, "invalid session"),
                DurableFailure.Busy => Failed(NpcServiceStatus.Busy, "save or character operation pending; retry"),
                DurableFailure.StorageFailed => Failed(NpcServiceStatus.StorageFailed, "storage rejected operation; character unchanged"),
                DurableFailure.Conflict => Failed(NpcServiceStatus.Conflict, "the warehouse changed elsewhere; open it again"),
                _ => Failed(NpcServiceStatus.Rejected, "character is not playing")
            },
            StoreAsync);
    }

    // The character, and the warehouse with it when the service changed it. A warehouse stored elsewhere since this
    // session read it is a conflict, and the session drops its copy.
    private async Task<DurableFailure?> StoreAsync(PlayerSession session, PreparedNpcOperation prepared)
    {
        var record = prepared.Snapshot!.Encode();
        if (prepared.Warehouse is null)
        {
            return await characters.UpdateAsync(record, CancellationToken.None) ? null : DurableFailure.StorageFailed;
        }

        if (await warehouses.SaveWithCharacterAsync(record, WarehouseStateCodec.Capture(session.AccountId, prepared.Warehouse),
                prepared.ExpectedWarehouseRevision, CancellationToken.None))
        {
            return null;
        }

        session.Warehouse = null;
        return DurableFailure.Conflict;
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
