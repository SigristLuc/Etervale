using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;

namespace PristonTale.Simulation.Skills;

public static class FirstTierSkills
{
    private static readonly IReadOnlyDictionary<PlayerClass, IReadOnlyList<Skill>> Classes =
        new Dictionary<PlayerClass, IReadOnlyList<Skill>>
        {
            [PlayerClass.Mechanician] = Array.AsReadOnly(new Skill[]
            {
                new Skill
                {
                    Code = 0x01010001,
                    Name = "Extreme Shield",
                    Description = "Temporarily increases block rating of the current shield",
                    IconKey = "TM10 Ex_shield",
                    Tier = JobTier.Job1,
                    Category = SkillCategory.Buff,
                    RequiredLevel = 10,
                    MouseUse = SkillMouseUse.Right,
                    ManaCost = 12,
                    ManaByRank = Array.AsReadOnly(new[] { 12, 14, 17, 20, 23, 26, 30, 34, 39, 44 }),
                    StaminaBase = 34,
                    StaminaPerRank = 2,
                    AllowedWeaponsMask = (1u << (int)ItemKind.Shield)
                },
                new Skill
                {
                    Code = 0x01010002,
                    Name = "Mechanic Bomb",
                    Description = "Throws a bomb to enemies within explosive blast radius",
                    IconKey = "TM12 M_bomb",
                    Tier = JobTier.Job1,
                    Category = SkillCategory.AreaAttack,
                    RequiredLevel = 12,
                    MouseUse = SkillMouseUse.Right,
                    ManaCost = 8,
                    ManaByRank = Array.AsReadOnly(new[] { 8, 10, 12, 14, 17, 20, 23, 26, 29, 33 }),
                    StaminaBase = 28,
                    StaminaPerRank = 2,
                    AllowedWeaponsMask = 0
                },
                new Skill
                {
                    Code = 0x01010004,
                    Name = "Poison Attribute",
                    Description = "Permanently raises resistance to poison",
                    IconKey = "TM14 P_attribute",
                    Tier = JobTier.Job1,
                    Category = SkillCategory.Passive,
                    RequiredLevel = 14,
                    MouseUse = SkillMouseUse.None,
                    ManaCost = 0,
                    ManaByRank = Array.AsReadOnly(new[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }),
                    StaminaBase = 0,
                    StaminaPerRank = 0,
                    AllowedWeaponsMask = 0
                },
                new Skill
                {
                    Code = 0x01010003,
                    Name = "Physical Absorption",
                    Description = "Temporarily increases the body's ability to absorb damage",
                    IconKey = "TM17 Ph_absorb",
                    Tier = JobTier.Job1,
                    Category = SkillCategory.Buff,
                    RequiredLevel = 17,
                    MouseUse = SkillMouseUse.Right,
                    ManaCost = 20,
                    ManaByRank = Array.AsReadOnly(new[] { 20, 24, 28, 32, 36, 40, 44, 48, 52, 56 }),
                    StaminaBase = 30,
                    StaminaPerRank = 2,
                    AllowedWeaponsMask = 0
                },
            }),
            [PlayerClass.Fighter] = Array.AsReadOnly(new Skill[]
            {
                new Skill
                {
                    Code = 0x02010001,
                    Name = "Melee Mastery",
                    Description = "Increases efficiency in melee weapons, increasing Attack Power permanently",
                    IconKey = "TF10 M_mastery",
                    Tier = JobTier.Job1,
                    Category = SkillCategory.Passive,
                    RequiredLevel = 10,
                    MouseUse = SkillMouseUse.None,
                    ManaCost = 0,
                    ManaByRank = Array.AsReadOnly(new[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }),
                    StaminaBase = 0,
                    StaminaPerRank = 0,
                    AllowedWeaponsMask = (1u << (int)ItemKind.Axe) | (1u << (int)ItemKind.Claw) | (1u << (int)ItemKind.Hammer) | (1u << (int)ItemKind.Sword)
                },
                new Skill
                {
                    Code = 0x02010002,
                    Name = "Fire Attribute",
                    Description = "Permanently raises resistance to fire",
                    IconKey = "TF12 F_attribute",
                    Tier = JobTier.Job1,
                    Category = SkillCategory.Passive,
                    RequiredLevel = 12,
                    MouseUse = SkillMouseUse.None,
                    ManaCost = 0,
                    ManaByRank = Array.AsReadOnly(new[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }),
                    StaminaBase = 0,
                    StaminaPerRank = 0,
                    AllowedWeaponsMask = 0
                },
                new Skill
                {
                    Code = 0x02010003,
                    Name = "Raving",
                    Description = "Goes berserk for a short period of time, but loses HP with each attack",
                    IconKey = "TF14 raving",
                    Tier = JobTier.Job1,
                    Category = SkillCategory.MultiHit,
                    RequiredLevel = 14,
                    MouseUse = SkillMouseUse.Both,
                    ManaCost = 14,
                    ManaByRank = Array.AsReadOnly(new[] { 14, 16, 18, 20, 22, 24, 26, 29, 32, 35 }),
                    StaminaBase = 35,
                    StaminaPerRank = 2,
                    AllowedWeaponsMask = (1u << (int)ItemKind.Axe) | (1u << (int)ItemKind.Claw) | (1u << (int)ItemKind.Hammer) | (1u << (int)ItemKind.Pole) | (1u << (int)ItemKind.Sword)
                },
                new Skill
                {
                    Code = 0x02010004,
                    Name = "Impact",
                    Description = "Raises attack rating to accurately strike the enemy",
                    IconKey = "TF17 impact",
                    Tier = JobTier.Job1,
                    Category = SkillCategory.ActiveAttack,
                    RequiredLevel = 17,
                    MouseUse = SkillMouseUse.Both,
                    ManaCost = 18,
                    ManaByRank = Array.AsReadOnly(new[] { 18, 20, 22, 24, 26, 29, 33, 37, 41, 45 }),
                    StaminaBase = 37,
                    StaminaPerRank = 2,
                    AllowedWeaponsMask = (1u << (int)ItemKind.Axe) | (1u << (int)ItemKind.Claw) | (1u << (int)ItemKind.Pole) | (1u << (int)ItemKind.Sword)
                },
            }),
            [PlayerClass.Pikeman] = Array.AsReadOnly(new Skill[]
            {
                new Skill
                {
                    Code = 0x03010001,
                    Name = "Pike Wind",
                    Description = "Creates a whirlwind to push back and damage enemies",
                    IconKey = "TP10 P_wind",
                    Tier = JobTier.Job1,
                    Category = SkillCategory.AreaAttack,
                    RequiredLevel = 10,
                    MouseUse = SkillMouseUse.Right,
                    ManaCost = 14,
                    ManaByRank = Array.AsReadOnly(new[] { 14, 16, 18, 20, 23, 26, 29, 32, 35, 39 }),
                    StaminaBase = 27,
                    StaminaPerRank = 2,
                    AllowedWeaponsMask = (1u << (int)ItemKind.Pole)
                },
                new Skill
                {
                    Code = 0x03010002,
                    Name = "Ice Attribute",
                    Description = "Permanently raises resistance to frost",
                    IconKey = "TP12 I_attribute",
                    Tier = JobTier.Job1,
                    Category = SkillCategory.Passive,
                    RequiredLevel = 12,
                    MouseUse = SkillMouseUse.None,
                    ManaCost = 0,
                    ManaByRank = Array.AsReadOnly(new[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }),
                    StaminaBase = 0,
                    StaminaPerRank = 0,
                    AllowedWeaponsMask = 0
                },
                new Skill
                {
                    Code = 0x03010003,
                    Name = "Critical Hit",
                    Description = "Aims for enemies' weak spots to raise probability of a critical strike",
                    IconKey = "TP14 Cri_hit",
                    Tier = JobTier.Job1,
                    Category = SkillCategory.ActiveAttack,
                    RequiredLevel = 14,
                    MouseUse = SkillMouseUse.Both,
                    ManaCost = 14,
                    ManaByRank = Array.AsReadOnly(new[] { 14, 16, 18, 20, 23, 26, 29, 33, 37, 41 }),
                    StaminaBase = 31,
                    StaminaPerRank = 2,
                    AllowedWeaponsMask = (1u << (int)ItemKind.Pole) | (1u << (int)ItemKind.Claw) | (1u << (int)ItemKind.Hammer) | (1u << (int)ItemKind.Sword)
                },
                new Skill
                {
                    Code = 0x03010004,
                    Name = "Jumping Crash",
                    Description = "Leaps up into the air to strikedown with a powerful blow",
                    IconKey = "TP17 J_Crash",
                    Tier = JobTier.Job1,
                    Category = SkillCategory.ActiveAttack,
                    RequiredLevel = 17,
                    MouseUse = SkillMouseUse.Both,
                    ManaCost = 16,
                    ManaByRank = Array.AsReadOnly(new[] { 16, 18, 20, 23, 26, 29, 32, 35, 39, 43 }),
                    StaminaBase = 34,
                    StaminaPerRank = 2,
                    AllowedWeaponsMask = (1u << (int)ItemKind.Axe) | (1u << (int)ItemKind.Pole) | (1u << (int)ItemKind.Sword)
                },
            }),
            [PlayerClass.Archer] = Array.AsReadOnly(new Skill[]
            {
                new Skill
                {
                    Code = 0x04010001,
                    Name = "Scout Hawk",
                    Description = "Summons a hawk to scout the surrounding area, raising attack rating",
                    IconKey = "TA10 S_hawk",
                    Tier = JobTier.Job1,
                    Category = SkillCategory.Summon,
                    RequiredLevel = 10,
                    MouseUse = SkillMouseUse.Right,
                    ManaCost = 14,
                    ManaByRank = Array.AsReadOnly(new[] { 14, 16, 18, 20, 23, 26, 30, 34, 39, 44 }),
                    StaminaBase = 25,
                    StaminaPerRank = 2,
                    AllowedWeaponsMask = 0
                },
                new Skill
                {
                    Code = 0x04010002,
                    Name = "Shooting Mastery",
                    Description = "Permanently raises attack power with bows and crossbows",
                    IconKey = "TA12 S_mastery",
                    Tier = JobTier.Job1,
                    Category = SkillCategory.Passive,
                    RequiredLevel = 12,
                    MouseUse = SkillMouseUse.None,
                    ManaCost = 0,
                    ManaByRank = Array.AsReadOnly(new[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }),
                    StaminaBase = 0,
                    StaminaPerRank = 0,
                    AllowedWeaponsMask = (1u << (int)ItemKind.Bow)
                },
                new Skill
                {
                    Code = 0x04010003,
                    Name = "Wind Arrow",
                    Description = "Imbues an arrow with wind properties to increase attack power",
                    IconKey = "TA14 W_arrow",
                    Tier = JobTier.Job1,
                    Category = SkillCategory.ActiveAttack,
                    RequiredLevel = 14,
                    MouseUse = SkillMouseUse.Both,
                    ManaCost = 12,
                    ManaByRank = Array.AsReadOnly(new[] { 12, 14, 16, 18, 20, 23, 26, 29, 32, 35 }),
                    StaminaBase = 25,
                    StaminaPerRank = 2,
                    AllowedWeaponsMask = (1u << (int)ItemKind.Bow)
                },
                new Skill
                {
                    Code = 0x04010004,
                    Name = "Perfect Aim",
                    Description = "Makes an aimed shot with increased Attack Rating and Power",
                    IconKey = "TA17 P_aim",
                    Tier = JobTier.Job1,
                    Category = SkillCategory.ActiveAttack,
                    RequiredLevel = 17,
                    MouseUse = SkillMouseUse.Both,
                    ManaCost = 14,
                    ManaByRank = Array.AsReadOnly(new[] { 14, 16, 18, 20, 22, 25, 28, 31, 35, 39 }),
                    StaminaBase = 29,
                    StaminaPerRank = 2,
                    AllowedWeaponsMask = (1u << (int)ItemKind.Bow)
                },
            }),
            [PlayerClass.Knight] = Array.AsReadOnly(new Skill[]
            {
                new Skill
                {
                    Code = 0x05010001,
                    Name = "Sword Blast",
                    Description = "throws a devastating sword to attack enemies",
                    IconKey = "MN10 S_Blast",
                    Tier = JobTier.Job1,
                    Category = SkillCategory.ActiveAttack,
                    RequiredLevel = 10,
                    MouseUse = SkillMouseUse.Both,
                    ManaCost = 8,
                    ManaByRank = Array.AsReadOnly(new[] { 8, 10, 12, 15, 18, 21, 25, 29, 33, 38 }),
                    StaminaBase = 22,
                    StaminaPerRank = 2,
                    AllowedWeaponsMask = (1u << (int)ItemKind.Sword)
                },
                new Skill
                {
                    Code = 0x05010002,
                    Name = "Holy Body",
                    Description = "become holy to increase absorption against undead units.",
                    IconKey = "MN12 H_Body",
                    Tier = JobTier.Job1,
                    Category = SkillCategory.Buff,
                    RequiredLevel = 12,
                    MouseUse = SkillMouseUse.Right,
                    ManaCost = 18,
                    ManaByRank = Array.AsReadOnly(new[] { 18, 21, 24, 27, 30, 34, 38, 42, 46, 50 }),
                    StaminaBase = 28,
                    StaminaPerRank = 2,
                    AllowedWeaponsMask = 0
                },
                new Skill
                {
                    Code = 0x05010003,
                    Name = "Physical Training",
                    Description = "physical training increases stamina permanently",
                    IconKey = "MN14 P_Traning",
                    Tier = JobTier.Job1,
                    Category = SkillCategory.Passive,
                    RequiredLevel = 14,
                    MouseUse = SkillMouseUse.None,
                    ManaCost = 0,
                    ManaByRank = Array.AsReadOnly(new[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }),
                    StaminaBase = 0,
                    StaminaPerRank = 0,
                    AllowedWeaponsMask = 0
                },
                new Skill
                {
                    Code = 0x05010004,
                    Name = "Double Crash",
                    Description = "makes 2 consecutive attacks to enemies.",
                    IconKey = "MN17 D_Crash",
                    Tier = JobTier.Job1,
                    Category = SkillCategory.MultiHit,
                    RequiredLevel = 17,
                    MouseUse = SkillMouseUse.Right,
                    ManaCost = 18,
                    ManaByRank = Array.AsReadOnly(new[] { 18, 20, 22, 24, 27, 30, 33, 36, 39, 42 }),
                    StaminaBase = 35,
                    StaminaPerRank = 2,
                    AllowedWeaponsMask = (1u << (int)ItemKind.Axe) | (1u << (int)ItemKind.Claw) | (1u << (int)ItemKind.Sword)
                },
            }),
            [PlayerClass.Atalanta] = Array.AsReadOnly(new Skill[]
            {
                new Skill
                {
                    Code = 0x06010001,
                    Name = "Shield Strike",
                    Description = "hurls a shield to attack enemies.",
                    IconKey = "MA10 S_Strike",
                    Tier = JobTier.Job1,
                    Category = SkillCategory.AreaAttack,
                    RequiredLevel = 10,
                    MouseUse = SkillMouseUse.Right,
                    ManaCost = 16,
                    ManaByRank = Array.AsReadOnly(new[] { 16, 18, 20, 22, 24, 27, 30, 33, 36, 40 }),
                    StaminaBase = 23,
                    StaminaPerRank = 2,
                    AllowedWeaponsMask = (1u << (int)ItemKind.Shield)
                },
                new Skill
                {
                    Code = 0x06010002,
                    Name = "Farina",
                    Description = "throws a spear that creates illusions",
                    IconKey = "MA12 Farina",
                    Tier = JobTier.Job1,
                    Category = SkillCategory.ActiveAttack,
                    RequiredLevel = 12,
                    MouseUse = SkillMouseUse.Both,
                    ManaCost = 17,
                    ManaByRank = Array.AsReadOnly(new[] { 17, 19, 21, 23, 26, 29, 32, 35, 38, 41 }),
                    StaminaBase = 26,
                    StaminaPerRank = 2,
                    AllowedWeaponsMask = (1u << (int)ItemKind.ThrowingWeapon)
                },
                new Skill
                {
                    Code = 0x06010003,
                    Name = "Throwing Mastery",
                    Description = "train javelin attacks to increase damage permanently.",
                    IconKey = "MA14 T_Mastery",
                    Tier = JobTier.Job1,
                    Category = SkillCategory.Passive,
                    RequiredLevel = 14,
                    MouseUse = SkillMouseUse.None,
                    ManaCost = 0,
                    ManaByRank = Array.AsReadOnly(new[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }),
                    StaminaBase = 0,
                    StaminaPerRank = 0,
                    AllowedWeaponsMask = (1u << (int)ItemKind.ThrowingWeapon)
                },
                new Skill
                {
                    Code = 0x06010004,
                    Name = "Bigger Spear",
                    Description = "attack an enemy with holy javelin.",
                    IconKey = "MA17 V_Spear",
                    Tier = JobTier.Job1,
                    Category = SkillCategory.ActiveAttack,
                    RequiredLevel = 17,
                    MouseUse = SkillMouseUse.Both,
                    ManaCost = 19,
                    ManaByRank = Array.AsReadOnly(new[] { 19, 21, 23, 25, 28, 31, 34, 37, 40, 43 }),
                    StaminaBase = 32,
                    StaminaPerRank = 2,
                    AllowedWeaponsMask = (1u << (int)ItemKind.Pole) | (1u << (int)ItemKind.ThrowingWeapon)
                },
            }),
            [PlayerClass.Priestess] = Array.AsReadOnly(new Skill[]
            {
                new Skill
                {
                    Code = 0x07010001,
                    Name = "Healing",
                    Description = "heals life with its holy magic.",
                    IconKey = "MP10 Healing",
                    Tier = JobTier.Job1,
                    Category = SkillCategory.Heal,
                    RequiredLevel = 10,
                    MouseUse = SkillMouseUse.Right,
                    ManaCost = 11,
                    ManaByRank = Array.AsReadOnly(new[] { 11, 14, 17, 20, 24, 28, 32, 37, 42, 47 }),
                    StaminaBase = 15,
                    StaminaPerRank = 1,
                    AllowedWeaponsMask = (1u << (int)ItemKind.Staff)
                },
                new Skill
                {
                    Code = 0x07010002,
                    Name = "Holy Bolt",
                    Description = "hurls a holy bolt to enemies.",
                    IconKey = "MP12 HolyBolt",
                    Tier = JobTier.Job1,
                    Category = SkillCategory.ActiveAttack,
                    RequiredLevel = 12,
                    MouseUse = SkillMouseUse.Both,
                    ManaCost = 14,
                    ManaByRank = Array.AsReadOnly(new[] { 14, 17, 20, 23, 27, 31, 35, 40, 45, 50 }),
                    StaminaBase = 11,
                    StaminaPerRank = 1,
                    AllowedWeaponsMask = (1u << (int)ItemKind.Staff)
                },
                new Skill
                {
                    Code = 0x07010003,
                    Name = "Multi Spark",
                    Description = "Releases magical sparks to attack enemies.",
                    IconKey = "MP14 M_Spark",
                    Tier = JobTier.Job1,
                    Category = SkillCategory.MultiHit,
                    RequiredLevel = 14,
                    MouseUse = SkillMouseUse.Right,
                    ManaCost = 16,
                    ManaByRank = Array.AsReadOnly(new[] { 16, 19, 22, 25, 29, 33, 37, 42, 47, 52 }),
                    StaminaBase = 14,
                    StaminaPerRank = 1,
                    AllowedWeaponsMask = (1u << (int)ItemKind.Staff)
                },
                new Skill
                {
                    Code = 0x07010004,
                    Name = "Holy Mind",
                    Description = "It weakens enemy's attack in a given time.",
                    IconKey = "MP17 HolyMind",
                    Tier = JobTier.Job1,
                    Category = SkillCategory.Debuff,
                    RequiredLevel = 17,
                    MouseUse = SkillMouseUse.Right,
                    ManaCost = 22,
                    ManaByRank = Array.AsReadOnly(new[] { 22, 25, 28, 32, 36, 40, 45, 50, 55, 60 }),
                    StaminaBase = 19,
                    StaminaPerRank = 1,
                    AllowedWeaponsMask = (1u << (int)ItemKind.Staff)
                },
            }),
            [PlayerClass.Magician] = Array.AsReadOnly(new Skill[]
            {
                new Skill
                {
                    Code = 0x08010001,
                    Name = "Agony",
                    Description = "sacrifice oneself to increase spirit.",
                    IconKey = "MM10 Agony",
                    Tier = JobTier.Job1,
                    Category = SkillCategory.Buff,
                    RequiredLevel = 10,
                    MouseUse = SkillMouseUse.Right,
                    ManaCost = 5,
                    ManaByRank = Array.AsReadOnly(new[] { 5, 6, 7, 8, 9, 10, 11, 12, 13, 14 }),
                    StaminaBase = 12,
                    StaminaPerRank = 1,
                    AllowedWeaponsMask = (1u << (int)ItemKind.Staff)
                },
                new Skill
                {
                    Code = 0x08010002,
                    Name = "Fire Bolt",
                    Description = "calls down small fireballs to attack enemies",
                    IconKey = "MM12 FireBolt",
                    Tier = JobTier.Job1,
                    Category = SkillCategory.ActiveAttack,
                    RequiredLevel = 12,
                    MouseUse = SkillMouseUse.Both,
                    ManaCost = 16,
                    ManaByRank = Array.AsReadOnly(new[] { 16, 18, 20, 22, 25, 28, 31, 34, 38, 42 }),
                    StaminaBase = 11,
                    StaminaPerRank = 1,
                    AllowedWeaponsMask = (1u << (int)ItemKind.Staff)
                },
                new Skill
                {
                    Code = 0x08010003,
                    Name = "Zenith",
                    Description = "Increase element features with strong spirit for a given time.",
                    IconKey = "MM14 Zenith",
                    Tier = JobTier.Job1,
                    Category = SkillCategory.Buff,
                    RequiredLevel = 14,
                    MouseUse = SkillMouseUse.Right,
                    ManaCost = 18,
                    ManaByRank = Array.AsReadOnly(new[] { 18, 20, 22, 25, 28, 31, 35, 39, 43, 48 }),
                    StaminaBase = 15,
                    StaminaPerRank = 1,
                    AllowedWeaponsMask = (1u << (int)ItemKind.Staff)
                },
                new Skill
                {
                    Code = 0x08010004,
                    Name = "Fire Ball",
                    Description = "calls down huge fireballs to attack enemies",
                    IconKey = "MM17 FireBall",
                    Tier = JobTier.Job1,
                    Category = SkillCategory.AreaAttack,
                    RequiredLevel = 17,
                    MouseUse = SkillMouseUse.Right,
                    ManaCost = 22,
                    ManaByRank = Array.AsReadOnly(new[] { 22, 24, 26, 28, 31, 34, 38, 42, 46, 50 }),
                    StaminaBase = 19,
                    StaminaPerRank = 1,
                    AllowedWeaponsMask = (1u << (int)ItemKind.Staff)
                },
            }),
            [PlayerClass.Assassin] = Array.AsReadOnly(new Skill[]
            {
                new Skill
                {
                    Code = 0x10010001,
                    Name = "Stinger",
                    Description = "Dash straight into the enemy and deal damage.",
                    IconKey = "TA10 Stingger",
                    Tier = JobTier.Job1,
                    Category = SkillCategory.ActiveAttack,
                    RequiredLevel = 10,
                    MouseUse = SkillMouseUse.Both,
                    ManaCost = 8,
                    ManaByRank = Array.AsReadOnly(new[] { 8, 10, 12, 15, 18, 21, 25, 29, 33, 38 }),
                    StaminaBase = 22,
                    StaminaPerRank = 2,
                    AllowedWeaponsMask = (1u << (int)ItemKind.Dagger)
                },
                new Skill
                {
                    Code = 0x10010002,
                    Name = "Running Hit",
                    Description = "Deals continuous damage to the enemy while spinning",
                    IconKey = "TA12 R_Hit",
                    Tier = JobTier.Job1,
                    Category = SkillCategory.MultiHit,
                    RequiredLevel = 12,
                    MouseUse = SkillMouseUse.Both,
                    ManaCost = 14,
                    ManaByRank = Array.AsReadOnly(new[] { 14, 16, 18, 20, 23, 26, 29, 32, 35, 39 }),
                    StaminaBase = 27,
                    StaminaPerRank = 2,
                    AllowedWeaponsMask = (1u << (int)ItemKind.Dagger)
                },
                new Skill
                {
                    Code = 0x10010003,
                    Name = "Dual Wield Mastery",
                    Description = "Permanently raises the base attack power of dagger-type weapons",
                    IconKey = "TA14 D_Mastery",
                    Tier = JobTier.Job1,
                    Category = SkillCategory.Passive,
                    RequiredLevel = 14,
                    MouseUse = SkillMouseUse.None,
                    ManaCost = 0,
                    ManaByRank = Array.AsReadOnly(new[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }),
                    StaminaBase = 0,
                    StaminaPerRank = 0,
                    AllowedWeaponsMask = (1u << (int)ItemKind.Dagger)
                },
                new Skill
                {
                    Code = 0x10010004,
                    Name = "Wisp",
                    Description = "Reduces targeted enemy damage for a period of time",
                    IconKey = "TA17 Wisp",
                    Tier = JobTier.Job1,
                    Category = SkillCategory.Debuff,
                    RequiredLevel = 17,
                    MouseUse = SkillMouseUse.Both,
                    ManaCost = 18,
                    ManaByRank = Array.AsReadOnly(new[] { 18, 20, 22, 24, 26, 29, 33, 37, 41, 45 }),
                    StaminaBase = 37,
                    StaminaPerRank = 2,
                    AllowedWeaponsMask = (1u << (int)ItemKind.Dagger)
                },
            }),
            [PlayerClass.Shaman] = Array.AsReadOnly(new Skill[]
            {
                new Skill
                {
                    Code = 0x50010001,
                    Name = "Dark Bolt",
                    Description = "Hits the enemy with an almighty bolt of dark magic created with sorcery",
                    IconKey = "MS10 Darkbolt",
                    Tier = JobTier.Job1,
                    Category = SkillCategory.ActiveAttack,
                    RequiredLevel = 10,
                    MouseUse = SkillMouseUse.Both,
                    ManaCost = 14,
                    ManaByRank = Array.AsReadOnly(new[] { 14, 16, 18, 20, 22, 24, 26, 28, 30, 32 }),
                    StaminaBase = 8,
                    StaminaPerRank = 1,
                    AllowedWeaponsMask = (1u << (int)ItemKind.Phantom)
                },
                new Skill
                {
                    Code = 0x50010002,
                    Name = "Dark Wave",
                    Description = "Performs a consecutive area attack with almighty bolts of dark magic",
                    IconKey = "MS12 Darkwave",
                    Tier = JobTier.Job1,
                    Category = SkillCategory.AreaAttack,
                    RequiredLevel = 12,
                    MouseUse = SkillMouseUse.Both,
                    ManaCost = 22,
                    ManaByRank = Array.AsReadOnly(new[] { 22, 24, 26, 28, 31, 34, 38, 42, 46, 50 }),
                    StaminaBase = 19,
                    StaminaPerRank = 1,
                    AllowedWeaponsMask = (1u << (int)ItemKind.Phantom)
                },
                new Skill
                {
                    Code = 0x50010003,
                    Name = "Inertia",
                    Description = "Throws a curse onto the enemies, slowing them down temporarily",
                    IconKey = "MS14 Curselazy",
                    Tier = JobTier.Job1,
                    Category = SkillCategory.Debuff,
                    RequiredLevel = 14,
                    MouseUse = SkillMouseUse.Right,
                    ManaCost = 25,
                    ManaByRank = Array.AsReadOnly(new[] { 25, 28, 32, 36, 40, 45, 50, 55, 60, 65 }),
                    StaminaBase = 20,
                    StaminaPerRank = 1,
                    AllowedWeaponsMask = (1u << (int)ItemKind.Phantom)
                },
                new Skill
                {
                    Code = 0x50010004,
                    Name = "Inner Peace",
                    Description = "Through mental training, increases MP potion recovery and mana capacity permanently",
                    IconKey = "MS17 I_peace",
                    Tier = JobTier.Job1,
                    Category = SkillCategory.Passive,
                    RequiredLevel = 17,
                    MouseUse = SkillMouseUse.None,
                    ManaCost = 0,
                    ManaByRank = Array.AsReadOnly(new[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }),
                    StaminaBase = 0,
                    StaminaPerRank = 0,
                    AllowedWeaponsMask = 0
                },
            }),
        };

    public static IReadOnlyList<Skill> For(PlayerClass playerClass) => Classes[playerClass];

    public static void AddTo(IList<Skill> destination, PlayerClass playerClass)
    {
        foreach (var skill in For(playerClass))
        {
            destination.Add(skill);
        }
    }
}
