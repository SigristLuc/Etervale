using System.Numerics;
using PristonTale.Application.World;
using PristonTale.Client.Net.Presence;
using PristonTale.Protocol.Packets;
using PristonTale.Server.Net;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Modifiers;

namespace PristonTale.Tests;

// The shared presence pipeline: frames reused between ticks, entries encoded once for every receiver, field-wide
// descriptor revisions, the crowd cap, per-tick event segments, the grid and the running-status book.
internal static class PresencePipelineTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("presence frames are reused only when no reader pins them and never while an outbox can still hand them out", FramesAsync);
        yield return ("an entry encoded once for all receivers is byte for byte what one receiver would encode", SharedWireAsync);
        yield return ("events routed by the entities a receiver knows are the events a full scan finds, in the field's order", RoutedEventsAsync);
        yield return ("presence entries carry 64-bit ids and sequences, 32-bit life and world-wide positions at an eighth of a unit", EntriesAsync);
        yield return ("descriptor revisions are field-wide and rise only when what a description shows changes", RevisionsAsync);
        yield return ("a receiver in a crowd shows its nearest entities and never loses its NPCs, target, attackers or summons", CrowdCapAsync);
        yield return ("events of ticks a slow reader fell behind on keep their own tick on the timeline", SegmentsAsync);
        yield return ("the entity grid finds, counts and relates entities on both sides of zero", GridAsync);
        yield return ("running statuses are kept per field, carried across fields on the new clock and pruned when they end", StatusBookAsync);
    }

    private static Task FramesAsync()
    {
        var frames = new FieldPresenceFrames();
        var first = frames.Rent(3);
        var generation = first.Generation;
        frames.Published(first);
        Assert.True(first.TryPin(generation, 7), "a published frame refused a reader");
        var second = frames.Rent(3);
        Assert.True(!ReferenceEquals(first, second), "the latest published frame was reused");
        frames.Published(second);
        var third = frames.Rent(4);
        Assert.True(!ReferenceEquals(third, first) && third.Views.Length == 4, "a pinned frame was reused");
        frames.Published(third);
        first.Release(7);
        var reused = frames.Rent(3);
        Assert.True(ReferenceEquals(reused, first) && first.Generation == generation + 1, "an unpinned old frame was not reused");
        Assert.True(!first.TryPin(generation, 9), "a reader pinned a reused frame under its old generation");
        frames.Published(reused);
        Assert.True(first.TryPin(first.Generation, 9), "the reused frame refused its new readers");
        first.Release(9);
        return Task.CompletedTask;
    }

    private static Task RoutedEventsAsync()
    {
        static EntityView At(ulong id, float x) =>
            new(id, id == 1 ? EntityKindView.Player : EntityKindView.Monster, false, new Vector3(x, 0, 0), 0f, MoveStateView.Idle, 1, 100, 100, default);
        EntityView[] views = [At(1, 0), At(10, 20), At(11, 40), At(12, 60), At(98, 50_000), At(99, 50_020)];
        // Far fights the receiver never sees, with its own monsters' events among them.
        var combat = Enumerable.Range(0, 40).Select(index => new CombatEventView(99, 98, CombatEventKindView.Hit, damage: index)).ToList();
        combat.Insert(5, new CombatEventView(10, 1, CombatEventKindView.Hit, damage: 1000));
        combat.Insert(20, new CombatEventView(11, 12, CombatEventKindView.Hit, damage: 2000));
        combat.Add(new CombatEventView(1, 1, CombatEventKindView.Heal, damage: 3000));
        var events = combat.ToArray();
        StatusEventView[] statuses = [new(98, 99, 7, StatusEventOpView.Add, 1, 0, 0, false, false, 1000, 1000, 0), new(12, 11, 8, StatusEventOpView.Add, 1, 0, 0, false, false, 1000, 1000, 0)];
        var frame = new PresenceFrame(views.Length);
        views.CopyTo(frame.Views, 0);
        frame.Grid.Rebuild(frame.Views);
        frame.Routes.Rebuild(events, [], statuses, []);
        var routed = new WireRoundTrip(1).PushMerged(new WorldSnapshot(5, frame.Views, events, 3) { Grid = frame.Grid, Frame = frame, StatusEvents = statuses });
        var scanned = new WireRoundTrip(1).PushMerged(new WorldSnapshot(5, views, events, 3) { StatusEvents = statuses });
        Assert.True(routed.Events.Select(item => item.Damage).SequenceEqual([1000, 2000, 3000]), $"routed {string.Join(",", routed.Events.Select(item => item.Damage))}");
        Assert.True(scanned.Events.Select(item => item.Damage).SequenceEqual([1000, 2000, 3000]), $"scanned {string.Join(",", scanned.Events.Select(item => item.Damage))}");
        Assert.True(routed.StatusEffects.Select(item => item.StatusCode).SequenceEqual([8u]) && scanned.StatusEffects.Select(item => item.StatusCode).SequenceEqual([8u]),
            "a status of an unseen entity was routed, or a seen one was lost");
        return Task.CompletedTask;
    }

    private static Task SharedWireAsync()
    {
        var view = new EntityView(42, EntityKindView.Monster, false, new Vector3(-1234.5f, 17f, 999.25f), 1.1f, MoveStateView.Walking, 30, 77, 120, default,
            templateId: 9) { Revision = 3 };
        var wire = SharedPresenceWire.Create(4);
        var direct = new byte[PresenceWire.MaxFullEntrySize];
        var length = WireMapping.WriteFull(direct, view, 2, view.Revision);
        Assert.True(wire.Full(1, view, 2).SequenceEqual(direct.AsSpan(0, length)), "the shared full entry differs from a receiver's own");
        Assert.True(wire.Full(1, view, 2).SequenceEqual(direct.AsSpan(0, length)), "a second receiver got other bytes");
        var motion = new byte[PresenceWire.MaxMotionEntrySize];
        var motionLength = WireMapping.WriteMotion(motion, view);
        Assert.True(wire.Motion(1, view).SequenceEqual(motion.AsSpan(0, motionLength)) && motionLength == PresenceWire.MotionEntrySize(42),
            "the shared motion entry differs");
        var description = new byte[EntityDescriptionPacket.MaximumSize];
        var described = PresenceEncoder.DescriptionPacket(view, view.Revision, WorldData.Empty).Write(description);
        Assert.True(wire.Description(1, view, WorldData.Empty).SequenceEqual(description.AsSpan(0, described)), "the shared description differs");
        wire.Reset(4);
        var moved = new EntityView(42, EntityKindView.Monster, false, Vector3.Zero, 0f, MoveStateView.Idle, 30, 77, 120, default, templateId: 9) { Revision = 3 };
        length = WireMapping.WriteFull(direct, moved, 2, moved.Revision);
        Assert.True(wire.Full(1, moved, 2).SequenceEqual(direct.AsSpan(0, length)), "a reset frame kept the previous tick's entry");
        return Task.CompletedTask;
    }

    private static Task EntriesAsync()
    {
        var entry = new PresenceEntry(uint.MaxValue, true, new Vector3(-999_999.875f, 1234.5f, 999_999.875f), 4.71f, PresenceMotion.Skill, 100_000, 2_000_000,
            285, 0x4000_0001, ulong.MaxValue - 3, 255, 0x3020, -1);
        var buffer = new byte[PresenceWire.HeaderSize + PresenceWire.MaxFullEntrySize + PresenceWire.MaxMotionEntrySize];
        var writer = new PresenceEntriesWriter(buffer, 77);
        var full = PresenceWire.WriteFull(writer.Reserve(PresenceWire.MaxFullEntrySize), entry);
        writer.Commit(full);
        var motion = PresenceWire.WriteMotion(writer.Reserve(PresenceWire.MaxMotionEntrySize), 1, new Vector3(10.125f, -3f, 7.5f), 0.5f, PresenceMotion.Run);
        writer.Commit(motion);
        var read = new List<PresenceEntry>();
        var sequence = PresenceEntriesReader.Read(writer.Finish(), (in PresenceEntry value) => read.Add(value));
        Assert.True(sequence == 77 && read.Count == 2 && full <= PresenceWire.MaxFullEntrySize && motion == PresenceWire.MotionEntrySize(1),
            "the presence message lost entries or overran its sizes");
        var back = read[0];
        Assert.True(back.Full && back.Serial == uint.MaxValue && back.Hp == 100_000 && back.MaxHp == 2_000_000 && back.Level == 285
            && back.Target == 0x4000_0001 && back.AttackSequence == ulong.MaxValue - 3 && back.Revision == 255 && back.Template == 0x3020
            && back.FieldCode == -1 && back.Motion == PresenceMotion.Skill, $"a full entry did not round-trip: {back}");
        Assert.True(Vector3.Distance(back.Position, entry.Position) <= 0.125f && MathF.Abs(back.Facing - entry.Facing) < 0.001f,
            "positions or facing lost more than the wire's precision");
        Assert.True(!read[1].Full && read[1].Position == new Vector3(10.125f, -3f, 7.5f) && read[1].Motion == PresenceMotion.Run, "a motion entry did not round-trip");
        foreach (var value in new ulong[] { 0, 127, 128, 16_383, 16_384, uint.MaxValue, ulong.MaxValue })
        {
            var bytes = new byte[VarInt.MaxBytes64];
            var size = VarInt.Write(bytes, value);
            var offset = 0;
            Assert.True(size == VarInt.Size(value) && VarInt.Read(bytes, ref offset) == value && offset == size, $"varint {value} did not round-trip");
        }

        Assert.True(VarInt.UnZigZag(VarInt.ZigZag(-1)) == -1 && VarInt.UnZigZag(VarInt.ZigZag(int.MinValue)) == int.MinValue, "zigzag lost the sign");

        var buffer2 = new byte[EntityDescriptionPacket.MaximumSize];
        var player = new EntityDescriptionPacket(7, 4, DescribedKind.Player, 285, 3, 1.25f, "Ário Ñandú", "char/tmABCD/b001.ini",
            new DescribedPlayer(2, 3, 1, 4, "char/tmABCD/tmh-b01.inf", 0x01020304, 0x02030405, 0x03040506, true, true, 5, 2));
        var length2 = player.Write(buffer2);
        var readPlayer = EntityDescriptionPacket.Read(buffer2.AsSpan(0, length2));
        Assert.True(readPlayer == player with { Player = readPlayer.Player } && readPlayer.Player == player.Player, "a player description did not round-trip");
        var summon = new EntityDescriptionPacket(0x4000_0002, 9, DescribedKind.Monster, 70, 3, 1f, "Metal Golem", "char/monster/Metalgolem/metalgolem.inx",
            Template: new DescribedTemplate(0x3020, 1f, 0x3020, -8, 200, 4, uint.MaxValue + 5UL, 1.5f, 0, 1, -1));
        length2 = summon.Write(buffer2);
        var readSummon = EntityDescriptionPacket.Read(buffer2.AsSpan(0, length2));
        Assert.True(readSummon.Template == summon.Template && readSummon.Name == summon.Name && readSummon.Level == 70 && length2 <= EntityDescriptionPacket.MaximumSize,
            "a summon description did not round-trip");
        return Task.CompletedTask;
    }

    private static Task RevisionsAsync()
    {
        var field = new FieldInstance(3);
        var classes = new CharacterClassRegistry();
        var character = classes.CreatePlayer(PlayerClass.Fighter, "Revised", 10);
        var player = new WorldPlayer(1, Vector3.Zero, character, false, new PlayerAppearanceView("Revised", 1, 0, 0, 0, "", ""));
        field.Add(player);
        field.Add(new WorldPlayer(2, new Vector3(50, 0, 0), classes.CreatePlayer(PlayerClass.Fighter, "Other", 10), false,
            new PlayerAppearanceView("Other", 1, 0, 0, 0, "", "")));
        var first = field.BuildViews();
        Assert.True(first.All(view => view.Revision == 1), "new entities do not start at revision 1");
        player.ReportMovement(new Vector3(20, 0, 0), 0.5f, true, false, 0, 1000);
        var moved = field.BuildViews();
        Assert.True(moved.All(view => view.Revision == 1), "a move changed a description revision");
        character.Equip(EquipmentSlot.Weapon, SkillScenario.Weapon(ItemKind.Axe, "WA101"));
        var equipped = field.BuildViews();
        Assert.True(equipped.Single(view => view.EntityId == 1).Revision == 2 && equipped.Single(view => view.EntityId == 2).Revision == 1,
            "equipment did not raise only its owner's revision");
        return Task.CompletedTask;
    }

    private static Task CrowdCapAsync()
    {
        static EntityView Player(ulong id, Vector3 at, ulong target = 0) => new(id, EntityKindView.Player, false, at, 0f, MoveStateView.Idle, 1, 100, 100,
            new PlayerAppearanceView("P" + id, 2, 0, 0, 0, "", ""), attackTargetId: target) { Revision = 1 };
        var views = new List<EntityView> { Player(1, Vector3.Zero, target: 900) };
        for (ulong id = 2; id < 402; id++)
        {
            views.Add(Player(id, new Vector3(id % 20 * 8f, 0f, id / 20 * 8f)));
        }

        // Beyond the crowd but in view: an NPC, the receiver's target, a monster attacking it and its summon.
        views.Add(new EntityView(900, EntityKindView.Monster, false, new Vector3(700, 0, 0), 0f, MoveStateView.Idle, 1, 10, 10, default, templateId: 5)
            { Revision = 1 });
        views.Add(new EntityView(901, EntityKindView.Monster, false, new Vector3(0, 0, 700), 0f, MoveStateView.Attack, 1, 10, 10, default, templateId: 5,
            attackTargetId: 1) { Revision = 1 });
        views.Add(new EntityView(902, EntityKindView.Monster, false, new Vector3(-700, 0, 0), 0f, MoveStateView.Idle, 1, 10, 10, default, templateId: 6)
            { OwnerId = 1, Revision = 1 });
        views.Add(new EntityView(903, EntityKindView.Npc, false, new Vector3(0, 0, -700), 0f, MoveStateView.Idle, 1, 1, 1, default, templateId: 7)
            { Revision = 1 });
        var wire = new WireRoundTrip(1);
        var update = wire.PushMerged(new WorldSnapshot(1, views.ToArray(), fieldId: 3));
        var spawned = update.Spawns.Select(spawn => spawn.EntityId).ToHashSet();
        Assert.True(spawned.Count == PresenceEncoder.MaxVisible + 1, $"the crowd showed {spawned.Count} entities");
        Assert.True(new ulong[] { 900, 901, 902, 903 }.All(spawned.Contains), "an essential entity was dropped for the crowd");
        Assert.True(spawned.Contains(2) && !spawned.Contains(401), "the crowd did not keep its nearest members");
        var again = wire.PushMerged(new WorldSnapshot(2, views.ToArray(), fieldId: 3));
        Assert.True(again.Spawns.Count == 0 && again.Removed.Count == 0, "an unchanged crowd churned its visible set");
        return Task.CompletedTask;
    }

    private static Task SegmentsAsync()
    {
        var self = new EntityView(1, EntityKindView.Player, false, Vector3.Zero, 0f, MoveStateView.Idle, 1, 100, 100,
            new PlayerAppearanceView("Self", 2, 0, 0, 0, "", ""));
        var outbox = new WorldOutbox();
        CombatEventView Hit(int damage) => new(1, 1, CombatEventKindView.Hit, damage: damage);
        Assert.True(outbox.Writer.TryWrite(new WorldSnapshot(10, [self], [Hit(5)], 3)), "first tick refused");
        Assert.True(outbox.Writer.TryWrite(new WorldSnapshot(11, [self], [], 3)), "second tick refused");
        Assert.True(outbox.Writer.TryWrite(new WorldSnapshot(12, [self], [Hit(7)], 3)), "third tick refused");
        Assert.True(outbox.Reader.TryRead(out var snapshot), "the outbox delivered nothing");
        Assert.True(snapshot.Events.Length == 2 && snapshot.EventSegments is { Length: 2 } segments && segments[0].Tick == 10 && segments[1].Tick == 12,
            "the joined events lost their ticks");
        var updates = new WireRoundTrip(1).Push(snapshot);
        var timed = updates.Where(update => update.Events.Count > 0).SelectMany(update => update.Events.Select(value => (update.ServerTimeMs ?? -1d, value.Damage))).ToList();
        Assert.True(timed.SequenceEqual([(500d, 5), (600d, 7)]), $"events were placed at {string.Join(",", timed)}");

        var single = new WorldOutbox();
        Assert.True(single.Writer.TryWrite(new WorldSnapshot(20, [self], [Hit(3)], 3)) && single.Writer.TryWrite(new WorldSnapshot(21, [self], [], 3)), "writes refused");
        Assert.True(single.Reader.TryRead(out var late) && late.EventSegments is [{ Tick: 20 }], "a lone older batch lost its tick");
        return Task.CompletedTask;
    }

    private static Task GridAsync()
    {
        static EntityView At(ulong id, float x, float z, ulong target = 0, ulong owner = 0, EntityKindView kind = EntityKindView.Monster) =>
            new(id, kind, false, new Vector3(x, 0, z), 0f, MoveStateView.Idle, 1, 1, 1, default, attackTargetId: target) { OwnerId = owner };
        var grid = EntityGrid.Build([At(1, -10, -10), At(2, 10, 10, target: 1), At(3, -5000, 0, owner: 1), At(4, 0, 300, kind: EntityKindView.Npc), At(5, 63, -1)]);
        var found = new List<int>();
        grid.Query(Vector3.Zero, 70f, found);
        Assert.True(found.Order().SequenceEqual([0, 1, 4]), $"the grid found {string.Join(",", found)} around the origin");
        Assert.True(grid.CountInSquare(Vector3.Zero, 70f) == 3, "the square count missed an entity across zero");
        var related = new List<int>();
        grid.Related(1, related);
        Assert.True(related.Order().SequenceEqual([1, 2]), "the attacker and the summon of entity 1 were not related to it");
        Assert.True(grid.Npcs.SequenceEqual([3]), "the NPC index is wrong");
        return Task.CompletedTask;
    }

    private static Task StatusBookAsync()
    {
        var book = new FieldStatusBook();
        StatusEventView Status(ulong entity, StatusEventOpView op, int remaining, bool permanent = false) =>
            new(entity, 9, 0x01010003, op, 1, 1, 0, false, permanent, 10000, remaining, 5);
        book.Apply([Status(1, StatusEventOpView.Add, 10000), Status(2, StatusEventOpView.Add, 4000)], 1000);
        Assert.True(book.Snapshot.For(1).Single().At(4000).RemainingMs == 7000, "a running status did not count down");
        book.Apply([Status(1, StatusEventOpView.Tick, 0)], 2000);
        Assert.True(book.Snapshot.For(1).Length == 1, "a periodic tick changed the book");
        var carried = book.Take(1);
        Assert.True(book.Snapshot.For(1).IsEmpty && carried.Length == 1, "a leaving entity kept its statuses in the field");
        var other = new FieldStatusBook();
        other.Carry(1, carried, 5000, 200);
        Assert.True(other.Snapshot.For(1).Single().At(700).RemainingMs == 5500, "a carried status was not rebased onto the new field clock");
        book.Apply([Status(2, StatusEventOpView.Remove, 0)], 3000);
        Assert.True(book.Snapshot.For(2).IsEmpty, "a removed status stayed");
        book.Apply([Status(3, StatusEventOpView.Add, 1000)], 3000);
        book.Prune(9000, id => id == 3);
        Assert.True(book.Snapshot.For(3).IsEmpty, "an ended status survived pruning");
        return Task.CompletedTask;
    }
}
