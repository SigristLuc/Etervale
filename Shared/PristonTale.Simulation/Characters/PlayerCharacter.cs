using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Simulation.Modifiers;
using PristonTale.Simulation.Progression;
using PristonTale.Simulation.Skills;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Simulation.Characters;

public sealed class PlayerCharacter
{
    private readonly Dictionary<EquipmentSlot, ItemInstance> _equipment = new();
    private readonly List<AppliedInventoryOperation> _inventoryOperations = new();
    private readonly List<AppliedProgressionOperation> _progressionOperations = new();
    private readonly List<AppliedServiceOperation> _serviceOperations = new();

    public PlayerCharacter(CharacterClass characterClass, string name, int level = 1, CharacterAttributes attributes = null)
    {
        Class = characterClass;
        Name = name;
        Level = Math.Max(1, level);
        Attributes = attributes ?? Definition.AttributesAtLevel(Level);
        Skills = new LearnedSkills(characterClass.Skills);
        Skills.Changed += Recompute;
        Premium.Changed += Recompute;
        Recompute();
        Life = MaxLife;
        Mana = MaxMana;
        Stamina = MaxStamina;
        Class.OnSpawn(this);
        Inventory.Changed += OnInventoryChanged;
    }

    public CharacterClass Class { get; }
    public CharacterClassDef Definition => Class.Definition;
    public string Name { get; }
    public int Level { get; private set; }
    public long Experience { get; private set; }
    public long Money { get; private set; }
    public int StatPoints { get; private set; }
    public int SkillPoints { get; private set; }
    public int EliteSkillPoints { get; private set; }
    public short Tier { get; private set; }

    public void RestoreProgress(long experience, long money, int statPoints = 0, short tier = 0, int skillPoints = 0, int eliteSkillPoints = 0)
    {
        if (experience < 0 || money < 0 || statPoints < 0 || skillPoints < 0 || eliteSkillPoints < 0 || tier < 0 || tier > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(experience));
        }

        Experience = experience;
        Money = money;
        StatPoints = statPoints;
        Tier = tier;
        SkillPoints = skillPoints;
        EliteSkillPoints = eliteSkillPoints;
    }

    public int AwardExperience(int amount)
    {
        if (amount <= 0)
        {
            return 0;
        }

        // As in the English release, experience stops accumulating at the threshold of the level below the cap.
        var ceiling = ExperienceTable.ForLevel(ExperienceTable.MaximumLevel - 1);
        if (Experience >= ceiling)
        {
            return 0;
        }

        var next = Experience + amount;
        if (Tier < 3 && next > ExperienceTable.ForLevel(100))
        {
            return 0;
        }

        Experience = Math.Min(ceiling, next);
        var previousLevel = Level;
        var targetLevel = ExperienceTable.LevelFor(Experience);
        while (Level < targetLevel)
        {
            LevelUp();
        }

        return Level - previousLevel;
    }

    public bool ReceiveGroundLoot(ItemInstance item, int gold)
    {
        if (InventoryRevision == long.MaxValue)
        {
            return false;
        }

        if (item is not null)
        {
            return gold == 0 && Inventory.TryAdd(item);
        }

        if (gold <= 0 || !TryChangeMoney(gold))
        {
            return false;
        }

        OnInventoryChanged();
        return true;
    }

    public bool CanChangeMoney(long delta) => delta != long.MinValue && (delta >= 0 || Money >= -delta) && (delta <= 0 || Money <= long.MaxValue - delta);

    public bool TryChangeMoney(long delta)
    {
        if (!CanChangeMoney(delta))
        {
            return false;
        }

        Money += delta;
        return true;
    }

    public CharacterAttributes Attributes { get; private set; }
    public Inventory Inventory { get; private set; } = new();
    public long InventoryRevision { get; private set; }
    public IReadOnlyList<AppliedInventoryOperation> InventoryOperations => _inventoryOperations;
    public IReadOnlyList<AppliedServiceOperation> ServiceOperations => _serviceOperations;
    public long ProgressionRevision { get; private set; }
    public IReadOnlyList<AppliedProgressionOperation> ProgressionOperations => _progressionOperations;
    public LearnedSkills Skills { get; }
    public IReadOnlyDictionary<EquipmentSlot, ItemInstance> Equipment => _equipment;

    // Timed and permanent modifiers from every source; the derived stats follow its revision.
    public EffectContainer Effects { get; } = new();

    public PremiumTimers Premium { get; } = new();

    public ItemUseHistory ItemUses { get; } = new();

    private DerivedStats _derived;
    private long _derivedEffectsRevision = -1;

    public DerivedStats Derived
    {
        get
        {
            if (_derivedEffectsRevision != Effects.Revision)
            {
                Recompute();
            }

            return _derived;
        }
    }

    // The attack animation rate: the attack speed and the attack-speed buffs.
    public float AttackRate => AnimationRate.AttackRate(Derived.AttackSpeed, Effects.Totals.Flat(StatKind.AttackMotionSpeed));

    public int Life { get; set; }
    public int Mana { get; set; }
    public int Stamina { get; set; }
    public int MaxLife => Derived.MaxLife;
    public int MaxMana => Derived.MaxMana;
    public int MaxStamina => Derived.MaxStamina;

    public event Action<PlayerCharacter, int> LeveledUp;
    public event Action<PlayerCharacter, EquipmentSlot, ItemInstance> Equipped;

    public void LevelUp()
    {
        if (Level >= ExperienceTable.MaximumLevel)
        {
            return;
        }

        Level++;
        StatPoints += 5;
        if (Level >= 10 && Level % 2 == 0)
        {
            SkillPoints++;
        }

        if (Level >= 60 && Level % 2 == 0)
        {
            EliteSkillPoints++;
        }
        Recompute();
        // The original restores HP/MP/SP to full on level-up.
        Life = MaxLife;
        Mana = MaxMana;
        Stamina = MaxStamina;
        Class.OnLevelUp(this);
        LeveledUp?.Invoke(this, Level);
    }

    public ItemInstance GetEquipped(EquipmentSlot slot) =>
        _equipment.GetValueOrDefault(slot);

    public void Equip(EquipmentSlot slot, ItemInstance item)
    {
        if (item is null)
        {
            _equipment.Remove(slot);
        }
        else
        {
            if (!CanEquip(slot, item))
            {
                throw new ArgumentException("Item requirements or equipment slot are invalid.", nameof(item));
            }

            _equipment[slot] = item;
        }
        Recompute();
        // Equipping can lower a max (e.g. swapping out a +life item); keep pools within bounds.
        Life = Math.Min(Life, MaxLife);
        Mana = Math.Min(Mana, MaxMana);
        Stamina = Math.Min(Stamina, MaxStamina);
        Equipped?.Invoke(this, slot, item);
        InventoryRevision++;
    }

    public bool CanEquip(EquipmentSlot slot, ItemInstance item)
    {
        if (item is null || item.Count != 1 || item.UpgradeLevel != 0 || !Enum.IsDefined(slot))
        {
            return false;
        }

        var definition = item.Definition;
        var sameSlot = definition.EquipSlot == slot;
        var ringSlot = definition.Kind == ItemKind.Ring && slot is EquipmentSlot.RingLeft or EquipmentSlot.RingRight;
        var requirements = definition.Requirements;
        if ((!sameSlot && !ringSlot) || Level < definition.RequiredLevel
            || definition.AllowedJobs != 0 && (definition.AllowedJobs & (uint)Definition.JobBitCode) == 0
            || Attributes.Strength < requirements.Strength || Attributes.Spirit < requirements.Spirit
            || Attributes.Talent < requirements.Talent || Attributes.Agility < requirements.Agility || Attributes.Health < requirements.Health)
        {
            return false;
        }

        if (slot == EquipmentSlot.Weapon && definition.TwoHanded && GetEquipped(EquipmentSlot.OffHand) is not null)
        {
            return false;
        }

        if (slot == EquipmentSlot.OffHand && GetEquipped(EquipmentSlot.Weapon)?.Definition.TwoHanded == true)
        {
            return false;
        }

        foreach (var equipped in _equipment)
        {
            if (equipped.Key != slot && equipped.Value.InstanceId == item.InstanceId)
            {
                return false;
            }
        }

        return true;
    }

    public bool TryEquipFromInventory(int anchor, EquipmentSlot slot)
    {
        if (anchor < 0 || anchor >= Inventory.Capacity || !CanEquip(slot, Inventory[anchor]))
        {
            return false;
        }

        var item = Inventory[anchor];
        if (!Inventory.TryExchange(anchor, GetEquipped(slot)))
        {
            return false;
        }

        Equip(slot, item);
        return true;
    }

    public bool TryUnequip(EquipmentSlot slot)
    {
        var item = GetEquipped(slot);
        if (item is null || !Inventory.TryAdd(item))
        {
            return false;
        }

        Equip(slot, null);
        return true;
    }

    public bool TryAllocateAttribute(CharacterAttribute attribute, int amount)
    {
        if (amount <= 0 || amount > StatPoints || !Enum.IsDefined(attribute))
        {
            return false;
        }

        var delta = attribute switch
        {
            CharacterAttribute.Strength => new CharacterAttributes(amount, 0, 0, 0, 0),
            CharacterAttribute.Spirit => new CharacterAttributes(0, amount, 0, 0, 0),
            CharacterAttribute.Talent => new CharacterAttributes(0, 0, amount, 0, 0),
            CharacterAttribute.Agility => new CharacterAttributes(0, 0, 0, amount, 0),
            CharacterAttribute.Health => new CharacterAttributes(0, 0, 0, 0, amount),
            _ => CharacterAttributes.Empty
        };
        if ((long)Attributes.Strength + delta.Strength > int.MaxValue || (long)Attributes.Spirit + delta.Spirit > int.MaxValue
            || (long)Attributes.Talent + delta.Talent > int.MaxValue || (long)Attributes.Agility + delta.Agility > int.MaxValue
            || (long)Attributes.Health + delta.Health > int.MaxValue)
        {
            return false;
        }

        Attributes = Attributes.Add(delta);
        StatPoints -= amount;
        Recompute();
        return true;
    }

    // Sets the attributes and the unallocated points of a stat reset; the total of both must not change.
    public void RedistributeAttributes(CharacterAttributes attributes, int statPoints)
    {
        if (attributes is null || statPoints < 0 || Sum(attributes) + statPoints != Sum(Attributes) + StatPoints)
        {
            throw new ArgumentException("A stat reset must keep the total of attributes and points.");
        }

        Attributes = attributes;
        StatPoints = statPoints;
        Recompute();
    }

    private static long Sum(CharacterAttributes a) => (long)a.Strength + a.Spirit + a.Talent + a.Agility + a.Health;

    // Item kinds in the main and the off hand (bit per kind), for effects bound to a weapon or a shield.
    private uint HeldKinds()
    {
        var held = 0u;
        foreach (var slot in new[] { EquipmentSlot.Weapon, EquipmentSlot.OffHand })
        {
            if (GetEquipped(slot) is { IsBroken: false } item && Enum.IsDefined(item.Definition.Kind))
            {
                held |= 1u << (int)item.Definition.Kind;
            }
        }

        return held;
    }

    public ItemStatBlock EquippedStats()
    {
        var total = ItemStatBlock.Zero;
        foreach (var item in _equipment.Values)
        {
            if (item is null)
            {
                continue;
            }

            total = total.Add(item.TotalStats);
        }
        return total;
    }

    // The single derived-stats path: attributes and level, equipment, passive skills, then every active modifier.
    private void Recompute()
    {
        PassiveSkillEffects.Sync(this);
        PremiumEffects.Sync(this);
        Effects.SetHeld(HeldKinds());
        var equipped = EquippedStats();
        var armorPercent = Effects.Totals.Flat(StatKind.ArmorDefence);
        if (armorPercent != 0 && GetEquipped(EquipmentSlot.Armor) is { IsBroken: false } armor
            && (armor.JobSpecMask & (uint)Definition.JobBitCode) != 0)
        {
            // The class armor's own defence grows by the percent before defence feeds absorption.
            equipped = equipped with { Defence = equipped.Defence + armor.TotalStats.Defence * armorPercent / 100 };
        }

        var stats = DerivedStats.Compute(Attributes, Level, Definition.FunctionProfile, equipped);
        _derivedEffectsRevision = Effects.Revision;
        _derived = ModifierRules.Apply(PassiveSkills.Apply(stats, this), Effects.Totals);
        _derived = _derived with { MaxWeight = _derived.MaxWeight + PremiumRules.WeightBonus(Premium) };
        Life = Math.Min(Life, _derived.MaxLife);
        Mana = Math.Min(Mana, _derived.MaxMana);
        Stamina = Math.Min(Stamina, _derived.MaxStamina);
    }

    private void OnInventoryChanged()
    {
        InventoryRevision++;
    }

    public void RestoreInventoryHistory(long revision, IEnumerable<AppliedInventoryOperation> operations)
    {
        if (revision < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(revision));
        }

        _inventoryOperations.Clear();
        foreach (var operation in operations)
        {
            if (_inventoryOperations.Count >= 64 || operation is null || operation.Command is null
                || operation.Command.OperationId == Guid.Empty || operation.Revision > revision || operation.Revision < 1
                || _inventoryOperations.Any(existing => existing.Command.OperationId == operation.Command.OperationId))
            {
                throw new ArgumentException("Invalid inventory operation history.");
            }

            _inventoryOperations.Add(operation);
        }

        InventoryRevision = revision;
    }

    public void RecordInventoryOperation(InventoryCommand command)
    {
        if (_inventoryOperations.Count == 64)
        {
            _inventoryOperations.RemoveAt(0);
        }

        _inventoryOperations.Add(new AppliedInventoryOperation(command, InventoryRevision));
    }

    public void RestoreServiceHistory(IEnumerable<AppliedServiceOperation> operations)
    {
        _serviceOperations.Clear();
        foreach (var operation in operations)
        {
            if (_serviceOperations.Count >= 64 || operation?.Command is null || operation.Command.OperationId == Guid.Empty
                || operation.Revision < 0 || operation.Revision > InventoryRevision
                || _serviceOperations.Any(existing => existing.Command.OperationId == operation.Command.OperationId))
            {
                throw new ArgumentException("Invalid NPC service operation history.");
            }

            _serviceOperations.Add(operation);
        }
    }

    public void RecordServiceOperation(NpcServiceCommand command)
    {
        if (_serviceOperations.Count == 64)
        {
            _serviceOperations.RemoveAt(0);
        }

        _serviceOperations.Add(new AppliedServiceOperation(command, InventoryRevision));
    }

    public ItemInstance FindOwnedItem(Guid instanceId)
    {
        var anchor = Inventory.Find(instanceId);
        if (anchor >= 0)
        {
            return Inventory[anchor];
        }

        foreach (var equipped in _equipment.Values)
        {
            if (equipped.InstanceId == instanceId)
            {
                return equipped;
            }
        }

        return null;
    }

    public IEnumerable<ItemInstance> OwnedItems()
    {
        for (var anchor = 0; anchor < Inventory.Capacity; anchor++)
        {
            if (Inventory[anchor] is { } item)
            {
                yield return item;
            }
        }

        foreach (var equipped in _equipment.Values)
        {
            yield return equipped;
        }
    }

    public bool RestoreDurability(Guid instanceId)
    {
        var item = FindOwnedItem(instanceId);
        if (item is null || item.DurabilityMax <= 0 || item.DurabilityCurrent >= item.DurabilityMax)
        {
            return false;
        }

        item.DurabilityCurrent = item.DurabilityMax;
        Recompute();
        OnInventoryChanged();
        return true;
    }

    // Marks a change of the carried state that moves no item, such as gold handed to storage.
    public void TouchInventory()
    {
        OnInventoryChanged();
    }

    public PlayerCharacter CloneForInventoryOperation()
    {
        var clone = new PlayerCharacter(Class, Name, Level, Attributes);
        clone.RestoreProgress(Experience, Money, StatPoints, Tier, SkillPoints, EliteSkillPoints);
        for (var anchor = 0; anchor < Inventory.Capacity; anchor++)
        {
            if (Inventory[anchor] is { } item)
            {
                clone.Inventory.TryPlace(item.Copy(item.Count, preserveId: true), anchor);
            }
        }

        foreach (var equipped in _equipment)
        {
            clone.Equip(equipped.Key, equipped.Value.Copy(1, preserveId: true));
        }

        foreach (var skill in Skills.Ranks)
        {
            clone.Skills.Restore(skill.Key, skill.Value, Skills.UsesOf(skill.Key));
        }

        clone.RestoreInventoryHistory(InventoryRevision, _inventoryOperations);
        clone.RestoreServiceHistory(_serviceOperations);
        clone.RestoreProgressionHistory(ProgressionRevision, _progressionOperations);
        clone.Premium.CopyFrom(Premium);
        clone.ItemUses.CopyFrom(ItemUses);
        clone.Life = Life;
        clone.Mana = Mana;
        clone.Stamina = Stamina;
        return clone;
    }

    public bool ApplyProgression(ProgressionCommand command)
    {
        if (command.OperationId == Guid.Empty || command.ExpectedRevision != ProgressionRevision || ProgressionRevision == long.MaxValue)
        {
            return false;
        }

        switch (command.Action)
        {
            case ProgressionAction.AllocateAttribute:
                if (command.SkillCode != 0 || command.NpcEntityId != 0 || !TryAllocateAttribute(command.Attribute, command.Amount))
                {
                    return false;
                }

                break;
            case ProgressionAction.LearnSkill:
                if (command.Amount != 0 || command.Attribute != CharacterAttribute.Strength || command.NpcEntityId == 0
                    || !SkillLearning.TryQuote(this, command.SkillCode, out var price, out var finalTier))
                {
                    return false;
                }

                Money -= price;
                if (finalTier)
                {
                    EliteSkillPoints--;
                }
                else
                {
                    SkillPoints--;
                }

                Skills.Increase(command.SkillCode);
                break;
            default:
                return false;
        }

        ProgressionRevision++;
        if (_progressionOperations.Count == 64)
        {
            _progressionOperations.RemoveAt(0);
        }

        _progressionOperations.Add(new AppliedProgressionOperation(command, ProgressionRevision));
        return true;
    }

    public void RestoreProgressionHistory(long revision, IEnumerable<AppliedProgressionOperation> operations)
    {
        if (revision < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(revision));
        }

        _progressionOperations.Clear();
        foreach (var operation in operations)
        {
            if (_progressionOperations.Count >= 64 || operation?.Command is null || operation.Command.OperationId == Guid.Empty
                || operation.Revision < 1 || operation.Revision > revision
                || _progressionOperations.Any(existing => existing.Command.OperationId == operation.Command.OperationId))
            {
                throw new ArgumentException("Invalid progression operation history.");
            }

            _progressionOperations.Add(operation);
        }

        ProgressionRevision = revision;
    }

    public void CommitInventoryOperation(PlayerCharacter candidate)
    {
        Inventory.Changed -= OnInventoryChanged;
        candidate.Inventory.Changed -= candidate.OnInventoryChanged;
        Inventory = candidate.Inventory;
        Inventory.Changed += OnInventoryChanged;
        _equipment.Clear();
        foreach (var equipped in candidate._equipment)
        {
            _equipment.Add(equipped.Key, equipped.Value);
        }

        RestoreInventoryHistory(candidate.InventoryRevision, candidate._inventoryOperations);
        RestoreServiceHistory(candidate._serviceOperations);
        Recompute();
        Life = Math.Min(Life, MaxLife);
        Mana = Math.Min(Mana, MaxMana);
        Stamina = Math.Min(Stamina, MaxStamina);
    }
}
