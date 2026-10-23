using PristonTale.Application.Sessions;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Application.World;

public sealed partial class WorldServer
{
    // Touched only by the tick thread.
    private readonly Random _dialogRandom = new();

    // A click answers with the NPC's typed services and one of its lines, chosen at random as the original does.
    public Task<NpcInteraction> InteractNpcAsync(PlayerSession session, uint npcId)
    {
        return EnqueueAsync(session.EntityId, () =>
        {
            if (!TryGetPlaying(session, out var connection) || !_fields.TryGetValue(connection.FieldId, out var field)
                || !field.TryGetNpc(npcId, out var npc) || !NpcServiceTransactions.CanReach(connection.Player, npc))
            {
                return NpcInteraction.Rejected("approach this NPC while alive");
            }

            var definition = npc.Definition;
            var offers = definition.Services
                .Select(service => new NpcServiceOffer(service.Kind, service.Value, NpcServiceAvailability.IsAvailable(service)))
                .ToArray();
            string[] line = definition.Dialog.Count == 0 ? [] : [definition.Dialog[_dialogRandom.Next(definition.Dialog.Count)]];
            return new NpcInteraction(true, npc.EntityId, definition.Label, offers, line, "");
        });
    }

    public Task<NpcServiceOutcome> CheckNpcServiceAsync(PlayerSession session, NpcServiceCommand command)
    {
        return EnqueueAsync(session.EntityId, () =>
        {
            if (!TryGetPlaying(session, out var connection) || !_fields.TryGetValue(connection.FieldId, out var field))
            {
                return NpcServiceOutcome.Failed(NpcServiceStatus.Rejected, "character is not playing");
            }

            return NpcServiceTransactions.Check(connection.Player, field, command, out _, out _);
        });
    }

    public Task<PreparedNpcOperation?> PrepareNpcServiceAsync(PlayerSession session, NpcServiceCommand command)
    {
        return EnqueueAsync<PreparedNpcOperation?>(session.EntityId, () =>
        {
            if (session.Loaded is null || !TryGetPlaying(session, out var connection) || !_fields.TryGetValue(connection.FieldId, out var field))
            {
                return null;
            }

            return NpcServiceTransactions.Prepare(session, connection.Player, field, command, _worldData.Current.Items, _catalog);
        });
    }

    public Task<bool> CommitNpcServiceAsync(PlayerSession session, PreparedNpcOperation operation)
    {
        return EnqueueAsync(NoOwner, () =>
        {
            // SaveGate remains held through commit, including when the connection leaves during storage I/O.
            if (!ReferenceEquals(session.Character, operation.Original) || operation.Candidate is null
                || !operation.Original.CanChangeMoney(operation.MoneyDelta))
            {
                throw new InvalidOperationException("Character ownership changed during NPC service commit.");
            }

            operation.Original.CommitInventoryOperation(operation.Candidate);
            operation.Original.TryChangeMoney(operation.MoneyDelta);
            if (operation.Warehouse is not null)
            {
                session.Warehouse = operation.Warehouse;
            }

            if (operation.Reply.Arrival is { } arrival)
            {
                Arrive(session, arrival);
            }

            return true;
        }, waitForCapacity: true);
    }

    private void Arrive(PlayerSession session, TeleportArrival arrival)
    {
        if (!_connections.TryGetValue(session.EntityId, out var connection) || !ReferenceEquals(connection.Session, session)
            || !_catalog.TryGetFieldByCode(arrival.FieldCode, out var destination))
        {
            session.FieldId = arrival.FieldCode;
            session.SpawnPosition = arrival.Position;
            return;
        }

        var player = connection.Player;
        var oldNowMs = _fields[connection.FieldId].NowMs;
        _fields[connection.FieldId].Remove(player.EntityId);
        var target = GetField(arrival.FieldCode);
        player.Cooldowns.Rebase(oldNowMs, target.NowMs);
        var position = target.Terrain.PlaceOnFloor(arrival.Position);
        var inVillage = destination.State == FieldState.Village;
        player.EnterField(position, inVillage, position);
        target.Add(player);
        _membershipChanged = true;
        _connections[player.EntityId] = connection with
        {
            FieldId = arrival.FieldCode
        };
        UpdatePlacement(session, arrival.FieldCode, position, inVillage);
    }

    private bool TryGetPlaying(PlayerSession session, out Connection connection)
    {
        if (session.IsPlaying && session.Character is not null && _connections.TryGetValue(session.EntityId, out var found)
            && ReferenceEquals(found.Session, session))
        {
            connection = found;
            return true;
        }

        connection = null!;
        return false;
    }
}
