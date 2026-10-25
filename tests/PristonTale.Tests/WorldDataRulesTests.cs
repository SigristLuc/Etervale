using System.Numerics;
using PristonTale.Application.World;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.World.Ai;
using PristonTale.Simulation.World.Fields;
using PristonTale.Simulation.World.Loot;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Simulation.World.Spawning;

namespace PristonTale.Tests;

// The game rules of the world data keys: death gold, daily drop limits, item sets and the hammer event.
internal static class WorldDataRulesTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("a monster's death gold falls on top of its drop table with its chance", DeathGoldAsync);
        yield return ("a daily-limited item stops dropping at its limit until the date changes", DailyLimitAsync);
        yield return ("an item set gives its bonus once its pieces are worn", SetBonusAsync);
        yield return ("ten hammer blows in a row give the player a big head and turn the monster into its event form", HammerAsync);
    }

    private static Task DeathGoldAsync()
    {
        var definition = Monster(1, MonsterClass.Normal) with
        {
            Drops = new DropTable([DropEntry.NothingSlot(100)], 1, false),
            DeathGold = new MonsterRange(8, 15),
            DeathGoldChance = 100,
            Combat = new MonsterCombatStats(4, 1, new MonsterRange(1, 1), 6, 30, 0, 0, 0, 1, ResistanceBlock.Zero)
        };
        var field = new FieldInstance(1, FieldSpawnData.Empty, new MonsterCatalog([definition]), new EntityIdAllocator(), new CombatRng(7));
        var player = new WorldPlayer(1, Vector3.Zero, new CharacterClassRegistry().CreatePlayer(PlayerClass.Fighter, "Killer"), false, default);
        field.Add(player);
        var monster = ((ISpawnSink)field).Spawn(definition, new Vector3(24, 0, 0), -1, 0);
        Assert.True(field.SetAttackTarget(1, monster.EntityId, true), "fixture target rejected");
        for (var index = 0; index < 400 && monster.IsAlive; index++)
        {
            field.Step(0.05f);
        }

        var drops = field.GroundLoot.Snapshot();
        Assert.True(!monster.IsAlive && drops.Length == 1 && drops[0].View.Gold && drops[0].View.Amount is >= 8 and <= 15,
            "the death gold did not fall in its range");
        return Task.CompletedTask;
    }

    private static Task DailyLimitAsync()
    {
        var limited = new ItemDefinition(ItemCodes.Parse("DA116"), "Limited", ItemKind.Armor) { CatalogKey = "DA116", DailyDropLimit = 2 };
        var free = new ItemDefinition(ItemCodes.Parse("DA115"), "Free", ItemKind.Armor) { CatalogKey = "DA115" };
        var clock = new ManualClock();
        var ledger = new DailyDropLedger(new ItemCatalog([limited, free]), clock);
        Assert.True(ledger.TryTake(limited.Code) && ledger.TryTake(limited.Code) && !ledger.TryTake(limited.Code),
            "the daily limit was not kept");
        Assert.True(Enumerable.Range(0, 10).All(_ => ledger.TryTake(free.Code)), "an unlimited item was limited");
        clock.Advance(TimeSpan.FromDays(1));
        Assert.True(ledger.TryTake(limited.Code), "the limit did not reset with the date");
        return Task.CompletedTask;
    }

    private static Task SetBonusAsync()
    {
        var set = new ItemSetDefinition(1, 2, 400, 2f, 1.5f, 0f, 0f, 0f, "체력+400");
        var axe = new ItemDefinition(ItemCodes.Parse("WA130"), "Verus Axe", ItemKind.Axe, EquipmentSlot.Weapon) { CatalogKey = "WA130", SetCode = 1 };
        var armor = new ItemDefinition(ItemCodes.Parse("DA302"), "Verus Armor", ItemKind.Armor, EquipmentSlot.Armor) { CatalogKey = "DA302", SetCode = 1 };
        var catalog = new ItemCatalog([axe, armor], sets: [set]);
        var character = new CharacterClassRegistry().CreatePlayer(PlayerClass.Fighter, "Wearer", 50);
        Assert.True(Equip(character, new ItemInstance(catalog.Get(axe.Code)), EquipmentSlot.Weapon), "the axe was not worn");
        var one = character.EquipmentTotals;
        Assert.True(one.AddLife == 0 && one.Block == 0, "one piece gave the set bonus");
        Assert.True(Equip(character, new ItemInstance(catalog.Get(armor.Code)), EquipmentSlot.Armor), "the armor was not worn");
        var both = character.EquipmentTotals;
        Assert.True(both.AddLife == 400 && Math.Abs(both.Block - 1.5f) < 0.001f && Math.Abs(both.Absorb - one.Absorb - 2f) < 0.001f,
            "two pieces did not give the set bonus");
        return Task.CompletedTask;
    }

    private static Task HammerAsync()
    {
        var hammer = Monster(1, MonsterClass.Hammer) with { EventModelInxPath = "char\\monster\\Hob\\Hob-b.inx", EventFormCode = 2 };
        var form = hammer with { Code = 2, Class = MonsterClass.Normal, ModelInxPath = hammer.EventModelInxPath, EventModelInxPath = "", EventFormCode = 0 };
        var field = new FieldInstance(1, FieldSpawnData.Empty, new MonsterCatalog([hammer, form]), new EntityIdAllocator(), new CombatRng(11));
        var character = new CharacterClassRegistry().CreatePlayer(PlayerClass.Fighter, "Target", 100);
        var player = new WorldPlayer(1, Vector3.Zero, character, false, default);
        field.Add(player);
        var monster = ((ISpawnSink)field).Spawn(hammer, new Vector3(20, 0, 0), -1, 0);
        for (var index = 0; index < 4000 && character.Premium.BigHeadMode == 0; index++)
        {
            character.Life = character.Derived.MaxLife;
            field.Step(0.05f);
        }

        Assert.True(character.Premium.BigHeadMode is 1 or 2, "ten hammer blows gave no big head");
        field.Step(0.05f);
        var now = field.MonstersForInspection.Single();
        Assert.True(now.Def.Code == form.Code && now.HammerSpent && now.EntityId != monster.EntityId, "the monster did not take its event form");
        return Task.CompletedTask;
    }

    private static MonsterDefinition Monster(int code, MonsterClass monsterClass) => new(code, "Hammer fixture", MonsterFaction.Enemy, monsterClass,
        MonsterBrood.Normal, new MonsterCombatStats(60, 100000, new MonsterRange(1, 1), 9, 60, 0, 0, 0, 5000, ResistanceBlock.Zero), "")
    {
        Ai = new MonsterAi(5, MonsterNature.Evil, 400, 1, 100, MonsterRange.Zero, MonsterActiveHour.Any)
    };

    private static bool Equip(PlayerCharacter character, ItemInstance item, EquipmentSlot slot)
    {
        for (var cell = 0; cell < character.Inventory.Capacity; cell++)
        {
            if (character.Inventory.TryPlace(item, cell))
            {
                return character.TryEquipFromInventory(cell, slot);
            }
        }

        return false;
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 25, 22, 0, 0, TimeSpan.Zero);
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan elapsed) => _now += elapsed;
    }
}
