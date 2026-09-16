using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Skills;

namespace PristonTale.Simulation.Characters.Classes;

public sealed class KnightClass : CharacterClass
{
    public KnightClass()
        : base(BuildDefinition(
            PlayerClass.Knight,
            "Knight",
            jobCode: 6,
            JobBitCode.Knight,
            CharacterBrood.Morion,
            bodyInx: "char\\tmABCD\\mA001.inx",
            headInx: "char\\tmABCD\\Mmh-A01.inx",
            armorTableIndex: 4,
            new CharacterAttributes(26, 13, 17, 19, 24),
            new CharacterFunctionProfile(190, 2, 2, 2, 1, 2, 0)))
    {
    }

    protected override void DefineSkills(IList<Skill> skills)
    {
        FirstTierSkills.AddTo(skills, PlayerClass.Knight);
        skills.Add(Skill.Of("Holy Valor", JobTier.Job2, SkillCategory.Buff, "MN20 H_Valor"));
        skills.Add(Skill.Of("Brandish", JobTier.Job2, SkillCategory.AreaAttack, "MN23 Brandish"));
        skills.Add(Skill.Of("Piercing", JobTier.Job2, SkillCategory.ActiveAttack, "MN26 Piercing"));
        skills.Add(Skill.Of("Drastic Spirit", JobTier.Job2, SkillCategory.Buff, "MN30 D_Spirit"));
        skills.Add(Skill.Of("Sword Mastery", JobTier.Job3, SkillCategory.Passive, "MN40 S_Mastery"));
        skills.Add(Skill.Of("Divine Inhalation", JobTier.Job3, SkillCategory.Buff, "MN43 D_Inhalation"));
        skills.Add(Skill.Of("Holy Incantation", JobTier.Job3, SkillCategory.Debuff, "MN46 H_Incantation"));
        skills.Add(Skill.Of("Grand Cross", JobTier.Job3, SkillCategory.AreaAttack, "MN50 G_Cross"));
        skills.Add(Skill.Of("Sword of Justice", JobTier.Job4, SkillCategory.ActiveAttack, "MN60 S_O_Justice"));
        skills.Add(Skill.Of("Godly Shield", JobTier.Job4, SkillCategory.Buff, "MN63 G_Shield"));
        skills.Add(Skill.Of("God Bless", JobTier.Job4, SkillCategory.Buff, "MN66 G_Bless"));
        skills.Add(Skill.Of("Divine Piercing", JobTier.Job4, SkillCategory.MultiHit, "MN70 D_Piercing"));
    }
}