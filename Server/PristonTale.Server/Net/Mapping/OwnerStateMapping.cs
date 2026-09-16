using Google.Protobuf;
using PristonTale.Protocol.Session;
using PristonTale.Simulation.Characters;

namespace PristonTale.Server.Net.Mapping;

internal static class OwnerStateMapping
{
    public static OwnedCharacterState ToProto(OwnerState state)
    {
        var result = new OwnedCharacterState
        {
            Level = state.Level,
            Experience = state.Experience,
            LevelStart = state.LevelStart,
            NextLevel = state.NextLevel,
            Money = state.Money,
            Hp = state.Hp,
            MaxHp = state.MaxHp,
            Mp = state.Mp,
            MaxMp = state.MaxMp,
            Sp = state.Sp,
            MaxSp = state.MaxSp,
            StatPoints = state.StatPoints,
            SkillPoints = state.SkillPoints,
            Strength = state.Strength,
            Spirit = state.Spirit,
            Talent = state.Talent,
            Agility = state.Agility,
            Health = state.Health,
            DamageMin = state.DamageMin,
            DamageMax = state.DamageMax,
            Defence = state.Defence,
            Absorption = state.Absorption,
            InventoryRevision = state.InventoryRevision,
            AttackRange = state.AttackRange,
            AttackRating = state.AttackRating,
            MoveSpeed = state.MoveSpeed,
            ProgressionRevision = state.ProgressionRevision,
            SkillsRevision = state.SkillsRevision,
        };
        foreach (var item in state.Items)
        {
            result.Items.Add(new OwnedItem
            {
                InstanceId = ByteString.CopyFrom(item.InstanceId.ToByteArray()),
                Code = item.Code,
                Name = item.Name,
                Kind = (int)item.Kind,
                Count = item.Count,
                Anchor = item.Anchor,
                Slot = item.Slot,
                Width = item.Width,
                Height = item.Height,
                CatalogKey = item.CatalogKey
            });
        }

        foreach (var skill in state.Skills)
        {
            result.Skills.Add(new OwnedSkill { Code = skill.Code, Rank = skill.Rank });
        }

        return result;
    }
}
