using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Skills;

namespace PristonTale.Simulation.Characters.Classes;

public sealed class AtalantaClass : CharacterClass
{
    public AtalantaClass()
        : base(BuildDefinition(
            PlayerClass.Atalanta,
            "Atalanta",
            jobCode: 5,
            JobBitCode.Atalanta,
            CharacterBrood.Morion,
            bodyInx: "char\\tmABCD\\mB001.inx",
            headInx: "char\\tmABCD\\Mfh-B01.inx",
            armorTableIndex: 5,
            new CharacterAttributes(23, 15, 19, 19, 23),
            new CharacterFunctionProfile(200, 2, 2, 2, 2, 1, 0)))
    {
    }

    protected override void DefineSkills(IList<Skill> skills)
    {
        FirstTierSkills.AddTo(skills, PlayerClass.Atalanta);
        skills.Add(Skill.Of("Windy", JobTier.Job2, SkillCategory.Buff, "MA20 Windy"));
        skills.Add(Skill.Of("Twist Javelin", JobTier.Job2, SkillCategory.ActiveAttack, "MA23 T_Javelin"));
        skills.Add(Skill.Of("Soul Sucker", JobTier.Job2, SkillCategory.ActiveAttack, "MA26 S_Sucker"));
        skills.Add(Skill.Of("Fire Javelin", JobTier.Job2, SkillCategory.ActiveAttack, "MA30 F_Javelin"));
        skills.Add(Skill.Of("Split Javelin", JobTier.Job3, SkillCategory.AreaAttack, "MA40 Split_Javelin"));
        skills.Add(Skill.Of("Triumph of Valhalla", JobTier.Job3, SkillCategory.Buff, "MA43 T_Of_Valhalla"));
        skills.Add(Skill.Of("Lightning Javelin", JobTier.Job3, SkillCategory.ActiveAttack, "MA46 L_Javelin"));
        skills.Add(Skill.Of("Storm Javelin", JobTier.Job3, SkillCategory.MultiHit, "MA50 Storm_Javelin"));
        skills.Add(Skill.Of("Hall of Valhalla", JobTier.Job4, SkillCategory.Buff, "MA60 H_O_Valhalla"));
        skills.Add(Skill.Of("X-Rage", JobTier.Job4, SkillCategory.Buff, "MA63 X_Rage"));
        skills.Add(Skill.Of("Frost Javelin", JobTier.Job4, SkillCategory.ActiveAttack, "MA66 F_Javelin"));
        skills.Add(Skill.Of("Vengeance", JobTier.Job4, SkillCategory.Buff, "MA70 Vengeance"));
    }
}