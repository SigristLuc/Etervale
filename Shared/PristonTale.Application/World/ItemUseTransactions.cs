using System.Numerics;
using PristonTale.Application.Sessions;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Application.World;

// A validated item use. A null Candidate is final (a refusal or a replay); otherwise the snapshot is stored before the
// commit applies the use to the live character and the world.
public sealed record PreparedItemUseOperation(
    PlayerCharacter Original,
    PlayerCharacter? Candidate,
    CharacterSaveSnapshot? Snapshot,
    ItemUseOutcome Outcome,
    ItemUseCommand Command,
    UsableItemRule? Rule = null,
    uint ItemCode = 0,
    long NowSeconds = 0,
    int Roll = 0,
    TeleportArrival? Arrival = null,
    string CatalogKey = "")
    : PreparedCharacterOperation<ItemUseOutcome>(Original, Candidate, Snapshot, Outcome);

// Runs on the world tick. The item must be carried (not equipped), usable, and the inventory revision current; the
// rule's own conditions decide the rest. Every outcome of a stored use is recorded with the character for retries.
internal static class ItemUseTransactions
{
    // Fields where items that leave the field or open a remote shop do not work: Bellatra and the quest arena; the
    // mobile shop is refused in towns and the castle as well.
    private static readonly int[] ReturnBlockedFields = [30, 32];
    private static readonly int[] MobileShopBlockedFields = [3, 21, 33, 30, 29];

    public static PreparedItemUseOperation Prepare(PlayerSession session, WorldPlayer player, FieldInstance field, ItemUseCommand command,
        WorldCatalog world, long nowSeconds, int roll)
    {
        var original = player.Character;
        if (original.ItemUses.Find(command.OperationId) is { } applied)
        {
            return applied.Command == command
                ? Final(original, command, applied.Outcome with { Message = "already committed" })
                : Final(original, command, ItemUseOutcome.Refused(original.InventoryRevision, ItemUseStatus.Conflict));
        }

        if (command.OperationId == Guid.Empty || command.ExpectedRevision != original.InventoryRevision)
        {
            return Final(original, command, ItemUseOutcome.Refused(original.InventoryRevision, ItemUseStatus.Stale));
        }

        var anchor = original.Inventory.Find(command.ItemId);
        var item = anchor >= 0 ? original.Inventory[anchor] : null;
        if (item is null || !UsableItemCatalog.TryGet(item.Definition.CatalogKey, out var rule))
        {
            return Final(original, command, ItemUseOutcome.Refused(original.InventoryRevision, ItemUseStatus.NotUsable));
        }

        var code = item.Definition.Code;
        var status = Check(session, player, field, command, rule, item, world, nowSeconds, out var arrival);
        if (status != ItemUseStatus.Used)
        {
            return Final(original, command, ItemUseOutcome.Refused(original.InventoryRevision, status, code));
        }

        var candidate = original.CloneForInventoryOperation();
        if (!candidate.Inventory.ConsumeAt(anchor))
        {
            return Final(original, command, ItemUseOutcome.Refused(original.InventoryRevision, ItemUseStatus.NotUsable, code));
        }

        UsableItemEffects.Apply(candidate, rule, nowSeconds, BigHeadMode(roll));
        var outcome = new ItemUseOutcome(true, candidate.InventoryRevision, ItemUseStatus.Used, code, Success(rule, item.Definition));
        candidate.ItemUses.Record(command, outcome);
        var fieldId = arrival?.FieldCode ?? field.FieldId;
        var position = arrival?.Position ?? player.Position;
        var snapshot = CharacterRecordFactory.Capture(session.Loaded!, candidate, fieldId, position);
        if (rule.Action == UsableItemAction.HairTint)
        {
            snapshot = snapshot with { Scalars = snapshot.Scalars with { HairIndex = rule.Argument } };
        }

        return new PreparedItemUseOperation(original, candidate, snapshot, outcome, command, rule, code, nowSeconds, roll, arrival, item.Definition.CatalogKey);
    }

    private static ItemUseStatus Check(PlayerSession session, WorldPlayer player, FieldInstance field, ItemUseCommand command, UsableItemRule rule,
        ItemInstance item, WorldCatalog world, long nowSeconds, out TeleportArrival? arrival)
    {
        arrival = null;
        var character = player.Character;
        if (player.IsDead)
        {
            return ItemUseStatus.Dead;
        }

        if (rule.ForbiddenInVillage && player.Appearance.InVillage)
        {
            return ItemUseStatus.NotHere;
        }

        if (character.Level < rule.MinimumLevel)
        {
            return ItemUseStatus.LevelTooLow;
        }

        world.TryGetFieldByCode(field.FieldId, out var here);
        switch (rule.Action)
        {
            case UsableItemAction.ScrollEffect:
            {
                var scroll = ItemScrolls.For((ScrollEffectKind)rule.Argument);
                if (character.Effects.TryGet(scroll.StatusCode, out _))
                {
                    return ItemUseStatus.AlreadyRunning;
                }

                return ItemScrolls.Excluded(scroll.Kind) is { } excluded && character.Effects.TryGet(ItemScrolls.For(excluded).StatusCode, out _)
                    ? ItemUseStatus.NotHere
                    : ItemUseStatus.Used;
            }
            case UsableItemAction.ForceOrb:
            {
                ForceOrbs.TryGet(item.Definition.CatalogKey, out var orb);
                if (character.Level < orb.MinimumLevel || character.Level > orb.MaximumLevel)
                {
                    return ItemUseStatus.LevelTooLow;
                }

                foreach (var effect in character.Effects.Effects)
                {
                    if (ForceOrbDefinition.IsOrbStatus(effect.Code))
                    {
                        return ItemUseStatus.AlreadyRunning;
                    }
                }

                return ItemUseStatus.Used;
            }
            case UsableItemAction.TeleportCore:
            {
                if (here?.State == FieldState.Room || !TeleportCoreDestinations.TryGet(command.Argument, out var destination)
                    || !world.TryGetFieldByCode(destination.FieldCode, out var target))
                {
                    return ItemUseStatus.NotHere;
                }

                if (character.Level < destination.RequiredLevel)
                {
                    return ItemUseStatus.LevelTooLow;
                }

                arrival = ArrivalAt(target);
                return ItemUseStatus.Used;
            }
            case UsableItemAction.ReturnToTown:
            {
                if (here is null || here.State == FieldState.Room || ReturnBlockedFields.Contains(field.FieldId)
                    || !world.TryGetFieldByCode(rule.Argument, out var town))
                {
                    return ItemUseStatus.NotHere;
                }

                arrival = ArrivalAt(town);
                return ItemUseStatus.Used;
            }
            case UsableItemAction.MobileShop:
                return here is null || here.State == FieldState.Room || MobileShopBlockedFields.Contains(field.FieldId)
                    ? ItemUseStatus.NotHere
                    : ItemUseStatus.Used;
            case UsableItemAction.HairTint:
                return session.Loaded!.HairIndex == rule.Argument ? ItemUseStatus.NothingToChange : ItemUseStatus.Used;
            default:
                return UsableItemEffects.Check(character, rule, nowSeconds);
        }
    }

    private static TeleportArrival ArrivalAt(FieldDef target)
    {
        var start = target.StartPoints.Count > 0 ? target.StartPoints[0] : target.Center;
        return new TeleportArrival(target.FieldCode, new Vector3(start.X, 0f, start.Z));
    }

    private static string Success(UsableItemRule rule, ItemDefinition item) => rule.Action switch
    {
        UsableItemAction.ResetAttributes or UsableItemAction.ResetAttributesAndSkills => "Your stat points can be distributed again",
        UsableItemAction.ResetSkills => "Your skill points can be distributed again",
        UsableItemAction.SkillMastery => "Skill training is complete",
        UsableItemAction.AgingMastery => "The aged item has matured",
        UsableItemAction.GravityScroll => "You used a Gravity Stone",
        _ => $"{item.Name} used"
    };

    // The big-head potion draws one of its two looks from the use's roll.
    public static int BigHeadMode(int roll) => 1 + roll / 2 % 2;

    private static PreparedItemUseOperation Final(PlayerCharacter original, ItemUseCommand command, ItemUseOutcome outcome) =>
        new(original, null, null, outcome, command);

    // The average attack power a force orb's percent bonus is taken from.
    public static int AverageAttack(PlayerCharacter character)
    {
        var damage = WeaponDamage.Compute(character);
        return (damage.Min + damage.Max) / 2;
    }
}
