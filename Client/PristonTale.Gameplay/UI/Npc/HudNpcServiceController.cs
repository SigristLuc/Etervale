using PristonTale.Engine3D.Diagnostics;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Gameplay.UI.Npc;

// The client side of the typed NPC services: one request in flight at a time, polled from Update so the frame never
// waits on the network. Opened windows show the state that came with the last reply.
public sealed class HudNpcServiceController(Func<NpcServiceCommand, Task<NpcServiceReply>> request, IClientLog log)
{
    private Task<NpcServiceReply> _pending;
    private NpcServiceCommand _pendingCommand;

    public ulong NpcId { get; private set; }
    public string NpcName { get; private set; } = "";
    public NpcServiceKind? Open { get; private set; }
    public IReadOnlyList<ShopOffer> Catalog { get; private set; } = [];
    public ShopQuotes Quotes { get; private set; }
    public WarehouseView Warehouse { get; private set; }
    public IReadOnlyList<TeleportOffer> Destinations { get; private set; } = [];
    public bool Pending => _pending is not null;
    public string Message { get; private set; } = "";

    public bool LastOk { get; private set; }
    public int Completions { get; private set; }
    public int Opened { get; private set; }

    // Every service request the server carried out, for its sound.
    public Action<NpcServiceCommand> Succeeded { get; set; }

    public bool Request(ulong npcId, string npcName, NpcServiceKind service, NpcServiceAction action, long expectedRevision = 0,
        uint itemCode = 0, Guid itemId = default, int anchor = -1, int count = 1, long amount = 0)
    {
        // NPC serial 0 names the mobile shop a used item opens; every other service needs an NPC.
        if (_pending is not null || request is null || npcId == 0 && service != NpcServiceKind.Shop)
        {
            return false;
        }

        var storageRevision = Warehouse?.Revision ?? 0;
        var command = new NpcServiceCommand(Guid.NewGuid(), npcId, service, action, expectedRevision, storageRevision, itemCode, itemId, anchor, count,
            amount);
        if (action == NpcServiceAction.Open)
        {
            NpcName = npcName;
        }

        _pendingCommand = command;
        _pending = SendAsync(command);
        return true;
    }

    public void Close()
    {
        if (Open is { } service && _pending is null)
        {
            Request(NpcId, NpcName, service, NpcServiceAction.Close);
        }

        Open = null;
        Catalog = [];
        Quotes = null;
        Warehouse = null;
        Destinations = [];
    }

    public void Update()
    {
        if (_pending is null || !_pending.IsCompleted)
        {
            return;
        }

        var reply = _pending.Result;
        var command = _pendingCommand;
        _pending = null;
        Apply(command, reply);
    }

    private void Apply(NpcServiceCommand command, NpcServiceReply reply)
    {
        var outcome = reply.Outcome;
        if (command.Action == NpcServiceAction.Close)
        {
            return;
        }

        Message = outcome.Ok ? Describe(command, outcome) : outcome.Message;
        LastOk = outcome.Ok;
        Completions++;
        if (outcome.Ok)
        {
            Succeeded?.Invoke(command);
        }
        if (!outcome.Ok)
        {
            if (outcome.Status is NpcServiceStatus.OutOfReach or NpcServiceStatus.NotOpen or NpcServiceStatus.Conflict)
            {
                Open = null;
            }

            return;
        }

        if (command.Action == NpcServiceAction.Open)
        {
            NpcId = command.NpcEntityId;
            Open = command.Service;
            Catalog = reply.Catalog ?? [];
            Destinations = reply.Destinations ?? [];
            Opened++;
        }

        Quotes = reply.Quotes ?? Quotes;
        Warehouse = reply.Warehouse ?? Warehouse;
        if (command.Action == NpcServiceAction.Teleport)
        {
            Open = null;
        }
    }

    private static string Describe(NpcServiceCommand command, NpcServiceOutcome outcome) => command.Action switch
    {
        NpcServiceAction.Buy => "Bought.",
        NpcServiceAction.Sell => "Sold.",
        NpcServiceAction.Repair or NpcServiceAction.RepairAll => "Repaired.",
        NpcServiceAction.Teleport => "Teleporting...",
        _ => outcome.Message == "committed" ? "" : outcome.Message
    };

    public ShopQuote QuoteFor(Guid itemId)
    {
        if (Quotes is null)
        {
            return null;
        }

        foreach (var quote in Quotes.Items)
        {
            if (quote.ItemId == itemId)
            {
                return quote;
            }
        }

        return null;
    }

    // Quotes are refreshed after any inventory change made outside the shop while it is open.
    public void RefreshQuotes(OwnerState state)
    {
        if (Open == NpcServiceKind.Shop && state is not null && Quotes is not null && Quotes.InventoryRevision != state.InventoryRevision)
        {
            Request(NpcId, NpcName, NpcServiceKind.Shop, NpcServiceAction.Refresh);
        }
    }

    private async Task<NpcServiceReply> SendAsync(NpcServiceCommand command)
    {
        try
        {
            return await request(command).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            log.Error("NPC service request failed", exception);
            return new NpcServiceReply(NpcServiceOutcome.Failed(NpcServiceStatus.Rejected, "Connection error. Please try again."));
        }
    }
}
