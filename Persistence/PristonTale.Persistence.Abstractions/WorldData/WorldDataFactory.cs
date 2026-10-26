using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.World.Fields;
using PristonTale.Simulation.World.Loot;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Simulation.World.Motion;
using PristonTale.Simulation.World.Npcs;
using PristonTale.Simulation.Items;

namespace PristonTale.Persistence.Abstractions.WorldData;

public static class WorldDataFactory
{

    public static ItemDefinition ToItemDefinition(ItemTemplateRecord r)
    {
        // The shop form: the low end of every authored range. Copies made by drops roll from the template.
        var weapon = r.DamageMinHigh != 0 || r.DamageMaxHigh != 0;
        var baseStats = new ItemStatBlock(
            DamageMin: weapon ? r.DamageMinLow : 0,
            DamageMax: weapon ? r.DamageMaxLow : 0,
            Defence: r.DefenceMin,
            AttackRating: r.AttackRatingMin,
            AttackSpeed: r.AttackSpeed,
            Strength: 0, Spirit: 0, Talent: 0, Agility: 0, Health: 0);

        return ItemEquipmentMetadata.Apply(new ItemDefinition(
            (uint)r.ItemCodeMask,
            r.Name,
            (ItemKind)r.Kind,
            GridWidth: Math.Max(1, r.GridWidth),
            GridHeight: Math.Max(1, r.GridHeight),
            MaxStack: ItemDefinition.StackLimit((ItemKind)r.Kind, r.MaxStack))
        {
            CatalogKey = r.Code,
            // Potions and event foods restore their authored ranges; only potions are drunk from the belt.
            Potion = (ItemKind)r.Kind == ItemKind.Potion || r.RestoreLifeMax > 0 || r.RestoreManaMax > 0 || r.RestoreStaminaMax > 0
                ? new PotionDefinition(r.RestoreLifeMin, r.RestoreLifeMax, r.RestoreManaMin, r.RestoreManaMax, r.RestoreStaminaMin, r.RestoreStaminaMax)
                : null,
            RequiredLevel = r.ReqLevel,
            Requirements = new(r.ReqStrength, r.ReqSpirit, r.ReqTalent, r.ReqDexterity, r.ReqHealth),
            AllowedJobs = ItemClassRestrictions.AllowedJobs(r.Code),
            SpecializationJobs = (uint)r.JobMask,
            ShootingRange = r.ShootingRange,
            BaseStats = baseStats,
            MaxDurability = r.DurabilityMin,
            Weight = r.Weight,
            Price = r.Price,
            Resistances = new ResistanceBlock((short)r.ResBionicMin, (short)r.ResEarthMin, (short)r.ResFireMin, (short)r.ResIceMin,
                (short)r.ResLightingMin, (short)r.ResPoisonMin, (short)r.ResWaterMin, (short)r.ResWindMin),
            Critical = r.CriticalHit,
            PotionSpace = r.PotionSpace,
            MagicMastery = r.MagicMastery,
            UniqueItem = (int)r.UniqueItem,
            Template = ToItemTemplate(r),
            Glow = new ItemGlow(Channel(r.GlowR), Channel(r.GlowG), Channel(r.GlowB), Channel(r.GlowA), Math.Clamp(r.GlowBlink, -1, 16), r.GlowScale),
            DisplayEffect = r.DisplayEffect,
            DailyDropLimit = Math.Max(0, r.DailyDropLimit),
            SetCode = r.SetCode,
        });
    }

    public static IReadOnlyList<ItemDefinition> ToItemDefinitions(IEnumerable<ItemTemplateRecord> records) =>
        records.Where(static record => record.ItemCodeMask != 0 && !IsQuestVariantKey(record.Code) && !IsNamedUniqueKey(record.Code))
            .Select(ToItemDefinition).ToList();

    // Rows of a code's named uniques are keyed "<code>#U<n>" by the importer.
    public const string NamedUniqueSuffix = "#U";

    public static bool IsNamedUniqueKey(string code) => code.Contains(NamedUniqueSuffix, StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<ItemDefinition> ToNamedUniqueDefinitions(IEnumerable<ItemTemplateRecord> records) =>
        records.Where(static record => record.ItemCodeMask != 0 && IsNamedUniqueKey(record.Code))
            .Select(static record =>
            {
                var at = record.Code.IndexOf(NamedUniqueSuffix, StringComparison.OrdinalIgnoreCase);
                var variant = int.Parse(record.Code[(at + NamedUniqueSuffix.Length)..], System.Globalization.CultureInfo.InvariantCulture);
                return ToItemDefinition(record with { Code = record.Code[..at] }) with { UniqueVariant = variant };
            })
            .ToList();

    public static IReadOnlyList<ItemSetDefinition> ToItemSets(IEnumerable<ItemSetRecord> records) =>
        records.Select(static r => new ItemSetDefinition(r.SetCode, r.Pieces, r.Life, r.Absorb, r.Block, r.LifeRegen, r.ManaRegen, r.StaminaRegen,
            r.Description)).ToList();

    // Rows of the quest variant of a code are keyed "<code>#Q" by the importer; they load beside the regular catalog.
    public const string QuestVariantSuffix = "#Q";

    public static bool IsQuestVariantKey(string code) => code.EndsWith(QuestVariantSuffix, StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<ItemDefinition> ToQuestVariantDefinitions(IEnumerable<ItemTemplateRecord> records) =>
        records.Where(static record => record.ItemCodeMask != 0 && IsQuestVariantKey(record.Code))
            .Select(static record => ToItemDefinition(record with { Code = record.Code[..^QuestVariantSuffix.Length] }))
            .ToList();

    // Special bonus codes as the importer stores them: 0 absorb, 1 defence, 2 move speed, 3 block, 4 attack speed,
    // 5 critical, 6 range, 7 magic mastery, 8 mana per level, 9 life per level, 10 attack rating per level, 11 damage per
    // level (second value), 12 mana regeneration, 13 life regeneration, 14 stamina regeneration, 15 life regeneration range.
    public static ItemTemplate ToItemTemplate(ItemTemplateRecord r)
    {
        var spec = new ItemSpecTemplate();
        foreach (var special in r.Specials)
        {
            var range = new ItemFloatRange(special.Value0, special.Value1);
            var whole = new ItemRange((int)special.Value0, (int)special.Value1);
            spec = special.BonusCode switch
            {
                0 => spec with { Absorb = range },
                1 => spec with { Defence = whole },
                2 => spec with { MoveSpeed = range },
                3 => spec with { Block = special.Value0 },
                4 => spec with { AttackSpeed = (int)special.Value0 },
                5 => spec with { Critical = (int)special.Value0 },
                6 => spec with { ShootingRange = (int)special.Value0 },
                7 => spec with { MagicMastery = range },
                8 => spec with { LevelMana = (int)special.Value0 },
                9 => spec with { LevelLife = (int)special.Value0 },
                10 => spec with { LevelAttackRating = whole },
                11 => spec with { LevelDamage = (int)special.Value1 },
                12 => spec with { ManaRegen = range },
                13 => spec with { LifeRegen = special.Value0, LifeRegenRange = ItemFloatRange.None },
                14 => spec with { StaminaRegen = special.Value0 },
                15 => spec with { LifeRegen = special.Value0, LifeRegenRange = range },
                _ => spec
            };
        }

        return new ItemTemplate
        {
            Durability = new ItemRange(r.DurabilityMin, r.DurabilityMax),
            DamageMin = new ItemRange(r.DamageMinLow, r.DamageMinHigh),
            DamageMax = new ItemRange(r.DamageMaxLow, r.DamageMaxHigh),
            AttackRating = new ItemRange(r.AttackRatingMin, r.AttackRatingMax),
            Defence = new ItemRange(r.DefenceMin, r.DefenceMax),
            Absorb = new ItemFloatRange(r.AbsorbMin, r.AbsorbMax),
            Block = new ItemFloatRange(r.BlockMin, r.BlockMax),
            MoveSpeed = new ItemFloatRange(r.SpeedMin, r.SpeedMax),
            LifeRegen = new ItemFloatRange(r.LifeRegenMin, r.LifeRegenMax),
            ManaRegen = new ItemFloatRange(r.ManaRegenMin, r.ManaRegenMax),
            StaminaRegen = new ItemFloatRange(r.StaminaRegenMin, r.StaminaRegenMax),
            AddLife = new ItemRange(r.AddLifeMin, r.AddLifeMax),
            AddMana = new ItemRange(r.AddManaMin, r.AddManaMax),
            AddStamina = new ItemRange(r.AddStaminaMin, r.AddStaminaMax),
            Resistances =
            [
                new(r.ResBionicMin, r.ResBionicMax), new(r.ResEarthMin, r.ResEarthMax), new(r.ResFireMin, r.ResFireMax),
                new(r.ResIceMin, r.ResIceMax), new(r.ResLightingMin, r.ResLightingMax), new(r.ResPoisonMin, r.ResPoisonMax),
                new(r.ResWaterMin, r.ResWaterMax), new(r.ResWindMin, r.ResWindMax)
            ],
            SpecJob = (uint)r.JobMask,
            RandomSpecJobs = r.JobRandom.OrderBy(static job => job.Ordinal).Select(static job => (uint)job.JobBitCode).Where(static job => job != 0).ToList(),
            Spec = spec
        };
    }

    // Source configs name authoring .ini files, while shipped model tables use .inx.
    // Keep the persisted value intact and normalize only the runtime definition.
    private static string ToInxModelPath(string modelPath) =>
        string.IsNullOrEmpty(modelPath) ? modelPath : System.IO.Path.ChangeExtension(modelPath, ".inx");

    public static (IReadOnlyList<MonsterDefinition> Definitions, IReadOnlyDictionary<string, int> CodeByName)
        ToMonsterDefinitions(IReadOnlyList<MonsterRecord> records, IReadOnlyDictionary<string, uint>? itemCodes = null)
    {
        var codeByName = new Dictionary<string, int>(records.Count);
        var defs = new List<MonsterDefinition>(records.Count);
        for (var i = 0; i < records.Count; i++)
        {
            var code = i + 1; // 1-based stable id; 0 stays "unset" for FieldEntitySpawn template ids.
            codeByName[records[i].Name] = code;
            defs.Add(ToMonsterDefinition(records[i], code, itemCodes));
        }

        // A monster with an event model gets its event form after every authored monster: the same monster, normal class,
        // wearing the event model (the hammer event turns it into this form).
        var authored = defs.Count;
        for (var i = 0; i < authored; i++)
        {
            if (defs[i].EventModelInxPath.Length == 0)
            {
                continue;
            }

            var formCode = defs.Count + 1;
            defs.Add(defs[i] with
            {
                Code = formCode, ModelInxPath = defs[i].EventModelInxPath, Class = MonsterClass.Normal, EventModelInxPath = "", EventFormCode = 0
            });
            defs[i] = defs[i] with { EventFormCode = formCode };
        }

        return (defs, codeByName);
    }

    private static MonsterDefinition ToMonsterDefinition(
        MonsterRecord r, int code, IReadOnlyDictionary<string, uint>? itemCodes)
    {
        var combat = new MonsterCombatStats(
            Level: r.Level,
            AttackRatingAccuracy: r.Accuracy,
            AttackDamage: new MonsterRange(r.AttackMin, r.AttackMax),
            AttackSpeed: r.AttackSpeed,
            MeleeRange: r.AttackRange,
            Defence: r.Defence,
            Absorption: r.AbsorbPct,
            BlockChance: r.BlockPct,
            MaxLife: r.Hp,
            Resistances: ToMonsterResistances(r))
        {
            DamageStunPercent = r.StunPct,
        };

        // Curse skills deal no damage and have no area; they still carry a rating and a curse code.
        var skill = (r.SkillDamageMin == 0 && r.SkillDamageMax == 0 && r.SkillRange == 0 && r.SkillDistance == 0 && r.SkillRatingPct == 0
                && r.SkillCurseCode == 0)
            ? MonsterSkillStats.None
            : new MonsterSkillStats(
                new MonsterRange(r.SkillDamageMin, r.SkillDamageMax),
                r.SkillRange, r.SkillDistance, r.SkillRatingPct, r.SkillCurseCode);

        var ai = new MonsterAi(
            Iq: r.Iq,
            Nature: (MonsterNature)r.Nature,
            SightRadius: r.Sight,
            MoveSpeed: r.MoveSpeed,
            MoveRange: r.MoveRange,
            GroupSize: new MonsterRange(r.GroupMin, r.GroupMax),
            ActiveHour: ToActiveHour(r.ActiveHour));

        var monsterClass = (MonsterClass)r.Class;
        return new MonsterDefinition(
            code, string.IsNullOrWhiteSpace(r.DisplayName) ? "Monster" : r.DisplayName,
            Faction: r.Hostile ? MonsterFaction.Enemy : MonsterFaction.Npc,
            Class: monsterClass,
            Brood: (MonsterBrood)r.Brood,
            Combat: combat,
            ModelInxPath: ToInxModelPath(r.Model))
        {
            Skill = skill,
            Key = r.Name,
            Ai = ai,
            Drops = ToDropTable(r, itemCodes),
            ExpReward = r.Exp >= 0 ? (ulong)r.Exp : throw new InvalidDataException($"Monster '{r.Name}' has negative experience {r.Exp}."),
            SoundCode = r.SoundCode,
            ModelScale = r.ModelScale,
            PortraitOffsetY = r.PortraitOffsetY,
            PortraitOffsetZ = r.PortraitOffsetZ,
            QuestCode = r.QuestCode,
            EventItem = TryResolveDrop(r.EventItem, itemCodes, out var eventItem) ? eventItem : 0,
            PotionCount = r.PotionCount,
            PotionPercent = r.PotionPercent,
            ShadowSizeLevel = r.SizeLevel,
            MagicResistance = r.ResMagic,
            MoveType = r.MoveType,
            SpecialAttackPercent = r.SpecialAttackPct,
            DeathGold = new MonsterRange(Math.Min(r.GoldMin, r.GoldMax), Math.Max(r.GoldMin, r.GoldMax)),
            DeathGoldChance = Math.Clamp(r.GoldPct, 0, 100),
            EventModelInxPath = string.IsNullOrWhiteSpace(r.EventModel) ? "" : ToInxModelPath(r.EventModel),
            EventCode = r.EventCode,
            EventInfo = r.EventInfo,
        };
    }

    private static ResistanceBlock ToMonsterResistances(MonsterRecord r) => new(
        (short)r.ResBionic, (short)r.ResEarth, (short)r.ResFire, (short)r.ResIce,
        (short)r.ResLighting, (short)r.ResPoison, (short)r.ResWater, (short)r.ResWind);

    private static MonsterActiveHour ToActiveHour(int raw) => raw switch
    {
        > 0 => MonsterActiveHour.Day,
        < 0 => MonsterActiveHour.Night,
        _ => MonsterActiveHour.Any,
    };

    // Unresolved authored drops retain their probability as empty slots; bonus drops have no probability slot.
    private static DropTable ToDropTable(MonsterRecord r, IReadOnlyDictionary<string, uint>? itemCodes)
    {
        if (r.DropConfig is null && r.DropItems.Count == 0 && r.DropsPlus.Count == 0)
        {
            return DropTable.Empty;
        }

        var entries = new List<DropEntry>(r.DropItems.Count + r.DropsPlus.Count);
        foreach (var d in r.DropItems)
        {
            if ((DropKind)d.Kind == DropKind.Gold)
            {
                entries.Add(DropEntry.GoldSlot(d.Weight, d.GoldMin, d.GoldMax));
            }
            else if ((DropKind)d.Kind == DropKind.Item && TryResolveDrop(d.ItemCode, itemCodes, out var code))
            {
                entries.Add(new DropEntry(DropKind.Item, d.Weight, code, 0, 0, IsBonus: false));
            }
            else
            {
                entries.Add(DropEntry.NothingSlot(d.Weight));
            }
        }
        foreach (var p in r.DropsPlus)
        {
            if (!TryResolveDrop(p.ItemCode, itemCodes, out var code))
            {
                continue;
            }

            entries.Add(new DropEntry(DropKind.Item, p.Weight, code, 0, 0, IsBonus: true));
        }

        return new DropTable(
            entries,
            MaxItemsPerKill: r.DropConfig?.MaxItemsPerKill ?? 0,
            AllPlayersSeeDrops: r.DropConfig?.AllPlayersSee ?? false);
    }

    private static bool TryResolveDrop(string? key, IReadOnlyDictionary<string, uint>? itemCodes, out uint code)
    {
        code = 0;
        return key is not null && itemCodes is not null && itemCodes.TryGetValue(key, out code) && code != 0;
    }

    public static (IReadOnlyList<NpcDefinition> Definitions, IReadOnlyDictionary<string, int> CodeByName)
        ToNpcDefinitions(IReadOnlyList<NpcRecord> records)
    {
        var codeByName = new Dictionary<string, int>(records.Count);
        var defs = new List<NpcDefinition>(records.Count);
        for (var i = 0; i < records.Count; i++)
        {
            var code = i + 1;
            codeByName[records[i].Name] = code;
            defs.Add(ToNpcDefinition(records[i], code));
        }
        return (defs, codeByName);
    }

    private static NpcDefinition ToNpcDefinition(NpcRecord r, int code)
    {
        var dialog = CharacterJson.Read<List<string>>(r.Dialog) ?? [];
        var weapons = r.ShopStock.Where(static s => s.Tab == 0).OrderBy(static s => s.Ordinal);
        var armour = r.ShopStock.Where(static s => s.Tab == 1).OrderBy(static s => s.Ordinal);
        var misc = r.ShopStock.Where(static s => s.Tab == 2).OrderBy(static s => s.Ordinal);
        var shop = r.ShopStock.Count == 0
            ? NpcShopStock.Empty
            : new NpcShopStock(
                weapons.Select(static s => s.ItemCode).Where(IsSupportedItemCode).Select(ItemCodes.Parse).ToList(),
                armour.Select(static s => s.ItemCode).Where(IsSupportedItemCode).Select(ItemCodes.Parse).ToList(),
                misc.Select(static s => s.ItemCode).Where(IsSupportedItemCode).Select(ItemCodes.Parse).ToList());

        return new NpcDefinition(code, r.Name, ToInxModelPath(r.Model), ToServices(r, shop))
        {
            DisplayName = r.DisplayName ?? "",
            Shop = shop,
            Dialog = dialog,
            SoundCode = r.SoundCode,
            ModelScale = r.ModelScale,
            MoveRange = r.MoveRange,
            PortraitOffsetY = r.PortraitOffsetY,
            PortraitOffsetZ = r.PortraitOffsetZ,
            Hostile = r.Hostile,
            Level = r.Level,
            ShadowSizeLevel = r.SizeLevel,
            AppearSlot = r.AppearSlot,
            AppearCycle = Math.Max(0, r.AppearCycle),
        };
    }

    private static bool IsSupportedItemCode(string code) => ItemCodes.TryParse(code, out _);

    // A shop whose stock has no runtime item is not offered; unknown service kinds from a newer importer are skipped.
    private static List<NpcService> ToServices(NpcRecord r, NpcShopStock shop)
    {
        var services = r.Services.Count > 0
            ? r.Services
                .OrderBy(static s => s.Ordinal)
                .Where(static s => Enum.IsDefined((NpcServiceKind)s.Kind))
                .Select(static s => new NpcService((NpcServiceKind)s.Kind, s.Value, s.Value2, s.Text ?? ""))
                .ToList()
            : LegacyServices(r);
        services.RemoveAll(service => service.Kind == NpcServiceKind.Shop && shop.IsEmpty);
        return services;
    }

    private static List<NpcService> LegacyServices(NpcRecord r)
    {
        var services = new List<NpcService>();
        if (r.SvcQuest) services.Add(new NpcService(NpcServiceKind.QuestScript, r.QuestCode, r.QuestParam));
        if (r.SvcShop) services.Add(new NpcService(NpcServiceKind.Shop));
        if (r.SvcSkillMaster) services.Add(new NpcService(NpcServiceKind.SkillMaster, r.SvcJobChange ? r.JobChangeCode : 0));
        if (r.SvcWarehouse) services.Add(new NpcService(NpcServiceKind.Warehouse));
        if (r.SvcItemMix) services.Add(new NpcService(NpcServiceKind.ItemMix, 1));
        if (r.SvcSmelting) services.Add(new NpcService(NpcServiceKind.Smelting));
        if (r.SvcManufacture) services.Add(new NpcService(NpcServiceKind.Manufacture));
        if (r.SvcCollectMoney) services.Add(new NpcService(NpcServiceKind.CollectMoney));
        if (r.SvcTeleport) services.Add(new NpcService(NpcServiceKind.Teleporter, r.TeleportCode == 0 ? 1 : r.TeleportCode));
        if (r.SvcGiveMoney) services.Add(new NpcService(NpcServiceKind.GiveMoney));
        if (r.SvcEventBooth) services.Add(new NpcService(NpcServiceKind.EventBooth, 1));
        if (r.SvcItemAging) services.Add(new NpcService(NpcServiceKind.ItemAging));
        if (r.SvcClan) services.Add(new NpcService(NpcServiceKind.Clan));
        if (r.SvcBlessCastle) services.Add(new NpcService(NpcServiceKind.BlessCastle, 1));
        if (r.SvcMixtureReset) services.Add(new NpcService(NpcServiceKind.MixtureReset));
        return services;
    }

    public static FieldSpawnData ToFieldSpawnData(
        FieldSpawnRecord r,
        IReadOnlyDictionary<string, int> monsterCodeByName,
        IReadOnlyDictionary<string, int> npcCodeByName)
    {
        var points = r.SpawnPoints
            .Select(static p => new SpawnPoint(p.X, p.Z, Active: p.State != 0))
            .ToList();

        var table = r.MonsterSpawns
            .Select(s => new SpawnTableEntry(Resolve(monsterCodeByName, s.MonsterName), s.Weight))
            .Where(static e => e.MonsterCode != 0)
            .ToList();

        var bosses = r.BossSpawns
            .Select(b => new BossSpawn(
                Resolve(monsterCodeByName, b.BossMonsterName),
                b.EscortMonsterName is null ? 0 : Resolve(monsterCodeByName, b.EscortMonsterName),
                b.EscortCount,
                ParseHours(b.SpawnHours)))
            .Where(static b => b.BossMonsterCode != 0)
            .ToList();

        var fixedNpcs = r.FixedNpcs
            .Select(n => new FixedNpc(Resolve(npcCodeByName, n.NpcName), n.X, n.Z, n.Angle, n.Y))
            .Where(static n => n.NpcCode != 0)
            .ToList();

        return new FieldSpawnData(points, table, bosses, fixedNpcs)
        {
            MaxConcurrentMonsters = r.MaxConcurrentMonsters,
            MaxPerSpawnPoint = r.MaxPerSpawnPoint,
            SpawnIntervalMask = r.SpawnInterval,
            PerPointCooldownMs = r.PerPointCooldownMs,
        };
    }

    public static IReadOnlyDictionary<int, FieldSpawnData> ToFieldSpawnData(
        IEnumerable<FieldSpawnRecord> records,
        IReadOnlyDictionary<string, int> monsterCodeByName,
        IReadOnlyDictionary<string, int> npcCodeByName) =>
        records.ToDictionary(
            static r => r.FieldCode,
            r => ToFieldSpawnData(r, monsterCodeByName, npcCodeByName));

    private static short Channel(int value) => (short)Math.Clamp(value, -255, 255);

    private static int Resolve(IReadOnlyDictionary<string, int> map, string name) =>
        map.TryGetValue(name, out var code) ? code : 0;

    private static IReadOnlyList<int> ParseHours(string csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
            return [];
        var hours = new List<int>();
        foreach (var part in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (int.TryParse(part, out var h))
                hours.Add(h);
        return hours;
    }

    public static MotionTimingCatalog ToMotionTimings(
        IReadOnlyList<MotionModelRecord> models,
        IReadOnlyList<MotionSlotRecord> slots)
    {
        if (models.Count == 0)
            return MotionTimingCatalog.Empty;

        // One MotionTimingSet per motion table (slots arrive grouped by table, ordered by slot).
        var setByTable = new Dictionary<string, MotionTimingSet>();
        var pending = new List<KeyValuePair<int, MotionClip>>();
        string? currentTable = null;

        void FlushTable()
        {
            if (currentTable != null && pending.Count > 0)
                setByTable[currentTable] = MotionTimingSet.Build(pending);
            pending.Clear();
        }

        foreach (var slot in slots)
        {
            if (slot.MotionTableKey != currentTable)
            {
                FlushTable();
                currentTable = slot.MotionTableKey;
            }

            pending.Add(new KeyValuePair<int, MotionClip>(
                slot.State,
                new MotionClip(
                    slot.StartFrame, slot.EndFrame,
                    unchecked((uint)slot.EventFrame0), unchecked((uint)slot.EventFrame1),
                    unchecked((uint)slot.EventFrame2), unchecked((uint)slot.EventFrame3),
                    slot.Repeat)
                {
                    Slot = slot.Slot,
                    Key = (char)(slot.KeyCode & 0xFF),
                }));
        }

        FlushTable();

        var byModel = new Dictionary<string, MotionTimingSet>(models.Count);
        foreach (var model in models)
        {
            if (setByTable.TryGetValue(model.MotionTableKey, out var set))
                byModel[model.ModelKey] = set;
        }

        return new MotionTimingCatalog(byModel);
    }
}
