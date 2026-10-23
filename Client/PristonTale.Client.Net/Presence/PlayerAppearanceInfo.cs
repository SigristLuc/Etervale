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
    string WeaponKey = "", string ArmorKey = "", string OffHandKey = "", bool InVillage = false)
{
    // Premium looks: the helper pet (1 Terry, 2 Nepsys, 3 Io, 4 Mute), the Phoenix and the big head (1 or 2).
    public int HelperPet { get; init; }
    public bool Phoenix { get; init; }
    public int BigHead { get; init; }
}
