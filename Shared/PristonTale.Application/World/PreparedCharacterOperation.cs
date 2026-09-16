using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Simulation.Characters;

namespace PristonTale.Application.World;

public record PreparedCharacterOperation<TOutcome>(
    PlayerCharacter Original,
    PlayerCharacter? Candidate,
    CharacterSaveSnapshot? Snapshot,
    TOutcome Outcome);
