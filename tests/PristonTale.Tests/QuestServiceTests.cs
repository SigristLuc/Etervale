using System.Numerics;
using PristonTale.Application.Services;
using PristonTale.Application.Sessions;
using PristonTale.Application.World;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Persistence.Abstractions.WorldData;
using PristonTale.Protocol.Binary;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Quests;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Fields;
using PristonTale.Simulation.World.Loot;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Simulation.World.Npcs;
using static PristonTale.Tests.QuestFixture;

namespace PristonTale.Tests;

// Quests through storage, the wire and the world: the journal record, the quest messages, durable requests with kill
// credit, the quest upkeep and the arena duel of the final rank-up.
internal static class QuestServiceTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("the quest journal persists with the character and quest weapons keep their quest variant", PersistenceAsync);
        yield return ("quest messages round-trip their layouts inside the quest extension range", ProtocolAsync);
        yield return ("quest presence reaches the client and the arena shows only the player's own duel", PresenceAsync);
        yield return ("quest requests store once, replay idempotently, refuse on storage failure and credit field kills", RequestsAsync);
        yield return ("the quest upkeep stores quest drops in the pack and keeps them when storage fails", UpkeepAsync);
        yield return ("the master duel travels to the arena, yields at a sixteenth and promotes from the victory dialog", ArenaAsync);
    }

    private static Task PersistenceAsync()
    {
        var fighter = Character(PlayerClass.Fighter, "Keeper", 40, tier: 1);
        Assert.True(Items.TryGetQuestVariant(Code("WA108"), out var variant), "fixture variant missing");
        var weapon = new ItemInstance(variant);
        Assert.True(QuestInventory.TryGiveAll(fighter, [weapon]), "fixture weapon did not fit");
        var journal = fighter.Quests;
        journal.Active = new ActiveQuest
        {
            Code = QuestCode.RankUpQuestWeapon, State = 3, WeaponId = weapon.InstanceId, WeaponStage = 2, WeaponKillsLeft = 17, WeaponLifeBonus = 4,
            WeaponCreatedUnixMs = 123456, StartLevel = 40, Target = 2, Kills = 3, KillsNeeded = 9, Timer = QuestTimerKind.Expiring, TimerMs = 4000,
            Variant = 2, ArenaWon = true, WeaponManaRegenTenths = 5
        };
        journal.LevelLog = LevelQuestLog.Level30 | LevelQuestLog.Level80Bonus;
        journal.ElementaryLog = ElementaryQuestLog.A | ElementaryQuestLog.FuryOfPhantom;
        journal.Finish(QuestCode.RankUpTrophies);
        journal.SkillResetOffered = true;
        var command = new QuestCommand(Guid.NewGuid(), 7, QuestAction.Accept, QuestCode.Level30, 0, 0);
        journal.Record(command);

        var starter = CharacterRecordFactory.NewStarter(Classes, 1, fighter.Name, 0, PlayerClass.Fighter, 3, level: 40, tier: 1) with { Id = 1 };
        var record = CharacterRecordFactory.FromLiveSession(starter, fighter, 3, Vector3.Zero);
        Assert.True(record.Tier == 1, "the record did not keep the tier");
        var restored = Classes.CreatePlayer(PlayerClass.Fighter, fighter.Name, 40);
        CharacterStateCodec.Restore(restored, record, Items);
        var copy = restored.Quests;
        Assert.True(copy.Active == journal.Active && copy.LevelLog == journal.LevelLog && copy.ElementaryLog == journal.ElementaryLog
            && copy.HasFinished(QuestCode.RankUpTrophies) && copy.SkillResetOffered && copy.Revision == journal.Revision
            && copy.Find(command.OperationId)?.Command == command, "the journal did not round-trip");
        Assert.True(restored.FindOwnedItem(weapon.InstanceId)?.Definition.Name == "Quest Axe", "the quest weapon lost its quest variant");

        var saved = QuestStateCodec.Capture(journal);
        Assert.True(saved.Version == QuestStateCodec.CurrentVersion && QuestStateCodec.Read(CharacterJson.Write(saved)).Active == journal.Active,
            "the quest column differs");
        Assert.True(QuestStateCodec.Read(JsonText.EmptyObject).Active.IsIdle, "an empty quest column did not read as idle");
        return Task.CompletedTask;
    }

    private static Task ProtocolAsync()
    {
        uint[] opcodes = [EtervaleOpcodes.QuestRequest, EtervaleOpcodes.QuestResult, EtervaleOpcodes.QuestJournal, EtervaleOpcodes.QuestNotices];
        Assert.True(opcodes.All(code => code is >= 0x45540500 and <= 0x455405FF) && opcodes.Distinct().Count() == 4, "quest opcodes leave their range");

        var buffer = new byte[FrameCodec.MaxFrameSize];
        var request = new QuestRequestPacket(Guid.NewGuid(), 77, (byte)QuestAction.ChooseReward, (ushort)QuestCode.FuryOfPhantom, 12, 3);
        Assert.True(request.Write(buffer) == QuestRequestPacket.Size && QuestRequestPacket.Read(buffer.AsSpan(0, QuestRequestPacket.Size)) == request,
            "the quest request differs");

        var prompt = new QuestPromptEntry(0x28, 13, (byte)QuestFollowup.ConfirmChoice, 2, "haQuestFuryItem2", " Q U E S T ", "Do you want to",
            "receive the item?", "Extreme Talon", ["Minotaur Axe", "Extreme Talon"], ["haQuestFuryItem1", "haQuestFuryItem2"]);
        QuestNoticeEntry[] notices = [new((byte)QuestNoticeKind.Chat, "Quest> Bargon 3 number of monster needed"), new((byte)QuestNoticeKind.Fanfare, "")];
        var result = new QuestResultPacket(request.OperationId, (byte)QuestStatus.Ok, true, true, false, 41, "", prompt, notices);
        var length = result.Write(buffer);
        Assert.True(length <= result.Capacity, "the result capacity is too small");
        var readResult = QuestResultPacket.Read(buffer.AsSpan(0, length));
        Assert.True(readResult.Ok && readResult.ClassChangeOffered && !readResult.SkillResetOffered && readResult.Revision == 41
            && readResult.Notices.SequenceEqual(notices) && Same(readResult.Prompt!, prompt), "the quest result differs");

        var journal = new QuestJournalPacket(9, 0x10, (byte)QuestTimerKind.Expiring, true, 3, "Avelisk-L", 12, 50, 4_000_000, 0, 0,
            [new QuestBoardLine("Job Quest 3th[1]", "Quest4\\ChangeJob4_1.msg")], [new QuestBoardLine("For her(completed)", "LevelQuest\\Quest30a.Msg")]);
        length = journal.Write(buffer);
        var readJournal = QuestJournalPacket.Read(buffer.AsSpan(0, length));
        Assert.True(length <= journal.Capacity && readJournal.TargetName == "Avelisk-L" && readJournal.TimerMs == 4_000_000 && readJournal.SkillResetOffered
            && readJournal.Progress.SequenceEqual(journal.Progress) && readJournal.Finished.SequenceEqual(journal.Finished), "the quest journal differs");

        var world = new QuestNoticesPacket(notices, prompt with { Choices = [], ChoiceDocuments = [] });
        length = world.Write(buffer);
        var readNotices = QuestNoticesPacket.Read(buffer.AsSpan(0, length));
        Assert.True(length <= world.Capacity && readNotices.Notices.SequenceEqual(notices) && readNotices.Prompt!.Document == prompt.Document,
            "the quest notices differ");
        Assert.True(QuestNoticesPacket.Read(buffer.AsSpan(0, new QuestNoticesPacket([], null).Write(buffer))).Prompt is null, "an empty notice list differs");
        return Task.CompletedTask;
    }

    private static bool Same(QuestPromptEntry left, QuestPromptEntry right) =>
        left.Code == right.Code && left.State == right.State && left.Followup == right.Followup && left.Choice == right.Choice
        && left.Document == right.Document && left.Title == right.Title && left.ConfirmLine1 == right.ConfirmLine1 && left.ConfirmLine2 == right.ConfirmLine2
        && left.ConfirmDetail == right.ConfirmDetail && left.Choices.SequenceEqual(right.Choices) && left.ChoiceDocuments.SequenceEqual(right.ChoiceDocuments);

    private static Task PresenceAsync()
    {
        var origin = new Vector3(QuestArena.EntryX, 0, QuestArena.EntryZ);
        var self = new EntityView(1, EntityKindView.Player, true, origin, 0f, MoveStateView.Idle, 60, 100, 100, new PlayerAppearanceView("Self", 2, 2, 0, 0, "", ""));
        var other = new EntityView(2, EntityKindView.Player, false, origin + new Vector3(30, 0, 0), 0f, MoveStateView.Idle, 60, 100, 100,
            new PlayerAppearanceView("Other", 2, 2, 0, 0, "", ""));
        var mine = new EntityView(3, EntityKindView.Monster, false, origin + new Vector3(0, 0, 40), 0f, MoveStateView.Idle, 60, 900, 1000, default, 50)
        {
            DuelPlayerId = 1
        };
        var theirs = new EntityView(4, EntityKindView.Monster, false, origin + new Vector3(0, 0, -40), 0f, MoveStateView.Idle, 60, 900, 1000, default, 50)
        {
            DuelPlayerId = 2
        };
        var view = new QuestView(5, QuestCode.RankUpMasterTrial, 7, "", 0, 0, QuestTimerKind.None, 0,
            [new QuestBoardEntry("Job Quest 3th[4]", "Quest4\\ChangeJob4_4.msg")], []);
        var prompt = new QuestPrompt(QuestCode.RankUpMasterTrial, 8, "ChangeJob4_4End", QuestFollowup.Confirm) { ConfirmLine1 = "Do you wanna" };
        var update = new QuestUpdate(view, [new QuestNotice(QuestNoticeKind.Event, "<Quest> Hunt 50 of Muffin")], prompt);
        var snapshot = new WorldSnapshot(1, [self, other, mine, theirs], fieldId: QuestArena.FieldCode) { Quest = update };
        var updates = new WireRoundTrip(1).Push(snapshot);
        var spawned = updates.SelectMany(entry => entry.Spawns).Select(spawn => spawn.EntityId).ToHashSet();
        Assert.True(spawned.SetEquals([1u, 3u]), $"the arena showed {string.Join(",", spawned)}");
        var quest = updates.Single(entry => entry.Quest is not null).Quest!;
        Assert.True(quest.Revision == 5 && quest.Code == QuestCode.RankUpMasterTrial && quest.Progress.Single().Description == "Quest4\\ChangeJob4_4.msg",
            "the journal did not reach the client");
        var notice = updates.Single(entry => entry.QuestNotices.Count > 0);
        Assert.True(notice.QuestNotices.Single().Kind == QuestNoticeKind.Event && notice.QuestPrompt is { State: 8, Document: "ChangeJob4_4End" },
            "the world's quest dialog did not reach the client");

        var field = new WorldSnapshot(2, [self, other, mine, theirs], fieldId: 3);
        var town = new WireRoundTrip(1).Push(field).SelectMany(entry => entry.Spawns).Select(spawn => spawn.EntityId).ToHashSet();
        Assert.True(town.SetEquals([1u, 2u, 3u, 4u]), "other fields hid players or monsters");
        return Task.CompletedTask;
    }

    private static async Task RequestsAsync()
    {
        var bargon = Monster(1, "Bargon", QuestMonsters.Bargon, life: 6);
        var spawn = new FieldSpawnData([new SpawnPoint(24, 0, true)], [new SpawnTableEntry(1, 100)], [], [])
        {
            MaxConcurrentMonsters = 1,
            MaxPerSpawnPoint = 1
        };
        var world = await QuestWorld.CreateAsync(PlayerClass.Fighter, 30, 0, [new NpcService(NpcServiceKind.QuestGiver, 1001)], [bargon], spawn);
        var (server, quests, token, npc) = (world.Server, world.Quests, world.Session.SessionToken, world.NpcId);
        var character = world.Session.Character!;

        var writes = world.Characters.UpdateCount;
        var talk = await world.Advance(quests.RequestAsync(token, new QuestCommand(Guid.NewGuid(), npc, QuestAction.Talk, QuestCode.None, 0, 0)));
        Assert.True(talk.Ok && talk.Prompt is { Code: QuestCode.Level30, State: 0 } && world.Characters.UpdateCount == writes, "a talk wrote or found no offer");
        Assert.True(!(await world.Advance(quests.RequestAsync(token, new QuestCommand(Guid.NewGuid(), 0, QuestAction.Talk, QuestCode.None, 0, 0)))).Ok,
            "a talk without an NPC was accepted");

        var accept = new QuestCommand(Guid.NewGuid(), npc, QuestAction.Accept, QuestCode.Level30, 0, 0);
        world.Characters.BeforeUpdate = _ => Task.FromResult(false);
        var refused = await world.Advance(quests.RequestAsync(token, accept));
        Assert.True(refused.Status == QuestStatus.StorageFailed && character.Quests.Active.IsIdle, "a failed write started the quest");
        world.Characters.BeforeUpdate = null;
        var accepted = await world.Advance(quests.RequestAsync(token, accept));
        Assert.True(accepted.Ok && character.Quests.Active is { Code: QuestCode.Level30, State: 1 }
            && QuestStateCodec.Read(world.Characters.Record.Quests).Active.Code == QuestCode.Level30, "the accepted quest was not stored");
        writes = world.Characters.UpdateCount;
        Assert.True((await world.Advance(quests.RequestAsync(token, accept))).Message == "already committed" && world.Characters.UpdateCount == writes,
            "a replayed accept wrote again");
        Assert.True((await world.Advance(quests.RequestAsync(token, accept with { State = 1 }))).Status == QuestStatus.Conflict,
            "a reused operation id was accepted for another step");

        world.Player.Teleport(new Vector3(2000, 0, 2000));
        var far = await world.Advance(quests.RequestAsync(token, accept with { OperationId = Guid.NewGuid(), Action = QuestAction.Proceed, State = 2 }));
        Assert.True(far.Status == QuestStatus.OutOfReach, "a remote NPC answered");
        world.Player.Teleport(Vector3.Zero);

        character.Quests.Active = character.Quests.Active with { Kills = 19 };
        var target = world.WaitFor(snapshot => snapshot.Entities.Any(entity => entity.Kind == EntityKindView.Monster))
            .Entities.First(entity => entity.Kind == EntityKindView.Monster);
        await world.Advance(server.SetAttackTargetAsync(3, world.Session.EntityId, target.EntityId, true));
        var credited = world.WaitFor(snapshot => snapshot.Quest?.View is { State: 2 });
        Assert.True(credited.Quest!.View!.Kills == 20 && credited.Quest.Notices.Any(n => n.Text == QuestTexts.TwentyBargons)
            && credited.Quest.Notices.Any(n => n.Kind == QuestNoticeKind.Chat), "the field kill did not credit the hunt");

        var points = character.StatPoints;
        var reward = await world.Advance(quests.RequestAsync(token, new QuestCommand(Guid.NewGuid(), npc, QuestAction.Proceed, QuestCode.Level30, 2, 0)));
        Assert.True(reward.Ok && character.StatPoints == points + 5 && world.Characters.Record.StatPoints == points + 5
            && reward.Notices.Any(n => n.Text == QuestTexts.StatPointsFive), "the reward was not stored");
        Assert.True(!(await world.Advance(quests.RequestAsync(token, new QuestCommand(Guid.NewGuid(), npc, QuestAction.Proceed, QuestCode.Level30, 2, 0)))).Ok
            && character.StatPoints == points + 5, "a new request paid the reward twice");
    }

    private static async Task UpkeepAsync()
    {
        var world = await QuestWorld.CreateAsync(PlayerClass.Fighter, 20, 0, [new NpcService(NpcServiceKind.SkillMaster, 4)], [], null);
        var character = world.Session.Character!;
        character.Quests.Active = new ActiveQuest { Code = QuestCode.RankUpTrophies, State = 1 };
        world.Player.QuestState.AddDrop(new PendingQuestDrop(QuestItems.SilverWolverineTrophy, 1, 0, QuestDropSource.MainTable));
        world.Characters.BeforeUpdate = _ => Task.FromResult(false);
        Assert.True(!await world.Advance(world.Quests.RunUpkeepAsync(world.Session)) && !QuestInventory.Has(character, QuestItems.SilverWolverineTrophy)
            && world.Player.QuestState.Drops.Count == 1, "a failed upkeep lost or stored its drop");
        world.Characters.BeforeUpdate = null;
        Assert.True(await world.Advance(world.Quests.RunUpkeepAsync(world.Session)) && QuestInventory.Has(character, QuestItems.SilverWolverineTrophy)
            && world.Player.QuestState.Drops.Count == 0, "the upkeep did not store the drop");
        var record = Classes.CreatePlayer(PlayerClass.Fighter, character.Name, 20);
        CharacterStateCodec.Restore(record, world.Characters.Record, Items);
        Assert.True(QuestInventory.Has(record, QuestItems.SilverWolverineTrophy), "the stored trophy is missing from the record");
        Assert.True(await world.Advance(world.Quests.RunUpkeepAsync(world.Session)), "an idle upkeep failed");
    }

    private static async Task ArenaAsync()
    {
        var master = Monster(50, "Skill Master", 0, life: 1000, level: 60) with { SoundCode = LegacyNpcSoundCode.SkillMaster };
        var world = await QuestWorld.CreateAsync(PlayerClass.Fighter, 60, 2, [new NpcService(NpcServiceKind.SkillMaster, 4)], [master], null,
            strongWeapon: true);
        var (quests, token, npc) = (world.Quests, world.Session.SessionToken, world.NpcId);
        var character = world.Session.Character!;
        character.Quests.Active = new ActiveQuest { Code = QuestCode.RankUpMasterTrial, State = 6 };
        Give(character, QuestItems.RoyalAmulet);

        var travel = await world.Advance(quests.RequestAsync(token, new QuestCommand(Guid.NewGuid(), npc, QuestAction.Proceed, QuestCode.RankUpMasterTrial, 6, 0)));
        Assert.True(travel.Ok && character.Quests.Active.State == 7 && !QuestInventory.Has(character, QuestItems.RoyalAmulet), "the duel step failed");
        var arena = world.WaitFor(snapshot => snapshot.FieldId == QuestArena.FieldCode
            && snapshot.Entities.Any(entity => entity.Kind == EntityKindView.Monster && entity.DuelPlayerId == world.Session.EntityId));
        var opponent = arena.Entities.Single(entity => entity.Kind == EntityKindView.Monster);
        Assert.True(opponent.MaxHp == 1000, "the opponent did not scale with the player");
        Assert.True(world.Characters.Record.FieldId == 3 || world.Characters.Record.FieldId == QuestArena.FieldCode, "the record field differs");

        // The client walks the player into reach; the test places it there.
        world.Player.Teleport(opponent.Position + new Vector3(20, 0, 0));
        await world.Advance(world.Server.SetAttackTargetAsync(QuestArena.FieldCode, world.Session.EntityId, opponent.EntityId, true));
        var won = world.WaitFor(snapshot => snapshot.Quest?.Prompt is { State: 8 }, 6000);
        var yielded = won.Entities.Single(entity => entity.EntityId == opponent.EntityId);
        Assert.True(yielded.Hp == QuestArena.YieldLife(1000) && character.Quests.Active is { State: 8, ArenaWon: true }, "the master did not yield");

        var promote = await world.Advance(quests.RequestAsync(token, new QuestCommand(Guid.NewGuid(), 0, QuestAction.Accept, QuestCode.RankUpMasterTrial, 8, 0)));
        Assert.True(promote.Ok && character.Tier == 3 && world.Characters.Record.Tier == 3 && character.Quests.HasFinished(QuestCode.RankUpMasterTrial),
            "the victory dialog did not promote");
        world.WaitFor(snapshot => snapshot.FieldId == 3);
        Assert.True(!(await world.Advance(quests.RequestAsync(token, new QuestCommand(Guid.NewGuid(), 0, QuestAction.Talk, QuestCode.None, 0, 0)))).Ok,
            "a talk without an NPC was accepted after the duel");
    }

    private static MonsterDefinition Monster(int code, string name, int questCode, int life, int level = 10) =>
        new(code, name, MonsterFaction.Enemy, MonsterClass.Normal, MonsterBrood.Normal,
            new MonsterCombatStats(level, 1, new MonsterRange(1, 1), 6, 30, 0, 0, 0, life, ResistanceBlock.Zero), "")
        {
            QuestCode = questCode,
            Ai = new MonsterAi(5, MonsterNature.Evil, 200, 1, 0, MonsterRange.Zero, MonsterActiveHour.Any)
        };

    private sealed class QuestWorld
    {
        public required WorldServer Server { get; init; }
        public required QuestAppService Quests { get; init; }
        public required PlayerSession Session { get; init; }
        public required WorldPlayer Player { get; init; }
        public required MemoryCharacters Characters { get; init; }
        public required WorldOutbox Outbox { get; init; }
        public required uint NpcId { get; init; }
        private uint _tick;

        public static async Task<QuestWorld> CreateAsync(PlayerClass playerClass, int level, short tier, IReadOnlyList<NpcService> services,
            IReadOnlyList<MonsterDefinition> monsters, FieldSpawnData? spawn, bool strongWeapon = false)
        {
            var name = "Quester";
            var starter = CharacterRecordFactory.NewStarter(Classes, 1, name, 0, playerClass, 3, level: level, tier: tier) with { Id = 1 };
            var character = Character(playerClass, name, level, tier);
            if (strongWeapon)
            {
                Assert.True(Items.TryGet(Code("WA199"), out var axe), "fixture weapon missing");
                character.Equip(EquipmentSlot.Weapon, new ItemInstance(axe));
            }

            var characters = new MemoryCharacters(CharacterRecordFactory.FromLiveSession(starter, character, 3, Vector3.Zero));
            var npcDefinition = new NpcDefinition(1, "퀘스트", "quest.inx", services) { DisplayName = "Quest NPC", Dialog = ["Hello."] };
            var empty = WorldData.Empty;
            var holder = new WorldDataHolder
            {
                Current = new WorldData(Items, new MonsterCatalog(monsters), new NpcCatalog([npcDefinition]),
                    new Dictionary<int, FieldSpawnData>
                    {
                        [3] = (spawn ?? FieldSpawnData.Empty) with { FixedNpcs = [new FixedNpc(1, 10 * 256, 0, 0)] }
                    },
                    empty.Motions)
            };
            var catalog = new WorldCatalog(
            [
                new FieldDef("ricarten", "Ricarten", FieldState.Village, new FieldBackdrop("", "", ""), "", []) { FieldCode = 3 },
                new FieldDef("arena", "Arena", FieldState.QuestArena, new FieldBackdrop("", "", ""), "", []) { FieldCode = QuestArena.FieldCode }
            ]);
            var server = new WorldServer(holder, catalog);
            var sessions = new SessionRegistry();
            var session = sessions.Create(1, "owner");
            var sessionService = new SessionAppService(sessions, Classes, characters, catalog, server, holder);
            Assert.True((await sessionService.EnterWorldAsync(session.SessionToken, name)).Ok && session.TryBeginPlay(), "quest fixture entry failed");
            var player = WorldPlayerFactory.Create(session);
            var outbox = new WorldOutbox();
            var world = new QuestWorld
            {
                Server = server,
                Quests = new QuestAppService(sessions, characters, server),
                Session = session,
                Player = player,
                Characters = characters,
                Outbox = outbox,
                NpcId = 0
            };
            await world.Advance(server.JoinAsync(3, player, outbox.Writer, session));
            var roster = world.WaitFor(snapshot => snapshot.Entities.Any(entity => entity.Kind == EntityKindView.Npc));
            return new QuestWorld
            {
                Server = server,
                Quests = world.Quests,
                Session = session,
                Player = player,
                Characters = characters,
                Outbox = outbox,
                NpcId = roster.Entities.First(entity => entity.Kind == EntityKindView.Npc).EntityId,
                _tick = world._tick
            };
        }

        public async Task<T> Advance<T>(Task<T> task)
        {
            for (var step = 0; !task.IsCompleted && step < 1000; step++)
            {
                Server.Tick(0.05f, ++_tick);
                await Task.Delay(1);
            }

            return await task.WaitAsync(TimeSpan.FromSeconds(1));
        }

        public WorldSnapshot WaitFor(Func<WorldSnapshot, bool> condition, int ticks = 1500)
        {
            for (var step = 0; step < ticks; step++)
            {
                Server.Tick(0.05f, ++_tick);
                while (Outbox.Reader.TryRead(out var snapshot))
                {
                    if (condition(snapshot))
                    {
                        return snapshot;
                    }
                }
            }

            throw new InvalidOperationException("the world never reached the expected state");
        }
    }
}
