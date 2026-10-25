using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;

namespace PristonTale.Application.World;

// An item the player throws on the ground: the bag without it is stored first, then the commit places it in the field.
public sealed record PreparedDropOperation(
    PlayerCharacter Original,
    PlayerCharacter Candidate,
    CharacterSaveSnapshot Snapshot,
    InventoryOutcome Outcome,
    ItemInstance Item,
    int FieldId)
    : PreparedCharacterOperation<InventoryOutcome>(Original, Candidate, Snapshot, Outcome);
