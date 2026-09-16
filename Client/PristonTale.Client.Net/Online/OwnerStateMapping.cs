using PristonTale.Protocol.Session;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;

namespace PristonTale.Client.Net.Online;

internal static class OwnerStateMapping
{
    public static OwnerState? ToNeutral(OwnedCharacterState? state)
    {
        if (state is null)
        {
            return null;
        }

        var items = new List<OwnedItemView>(state.Items.Count);
        foreach (var item in state.Items)
        {
            if (item.InstanceId.Length != 16)
            {
                throw new InvalidDataException("Invalid item instance identity in owner state.");
            }

            items.Add(new OwnedItemView(new Guid(item.InstanceId.Span), item.Code, item.Name, (ItemKind)item.Kind,
                item.Count, item.Anchor, item.Slot, item.Width, item.Height, item.CatalogKey));
        }

        return new OwnerState(
            state.Level,
            state.Experience,
            state.LevelStart,
            state.NextLevel,
            state.Money,
            state.Hp,
            state.MaxHp,
            state.Mp,
            state.MaxMp,
            state.Sp,
            state.MaxSp,
            state.StatPoints,
            state.SkillPoints,
            state.Strength,
            state.Spirit,
            state.Talent,
            state.Agility,
            state.Health,
            state.DamageMin,
            state.DamageMax,
            state.Defence,
            state.Absorption,
            state.InventoryRevision,
            items.AsReadOnly())
        {
            AttackRange = state.AttackRange,
            AttackRating = state.AttackRating,
            MoveSpeed = state.MoveSpeed,
            ProgressionRevision = state.ProgressionRevision,
            SkillsRevision = state.SkillsRevision,
            Skills = Array.AsReadOnly(state.Skills.Select(skill => new OwnedSkillView(skill.Code, skill.Rank)).ToArray())
        };
    }
}
