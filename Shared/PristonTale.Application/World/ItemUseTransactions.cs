using System.Numerics;
using PristonTale.Application.Sessions;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Ai;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Application.World;

// A validated item use. A null Candidate is final (a refusal or a replay); otherwise the snapshot is stored before the
// commit applies the use to the live character and the world. Summon names the monster a crystal calls; Money is the
// gold the use takes, applied again at commit.
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
    string CatalogKey = "",
    string Summon = "",
    long Money = 0,
    PotionDefinition? Food = null)
    : PreparedCharacterOperation<ItemUseOutcome>(Original, Candidate, Snapshot, Outcome);

// The tick's view of one use: who, where, the catalogs, the real clock and the rolls. Pick is 0..9999 for the tables of
// crystals, boxes and puzzles; NowMs is the real clock in milliseconds for the crystal delay.
internal sealed record ItemUseContext(PlayerSession Session, WorldPlayer Player, FieldInstance Field, WorldCatalog World, ItemCatalog Items,
    long NowSeconds, long NowMs, int Roll, int Pick, ISimRng Rng, bool CrystalEvent);

// What a use adds to its candidate beyond the character part: a refusal, the crystal's monster, the gold taken, the
// food eaten and the line the player reads.
internal readonly record struct ItemUseExtra(ItemUseStatus Status, string Summon = "", long Money = 0, PotionDefinition? Food = null, string Message = "")
{
    public static ItemUseExtra None => new(ItemUseStatus.Used);
}

// Runs on the world tick. The item must be carried (not equipped), usable, and the inventory revision current; the
// rule's own conditions decide the rest. Every outcome of a stored use is recorded with the character for retries. The
// families with their own checks live in the partial files named after them.
internal static partial class ItemUseTransactions
{
    // Fields where items that leave the field or open a remote shop do not work: Bellatra and the quest arena; the
    // mobile shop is refused in towns and the castle as well.
    private static readonly int[] ReturnBlockedFields = [30, 32];
    private static readonly int[] MobileShopBlockedFields = [3, 21, 33, 30, 29];

    // Ether cores guard their user for ten seconds when the return motion ends.
    public const float EtherCoreGuardMs = 10_000f;

    public static PreparedItemUseOperation Prepare(ItemUseContext context, ItemUseCommand command)
    {
        var session = context.Session;
        var player = context.Player;
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
        var status = Check(context, command, rule, item, out var arrival);
        if (status != ItemUseStatus.Used)
        {
            return Final(original, command, ItemUseOutcome.Refused(original.InventoryRevision, status, code));
        }

        // A revealed puzzle piece stays; every other use consumes one before its prize joins the inventory.
        var candidate = original.CloneForInventoryOperation();
        if (rule.Action != UsableItemAction.PuzzleBag && !candidate.Inventory.ConsumeAt(anchor))
        {
            return Final(original, command, ItemUseOutcome.Refused(original.InventoryRevision, ItemUseStatus.NotUsable, code));
        }

        var extra = Extend(context, rule, item, candidate);
        if (extra.Status != ItemUseStatus.Used)
        {
            return Final(original, command, ItemUseOutcome.Refused(original.InventoryRevision, extra.Status, code));
        }

        UsableItemEffects.Apply(candidate, rule, context.NowSeconds, BigHeadMode(context.Roll));
        var message = extra.Message.Length > 0 ? extra.Message : Success(rule, item.Definition);
        var outcome = new ItemUseOutcome(true, candidate.InventoryRevision, ItemUseStatus.Used, code, message);
        candidate.ItemUses.Record(new AppliedItemUse(command, outcome.Revision, outcome));
        var fieldId = arrival?.FieldCode ?? context.Field.FieldId;
        var position = arrival?.Position ?? player.Position;
        var snapshot = CharacterRecordFactory.Capture(session.Loaded!, candidate, fieldId, position);
        if (rule.Action == UsableItemAction.HairTint)
        {
            snapshot = snapshot with { Scalars = snapshot.Scalars with { HairIndex = rule.Argument } };
        }

        return new PreparedItemUseOperation(original, candidate, snapshot, outcome, command, rule, code, context.NowSeconds, context.Roll, arrival,
            item.Definition.CatalogKey, extra.Summon, extra.Money, extra.Food);
    }

    private static ItemUseStatus Check(ItemUseContext context, ItemUseCommand command, UsableItemRule rule, ItemInstance item, out TeleportArrival? arrival)
    {
        arrival = null;
        var player = context.Player;
        var field = context.Field;
        var world = context.World;
        var character = player.Character;
        if (player.IsDead != (rule.Action == UsableItemAction.Restoration))
        {
            return player.IsDead ? ItemUseStatus.Dead : ItemUseStatus.NotUsable;
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

                if (CastleItems.All.Any(castle => castle.Kind == CastleItemKind.Scroll && castle.Target == (int)scroll.Kind
                    && character.Effects.TryGet(castle.StatusCode, out _)))
                {
                    return ItemUseStatus.SameAttribute;
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
                if (here is null || here.State == FieldState.Room || ReturnBlockedFields.Contains(field.FieldId))
                {
                    return ItemUseStatus.NotHere;
                }

                // A core without a destination plays its return and guard where the player stands.
                if (rule.Argument < 0)
                {
                    return ItemUseStatus.Used;
                }

                if (!world.TryGetFieldByCode(rule.Argument, out var town))
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
                return context.Session.Loaded!.HairIndex == rule.Argument ? ItemUseStatus.NothingToChange : ItemUseStatus.Used;
            case UsableItemAction.MonsterCrystal:
                return CheckCrystal(context, item, here);
            case UsableItemAction.CastleItem:
                return CheckCastleItem(context, rule);
            case UsableItemAction.Restoration:
                return CheckRestoration(context, here);
            case UsableItemAction.Food:
                return CheckFood(context, item);
            case UsableItemAction.SoccerHead:
                return character.Premium.BigHeadMode != 0 && character.Premium.BigHeadExpiresAtSeconds > context.NowSeconds
                    ? ItemUseStatus.SameAttribute
                    : ItemUseStatus.Used;
            case UsableItemAction.Chocolate:
                return CheckChocolate(context, item);
            case UsableItemAction.UnionCore:
                return ItemUseStatus.ClanOnly;
            case UsableItemAction.PuzzleBag:
                return CheckPuzzleBag(context, item);
            case UsableItemAction.GiftBox:
                return CheckGiftBox(context, rule);
            default:
                return UsableItemEffects.Check(character, rule, context.NowSeconds);
        }
    }

    // The part of a use that changes the candidate beyond the character rules: prizes, crystal monsters, gold.
    private static ItemUseExtra Extend(ItemUseContext context, UsableItemRule rule, ItemInstance item, PlayerCharacter candidate) => rule.Action switch
    {
        UsableItemAction.MonsterCrystal => ExtendCrystal(context, item),
        UsableItemAction.GiftBox => ExtendGiftBox(context, rule, candidate),
        UsableItemAction.PuzzleBag => ExtendPuzzleBag(item, candidate),
        UsableItemAction.Food => new ItemUseExtra(ItemUseStatus.Used, Food: item.Definition.Potion),
        _ => ItemUseExtra.None
    };

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
