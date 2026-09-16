using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Skills;

namespace PristonTale.Simulation.Characters.Classes;

public sealed class MechanicianClass : CharacterClass
{
    public MechanicianClass()
        : base(BuildDefinition(
            PlayerClass.Mechanician,
            "Mechanician",
            jobCode: 2,
            JobBitCode.Mechanician,
            CharacterBrood.Tempskron,
            bodyInx: "char\\tmABCD\\A001.inx",
            headInx: "char\\tmABCD\\tmh-a01.inx",
            armorTableIndex: 0,
            new CharacterAttributes(24, 8, 25, 18, 24),
            new CharacterFunctionProfile(65, 2, 2, 2, 2, 2, 0)))
    {
    }

    protected override void DefineSkills(IList<Skill> skills)
    {
        FirstTierSkills.AddTo(skills, PlayerClass.Mechanician);
        skills.Add(Skill.Of("Great Smash", JobTier.Job2, SkillCategory.ActiveAttack, "TM20 G_Smash"));
        skills.Add(Skill.Of("Maximize", JobTier.Job2, SkillCategory.Buff, "TM23 Maximize"));
        skills.Add(Skill.Of("Automation", JobTier.Job2, SkillCategory.Buff, "TM26 Automation"));
        skills.Add(Skill.Of("Spark", JobTier.Job2, SkillCategory.ActiveAttack, "TM30 Spark"));
        skills.Add(Skill.Of("Metal Armor", JobTier.Job3, SkillCategory.Buff, "TM40 M_Armor"));
        skills.Add(Skill.Of("Grand Smash", JobTier.Job3, SkillCategory.ActiveAttack, "TM43 Grand_Smash"));
        skills.Add(Skill.Of("Mechanic Weapon", JobTier.Job3, SkillCategory.Buff, "TM44 M_Weapon"));
        skills.Add(Skill.Of("Spark Shield", JobTier.Job3, SkillCategory.Buff, "TM50 S_Shield"));
        skills.Add(Skill.Of("Impulsion", JobTier.Job4, SkillCategory.ActiveAttack, "TM60 Impulsion"));
        skills.Add(Skill.Of("Compulsion", JobTier.Job4, SkillCategory.Buff, "TM63 Compulsion"));
        skills.Add(Skill.Of("Magnetic Sphere", JobTier.Job4, SkillCategory.AreaAttack, "TM66 M_Sphere"));
        skills.Add(Skill.Of("Metal Golem", JobTier.Job4, SkillCategory.Summon, "TM70 M_Golem"));
    }
}