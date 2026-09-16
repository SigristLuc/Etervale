using PristonTale.Simulation.Items;
using PristonTale.Simulation.Progression;

namespace PristonTale.Simulation.Characters;

public sealed record OwnedItemView(
    Guid InstanceId, uint Code, string Name, ItemKind Kind, int Count, int Anchor, int Slot, int Width, int Height, string CatalogKey = "");
public sealed record OwnedSkillView(uint Code, int Rank);

public sealed record OwnerState(
    int Level, long Experience, long LevelStart, long NextLevel,
    long Money, int Hp, int MaxHp, int Mp, int MaxMp, int Sp, int MaxSp,
    int StatPoints, int SkillPoints, int Strength, int Spirit, int Talent, int Agility, int Health,
    int DamageMin, int DamageMax, int Defence, int Absorption, long InventoryRevision,
    IReadOnlyList<OwnedItemView> Items)
{
    public float AttackRange { get; init; } = Combat.MeleeReach.PlayerBaseRange;
    public int AttackRating { get; init; }
    public int MoveSpeed { get; init; }
    public long ProgressionRevision { get; init; }
    public long SkillsRevision { get; init; }
    public IReadOnlyList<OwnedSkillView> Skills { get; init; } = Array.Empty<OwnedSkillView>();

    public static OwnerState Capture(PlayerCharacter character, OwnerState previous = null)
    {
        var attributes = character.Attributes;
        if (previous is not null && previous.Level == character.Level && previous.Experience == character.Experience
            && previous.Money == character.Money && previous.Hp == character.Life && previous.MaxHp == character.MaxLife
            && previous.Mp == character.Mana && previous.MaxMp == character.MaxMana
            && previous.Sp == character.Stamina && previous.MaxSp == character.MaxStamina
            && previous.StatPoints == character.StatPoints && previous.SkillPoints == character.SkillPoints
            && previous.Strength == attributes.Strength && previous.Spirit == attributes.Spirit && previous.Talent == attributes.Talent
            && previous.Agility == attributes.Agility && previous.Health == attributes.Health
            && previous.AttackRating == character.Derived.AttackRating && previous.MoveSpeed == character.Derived.MoveSpeed
            && previous.Defence == character.Derived.Defence && previous.Absorption == character.Derived.Absorption
            && previous.InventoryRevision == character.InventoryRevision
            && previous.ProgressionRevision == character.ProgressionRevision && previous.SkillsRevision == character.Skills.Revision)
        {
            return previous;
        }

        IReadOnlyList<OwnedItemView> items;
        if (previous is not null && previous.InventoryRevision == character.InventoryRevision)
        {
            items = previous.Items;
        }
        else
        {
            var captured = new List<OwnedItemView>();
            for (var anchor = 0; anchor < character.Inventory.Capacity; anchor++)
            {
                if (character.Inventory[anchor] is { } item)
                {
                    captured.Add(Project(item, anchor, -1));
                }
            }

            foreach (var pair in character.Equipment)
            {
                captured.Add(Project(pair.Value, -1, (int)pair.Key));
            }

            items = captured.AsReadOnly();
        }

        var damage = Combat.WeaponDamage.Compute(character);
        return new OwnerState(character.Level, character.Experience,
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
            Skills = previous is not null && previous.SkillsRevision == character.Skills.Revision ? previous.Skills
                : Array.AsReadOnly(character.Skills.Ranks.Select(pair => new OwnedSkillView(pair.Key, pair.Value)).ToArray())
        };
    }

    private static OwnedItemView Project(ItemInstance item, int anchor, int slot)
    {
        var definition = item.Definition;
        return new OwnedItemView(item.InstanceId, definition.Code, item.DisplayName, definition.Kind, item.Count,
            anchor, slot, definition.GridWidth, definition.GridHeight, definition.CatalogKey);
    }
}
