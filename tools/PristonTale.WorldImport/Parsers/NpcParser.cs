namespace PristonTale.WorldImport.Parsers;

using PristonTale.Simulation.World.Npcs;

/// <summary>
/// Parses one NPC <c>.npc</c> → <see cref="ParsedNpc"/> with the original character-definition rules for
/// NPCs and their service flags. Service flags are single tokens (TRUE if the
/// line is present); sell lists are ≤32 quoted-or-plain item codes; <c>*대화</c> appends a dialog line
/// (≤20). <c>*연결파일</c> reopens a linked file and keeps parsing it (the original closes the current file
/// and continues in the linked one) — we follow it only when it resolves on disk, else record nothing extra.
///
/// The name is preserved VERBATIM so field <c>.spc</c> fixed-NPC placements can cross-reference it exactly.
/// </summary>
public sealed class NpcParser
{
    public ParsedNpc Parse(string path)
    {
        var lines = SourceTextReader.ReadChain(path);
        var name = FindName(lines);
        var npc = new ParsedNpc { Name = name };

        var ordinals = new int[3];
        ApplyLines(lines, npc, ordinals);
        npc.SvcShop = npc.Shop.Count > 0;
        return npc;
    }

    private static void ApplyLines(IReadOnlyList<SourceLine> lines, ParsedNpc npc, int[] ordinals)
    {
        foreach (var sourceLine in lines)
        {
            var line = sourceLine.Text;
            var key = Tokenizer.LeadingKey(line);
            if (key.Length == 0)
                continue;
            var t = new Tokenizer(line);
            t.NextWord();

            if (KeyMaps.CharSound.Contains(key, StringComparer.Ordinal))
            {
                npc.SoundCode = LegacyNpcSoundCode.Resolve(t.NextWord());
                continue;
            }

            if (key == KeyMaps.MonMoveRange)
            {
                npc.MoveRange = Math.Max(0, (int)t.NextFloat());
                continue;
            }

            switch (key)
            {
                case KeyMaps.CharModel: npc.Model = t.NextValueOrQuoted(); break;
                case "*모델크기": npc.ModelScale = t.NextFloat(); break;
                case KeyMaps.CharDialog:
                    if (npc.Dialog.Count < 20)
                        npc.Dialog.Add(t.NextValueOrQuoted());
                    break;

                case KeyMaps.NpcWarehouse: npc.SvcWarehouse = true; break;
                case KeyMaps.NpcSkillMaster: npc.SvcSkillMaster = true; break;
                case KeyMaps.NpcJobChange: npc.SvcJobChange = true; npc.JobChangeCode = t.NextInt(); break;
                case KeyMaps.NpcTeleport: npc.SvcTeleport = true; npc.TeleportCode = t.NextInt(); break;
                case KeyMaps.NpcItemMix: npc.SvcItemMix = true; break;
                case KeyMaps.NpcItemAlchemy: npc.SvcItemMix = true; break;  // ItemMix=200 variant
                case KeyMaps.NpcItemAging: npc.SvcItemAging = true; break;
                case KeyMaps.NpcSmelting: npc.SvcSmelting = true; break;
                case KeyMaps.NpcManufacture: npc.SvcManufacture = true; break;
                case KeyMaps.NpcMixtureReset: npc.SvcMixtureReset = true; break;
                case KeyMaps.NpcClan: npc.SvcClan = true; break;
                case KeyMaps.NpcBlessCastle: npc.SvcBlessCastle = true; t.NextInt(); break;
                case KeyMaps.NpcEventBooth: npc.SvcEventBooth = true; break;
                case KeyMaps.NpcCollectMoney: npc.SvcCollectMoney = true; break;
                case KeyMaps.NpcGiveMoney: npc.SvcGiveMoney = true; break;
                case KeyMaps.NpcQuestCode:
                    npc.SvcQuest = true; npc.QuestCode = t.NextInt(); npc.QuestParam = t.NextInt(); break;

                case KeyMaps.NpcSellWeapon: AddShop(npc, 0, ordinals, t); break;
                case KeyMaps.NpcSellDefence: AddShop(npc, 1, ordinals, t); break;
                case KeyMaps.NpcSellEtc: AddShop(npc, 2, ordinals, t); break;

                case KeyMaps.NpcLinkedFile:
                    npc.LinkedFile = t.NextValueOrQuoted();
                    break;
            }
        }
    }

    private static void AddShop(ParsedNpc npc, int tab, int[] ordinals, Tokenizer t)
    {
        foreach (var code in t.RemainingWords())
        {
            if (code.Length == 0)
                continue;
            // Strip surrounding quotes if the code was quoted (rare). RemainingWords splits on whitespace.
            var clean = code.Trim('"');
            if (clean.Length == 0)
                continue;
            if (ordinals[tab] >= 32) // ≤32 per tab
                break;
            npc.Shop.Add((tab, ordinals[tab]++, clean));
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
