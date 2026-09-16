namespace PristonTale.Simulation.Characters;

public enum PlayerClass
{
    Fighter = 1,
    Mechanician = 2,
    Archer = 3,
    Pikeman = 4,
    Atalanta = 5,
    Knight = 6,
    Magician = 7,
    Priestess = 8,
    Assassin = 9,
    Shaman = 10
}

public enum CharacterBrood
{
    Tempskron = 1,
    Morion = 2,
    Sophetio = 4
}

[Flags]
public enum JobBitCode : uint
{
    Mechanician = 0x00000001,
    Fighter = 0x00000002,
    Pikeman = 0x00000004,
    Archer = 0x00000008,
    Knight = 0x00010000,
    Atalanta = 0x00020000,
    Priestess = 0x00040000,
    Magician = 0x00080000,
    Assassin = 0x00000010,
    Shaman = 0x00100000
}

public enum CharacterMotionName
{
    Stand,
    Walk,
    Run,
    Attack,
    Damage,
    Dead,
    Skill
}

// Persisted sInven order has no helmet slot and keeps Sheltom separate.
public enum EquipmentSlot
{
    Weapon,
    OffHand,
    Armor,
    Gloves,
    Boots,
    Bracelet,
    Amulet,
    RingLeft,
    RingRight,
    Sheltom
}
