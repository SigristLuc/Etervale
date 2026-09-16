using PristonTale.Simulation.Skills;

namespace PristonTale.Simulation.Characters.Classes;

public sealed class AssassinClass : CharacterClass
{
    public AssassinClass()
        : base(BuildDefinition(
            PlayerClass.Assassin,
            "Assassin",
            jobCode: 9,
            JobBitCode.Assassin,
            CharacterBrood.Tempskron,
            bodyInx: "char\\tmABCD\\e001.inx",
            headInx: "char\\tmABCD\\tfh-e01.inx",
            armorTableIndex: 8,
            new CharacterAttributes(25, 10, 22, 20, 22),
            new CharacterFunctionProfile(80, 3, 3, 2, 3, 1, 0)))
    {
    }

    protected override void DefineSkills(IList<Skill> skills)
    {
        FirstTierSkills.AddTo(skills, PlayerClass.Assassin);
        skills.Add(Skill.Of("Venom Thorn", JobTier.Job2, SkillCategory.MultiHit, "TA20 V_Throne"));
        skills.Add(Skill.Of("Alas", JobTier.Job2, SkillCategory.Buff, "TA23 Alas"));
        skills.Add(Skill.Of("Soul Shock", JobTier.Job2, SkillCategory.Debuff, "TA26 S_Shock"));
        skills.Add(Skill.Of("Attack Mastery", JobTier.Job2, SkillCategory.Passive, "TA30 A_Mastery"));
        skills.Add(Skill.Of("Sore Blade", JobTier.Job3, SkillCategory.ActiveAttack, "TA40 S_Sword"));
        skills.Add(Skill.Of("Gust Slash", JobTier.Job3, SkillCategory.ActiveAttack, "TA43 B_Up"));
        skills.Add(Skill.Of("Inpes", JobTier.Job3, SkillCategory.Buff, "TA46 Inpes"));
        skills.Add(Skill.Of("Blind", JobTier.Job3, SkillCategory.Debuff, "TA50 Blind"));
        skills.Add(Skill.Of("Cripple", JobTier.Job4, SkillCategory.Debuff, "TA60 F_Wind"));
        skills.Add(Skill.Of("Assassin Mastery", JobTier.Job4, SkillCategory.Passive, "TA63 F_Mastery"));
        skills.Add(Skill.Of("Polluted", JobTier.Job4, SkillCategory.Debuff, "TA66 Polluted"));
        skills.Add(Skill.Of("Ninja Shadow", JobTier.Job4, SkillCategory.Buff, "TA70 P_Shadow"));
    }
}
