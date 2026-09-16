using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.World.Monsters;

namespace PristonTale.Simulation.Combat;

public enum WeaponDamageStyle
{
    Melee = 0,

    Ranged = 1,

    Magic = 2
}

public readonly record struct AttackProfile(
    int AttackRating,
    int DamageMin,
    int DamageMax,
    int AttackerLevel,
    Element Element,
    int CritChance,
    int SkillMultiplierPercent,
    int SkillFlatAdd)
{
    public const int CritMultiplierPercent = 170;
}

public readonly record struct DefenderProfile(
    int Defence,
    int Absorption,
    int BlockChance,
    int Level,
    ResistanceBlock Resistances,
    int MaxLife,
    int CurrentLife,
    bool IsMonster)
{
    public static DefenderProfile FromMonster(MonsterCombatStats stats, int currentLife) =>
        new(
            Defence: stats.Defence,
            Absorption: stats.Absorption,
            BlockChance: stats.BlockChance,
            Level: stats.Level,
            Resistances: stats.Resistances,
            MaxLife: stats.MaxLife,
            CurrentLife: currentLife,
            IsMonster: true);
}
