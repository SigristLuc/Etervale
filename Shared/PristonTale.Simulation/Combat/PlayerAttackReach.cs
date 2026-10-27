using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Contracts.Characters;

namespace PristonTale.Simulation.Combat;

public static class PlayerAttackReach
{
    public static float For(PlayerCharacter character)
    {
        var weapon = character.GetEquipped(EquipmentSlot.Weapon);
        if (weapon is null)
        {
            return MeleeReach.PlayerBaseRange;
        }

        var definition = weapon.Definition;
        bool ranged = definition.Kind is ItemKind.Bow or ItemKind.ThrowingWeapon
            || definition.Kind == ItemKind.Staff && character.Definition.Class is PlayerClass.Magician or PlayerClass.Priestess
            || definition.Kind == ItemKind.Phantom && character.Definition.Class == PlayerClass.Shaman;
        // Shooting range from skill effects (a wind's longer javelin throw) extends ranged weapons only.
        return ranged ? Math.Max(0, character.Derived.ShootingRange + character.Effects.Totals.Flat(Modifiers.StatKind.AttackRange)) : MeleeReach.PlayerBaseRange;
    }
}
