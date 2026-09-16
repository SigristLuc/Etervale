namespace PristonTale.Application.Services;

public sealed record CharacterSummary(
    string Name,
    int ClassId,
    int Level,
    int FaceIndex,
    int Tier = 0,
    int HairIndex = 0,
    string ArmorKey = "",
    string WeaponKey = "",
    string OffHandKey = "",
    bool InVillage = true);
