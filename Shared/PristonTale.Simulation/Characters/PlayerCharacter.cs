using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Simulation.Modifiers;
using PristonTale.Simulation.Progression;
using PristonTale.Simulation.Quests;
using PristonTale.Simulation.Skills;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Simulation.Characters;

public sealed partial class PlayerCharacter
{
    private readonly Dictionary<EquipmentSlot, ItemInstance> _equipment = new();
    private readonly Dictionary<EquipmentSlot, ItemInstance> _spareHands = new();
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
        Belt.Changed += OnInventoryChanged;
    }

    public CharacterClass Class { get; }
    public CharacterClassDef Definition => Class.Definition;
    public string Name { get; }
    public int Level { get; private set; }
    public ulong Experience { get; private set; }
    public long Money { get; private set; }
    public int StatPoints { get; private set; }
    public int SkillPoints { get; private set; }
    public int EliteSkillPoints { get; private set; }
    public short Tier { get; private set; }

    // Quest progress: the running quest, finished quests and the logs whose bits pay lasting rewards.
    public QuestJournal Quests { get; private set; } = new();

    public void RestoreProgress(ulong experience, long money, int statPoints = 0, short tier = 0, int skillPoints = 0, int eliteSkillPoints = 0)
    {
        if (money < 0 || statPoints < 0 || skillPoints < 0 || eliteSkillPoints < 0 || tier < 0 || tier > 3)
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

    public int AwardExperience(ulong amount)
    {
        if (amount == 0)
        {
            return 0;
        }

        // Experience saturates below the unsigned maximum (no wrap); below the third rank-up an award that would pass the
        // level 100 threshold is discarded whole.
        const ulong ceiling = ExperienceTable.Ceiling;
        if (Experience >= ceiling)
        {
            return 0;
        }

        var next = amount > ceiling - Experience ? ceiling : Experience + amount;
        if (Tier < 3 && next > ExperienceTable.ForLevel(100))
        {
            return 0;
        }

        Experience = next;
        var previousLevel = Level;
        var targetLevel = ExperienceTable.LevelFor(Experience);
        while (Level < targetLevel)
        {
            LevelUp();
        }

        return Level - previousLevel;
    }

    // Takes experience away (the death penalties), never below the start of the current level; returns what was taken.
    public ulong LoseExperience(ulong amount)
    {
        var floor = ExperienceTable.ForLevel(Math.Clamp(Level, 1, ExperienceTable.MaximumLevel));
        if (amount == 0 || Experience <= floor)
        {
            return 0;
        }

        var taken = Math.Min(amount, Experience - floor);
        Experience -= taken;
        return taken;
    }

    public bool ReceiveGroundLoot(ItemInstance item, int gold)
    {
        if (InventoryRevision == long.MaxValue)
        {
            return false;
        }

        if (item is not null)
        {
            return gold == 0 && Items.CarriedWeight.CanTake(this, Items.CarriedWeight.Of(item)) && TryCarry(item);
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
    private bool _derivedStale;

    public DerivedStats Derived
    {
        get
        {
            if (_derivedEffectsRevision != Effects.Revision || _derivedStale)
            {
                Recompute();
            }

            return _derived;
        }
    }

    private EquipmentTotals _equipmentTotals = EquipmentTotals.None;

    // What the usable equipped items add; reading it brings the derived stats up to date first.
    public EquipmentTotals EquipmentTotals
    {
        get
        {
            _ = Derived;
            return _equipmentTotals;
        }
    }

    public int CarriedWeight => Derived.CarriedWeight;

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
        StatPoints += 5 + QuestRewards.LevelUpBonusStatPoints(Level, Quests.LevelLog);
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

    // The inactive weapon set (A is 0, B is 1): its weapon and off-hand item wait here, carried but not worn, until the
    // next swap trades them with the worn hands.
    public int WeaponSet { get; private set; }
    public IReadOnlyDictionary<EquipmentSlot, ItemInstance> SpareHands => _spareHands;

    // A worn weapon or off-hand item the player holds on the cursor (carried) goes along to the set coming into use: only
    // the other hand trades with the spare set. When that would put a two-handed weapon with an off-hand item, the carried
    // item leaves the hands for the bag (it stays on the cursor) and both hands trade, as the original's cursor item does.
    public bool TrySwapWeaponSet(Guid carried = default)
    {
        if (carried != Guid.Empty)
        {
            return TrySwapOtherHand(carried);
        }

        var weapon = GetEquipped(EquipmentSlot.Weapon);
        var offHand = GetEquipped(EquipmentSlot.OffHand);
        var spareWeapon = _spareHands.GetValueOrDefault(EquipmentSlot.Weapon);
        var spareOffHand = _spareHands.GetValueOrDefault(EquipmentSlot.OffHand);
        _equipment.Remove(EquipmentSlot.Weapon);
        _equipment.Remove(EquipmentSlot.OffHand);
        _spareHands.Clear();
        SetSpare(EquipmentSlot.Weapon, weapon);
        SetSpare(EquipmentSlot.OffHand, offHand);
        Equip(EquipmentSlot.Weapon, spareWeapon);
        Equip(EquipmentSlot.OffHand, spareOffHand);
        WeaponSet ^= 1;
        return true;
    }

    // Whether the carried worn hand item must go to the bag on a set swap and the bag has no place for it.
    public bool CarriedHandHasNoRoom(Guid carried) =>
        CarriedHand(carried) is { } held && WouldConflict(held == EquipmentSlot.Weapon ? EquipmentSlot.OffHand : EquipmentSlot.Weapon)
        && !Inventory.HasRoomFor(GetEquipped(held));

    private EquipmentSlot? CarriedHand(Guid carried) =>
        GetEquipped(EquipmentSlot.Weapon)?.InstanceId == carried ? EquipmentSlot.Weapon
        : GetEquipped(EquipmentSlot.OffHand)?.InstanceId == carried ? EquipmentSlot.OffHand
        : null;

    private bool WouldConflict(EquipmentSlot other)
    {
        var spare = _spareHands.GetValueOrDefault(other);
        var weapon = other == EquipmentSlot.Weapon ? spare : GetEquipped(EquipmentSlot.Weapon);
        var offHand = other == EquipmentSlot.OffHand ? spare : GetEquipped(EquipmentSlot.OffHand);
        return weapon?.Definition.TwoHanded == true && offHand is not null;
    }

    private bool TrySwapOtherHand(Guid carried)
    {
        if (CarriedHand(carried) is not { } held)
        {
            return false;
        }

        var other = held == EquipmentSlot.Weapon ? EquipmentSlot.OffHand : EquipmentSlot.Weapon;
        if (WouldConflict(other))
        {
            if (!Inventory.TryAdd(GetEquipped(held)))
            {
                return false;
            }

            Equip(held, null);
            return TrySwapWeaponSet();
        }

        var worn = GetEquipped(other);
        var spare = _spareHands.GetValueOrDefault(other);
        _equipment.Remove(other);
        _spareHands.Remove(other);
        SetSpare(other, worn);
        Equip(other, spare);
        WeaponSet ^= 1;
        return true;
    }

    public void RestoreWeaponSet(int weaponSet, IEnumerable<(EquipmentSlot Slot, ItemInstance Item)> spares)
    {
        if (weaponSet is not (0 or 1))
        {
            throw new ArgumentOutOfRangeException(nameof(weaponSet));
        }

        WeaponSet = weaponSet;
        _spareHands.Clear();
        foreach (var (slot, item) in spares)
        {
            if (slot is not (EquipmentSlot.Weapon or EquipmentSlot.OffHand) || _spareHands.ContainsKey(slot) || !ItemEquipmentMetadata.Fits(item.Definition, slot))
            {
                throw new ArgumentException("Invalid spare weapon-set item.", nameof(spares));
            }

            _spareHands[slot] = item;
        }

        Recompute();
    }

    // The other bag page (I is 0, II is 1): its items are carried and weighed but stay out of reach until the next swap
    // makes it the bag.
    public int BagPage { get; private set; }
    public Inventory SpareBag { get; private set; } = new();

    // An item the player holds on the cursor (carried) travels to the page coming into use, at its first free place and
    // never merged into a stack, so it stays the held item; the swap is refused when that page has no room for it.
    public bool TrySwapBagPage(Guid carried = default)
    {
        var anchor = carried == Guid.Empty ? -1 : Inventory.Find(carried);
        if (carried != Guid.Empty && anchor < 0)
        {
            return false;
        }

        var held = anchor >= 0 ? Inventory.TakeAt(anchor) : null;
        if (held is not null && !Enumerable.Range(0, SpareBag.Capacity).Any(free => SpareBag.TryPlace(held, free)))
        {
            Inventory.TryPlace(held, anchor);
            return false;
        }

        Inventory.Changed -= OnInventoryChanged;
        (Inventory, SpareBag) = (SpareBag, Inventory);
        Inventory.Changed += OnInventoryChanged;
        BagPage ^= 1;
        OnInventoryChanged();
        return true;
    }

    public void RestoreBagPage(int page, IEnumerable<(int Anchor, ItemInstance Item)> spares)
    {
        if (page is not (0 or 1))
        {
            throw new ArgumentOutOfRangeException(nameof(page));
        }

        BagPage = page;
        SpareBag = new Inventory();
        foreach (var (anchor, item) in spares)
        {
            if (!SpareBag.TryPlace(item, anchor))
            {
                throw new ArgumentException("Invalid spare bag footprint.", nameof(spares));
            }
        }

        OnInventoryChanged();
    }

    public IEnumerable<(int Anchor, ItemInstance Item)> SpareBagItems()
    {
        for (var anchor = 0; anchor < SpareBag.Capacity; anchor++)
        {
            if (SpareBag[anchor] is { } item)
            {
                yield return (anchor, item);
            }
        }
    }

    private void SetSpare(EquipmentSlot slot, ItemInstance item)
    {
        if (item is not null)
        {
            _spareHands[slot] = item;
        }
    }

    public void Equip(EquipmentSlot slot, ItemInstance item)
    {
        if (item is null)
        {
            _equipment.Remove(slot);
        }
        else
        {
            if (!CanHold(slot, item))
            {
                throw new ArgumentException("Item equipment slot is invalid.", nameof(item));
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

    // A worn item must fit the slot, be usable by the character and leave the hands consistent: a two-handed weapon
    // holds the off hand too.
    public bool CanEquip(EquipmentSlot slot, ItemInstance item) => CanHold(slot, item) && ItemUsability.CanUse(this, item);

    // Slot, usability and identity checks without the hand rules, which equipping from the bag resolves by swapping.
    public bool CanWear(EquipmentSlot slot, ItemInstance item) => Fits(slot, item) && ItemUsability.CanUse(this, item);

    // Whether the slot can hold the item at all, usable or not: a saved item whose requirements are no longer met stays
    // worn without adding anything.
    public bool CanHold(EquipmentSlot slot, ItemInstance item)
    {
        if (!Fits(slot, item))
        {
            return false;
        }

        if (slot == EquipmentSlot.Weapon && item.Definition.TwoHanded && GetEquipped(EquipmentSlot.OffHand) is not null)
        {
            return false;
        }

        return slot != EquipmentSlot.OffHand || GetEquipped(EquipmentSlot.Weapon)?.Definition.TwoHanded != true;
    }

    private bool Fits(EquipmentSlot slot, ItemInstance item)
    {
        if (item is null || item.Count != 1 || !Enum.IsDefined(slot))
        {
            return false;
        }

        var definition = item.Definition;
        if (!ItemEquipmentMetadata.Fits(definition, slot))
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

    // The item replaced in the slot takes the bag cell; a two-handed weapon also sends the off-hand item to the bag, and
    // an off-hand item sends a held two-handed weapon there.
    public bool TryEquipFromInventory(int anchor, EquipmentSlot slot)
    {
        if (anchor < 0 || anchor >= Inventory.Capacity || !CanWear(slot, Inventory[anchor]))
        {
            return false;
        }

        var item = Inventory[anchor];
        EquipmentSlot? otherHand = null;
        if (slot == EquipmentSlot.Weapon && item.Definition.TwoHanded)
        {
            otherHand = EquipmentSlot.OffHand;
        }
        else if (slot == EquipmentSlot.OffHand && GetEquipped(EquipmentSlot.Weapon)?.Definition.TwoHanded == true)
        {
            otherHand = EquipmentSlot.Weapon;
        }

        var displaced = otherHand.HasValue ? GetEquipped(otherHand.Value) : null;
        if (displaced is not null)
        {
            if (!Inventory.TryAdd(displaced))
            {
                return false;
            }

            Equip(otherHand.Value, null);
        }

        if (!Inventory.TryExchange(Inventory.Find(item.InstanceId), GetEquipped(slot)))
        {
            if (displaced is not null)
            {
                Inventory.TakeAt(Inventory.Find(displaced.InstanceId));
                Equip(otherHand.Value, displaced);
            }

            return false;
        }

        Equip(slot, item);
        return true;
    }

    // An equipped item cannot be taken off while the character carries more than the weight limit.
    // The item goes to the bag cell asked for (anchor) when it fits there, else to the first free place.
    public bool TryUnequip(EquipmentSlot slot, int anchor = -1)
    {
        var item = GetEquipped(slot);
        if (item is null || Items.CarriedWeight.IsOverweight(this)
            || !(anchor >= 0 && anchor < Inventory.Capacity && Inventory.TryPlace(item, anchor)) && !Inventory.TryAdd(item))
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
            if (GetEquipped(slot) is { } item && Enum.IsDefined(item.Definition.Kind))
            {
                held |= 1u << (int)item.Definition.Kind;
            }
        }

        return held;
    }

    // The single derived-stats path: attributes and level, usable equipment with its class specialization, carried
    // weight, passive skills, then every active modifier.
    private void Recompute()
    {
        PassiveSkillEffects.Sync(this);
        QuestEffects.Sync(this);
        PremiumEffects.Sync(this);
        Effects.SetHeld(HeldKinds());
        _derivedStale = false;
        var equipped = Items.EquipmentTotals.Sum(this);
        var armorPercent = Effects.Totals.Flat(StatKind.ArmorDefence);
        if (armorPercent != 0 && GetEquipped(EquipmentSlot.Armor) is { } armor && armor.Roll.SpecializedFor(Definition.JobBitCode))
        {
            // The class armor's own defence, specialization included, grows by the percent before defence feeds absorption.
            var armorDefence = armor.Roll.Defence + armor.Roll.Spec.Defence;
            equipped = equipped with { Defence = equipped.Defence + armorDefence * armorPercent / 100 };
        }

        _equipmentTotals = equipped;
        var stats = DerivedStats.Compute(Attributes, Level, Definition.FunctionProfile, equipped, Items.CarriedWeight.Of(this));
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
        _derivedStale = true;
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

        foreach (var equipped in _equipment.Values.Concat(_spareHands.Values).Concat(SpareBagItems().Select(static pair => pair.Item))
            .Concat(BeltItems().Select(static pair => pair.Item)))
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

        foreach (var spare in _spareHands.Values)
        {
            yield return spare;
        }

        foreach (var (_, item) in SpareBagItems())
        {
            yield return item;
        }

        foreach (var (_, item) in BeltItems())
        {
            yield return item;
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

        clone.RestoreWeaponSet(WeaponSet, _spareHands.Select(static spare => (spare.Key, spare.Value.Copy(1, preserveId: true))));
        clone.RestoreBagPage(BagPage, SpareBagItems().Select(static pair => (pair.Anchor, pair.Item.Copy(pair.Item.Count, preserveId: true))));
        clone.RestoreBelt(BeltItems().Select(static pair => (pair.Slot, pair.Item.Copy(pair.Item.Count, preserveId: true))));

        foreach (var skill in Skills.Ranks)
        {
            clone.Skills.Restore(skill.Key, skill.Value, Skills.UsesOf(skill.Key));
        }

        clone.RestoreInventoryHistory(InventoryRevision, _inventoryOperations);
        clone.RestoreServiceHistory(_serviceOperations);
        clone.RestoreProgressionHistory(ProgressionRevision, _progressionOperations);
        clone.Quests = Quests.Clone();
        clone.Premium.CopyFrom(Premium);
        clone.ItemUses.CopyFrom(ItemUses);
        clone.Recompute();
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

    public void RestoreQuests(QuestJournal journal)
    {
        Quests = journal ?? throw new ArgumentNullException(nameof(journal));
        Recompute();
    }

    // Quest changes made outside a stored operation (kill counts, timers) on the live character.
    public void RefreshQuestEffects() => Recompute();

    // Quest rewards that change progression: the class tier, and stat and skill points added on top of whatever the
    // tick changed meanwhile.
    public void ApplyQuestProgress(short tier, int statPoints, int skillPoints, int eliteSkillPoints)
    {
        if (tier < Tier || tier > 3 || StatPoints + (long)statPoints < 0 || SkillPoints + (long)skillPoints < 0 || EliteSkillPoints + (long)eliteSkillPoints < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tier));
        }

        Tier = tier;
        StatPoints += statPoints;
        SkillPoints += skillPoints;
        EliteSkillPoints += eliteSkillPoints;
    }

    // The free redistribution a rank-up offers: every learned rank returns to its pool (the first twelve window skills to
    // the skill points, the final tier's to its own points) and the proficiency is forgotten.
    public void ResetLearnedSkills()
    {
        var skills = Class.Skills;
        for (var index = 0; index < skills.Count; index++)
        {
            var rank = Skills.RankOf(skills[index].Code);
            if (SkillLearning.UsesFinalTierPoints(index))
            {
                EliteSkillPoints += rank;
            }
            else
            {
                SkillPoints += rank;
            }
        }

        Skills.Clear();
    }

    // Commits a stored quest operation: the candidate's quest journal (keeping kills counted meanwhile), its items and
    // the progression and gold deltas computed when it was prepared.
    public void CommitQuestOperation(PlayerCharacter candidate, short tier, int statPoints, int skillPoints, int eliteSkillPoints, long moneyDelta,
        bool resetSkills)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (!CanChangeMoney(moneyDelta))
        {
            throw new InvalidOperationException("Quest gold change no longer fits.");
        }

        CommitInventoryOperation(candidate);
        if (resetSkills)
        {
            // The refunded points are part of the prepared deltas; the save gate kept the ranks unchanged since then.
            Skills.Clear();
        }

        ApplyQuestProgress(tier, statPoints, skillPoints, eliteSkillPoints);
        TryChangeMoney(moneyDelta);
        Quests.AdoptCommitted(candidate.Quests);
        Recompute();
        Life = Math.Min(Life, MaxLife);
        Mana = Math.Min(Mana, MaxMana);
        Stamina = Math.Min(Stamina, MaxStamina);
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

        Belt.Changed -= OnInventoryChanged;
        candidate.Belt.Changed -= candidate.OnInventoryChanged;
        Belt = candidate.Belt;
        Belt.Changed += OnInventoryChanged;
        BagPage = candidate.BagPage;
        SpareBag = candidate.SpareBag;
        WeaponSet = candidate.WeaponSet;
        _spareHands.Clear();
        foreach (var spare in candidate._spareHands)
        {
            _spareHands.Add(spare.Key, spare.Value);
        }

        RestoreInventoryHistory(candidate.InventoryRevision, candidate._inventoryOperations);
        RestoreServiceHistory(candidate._serviceOperations);
        Recompute();
        Life = Math.Min(Life, MaxLife);
        Mana = Math.Min(Mana, MaxMana);
        Stamina = Math.Min(Stamina, MaxStamina);
    }
}
