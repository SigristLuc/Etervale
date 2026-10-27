using System.Numerics;
using PristonTale.Application.Sessions;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Contracts.Combat;

namespace PristonTale.Application.World;

public sealed partial class WorldServer
{
    // Touched only by the tick thread: the random rolls of an item use (firecracker colours, big-head look, prize tables).
    private readonly Random _itemRandom = new();

    public Task<PreparedItemUseOperation?> PrepareItemUseAsync(PlayerSession session, ItemUseCommand command)
    {
        return EnqueueAsync<PreparedItemUseOperation?>(session.EntityId, () =>
        {
            if (session.Loaded is null || !TryGetPlaying(session, out var connection) || !_fields.TryGetValue(connection.FieldId, out var field))
            {
                return null;
            }

            var now = _clock.GetUtcNow();
            var context = new ItemUseContext(session, connection.Player, field, _catalog, _worldData.Current.Items, now.ToUnixTimeSeconds(),
                now.ToUnixTimeMilliseconds(), _itemRandom.Next(0, 8192) * 2, _itemRandom.Next(0, 10000), new Simulation.Combat.CombatRng(_itemRandom.Next()),
                MonsterCrystals.EventActive(_options.CrystalEvent, _clock.GetLocalNow().Hour));
            return ItemUseTransactions.Prepare(context, command);
        });
    }

    // The open field of a code, for inspection outside the tick (tests and probes).
    internal bool TryGetOpenField(int fieldId, out FieldInstance field) => _fields.TryGetValue(fieldId, out field!);

    // Exempt from the player's quota: the use is already stored.
    public Task<bool> CommitItemUseAsync(PlayerSession session, PreparedItemUseOperation operation)
    {
        return EnqueueAsync(NoOwner, () =>
        {
            if (!ReferenceEquals(session.Character, operation.Original) || operation.Candidate is null || operation.Rule is null)
            {
                throw new CommitInvariantException("Character ownership changed during item use commit.");
            }

            var rule = operation.Rule;
            var original = operation.Original;
            original.CommitInventoryOperation(operation.Candidate);
            original.ItemUses.CopyFrom(operation.Candidate.ItemUses);
            if (operation.Money != 0 && !original.TryChangeMoney(operation.Money))
            {
                throw new CommitInvariantException("Money changed beyond capacity during item use commit.");
            }

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
                ApplyToWorld(connection.Player, field, operation, _clock.GetUtcNow().ToUnixTimeMilliseconds());
            }

            if (operation.Arrival is { } arrival)
            {
                Arrive(session, arrival);
            }

            return true;
        }, waitForCapacity: true);
    }

    private static void ApplyToWorld(WorldPlayer player, FieldInstance field, PreparedItemUseOperation operation, long nowMs)
    {
        var rule = operation.Rule!;
        var effect = ItemEffectKind.None;
        var position = player.Position;
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
                player.QueueYahoo();
                effect = ItemEffectKind.Firecracker;
                break;
            case UsableItemAction.SkillMastery:
                effect = ItemEffectKind.Mastery;
                break;
            case UsableItemAction.ReturnToTown:
                player.Guard(ItemUseTransactions.EtherCoreGuardMs);
                effect = ItemEffectKind.Return;
                break;
            case UsableItemAction.PremiumTimers when rule.Grants.Any(static grant => PremiumTimers.PausesOffline(grant.Kind)):
                effect = ItemEffectKind.Drink;
                break;
            case UsableItemAction.MonsterCrystal:
                player.CrystalDelayUntilMs = nowMs + (long)MonsterCrystals.SharedDelayMs;
                if (field.CallCrystalMonster(player, operation.CatalogKey, operation.Summon) is { } spawned)
                {
                    position = spawned;
                    effect = ItemEffectKind.Return;
                }

                break;
            case UsableItemAction.CastleItem when CastleItems.TryGetByIndex(rule.Argument, out var castle):
                field.StartCastleItem(player, castle);
                break;
            case UsableItemAction.Restoration:
                player.Revive(player.MaxLife / 2, ItemUseTransactions.RestorationGuardMs);
                field.EmitCombatEvent(new CombatEventView(player.EntityId, player.EntityId, CombatEventKind.Revive, damage: player.Life));
                effect = ItemEffectKind.Return;
                break;
            case UsableItemAction.Food when operation.Food is { } food:
                player.QueuePotion(food);
                break;
            case UsableItemAction.GiftBox:
                if (operation.Food is { } meal)
                {
                    player.QueuePotion(meal, cheers: rule.Argument == GiftBoxes.TigerCapsule);
                }

                effect = ItemEffectKind.GiftBox;
                break;
            case UsableItemAction.SoccerHead:
                player.Guard(EventFoods.HeadChangeGuardMs);
                effect = ItemEffectKind.HeadChange;
                break;
            case UsableItemAction.Chocolate:
                field.QueueItemEffect(player.EntityId, Chocolates.Spec(rule.Argument), Chocolates.DurationMs(rule.Argument), rule.Argument);
                break;
            case UsableItemAction.PuzzleBag:
                effect = ItemEffectKind.Drink;
                break;
        }

        if (effect != ItemEffectKind.None)
        {
            field.ItemEvents.Add(new ItemEffectView(player.EntityId, operation.ItemCode, effect, position, operation.Roll));
        }
    }
}
