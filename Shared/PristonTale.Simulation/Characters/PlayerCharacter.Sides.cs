using PristonTale.Simulation.Items;

namespace PristonTale.Simulation.Characters;

// The original's two sides (its E and W keys): a second bag page, swapped whole with the shown one, and a second weapon
// and off-hand pair, swapped with the held one. What is hidden is still carried: it weighs and it is saved.
public sealed partial class PlayerCharacter
{
    public const int Sides = 2;
    private ItemInstance _storedWeapon;
    private ItemInstance _storedOffHand;

    // The page not shown (Inventory is the shown one), and which page (0 A, 1 B) is shown.
    public Inventory HiddenBag { get; private set; } = new();
    public int BagPage { get; private set; }

    // The pair not held, and which pair (0 A, 1 B) is held.
    public ItemInstance StoredWeapon => _storedWeapon;
    public ItemInstance StoredOffHand => _storedOffHand;
    public int WeaponSet { get; private set; }

    public bool TrySwitchBagPage(int page)
    {
        if (page is < 0 or >= Sides || page == BagPage)
        {
            return false;
        }

        Inventory.Changed -= OnInventoryChanged;
        (Inventory, HiddenBag) = (HiddenBag, Inventory);
        Inventory.Changed += OnInventoryChanged;
        BagPage = page;
        OnInventoryChanged();
        return true;
    }

    // The held pair is stored and the stored one held; a pair equipped together holds together again.
    public bool TrySwitchWeaponSet(int set)
    {
        if (set is < 0 or >= Sides || set == WeaponSet)
        {
            return false;
        }

        var weapon = GetEquipped(EquipmentSlot.Weapon);
        var offHand = GetEquipped(EquipmentSlot.OffHand);
        Equip(EquipmentSlot.Weapon, null);
        Equip(EquipmentSlot.OffHand, null);
        Equip(EquipmentSlot.Weapon, _storedWeapon);
        Equip(EquipmentSlot.OffHand, _storedOffHand);
        (_storedWeapon, _storedOffHand) = (weapon, offHand);
        WeaponSet = set;
        OnInventoryChanged();
        return true;
    }

    // As saved: the shown page and held pair, then the hidden page's items and the stored pair.
    public void RestoreSides(int page, int set)
    {
        if (page is < 0 or >= Sides || set is < 0 or >= Sides)
        {
            throw new ArgumentOutOfRangeException(nameof(page));
        }

        BagPage = page;
        WeaponSet = set;
    }

    public bool RestoreStored(EquipmentSlot slot, ItemInstance item)
    {
        if (slot == EquipmentSlot.Weapon && _storedWeapon is null && ItemEquipmentMetadata.Fits(item.Definition.Kind, item.Definition.EquipSlot, slot))
        {
            _storedWeapon = item;
            return true;
        }

        if (slot == EquipmentSlot.OffHand && _storedOffHand is null && ItemEquipmentMetadata.Fits(item.Definition.Kind, item.Definition.EquipSlot, slot)
            && _storedWeapon?.Definition.TwoHanded != true)
        {
            _storedOffHand = item;
            return true;
        }

        return false;
    }

    private void CopySides(PlayerCharacter source)
    {
        for (var anchor = 0; anchor < source.HiddenBag.Capacity; anchor++)
        {
            if (source.HiddenBag[anchor] is { } item)
            {
                HiddenBag.TryPlace(item.Copy(item.Count, preserveId: true), anchor);
            }
        }

        _storedWeapon = source._storedWeapon?.Copy(1, preserveId: true);
        _storedOffHand = source._storedOffHand?.Copy(1, preserveId: true);
        BagPage = source.BagPage;
        WeaponSet = source.WeaponSet;
    }

    private IEnumerable<ItemInstance> HiddenItems()
    {
        for (var anchor = 0; anchor < HiddenBag.Capacity; anchor++)
        {
            if (HiddenBag[anchor] is { } item)
            {
                yield return item;
            }
        }

        if (_storedWeapon is not null)
        {
            yield return _storedWeapon;
        }

        if (_storedOffHand is not null)
        {
            yield return _storedOffHand;
        }
    }
}
