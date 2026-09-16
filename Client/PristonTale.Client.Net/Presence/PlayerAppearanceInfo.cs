namespace PristonTale.Client.Net.Presence;

public sealed record PlayerAppearanceInfo(
    string Name,
    int JobCode,
    int Tier,
    int FaceIndex,
    int HairIndex,
    string ModelName,
    string ModelName2,
    int Level,
    string WeaponKey = "", string ArmorKey = "", string OffHandKey = "", bool InVillage = false);
