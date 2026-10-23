using PristonTale.Application.Sessions;
using PristonTale.Simulation.Items.Usable;

namespace PristonTale.Application.World;

public sealed partial class WorldServer
{
    // Touched only by the tick thread: the random roll of an item use (firecracker colours, big-head look).
    private readonly Random _itemRandom = new();

    public Task<PreparedItemUseOperation?> PrepareItemUseAsync(PlayerSession session, ItemUseCommand command)
    {
        return EnqueueAsync<PreparedItemUseOperation?>(session.EntityId, () =>
        {
            if (session.Loaded is null || !TryGetPlaying(session, out var connection) || !_fields.TryGetValue(connection.FieldId, out var field))
            {
                return null;
            }

            var nowSeconds = _clock.GetUtcNow().ToUnixTimeSeconds();
            return ItemUseTransactions.Prepare(session, connection.Player, field, command, _catalog, nowSeconds, _itemRandom.Next(0, 8192) * 2);
        });
    }

    // Exempt from the player's quota: the use is already stored.
    public Task<bool> CommitItemUseAsync(PlayerSession session, PreparedItemUseOperation operation)
    {
        return EnqueueAsync(NoOwner, () =>
        {
            if (!ReferenceEquals(session.Character, operation.Original) || operation.Candidate is null || operation.Rule is null)
            {
                throw new InvalidOperationException("Character ownership changed during item use commit.");
            }

            var rule = operation.Rule;
            var original = operation.Original;
            original.CommitInventoryOperation(operation.Candidate);
            original.ItemUses.CopyFrom(operation.Candidate.ItemUses);
            if (rule.Action != UsableItemAction.AgingMastery)
            {
                UsableItemEffects.Apply(original, rule, operation.NowSeconds, ItemUseTransactions.BigHeadMode(operation.Roll));
            }

            if (rule.Action == UsableItemAction.HairTint)
            {
                session.Loaded = session.Loaded! with { HairIndex = rule.Argument };
            }

            if (rule.Action == UsableItemAction.MobileShop)
            {
                session.MobileShopOpen = true;
            }

            if (TryGetPlaying(session, out var connection) && _fields.TryGetValue(connection.FieldId, out var field))
            {
                ApplyToWorld(connection.Player, field, operation);
            }

            if (operation.Arrival is { } arrival)
            {
                Arrive(session, arrival);
            }

            return true;
        }, waitForCapacity: true);
    }

    private static void ApplyToWorld(WorldPlayer player, FieldInstance field, PreparedItemUseOperation operation)
    {
        var rule = operation.Rule!;
        var effect = ItemEffectKind.None;
        switch (rule.Action)
        {
            case UsableItemAction.ScrollEffect:
            {
                var scroll = ItemScrolls.For((ScrollEffectKind)rule.Argument);
                var durationMs = scroll.DurationSeconds * 1000f;
                field.QueueItemEffect(player.EntityId, scroll.Spec(), durationMs);
                if (scroll.Kind == ScrollEffectKind.Invulnerability)
                {
                    player.Guard(durationMs);
                }

                break;
            }
            case UsableItemAction.ForceOrb:
            {
                if (ForceOrbs.TryGet(operation.CatalogKey, out var orb))
                {
                    field.QueueItemEffect(player.EntityId, orb.Spec(ItemUseTransactions.AverageAttack(player.Character)), orb.DurationSeconds * 1000f);
                    effect = ItemEffectKind.ForceOrb;
                }

                break;
            }
            case UsableItemAction.HairTint:
                player.ChangeHair(rule.Argument);
                effect = ItemEffectKind.Learn;
                break;
            case UsableItemAction.Firecracker:
                effect = ItemEffectKind.Firecracker;
                break;
            case UsableItemAction.SkillMastery:
                effect = ItemEffectKind.Mastery;
                break;
        }

        if (effect != ItemEffectKind.None)
        {
            field.ItemEvents.Add(new ItemEffectView(player.EntityId, operation.ItemCode, effect, player.Position, operation.Roll));
        }
    }
}
