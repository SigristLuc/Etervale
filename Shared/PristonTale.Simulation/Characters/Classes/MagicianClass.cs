using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Skills;

namespace PristonTale.Simulation.Characters.Classes;

public sealed class MagicianClass : CharacterClass
{
    public MagicianClass()
        : base(BuildDefinition(
            PlayerClass.Magician,
            "Magician",
            jobCode: 7,
            JobBitCode.Magician,
            CharacterBrood.Morion,
            bodyInx: "char\\tmABCD\\mD001.inx",
            headInx: "char\\tmABCD\\Mmh-D01.inx",
            armorTableIndex: 7,
            new CharacterAttributes(16, 29, 19, 14, 21),
            new CharacterFunctionProfile(190, 5, 1, 3, 3, 2, 1)))
    {
    }

    protected override void DefineSkills(IList<Skill> skills)
    {
        FirstTierSkills.AddTo(skills, PlayerClass.Magician);
        skills.Add(Skill.Of("Mental Mastery", JobTier.Job2, SkillCategory.Passive, "MM20 M_Mastery"));
        skills.Add(Skill.Of("Watornado", JobTier.Job2, SkillCategory.AreaAttack, "MM23 Watornado"));
        skills.Add(Skill.Of("Enchant Weapon", JobTier.Job2, SkillCategory.Buff, "MM26 E_Weapon"));
        skills.Add(Skill.Of("Dead Ray", JobTier.Job2, SkillCategory.ActiveAttack, "MM30 D_Ray"));
        skills.Add(Skill.Of("Energy Shield", JobTier.Job3, SkillCategory.Buff, "MM40 E_Shield"));
        skills.Add(Skill.Of("Diastrophism", JobTier.Job3, SkillCategory.AreaAttack, "MM43 Diastrophism"));
        skills.Add(Skill.Of("Spirit Elemental", JobTier.Job3, SkillCategory.Buff, "MM46 S_Elemental"));
        skills.Add(Skill.Of("Dancing Sword", JobTier.Job3, SkillCategory.Buff, "MM50 D_Sword"));
        skills.Add(Skill.Of("Fire Elemental", JobTier.Job4, SkillCategory.Summon, "MM60 F_Elemental"));
        skills.Add(Skill.Of("Flame Wave", JobTier.Job4, SkillCategory.AreaAttack, "MM63 F_Wave"));
        skills.Add(Skill.Of("Distortion", JobTier.Job4, SkillCategory.Debuff, "MM66 Distortion"));
        skills.Add(Skill.Of("Meteo", JobTier.Job4, SkillCategory.AreaAttack, "MM70 Meteo"));
    }
}