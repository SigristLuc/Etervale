using PristonTale.Simulation.Items;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Simulation.Progression;

namespace PristonTale.Simulation.Characters;

public sealed record OwnedItemView(
    Guid InstanceId, uint Code, string Name, ItemKind Kind, int Count, int Anchor, int Slot, int Width, int Height, string CatalogKey = "")
{
    // The copy's values for tooltips and equipment rules; null until the owner receives them.
    public ItemDetailView Details { get; init; }

    // Where it is: the bag (Anchor), an equipment slot (Slot), or a potion belt box (Slot from PotionBelt.FirstOwnerSlot).
    public bool InBag => Slot < 0;
    public bool Equipped => Slot is >= 0 and < PotionBelt.FirstOwnerSlot;
    public int BeltBox => Slot >= PotionBelt.FirstOwnerSlot ? Slot - PotionBelt.FirstOwnerSlot : -1;
}
public sealed record OwnedSkillView(uint Code, int Rank);

public sealed record AgingProgressView(Guid InstanceId, int Count, int Goal);

public sealed record OwnerState(
    int Level, ulong Experience, ulong LevelStart, ulong NextLevel,
    long Money, int Life, int MaxLife, int Mana, int MaxMana, int Stamina, int MaxStamina,
    int StatPoints, int SkillPoints, int Strength, int Spirit, int Talent, int Agility, int Health,
    int DamageMin, int DamageMax, int Defence, int Absorption, long InventoryRevision,
    IReadOnlyList<OwnedItemView> Items)
{
    public float AttackRange { get; init; } = Combat.MeleeReach.PlayerBaseRange;
    public int AttackRating { get; init; }
    public int MoveSpeed { get; init; }
    public long ProgressionRevision { get; init; }
    public long SkillsRevision { get; init; }
    // The bag page shown and the weapon pair held (0 A, 1 B; PlayerCharacter.BagPage, WeaponSet).
    public int BagPage { get; init; }
    public int WeaponSet { get; init; }

    // Class tier 0..3 and the points the final tier's skills spend.
    public int Tier { get; init; }
    public int EliteSkillPoints { get; init; }
    public IReadOnlyList<OwnedSkillView> Skills { get; init; } = Array.Empty<OwnedSkillView>();

    // What the worn items add beyond the status values above.
    public int CarriedWeight { get; init; }
    public int MaxWeight { get; init; }
    public int CriticalChance { get; init; }
    public int BlockChance { get; init; }
    public int ShootingRange { get; init; }
    public int AttackSpeed { get; init; }
    public int PotionSpace { get; init; } = DerivedStats.DefaultPotionSpace;
    public float LifeRegen { get; init; }
    public float ManaRegen { get; init; }
    public float StaminaRegen { get; init; }
    public float MagicMastery { get; init; }
    public Combat.ResistanceBlock Resistances { get; init; }

    public bool Overweight => CarriedWeight > MaxWeight;

    // The running premium timers, their revision and the world clock (Unix seconds) they were captured at.
    public long PremiumRevision { get; init; } = -1;
    public IReadOnlyList<PremiumTimer> PremiumTimers { get; init; } = Array.Empty<PremiumTimer>();
    public long PremiumClockSeconds { get; init; }

    public long AgingRevision { get; init; } = -1;
    public IReadOnlyList<AgingProgressView> Aging { get; init; } = Array.Empty<AgingProgressView>();

    // Built in full and compared with the previous capture as a record: every member counts, and the lists are reused
    // while their revision holds, so an unchanged owner returns the previous instance (which the encoder skips).
    public static OwnerState Capture(PlayerCharacter character, OwnerState previous = null, long nowSeconds = 0)
    {
        var attributes = character.Attributes;
        var items = previous is not null && previous.InventoryRevision == character.InventoryRevision ? previous.Items : ProjectItems(character);
        var damage = Combat.WeaponDamage.Compute(character);
        var current = new OwnerState(character.Level, character.Experience,
            ExperienceTable.ForLevel(Math.Clamp(character.Level, 1, ExperienceTable.MaximumLevel)),
            ExperienceTable.ForLevel(Math.Clamp(character.Level + 1, 1, ExperienceTable.MaximumLevel)),
            character.Money, character.Life, character.MaxLife, character.Mana, character.MaxMana, character.Stamina, character.MaxStamina,
            character.StatPoints, character.SkillPoints, attributes.Strength, attributes.Spirit, attributes.Talent, attributes.Agility, attributes.Health,
            damage.Min, damage.Max, character.Derived.Defence, character.Derived.Absorption, character.InventoryRevision, items)
        {
            AttackRange = Combat.PlayerAttackReach.For(character),
            AttackRating = character.Derived.AttackRating,
            MoveSpeed = character.Derived.MoveSpeed,
            ProgressionRevision = character.ProgressionRevision,
            SkillsRevision = character.Skills.Revision,
            Tier = character.Tier,
            BagPage = character.BagPage,
            WeaponSet = character.WeaponSet,
            EliteSkillPoints = character.EliteSkillPoints,
            Skills = previous is not null && previous.SkillsRevision == character.Skills.Revision ? previous.Skills
                : Array.AsReadOnly(character.Skills.Ranks.Select(pair => new OwnedSkillView(pair.Key, pair.Value)).ToArray()),
            CarriedWeight = character.Derived.CarriedWeight,
            MaxWeight = character.Derived.MaxWeight,
            CriticalChance = Combat.CombatProfileFactory.CriticalChance(character),
            BlockChance = Combat.CombatProfileFactory.ForPlayerDefender(character).BlockChance,
            ShootingRange = character.Derived.ShootingRange,
            AttackSpeed = character.Derived.AttackSpeed,
            PotionSpace = character.Derived.PotionSpace,
            LifeRegen = character.Derived.LifeRegen,
            ManaRegen = character.Derived.ManaRegen,
            StaminaRegen = character.Derived.StaminaRegen,
            MagicMastery = character.Derived.MagicMastery,
            Resistances = Combat.CombatProfileFactory.Resistances(character),
            PremiumRevision = character.Premium.Revision,
            PremiumTimers = previous is not null && previous.PremiumRevision == character.Premium.Revision ? previous.PremiumTimers
                : Array.AsReadOnly(character.Premium.Timers.ToArray()),
            PremiumClockSeconds = previous is not null && previous.PremiumRevision == character.Premium.Revision ? previous.PremiumClockSeconds : nowSeconds,
            AgingRevision = character.AgingRevision,
            Aging = previous is not null && previous.AgingRevision == character.AgingRevision && previous.InventoryRevision == character.InventoryRevision
                ? previous.Aging
                : Array.AsReadOnly(character.Equipment.Values.Where(static item => item.Maturity is not null)
                    .Select(static item => new AgingProgressView(item.InstanceId, item.Maturity.Count, item.Maturity.Goal)).ToArray())
        };
        return current == previous ? previous : current;
    }

    private static IReadOnlyList<OwnedItemView> ProjectItems(PlayerCharacter character)
    {
        var captured = new List<OwnedItemView>();
        for (var anchor = 0; anchor < character.Inventory.Capacity; anchor++)
        {
            if (character.Inventory[anchor] is { } item)
            {
                captured.Add(Project(item, anchor, -1, character));
            }
        }

        foreach (var pair in character.Equipment)
        {
            captured.Add(Project(pair.Value, -1, (int)pair.Key, character));
        }

        for (var box = 0; box < PotionBelt.Boxes; box++)
        {
            if (character.Belt[box] is { } potion)
            {
                captured.Add(Project(potion, -1, PotionBelt.FirstOwnerSlot + box, character));
            }
        }

        return captured.AsReadOnly();
    }

    private static OwnedItemView Project(ItemInstance item, int anchor, int slot, PlayerCharacter owner)
    {
        var definition = item.Definition;
        if (item.Sealed)
        {
            // A sealed puzzle piece shows as the lucky bag until it is revealed.
            return new OwnedItemView(item.InstanceId, ItemCodes.Parse(Puzzles.LuckyBagIcon), Puzzles.LuckyBagName, definition.Kind, item.Count,
                anchor, slot, definition.GridWidth, definition.GridHeight, Puzzles.LuckyBagIcon)
            {
                Details = ItemDetailView.Of(item, owner)
            };
        }

        return new OwnedItemView(item.InstanceId, definition.Code, item.DisplayName, definition.Kind, item.Count,
            anchor, slot, definition.GridWidth, definition.GridHeight, definition.CatalogKey)
        {
            Details = ItemDetailView.Of(item, owner)
        };
    }
}
