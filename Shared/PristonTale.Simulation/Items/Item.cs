using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Combat;

namespace PristonTale.Simulation.Items;

public enum ItemKind
{
    Sword, Axe, Hammer, Claw, Pole, Bow, ThrowingWeapon, Staff,
    Armor, Shield, Gloves, Boots,
    Amulet, Bracelet, Ring, ForceOrb, Sheltom,
    Potion, Material, Quest, Misc,
    // Keep new weapon values appended: ItemKind is persisted as an integer in world-data rows.
    Dagger, Phantom
}

public readonly record struct ItemStatBlock(
    int DamageMin,
    int DamageMax,
    int Defence,
    int AttackRating,
    int AttackSpeed,
    int Strength,
    int Spirit,
    int Talent,
    int Agility,
    int Health)
{
    public static readonly ItemStatBlock Zero = default;

    public ItemStatBlock Add(in ItemStatBlock o) => new(
        DamageMin + o.DamageMin,
        DamageMax + o.DamageMax,
        Defence + o.Defence,
        AttackRating + o.AttackRating,
        AttackSpeed + o.AttackSpeed,
        Strength + o.Strength,
        Spirit + o.Spirit,
        Talent + o.Talent,
        Agility + o.Agility,
        Health + o.Health);
}

public sealed record ItemDefinition(
    uint Code,
    string Name,
    ItemKind Kind,
    EquipmentSlot? EquipSlot = null,
    bool TwoHanded = false,
    int GridWidth = 1,
    int GridHeight = 1,
    int MaxStack = 1)
{
    public string CatalogKey { get; init; } = "";
    public PotionDefinition Potion { get; init; }
    public int ShootingRange { get; init; }
    public uint SpecializationJobs { get; init; }
    public int RequiredLevel { get; init; }
    public CharacterAttributes Requirements { get; init; } = CharacterAttributes.Empty;

    public uint AllowedJobs { get; init; }

    public ItemStatBlock BaseStats { get; init; } = ItemStatBlock.Zero;

    public int MaxDurability { get; init; }
    public int Weight { get; init; }
    public int Price { get; init; }

    public ItemStatBlock RolledStatsMin { get; init; } = ItemStatBlock.Zero;

    public ItemStatBlock RolledStatsMax { get; init; } = ItemStatBlock.Zero;

    public ResistanceBlock Resistances { get; init; } = ResistanceBlock.Zero;

    public uint JobRandomSpecMask { get; init; }

    public IReadOnlyList<ItemSpecialBonus> SpecialBonuses { get; init; } = [];

    public bool IsEquippable => EquipSlot.HasValue;
    public bool IsStackable => MaxStack > 1;

    public bool IsWeapon => Kind is ItemKind.Sword or ItemKind.Axe or ItemKind.Hammer
        or ItemKind.Claw or ItemKind.Pole or ItemKind.Bow or ItemKind.ThrowingWeapon or ItemKind.Staff
        or ItemKind.Dagger or ItemKind.Phantom;
}

public sealed record ItemSpecialBonus(int BonusCode, int Value);

public sealed class ItemInstance
{
    public ItemInstance(ItemDefinition definition, int count = 1)
    {
        Definition = definition;
        Count = count;
        JobSpecMask = definition.SpecializationJobs;
        DurabilityMax = definition.MaxDurability;
        DurabilityCurrent = definition.MaxDurability;
    }

    public Guid InstanceId { get; init; } = Guid.NewGuid();
    public ItemDefinition Definition { get; }

    public ItemInstance Copy(int count, bool preserveId = false)
    {
        return new ItemInstance(Definition, count)
        {
            InstanceId = preserveId ? InstanceId : Guid.NewGuid(),
            UpgradeLevel = UpgradeLevel,
            DurabilityCurrent = DurabilityCurrent,
            DurabilityMax = DurabilityMax,
            JobSpecMask = JobSpecMask,
            RolledBonus = RolledBonus
        };
    }
    public int Count { get; set; }

    public int UpgradeLevel { get; set; }
    public int DurabilityCurrent { get; set; }
    public int DurabilityMax { get; set; }

    public uint JobSpecMask { get; set; }

    public ItemStatBlock RolledBonus { get; set; } = ItemStatBlock.Zero;

    public string DisplayName => UpgradeLevel > 0 ? $"+{UpgradeLevel} {Definition.Name}" : Definition.Name;

    public bool IsBroken => DurabilityMax > 0 && DurabilityCurrent <= 0;

    public ItemStatBlock TotalStats =>
        IsBroken ? ItemStatBlock.Zero : Definition.BaseStats.Add(RolledBonus);
}
