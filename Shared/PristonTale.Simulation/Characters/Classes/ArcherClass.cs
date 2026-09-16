using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Skills;

namespace PristonTale.Simulation.Characters.Classes;

public sealed class ArcherClass : CharacterClass
{
    public ArcherClass()
        : base(BuildDefinition(
            PlayerClass.Archer,
            "Archer",
            jobCode: 3,
            JobBitCode.Archer,
            CharacterBrood.Tempskron,
            bodyInx: "char\\tmABCD\\D001.inx",
            headInx: "char\\tmABCD\\tfh-D01.inx",
            armorTableIndex: 3,
            new CharacterAttributes(17, 11, 21, 27, 23),
            new CharacterFunctionProfile(80, 3, 3, 2, 3, 1, 0)))
    {
    }

    protected override void DefineSkills(IList<Skill> skills)
    {
        FirstTierSkills.AddTo(skills, PlayerClass.Archer);
        skills.Add(Skill.Of("Dions Eye", JobTier.Job2, SkillCategory.Buff, "TA20 D_Eye"));
        skills.Add(Skill.Of("Falcon", JobTier.Job2, SkillCategory.Buff, "TA23 Falcon"));
        skills.Add(Skill.Of("Arrow of Rage", JobTier.Job2, SkillCategory.ActiveAttack, "TA26 A_Rage"));
        skills.Add(Skill.Of("Avalanche", JobTier.Job2, SkillCategory.MultiHit, "TA30 Avalanchie"));
        skills.Add(Skill.Of("Elemental Shot", JobTier.Job3, SkillCategory.MultiHit, "TA40 E_Shot"));
        skills.Add(Skill.Of("Golden Falcon", JobTier.Job3, SkillCategory.Buff, "TA43 G_Falcon"));
        skills.Add(Skill.Of("Bomb Shot", JobTier.Job3, SkillCategory.MultiHit, "TA46 B_Shot"));
        skills.Add(Skill.Of("Perforation", JobTier.Job3, SkillCategory.ActiveAttack, "TA50 Perforation"));
        skills.Add(Skill.Of("Recall Wolverin", JobTier.Job4, SkillCategory.Summon, "TA60 R_Wolverin"));
        skills.Add(Skill.Of("Evasion Mastery", JobTier.Job4, SkillCategory.Passive, "TA63 E_Mastery"));
        skills.Add(Skill.Of("Phoenix Shot", JobTier.Job4, SkillCategory.ActiveAttack, "TA66 P_Shot"));
        skills.Add(Skill.Of("Force of Nature", JobTier.Job4, SkillCategory.Buff, "TA70 F_O_Nature"));
    }
}