using PristonTale.Simulation.Skills;

namespace PristonTale.Simulation.Characters.Classes;

public sealed class ShamanClass : CharacterClass
{
    public ShamanClass()
        : base(BuildDefinition(
            PlayerClass.Shaman,
            "Shaman",
            jobCode: 10,
            JobBitCode.Shaman,
            CharacterBrood.Morion,
            bodyInx: "char\\tmABCD\\me001.inx",
            headInx: "char\\tmABCD\\Mmh-E01.inx",
            armorTableIndex: 9,
            new CharacterAttributes(15, 27, 20, 15, 22),
            new CharacterFunctionProfile(190, 5, 1, 3, 3, 2, 1)))
    {
    }

    protected override void DefineSkills(IList<Skill> skills)
    {
        FirstTierSkills.AddTo(skills, PlayerClass.Shaman);
        skills.Add(Skill.Of("Spiritual Flare", JobTier.Job2, SkillCategory.AreaAttack, "MS20 S_Flare"));
        skills.Add(Skill.Of("Spiritual Manacle", JobTier.Job2, SkillCategory.Debuff, "MS23 S_Manacle"));
        skills.Add(Skill.Of("Chasing Hunt", JobTier.Job2, SkillCategory.Buff, "MS26 C_Hunt"));
        skills.Add(Skill.Of("Advent Migal", JobTier.Job2, SkillCategory.Buff, "MS30 A_Migal"));
        skills.Add(Skill.Of("Rainmaker", JobTier.Job3, SkillCategory.AreaAttack, "MS40 R_Maker"));
        skills.Add(Skill.Of("Phantom Call", JobTier.Job3, SkillCategory.Summon, "MS43 L_Ghost"));
        skills.Add(Skill.Of("Haunt", JobTier.Job3, SkillCategory.ActiveAttack, "MS46 Haunt"));
        skills.Add(Skill.Of("Scratch", JobTier.Job3, SkillCategory.ActiveAttack, "MS50 Scratch"));
        skills.Add(Skill.Of("Recall Hestian", JobTier.Job4, SkillCategory.Summon, "MS60 R_Knight"));
        skills.Add(Skill.Of("Judgement", JobTier.Job4, SkillCategory.ActiveAttack, "MS63 Judge"));
        skills.Add(Skill.Of("Advent Midranda", JobTier.Job4, SkillCategory.Buff, "MS66 A_Midranda"));
        skills.Add(Skill.Of("Mourning Pray", JobTier.Job4, SkillCategory.Buff, "MS70 M_pray"));
    }
}
