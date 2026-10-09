using System.Numerics;
using PristonTale.Application.World;
using PristonTale.Client.Net.Online;
using PristonTale.Protocol.Binary;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Progression;
using PristonTale.Simulation.World.Fields;
using PristonTale.Simulation.World.Loot;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Simulation.World.Spawning;

namespace PristonTale.Tests;

internal static class WorldRewardsTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("reference XP event multiplies before level penalty and crosses real level thresholds", ExperienceAsync);
        yield return ("field death materializes its rolled loot and grants XP exactly once", DeathAsync);
        yield return ("ground loot respects ownership, reservations, rollback and expiry", GroundAsync);
        yield return ("ground presence survives skipped snapshots", PresenceAsync);
    }

    private static Task ExperienceAsync()
    {
        Assert.True(KillReward.ExpForKill(105, 4, 1, 5) == 525, "Hopy event reward differs from reference");
        Assert.True(KillReward.ExpForKill(105, 4, 24, 5) == 446, "event applied after integer penalty");
        Assert.True(KillReward.ExpForKill(100_000_000, 1, 40, 5) == 275_000_000, "XP intermediate overflow");
        var player = new CharacterClassRegistry().CreatePlayer(PlayerClass.Fighter, "XP");
        player.AwardExperience(525);
        Assert.True(player.Level == 1, "first Hopy granted an extra level");
        player.AwardExperience(525);
        Assert.True(player.Level == 2 && player.Experience == 1050 && ExperienceTable.ForLevel(2) == 1000, "second Hopy failed to level up");
        return Task.CompletedTask;
    }

    private static Task DeathAsync()
    {
        var definition = new MonsterDefinition(1, "Hopy fixture", MonsterFaction.Enemy, MonsterClass.Normal, MonsterBrood.Normal,
            new MonsterCombatStats(4, 1, new MonsterRange(1, 1), 6, 30, 0, 0, 0, 1, ResistanceBlock.Zero), "")
        {
            ExpReward = 105,
            Drops = new DropTable([DropEntry.GoldSlot(100, 8, 8)], 1, false)
        };
        var field = new FieldInstance(1, FieldSpawnData.Empty, new MonsterCatalog([definition]), new EntityIdAllocator(), new CombatRng(123),
            experienceMultiplier: 5);
        var character = new CharacterClassRegistry().CreatePlayer(PlayerClass.Fighter, "Killer");
        var player = new WorldPlayer(1, Vector3.Zero, character, false, default);
        field.Add(player);
        var monster = ((ISpawnSink)field).Spawn(definition, new Vector3(24, 0, 0), -1, 0);
        Assert.True(field.SetAttackTarget(1, monster.EntityId, true), "fixture target rejected");
        for (int index = 0; index < 400 && monster.IsAlive; index++)
        {
            if (!player.InAttackRange(monster.Position))
            {
                // The client chases its attack target and reports each step, as the original client does.
                var step = Vector3.Normalize(monster.Position - player.Position) * 6f;
                player.ReportMovement(player.Position + step, 0f, true, true, monster.EntityId, field.NowMs);
            }

            field.Step(0.05f);
        }

        Assert.True(!monster.IsAlive && field.GroundLoot.Count == 1 && character.Experience == 525, "kill discarded its loot or XP");
        for (int index = 0; index < 20; index++)
        {
            field.Step(0.05f);
        }

        Assert.True(field.GroundLoot.Count == 1 && character.Experience == 525, "corpse paid twice");
        return Task.CompletedTask;
    }

    private static Task GroundAsync()
    {
        var definition = new ItemDefinition(55, "Stone Axe", ItemKind.Axe) { CatalogKey = "WA101" };
        var ground = new FieldGroundLoot(new ItemCatalog([definition]));
        ground.Place(new KillPayout(2, 1, 0, [RolledDrop.Item(55)]), Vector3.Zero, false, 0);
        var drop = ground.Snapshot().Single().View;
        var character = new CharacterClassRegistry().CreatePlayer(PlayerClass.Fighter, "Owner");
        var owner = new WorldPlayer(1, Vector3.Zero, character, false, default);
        var stranger = new WorldPlayer(3, Vector3.Zero, character, false, default);
        var operation = Guid.NewGuid();
        Assert.True(!ground.TryReserve(drop.Id, operation, stranger, out _, out _), "private drop could be stolen");
        Assert.True(ground.TryReserve(drop.Id, operation, owner, out _, out var item) && item!.InstanceId == drop.Id, "drop identity lost");
        Assert.True(!ground.TryReserve(drop.Id, Guid.NewGuid(), owner, out _, out _), "concurrent pickup claimed reserved item");
        ground.Step(5001);
        Assert.True(ground.Count == 1, "public release removed a pending reservation");
        ground.Resolve(drop.Id, operation, false);
        Assert.True(ground.TryReserve(drop.Id, operation, stranger, out _, out _), "rollback did not release now-public item");
        ground.Resolve(drop.Id, operation, true);
        Assert.True(ground.Count == 0, "successful pickup left a duplicate on the ground");
        ground.Place(new KillPayout(2, 1, 0, [RolledDrop.Item(55)]), Vector3.Zero, false, 5001);
        var expired = ground.Snapshot().Single().View;
        Assert.True(ground.TryReserve(expired.Id, operation, owner, out _, out _), "expiry fixture reservation rejected");
        ground.Step(185_002);
        Assert.True(ground.Count == 1, "expiry removed a pending storage reservation");
        ground.Resolve(expired.Id, operation, false);
        Assert.True(!ground.TryReserve(expired.Id, operation, stranger, out _, out _), "expired rollback could be picked up again");
        ground.Step(185_003);
        ground.Place(new KillPayout(2, 1, 0, [RolledDrop.Gold(8)]), Vector3.Zero, true, 185_003);
        ground.Step(275_004);
        Assert.True(ground.Count == 0, "gold did not expire after 90 seconds");
        return Task.CompletedTask;
    }

    private static Task PresenceAsync()
    {
        var code = LegacyItemCode.Parse("WA101");
        var drop = new GroundDropView(Guid.NewGuid(), code, "WA101", "Stone Axe", 1, false, new Vector3(12, 3, -24)) { Handle = 7 };
        EntityView Avatar(uint id) => new(id, EntityKindView.Player, false, Vector3.Zero, 0f, MoveStateView.Idle, 1, 10, 10, default);
        var snapshot = new WorldSnapshot(1, [Avatar(1), Avatar(2)], groundDrops: [new GroundDropState(drop, 1, false)]);
        var owner = new WireRoundTrip(1);
        var added = owner.PushMerged(snapshot).GroundAdded.Single();
        Assert.True(added.Handle == 7 && added.Id == GroundDropHandle.ToGuid(7) && added.ItemCode == code && added.CatalogKey == "WA101"
            && added.Name == "Stone Axe" && added.Amount == 1 && !added.Gold && added.Position == drop.Position, "ground loot changed over PLAYITEM");
        Assert.True(owner.PushMerged(snapshot).GroundAdded.Count == 0, "unchanged ground loot resent");
        Assert.True(new WireRoundTrip(2).PushMerged(snapshot).GroundAdded.Count == 0, "private loot leaked in presence");
        var outbox = new WorldOutbox();
        outbox.Writer.TryWrite(snapshot);
        outbox.Writer.TryWrite(new WorldSnapshot(2, [], groundDrops: snapshot.GroundDrops));
        Assert.True(outbox.Reader.TryRead(out var latest) && latest.GroundDrops.Single().View == drop, "latest roster lost loot");
        Assert.True(owner.PushMerged(new WorldSnapshot(3, [Avatar(1)])).GroundRemoved.Single() == GroundDropHandle.ToGuid(7),
            "pickup/expiry did not send DELITEM");
        return Task.CompletedTask;
    }
}
