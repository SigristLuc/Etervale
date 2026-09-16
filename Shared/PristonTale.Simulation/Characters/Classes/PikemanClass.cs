using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Skills;

namespace PristonTale.Simulation.Characters.Classes;

public sealed class PikemanClass : CharacterClass
{
    public PikemanClass()
        : base(BuildDefinition(
            PlayerClass.Pikeman,
            "Pikeman",
            jobCode: 4,
            JobBitCode.Pikeman,
            CharacterBrood.Tempskron,
            bodyInx: "char\\tmABCD\\C001.inx",
            headInx: "char\\tmABCD\\tmh-c01.inx",
            armorTableIndex: 2,
            new CharacterAttributes(26, 9, 20, 19, 25),
            new CharacterFunctionProfile(75, 1, 3, 1, 1, 2, 0)))
    {
    }

    protected override void DefineSkills(IList<Skill> skills)
    {
        FirstTierSkills.AddTo(skills, PlayerClass.Pikeman);
        skills.Add(Skill.Of("Ground Pike", JobTier.Job2, SkillCategory.AreaAttack, "TP20 G_Pike"));
        skills.Add(Skill.Of("Tornado", JobTier.Job2, SkillCategory.AreaAttack, "TP23 Tornado"));
        skills.Add(Skill.Of("Weapon Defence Mastery", JobTier.Job2, SkillCategory.Passive, "TP26 W_D_Mastery"));
        skills.Add(Skill.Of("Expansion", JobTier.Job2, SkillCategory.Buff, "TP30 Expasion"));
        skills.Add(Skill.Of("Venom Spear", JobTier.Job3, SkillCategory.ActiveAttack, "TP40 V_Spear"));
        skills.Add(Skill.Of("Vanish", JobTier.Job3, SkillCategory.Buff, "TP43 Vanish"));
        skills.Add(Skill.Of("Critical Mastery", JobTier.Job3, SkillCategory.Passive, "TP46 C_Mastery"));
        skills.Add(Skill.Of("Chain Lance", JobTier.Job3, SkillCategory.ActiveAttack, "TP50 C_Lance"));
        skills.Add(Skill.Of("Assassin Eye", JobTier.Job4, SkillCategory.Buff, "TP60 A_Eye"));
        skills.Add(Skill.Of("Charging Strike", JobTier.Job4, SkillCategory.MultiHit, "TP63 C_Strike"));
        skills.Add(Skill.Of("Vague", JobTier.Job4, SkillCategory.Buff, "TP66 Vague"));
        skills.Add(Skill.Of("Shadow Master", JobTier.Job4, SkillCategory.MultiHit, "TP70 S_Master"));
    }
}