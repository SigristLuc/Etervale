namespace PristonTale.WorldImport.Parsers;

using PristonTale.Simulation.World.Npcs;

// Parses one NPC `.npc` and the name file it links to into a ParsedNpc. Every service key the original reads becomes
// a typed NpcService; keys that share one original setting keep the last value read. Sell lists are at most 32 codes
// per tab. Dialog comes from the first locale with lines in the order English, the Brazilian build's English text,
// then the Korean base lines; the display name follows the same order, then the plain label. The identity name (`*이름`) is kept verbatim
// because field placements reference it.
public sealed class NpcParser
{
    private const int MaxDialogLines = 20;
    private const int FirstQuestTriggerCode = 1000;

    public ParsedNpc Parse(string path)
    {
        var lines = SourceTextReader.ReadChain(path);
        var npc = new ParsedNpc { Name = FindName(lines) };
        var facts = new Facts();
        ApplyLines(lines, npc, facts);
        npc.DisplayName = facts.LocalNames.FirstOrDefault(static name => !string.IsNullOrEmpty(name)) ?? facts.PlainName;
        ChooseDialog(npc, facts);
        BuildServices(npc, facts);
        return npc;
    }

    private sealed class Facts
    {
        public readonly string[] LocalNames = new string[KeyMaps.NpcNameLocales.Length];
        public readonly List<string>[] LocalDialog = KeyMaps.NpcDialogLocales.Select(static _ => new List<string>()).ToArray();
        public readonly List<string> KoreanDialog = [];
        public string PlainName = "";
        public readonly int[] ShopOrdinals = new int[3];
        public bool ShopHasStock;
        public int QuestTrigger;
        public int ItemMix;
        public int WingQuest;
        public bool WingQuestIsEvent;
        public int StarPoint;
        public int EventBooth;
        public bool EventGift;
        public bool GiftExpress;
        public int BlessCastle;
        public int Polling;
        public string MediaTitle = "";
        public string MediaPath = "";
        public int EventCode;
        public int EventInfo;
        public readonly List<string> Unported = [];
    }

    private static void ApplyLines(IReadOnlyList<SourceLine> lines, ParsedNpc npc, Facts facts)
    {
        foreach (var sourceLine in lines)
        {
            var line = sourceLine.Text;
            var key = Tokenizer.LeadingKey(line);
            if (key.Length == 0)
            {
                continue;
            }

            var t = new Tokenizer(line);
            t.NextWord();
            if (ApplyLocale(key, t, facts) || ApplyAppearance(key, t, npc) || ApplyService(key, t, npc, facts))
            {
                continue;
            }

            var unported = key == KeyMaps.CharName || KeyMaps.IsUnshippedLocale(key);
            foreach (var (token, label) in KeyMaps.NpcUnportedServices)
            {
                if (key == token)
                {
                    unported = true;
                    if (!facts.Unported.Contains(label))
                    {
                        facts.Unported.Add(label);
                    }
                }
            }

            if (!unported)
            {
                npc.UnknownKeys.Add(key);
            }
        }
    }

    private static bool ApplyLocale(string key, Tokenizer t, Facts facts)
    {
        for (var index = 0; index < KeyMaps.NpcNameLocales.Length; index++)
        {
            if (string.Equals(key, KeyMaps.NpcNameLocales[index], StringComparison.OrdinalIgnoreCase))
            {
                facts.LocalNames[index] = Normalize(t.NextValueOrQuoted());
                return true;
            }
        }

        for (var index = 0; index < KeyMaps.NpcDialogLocales.Length; index++)
        {
            if (string.Equals(key, KeyMaps.NpcDialogLocales[index], StringComparison.OrdinalIgnoreCase))
            {
                AddLine(facts.LocalDialog[index], t.NextValueOrQuoted());
                return true;
            }
        }

        if (string.Equals(key, KeyMaps.CharDisplayName, StringComparison.OrdinalIgnoreCase))
        {
            facts.PlainName = Normalize(t.NextValueOrQuoted());
            return true;
        }

        if (key == KeyMaps.CharDialog)
        {
            AddLine(facts.KoreanDialog, t.NextValueOrQuoted());
            return true;
        }

        return false;
    }

    private static bool ApplyAppearance(string key, Tokenizer t, ParsedNpc npc)
    {
        if (KeyMaps.CharSound.Contains(key, StringComparer.Ordinal))
        {
            npc.SoundCode = CharacterSoundCodes.Resolve(t.NextWord());
            return true;
        }

        switch (key)
        {
            case KeyMaps.MonMoveRange:
                npc.MoveRange = Math.Max(0, (int)t.NextFloat());
                return true;
            case KeyMaps.CharAttribute:
                npc.Hostile = t.NextValueOrQuoted() == KeyMaps.HostileAttribute;
                return true;
            case KeyMaps.MonLevel:
                npc.Level = t.NextInt();
                return true;
            case KeyMaps.CharSize:
                npc.SizeLevel = KeyMaps.ParseSizeLevel(t.NextWord());
                return true;
            case var appear when KeyMaps.SpmInterval.Contains(appear, StringComparer.OrdinalIgnoreCase):
                npc.AppearSlot = t.NextInt();
                npc.AppearCycle = t.NextInt();
                return true;
            case KeyMaps.CharModel:
                npc.Model = t.NextValueOrQuoted();
                return true;
            case "*모델크기":
                npc.ModelScale = t.NextFloat();
                return true;
            case KeyMaps.NpcLinkedFile:
                npc.LinkedFile = t.NextValueOrQuoted();
                return true;
            case KeyMaps.CharPortrait:
                npc.PortraitOffsetY = t.NextInt();
                npc.PortraitOffsetZ = t.NextInt();
                return true;
            default:
                return false;
        }
    }

    private static bool ApplyService(string key, Tokenizer t, ParsedNpc npc, Facts facts)
    {
        switch (key)
        {
            case KeyMaps.NpcWarehouse:
                npc.SvcWarehouse = true;
                return true;
            case KeyMaps.NpcSkillMaster:
                npc.SvcSkillMaster = true;
                return true;
            case KeyMaps.NpcJobChange:
                npc.SvcJobChange = true;
                npc.JobChangeCode = OptionalInt(t, 1);
                return true;
            case KeyMaps.NpcTeleport:
                npc.SvcTeleport = true;
                npc.TeleportCode = OptionalInt(t, 1);
                return true;
            case KeyMaps.NpcItemMix:
                npc.SvcItemMix = true;
                facts.ItemMix = 1;
                return true;
            case KeyMaps.NpcItemAlchemy:
                npc.SvcItemMix = true;
                facts.ItemMix = 200;
                return true;
            case KeyMaps.NpcItemAging:
                npc.SvcItemAging = true;
                return true;
            case KeyMaps.NpcSmelting:
                npc.SvcSmelting = true;
                return true;
            case KeyMaps.NpcManufacture:
                npc.SvcManufacture = true;
                return true;
            case KeyMaps.NpcMixtureReset:
                npc.SvcMixtureReset = true;
                return true;
            case KeyMaps.NpcClan:
                npc.SvcClan = true;
                return true;
            case KeyMaps.NpcBlessCastle:
                npc.SvcBlessCastle = true;
                facts.BlessCastle = OptionalInt(t, 1);
                return true;
            case KeyMaps.NpcEventBooth:
                npc.SvcEventBooth = true;
                facts.EventBooth = OptionalInt(t, 1);
                return true;
            case KeyMaps.NpcCollectMoney:
                npc.SvcCollectMoney = true;
                return true;
            case KeyMaps.NpcGiveMoney:
                npc.SvcGiveMoney = true;
                return true;
            case KeyMaps.NpcEventGift:
                facts.EventGift = true;
                return true;
            case KeyMaps.NpcGiftExpress:
                facts.GiftExpress = true;
                return true;
            case KeyMaps.NpcWingQuest:
                facts.WingQuest = OptionalInt(t, 1);
                facts.WingQuestIsEvent = false;
                return true;
            case KeyMaps.NpcQuestEvent:
                facts.WingQuest = OptionalInt(t, 2);
                facts.WingQuestIsEvent = true;
                return true;
            case KeyMaps.NpcStarPoint:
                facts.StarPoint = OptionalInt(t, 20);
                return true;
            case KeyMaps.NpcPolling:
                facts.Polling = OptionalInt(t, 1);
                return true;
            case KeyMaps.NpcMediaTitle:
                facts.MediaTitle = t.NextValueOrQuoted();
                return true;
            case KeyMaps.NpcMediaPath:
                facts.MediaPath = t.NextValueOrQuoted();
                return true;
            case KeyMaps.NpcEventCode:
                facts.EventCode = t.NextInt();
                return true;
            case KeyMaps.NpcEventInfo:
                facts.EventInfo = t.NextInt();
                return true;
            case KeyMaps.MonClass:
                facts.QuestTrigger = t.NextInt();
                return true;
            case KeyMaps.NpcQuestCode:
                npc.SvcQuest = true;
                npc.QuestCode = t.NextInt();
                npc.QuestParam = t.NextInt();
                return true;
            case KeyMaps.NpcSellWeapon:
                AddShop(npc, 0, facts, t);
                return true;
            case KeyMaps.NpcSellDefence:
                AddShop(npc, 1, facts, t);
                return true;
            case KeyMaps.NpcSellEtc:
                AddShop(npc, 2, facts, t);
                return true;
            default:
                return false;
        }
    }

    // The order in which the original answers a click: quest hooks first, then the shop and the windows it opens.
    private static void BuildServices(ParsedNpc npc, Facts facts)
    {
        var services = npc.Services;
        npc.SvcShop = facts.ShopHasStock;
        if (facts.QuestTrigger >= FirstQuestTriggerCode)
        {
            services.Add(new NpcService(NpcServiceKind.QuestGiver, facts.QuestTrigger));
        }

        AddIf(services, npc.SvcQuest, NpcServiceKind.QuestScript, npc.QuestCode, npc.QuestParam);
        AddIf(services, npc.SvcShop, NpcServiceKind.Shop);
        AddIf(services, npc.SvcSkillMaster, NpcServiceKind.SkillMaster, npc.SvcJobChange ? npc.JobChangeCode : 0);
        AddIf(services, npc.SvcWarehouse, NpcServiceKind.Warehouse);
        AddIf(services, facts.ItemMix != 0, NpcServiceKind.ItemMix, facts.ItemMix);
        AddIf(services, npc.SvcSmelting, NpcServiceKind.Smelting);
        AddIf(services, npc.SvcManufacture, NpcServiceKind.Manufacture);
        AddIf(services, npc.SvcCollectMoney, NpcServiceKind.CollectMoney);
        AddIf(services, facts.EventGift, NpcServiceKind.EventGift);
        AddIf(services, facts.GiftExpress, NpcServiceKind.GiftExpress);
        AddIf(services, facts.WingQuest != 0, facts.WingQuestIsEvent ? NpcServiceKind.QuestEvent : NpcServiceKind.WarpGate, facts.WingQuest);
        AddIf(services, npc.SvcTeleport, NpcServiceKind.Teleporter, npc.TeleportCode);
        AddIf(services, facts.StarPoint != 0, NpcServiceKind.StarPoint, facts.StarPoint);
        AddIf(services, npc.SvcGiveMoney, NpcServiceKind.GiveMoney);
        AddIf(services, npc.SvcEventBooth, NpcServiceKind.EventBooth, facts.EventBooth);
        AddIf(services, npc.SvcItemAging, NpcServiceKind.ItemAging);
        AddIf(services, npc.SvcClan, NpcServiceKind.Clan);
        AddIf(services, npc.SvcBlessCastle, NpcServiceKind.BlessCastle, facts.BlessCastle);
        AddIf(services, facts.Polling != 0, NpcServiceKind.Polling, facts.Polling);
        if (facts.MediaTitle.Length > 0 && facts.MediaPath.Length > 0)
        {
            services.Add(new NpcService(NpcServiceKind.Media, Text: facts.MediaPath + "\n" + facts.MediaTitle));
        }

        AddIf(services, npc.SvcMixtureReset, NpcServiceKind.MixtureReset);
        AddIf(services, facts.EventCode != 0 || facts.EventInfo != 0, NpcServiceKind.EventMonster, facts.EventCode, facts.EventInfo);
        foreach (var label in facts.Unported)
        {
            services.Add(new NpcService(NpcServiceKind.Unported, Text: label));
        }
    }

    private static void AddIf(List<NpcService> services, bool present, NpcServiceKind kind, int value = 0, int value2 = 0)
    {
        if (present)
        {
            services.Add(new NpcService(kind, value, value2));
        }
    }

    private static void ChooseDialog(ParsedNpc npc, Facts facts)
    {
        for (var index = 0; index < facts.LocalDialog.Length; index++)
        {
            if (facts.LocalDialog[index].Count > 0)
            {
                npc.Dialog.AddRange(facts.LocalDialog[index]);
                npc.DialogKey = KeyMaps.NpcDialogLocales[index];
                return;
            }
        }

        if (facts.KoreanDialog.Count > 0)
        {
            npc.Dialog.AddRange(facts.KoreanDialog);
            npc.DialogKey = KeyMaps.CharDialog;
        }
    }

    // Name files wrap their lines at a fixed width with runs of spaces; display text keeps single spaces.
    private static void AddLine(List<string> lines, string text)
    {
        var line = Normalize(text);
        if (line.Length > 0 && lines.Count < MaxDialogLines)
        {
            lines.Add(line);
        }
    }

    private static string Normalize(string text) =>
        string.Join(' ', text.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries));

    private static int OptionalInt(Tokenizer t, int fallback)
    {
        var word = t.NextWord();
        return word.Length == 0 ? fallback : Tokenizer.Atoi(word);
    }

    private static void AddShop(ParsedNpc npc, int tab, Facts facts, Tokenizer t)
    {
        foreach (var code in t.RemainingWords())
        {
            var clean = code.Trim('"');
            if (clean.Length == 0)
            {
                continue;
            }

            if (facts.ShopOrdinals[tab] >= 32)
            {
                break;
            }

            facts.ShopHasStock |= clean != KeyMaps.DropNone;
            npc.Shop.Add((tab, facts.ShopOrdinals[tab]++, clean));
        }
    }

    private static string FindName(IReadOnlyList<SourceLine> lines)
    {
        foreach (var sourceLine in lines)
        {
            var line = sourceLine.Text;
            if (Tokenizer.LeadingKey(line) == KeyMaps.CharName)
            {
                var t = new Tokenizer(line);
                t.NextWord();
                return t.NextValueOrQuoted();
            }
        }

        return "";
    }
}
