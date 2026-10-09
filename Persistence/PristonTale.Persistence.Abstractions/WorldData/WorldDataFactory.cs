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
        // Average of the min/max roll bounds for the equipped baseline (the per-copy roll lives elsewhere).
        var baseStats = new ItemStatBlock(
            DamageMin: Mid(r.DamageMinLow, r.DamageMinHigh),
            DamageMax: Mid(r.DamageMaxLow, r.DamageMaxHigh),
            Defence: Mid(r.DefenceMin, r.DefenceMax),
            AttackRating: Mid(r.AttackRatingMin, r.AttackRatingMax),
            AttackSpeed: r.AttackSpeed,
            Strength: 0, Spirit: 0, Talent: 0, Agility: 0, Health: 0);

        var rolledMin = new ItemStatBlock(
            r.DamageMinLow, r.DamageMaxLow, r.DefenceMin, r.AttackRatingMin, 0, 0, 0, 0, 0, 0);
        var rolledMax = new ItemStatBlock(
            r.DamageMinHigh, r.DamageMaxHigh, r.DefenceMax, r.AttackRatingMax, 0, 0, 0, 0, 0, 0);

        return ItemEquipmentMetadata.Apply(new ItemDefinition(
            (uint)r.ItemCodeMask,
            r.Name,
            (ItemKind)r.Kind,
            MaxStack: Math.Max(1, r.MaxStack))
        {
            CatalogKey = r.Code,
            Potion = (ItemKind)r.Kind == ItemKind.Potion
                ? new PotionDefinition(r.RestoreLifeMin, r.RestoreLifeMax, r.RestoreManaMin, r.RestoreManaMax, r.RestoreStaminaMin, r.RestoreStaminaMax)
                : null,
            RequiredLevel = r.ReqLevel,
            Requirements = new(r.ReqStrength, r.ReqSpirit, r.ReqTalent, r.ReqDexterity, r.ReqHealth),
            AllowedJobs = ItemClassRestrictions.AllowedJobs(r.Code),
            SpecializationJobs = (uint)r.JobMask,
            ShootingRange = r.ShootingRange,
            BaseStats = baseStats,
            MaxDurability = r.DurabilityMax,
            Weight = r.Weight,
            Price = r.Price,
            RolledStatsMin = rolledMin,
            RolledStatsMax = rolledMax,
            Resistances = ToItemResistances(r),
            SpecialBonuses = r.Specials
                .Select(static s => new ItemSpecialBonus(s.BonusCode, (int)s.Value0))
                .ToList(),
            JobRandomSpecMask = FoldJobRandom(r.JobRandom),
        });
    }

    public static IReadOnlyList<ItemDefinition> ToItemDefinitions(IEnumerable<ItemTemplateRecord> records) =>
        records.Where(static record => record.ItemCodeMask != 0).Select(ToItemDefinition).ToList();

    // Item resistances are min/max ranges; the equipped baseline uses the midpoint of each element.
    private static ResistanceBlock ToItemResistances(ItemTemplateRecord r) => new(
        (short)Mid(r.ResBionicMin, r.ResBionicMax),
        (short)Mid(r.ResEarthMin, r.ResEarthMax),
        (short)Mid(r.ResFireMin, r.ResFireMax),
        (short)Mid(r.ResIceMin, r.ResIceMax),
        (short)Mid(r.ResLightingMin, r.ResLightingMax),
        (short)Mid(r.ResPoisonMin, r.ResPoisonMax),
        (short)Mid(r.ResWaterMin, r.ResWaterMax),
        (short)Mid(r.ResWindMin, r.ResWindMax));

    private static uint FoldJobRandom(IReadOnlyList<ItemTemplateJobRandomRecord> jobs)
    {
        var mask = 0u;
        foreach (var j in jobs)
            mask |= (uint)j.JobBitCode;
        return mask;
    }

    private static int Mid(int min, int max) => (min + max) / 2;

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

        var skill = (r.SkillDamageMin == 0 && r.SkillDamageMax == 0 && r.SkillRange == 0)
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
            Faction: MonsterFaction.Enemy,
            Class: monsterClass,
            Brood: (MonsterBrood)r.Brood,
            Combat: combat,
            ModelInxPath: ToInxModelPath(r.Model))
        {
            Skill = skill,
            Ai = ai,
            Drops = ToDropTable(r, itemCodes),
            ExpReward = r.Exp,
            SoundCode = r.SoundCode,
            ModelScale = r.ModelScale,
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

        return new NpcDefinition(code, r.Name, ToInxModelPath(r.Model), FoldServices(r))
        {
            JobChangeCode = r.JobChangeCode,
            TeleportCode = r.TeleportCode,
            Shop = (r.ShopStock.Count == 0)
                ? NpcShopStock.Empty
                : new NpcShopStock(
                    weapons.Select(static s => s.ItemCode).Where(IsSupportedItemCode).Select(LegacyItemCode.Parse).ToList(),
                    armour.Select(static s => s.ItemCode).Where(IsSupportedItemCode).Select(LegacyItemCode.Parse).ToList(),
                    misc.Select(static s => s.ItemCode).Where(IsSupportedItemCode).Select(LegacyItemCode.Parse).ToList()),
            Quest = r.SvcQuest ? new NpcQuestHook(r.QuestCode, r.QuestParam) : NpcQuestHook.None,
            Dialog = dialog,
            SoundCode = r.SoundCode,
            ModelScale = r.ModelScale,
            MoveRange = r.MoveRange,
        };
    }

    private static bool IsSupportedItemCode(string code) => LegacyItemCode.TryParse(code, out _);

    private static NpcServices FoldServices(NpcRecord r)
    {
        var s = NpcServices.None;
        if (r.SvcWarehouse) s |= NpcServices.Warehouse;
        if (r.SvcShop) s |= NpcServices.Shop;
        if (r.SvcSkillMaster) s |= NpcServices.SkillMaster;
        if (r.SvcJobChange) s |= NpcServices.JobChange;
        if (r.SvcTeleport) s |= NpcServices.Teleport;
        if (r.SvcItemMix) s |= NpcServices.ItemMix;
        if (r.SvcItemAging) s |= NpcServices.ItemAging;
        if (r.SvcSmelting) s |= NpcServices.Smelting;
        if (r.SvcManufacture) s |= NpcServices.Manufacture;
        if (r.SvcMixtureReset) s |= NpcServices.MixtureReset;
        if (r.SvcClan) s |= NpcServices.Clan;
        if (r.SvcBlessCastle) s |= NpcServices.BlessCastle;
        if (r.SvcQuest) s |= NpcServices.Quest;
        if (r.SvcEventBooth) s |= NpcServices.EventBooth;
        if (r.SvcCollectMoney) s |= NpcServices.CollectMoney;
        if (r.SvcGiveMoney) s |= NpcServices.GiveMoney;
        return s;
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
                    slot.Repeat)));
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
