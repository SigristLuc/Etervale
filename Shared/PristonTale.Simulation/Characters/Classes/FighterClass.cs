using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Skills;

namespace PristonTale.Simulation.Characters.Classes;

public sealed class FighterClass : CharacterClass
{
    public FighterClass()
        : base(BuildDefinition(
            PlayerClass.Fighter,
            "Fighter",
            jobCode: 1,
            JobBitCode.Fighter,
            CharacterBrood.Tempskron,
            bodyInx: "char\\tmABCD\\B001.inx",
            headInx: "char\\tmABCD\\tmh-b01.inx",
            armorTableIndex: 1,
            new CharacterAttributes(28, 6, 21, 17, 27),
            new CharacterFunctionProfile(70, 1, 3, 1, 1, 2, 0)))
    {
    }

    protected override void DefineSkills(IList<Skill> skills)
    {
        FirstTierSkills.AddTo(skills, PlayerClass.Fighter);
        skills.Add(Skill.Of("Triple Impact", JobTier.Job2, SkillCategory.MultiHit, "TF20 T_Impact"));
        skills.Add(Skill.Of("Brutal Swing", JobTier.Job2, SkillCategory.AreaAttack, "TF23 B_Swing"));
        skills.Add(Skill.Of("Roar", JobTier.Job2, SkillCategory.AreaAttack, "TF26 Roar"));
        skills.Add(Skill.Of("Rage of Zecram", JobTier.Job2, SkillCategory.ActiveAttack, "TF30 R_Zecram"));
        skills.Add(Skill.Of("Concentration", JobTier.Job3, SkillCategory.Buff, "TF40 Concentration"));
        skills.Add(Skill.Of("Avanging Crash", JobTier.Job3, SkillCategory.ActiveAttack, "TF43 A_Crash"));
        skills.Add(Skill.Of("Swift Axe", JobTier.Job3, SkillCategory.Buff, "TF46 S_Axe"));
        skills.Add(Skill.Of("Bone Crash", JobTier.Job3, SkillCategory.ActiveAttack, "TF50 B_Crash"));
        skills.Add(Skill.Of("Destroyer", JobTier.Job4, SkillCategory.ActiveAttack, "TF60 Destoryer"));
        skills.Add(Skill.Of("Berserker", JobTier.Job4, SkillCategory.Buff, "TF63 Berserker"));
        skills.Add(Skill.Of("Cyclone Strike", JobTier.Job4, SkillCategory.AreaAttack, "TF66 C_Strike"));
        skills.Add(Skill.Of("Boost Health", JobTier.Job4, SkillCategory.Buff, "TF70 B_Health"));
    }
}