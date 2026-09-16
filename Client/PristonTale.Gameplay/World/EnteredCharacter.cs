using PristonTale.Simulation.Characters;

namespace PristonTale.Gameplay.World;

public sealed record EnteredCharacter(
    PlayerClass Class,
    string Name,
    int Level,
    int FaceIndex,
    int FieldCode,
    float SpawnX,
    float SpawnZ,
    int Tier = 0,
    int HairIndex = 0);
