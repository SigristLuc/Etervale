namespace PristonTale.Simulation.Items;

// The three potion boxes on the bottom bar (keys 1 to 3), as the original's: one potion stack each, of at most the
// character's potion space (PlayerCharacter.Belt). Owner views give a belt item the slot FirstOwnerSlot + its box.
public sealed class PotionBelt
{
    public const int Boxes = 3;
    // Owner slots follow the equipment's, as the original numbers the belt after it.
    public const int FirstOwnerSlot = Characters.EquipmentSlots.Count;
    private readonly ItemInstance[] _boxes = new ItemInstance[Boxes];

    public ItemInstance this[int box] => _boxes[box];

    public int Find(Guid instanceId) => Array.FindIndex(_boxes, item => item?.InstanceId == instanceId);

    public static bool IsBox(int box) => box is >= 0 and < Boxes;

    internal void Set(int box, ItemInstance item) => _boxes[box] = item;

    internal void CopyFrom(PotionBelt other)
    {
        for (var box = 0; box < Boxes; box++)
        {
            _boxes[box] = other._boxes[box]?.Copy(other._boxes[box].Count, preserveId: true);
        }
    }
}
