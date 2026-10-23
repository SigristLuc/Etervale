using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Application.World;

// A validated NPC service request. A null Candidate means nothing is written and Reply is final; otherwise the snapshot
// (and the warehouse, when set) are stored before the commit applies the candidate, the gold change and the arrival.
public sealed record PreparedNpcOperation(
    PlayerCharacter Original,
    PlayerCharacter? Candidate,
    CharacterSaveSnapshot? Snapshot,
    NpcServiceReply Reply,
    long MoneyDelta = 0,
    WarehouseStorage? Warehouse = null,
    long ExpectedWarehouseRevision = 0);
