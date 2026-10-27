using PristonTale.Persistence.Abstractions.Characters;

namespace PristonTale.Application.World;

// Save: the character as it left, captured in the leaving tick; null when the session had nothing loaded.
public readonly record struct WorldLeave(bool Left, CharacterSaveSnapshot? Save);
