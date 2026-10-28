using PristonTale.Contracts.Combat;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Simulation.World;

namespace PristonTale.Application.World.ItemUses;

// Bless Castle items: scrolls and stones work only in the castle field; a running item of the same code refuses, and a
// castle scroll refuses while the premium scroll of the same effect runs.
internal sealed class CastleItemUse : ItemUseAction
{
    public override ItemUseStatus Check(in ItemUseRequest use)
    {
        if (!CastleItems.TryGetByIndex(use.Rule.Argument, out var castle))
        {
            return ItemUseStatus.NotUsable;
        }

        if (use.Context.Field.FieldId != CastleItems.CastleFieldId)
        {
            return ItemUseStatus.NotHere;
        }

        var effects = use.Character.Effects;
        if (effects.TryGet(castle.StatusCode, out _))
        {
            return ItemUseStatus.AlreadyRunning;
        }

        return castle.Kind == CastleItemKind.Scroll && effects.TryGet(ItemScrolls.For((ScrollEffectKind)castle.Target).StatusCode, out _)
            ? ItemUseStatus.SameAttribute
            : ItemUseStatus.Used;
    }

    public override ItemUseEffect Apply(in ItemUseInWorld use)
    {
        if (CastleItems.TryGetByIndex(use.Rule.Argument, out var castle))
        {
            use.Field.StartCastleItem(use.Player, castle);
        }

        return ItemUseEffect.None;
    }
}

// Restoration is used by its dead owner in a castle field after ten seconds down: the owner rises with half its life,
// guarded for five seconds.
internal sealed class RestorationUse : ItemUseAction
{
    public const float DelayMs = 10_000f;
    public const float GuardMs = 5000f;

    public override bool ForTheDead => true;

    public override ItemUseStatus Check(in ItemUseRequest use)
    {
        if (use.Here?.State != FieldState.Castle)
        {
            return ItemUseStatus.NotHere;
        }

        return use.Context.Player.DeadMs >= DelayMs ? ItemUseStatus.Used : ItemUseStatus.Delay;
    }

    public override ItemUseEffect Apply(in ItemUseInWorld use)
    {
        var player = use.Player;
        player.Revive(player.MaxLife / 2, GuardMs);
        use.Field.EmitCombatEvent(new CombatEventView(player.EntityId, player.EntityId, CombatEventKind.Revive, damage: player.Life));
        return new ItemUseEffect(ItemEffectKind.Return);
    }
}
