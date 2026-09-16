using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Skills;

namespace PristonTale.Simulation.Characters.Classes;

public sealed class PriestessClass : CharacterClass
{
    public PriestessClass()
        : base(BuildDefinition(
            PlayerClass.Priestess,
            "Priestess",
            jobCode: 8,
            JobBitCode.Priestess,
            CharacterBrood.Morion,
            bodyInx: "char\\tmABCD\\mC001.inx",
            headInx: "char\\tmABCD\\Mfh-C01.inx",
            armorTableIndex: 6,
            new CharacterAttributes(15, 28, 21, 15, 20),
            new CharacterFunctionProfile(200, 4, 1, 3, 3, 2, 2)))
    {
    }

    protected override void DefineSkills(IList<Skill> skills)
    {
        FirstTierSkills.AddTo(skills, PlayerClass.Priestess);
        skills.Add(Skill.Of("Meditation", JobTier.Job2, SkillCategory.Buff, "MP20 Meditation"));
        skills.Add(Skill.Of("Divine Lightning", JobTier.Job2, SkillCategory.AreaAttack, "MP23 D_Lightning"));
        skills.Add(Skill.Of("Holy Reflection", JobTier.Job2, SkillCategory.Buff, "MP26 H_Reflection"));
        skills.Add(Skill.Of("Grand Healing", JobTier.Job2, SkillCategory.Heal, "MP30 G_Healing"));
        skills.Add(Skill.Of("Vigor Ball", JobTier.Job3, SkillCategory.ActiveAttack, "MP40 V_Ball"));
        skills.Add(Skill.Of("Resurrection", JobTier.Job3, SkillCategory.Heal, "MP43 Resurrection"));
        skills.Add(Skill.Of("Extinction", JobTier.Job3, SkillCategory.AreaAttack, "MP46 Extinction"));
        skills.Add(Skill.Of("Virtual Life", JobTier.Job3, SkillCategory.Buff, "MP50 V_Life"));
        skills.Add(Skill.Of("Glacial Spike", JobTier.Job4, SkillCategory.ActiveAttack, "MP60 G_Spike"));
        skills.Add(Skill.Of("Regeneration Field", JobTier.Job4, SkillCategory.Buff, "MP63 R_Field"));
        skills.Add(Skill.Of("Chain Lightning", JobTier.Job4, SkillCategory.ActiveAttack, "MP66 C_Lightning"));
        skills.Add(Skill.Of("Summon Muspell", JobTier.Job4, SkillCategory.Summon, "MP70 S_Muspell"));
    }
}