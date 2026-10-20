using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Progression;
using PristonTale.Simulation.Skills;

namespace PristonTale.Gameplay.UI.Widgets;

// The original skill window and its text: Skil24.bmp, job titles, skill buttons (gray "_" variant when unusable),
// rank gauges, F1-F8 short keys and the selected-skill hover frame. All sixteen nodes are shown for reference.
public sealed class HudSkillsPanel(SpriteFont font, HudSprites sprites)
{
    private const int TooltipWidth = 260;

    private sealed record Entry(Skill Skill, string Level, string[] Description, string[] Weapons, string Cost, string Mouse,
        int Rank = 0, string Status = "Unlearned");

    private static readonly CharacterClassRegistry Classes = new();
    private readonly List<string> _classSprites = [];
    private Entry[] _entries = [];
    private PlayerClass _class;
    private int _points = -1;
    private string _pointsText = "";
    private int _trainingIndex = -1;
    private string _trainingName = "";
    private string _trainingPrice = "";
    private IReadOnlyList<OwnedSkillView> _displayedSkills;
    private Entry _hovered;
    private bool _hoveredBasic;

    public bool TrainingPending => _trainingIndex >= 0;
    public void CancelTraining() => _trainingIndex = -1;

    public static Rectangle ClassRow(Point origin, int index) => HudSkillTreeLayout.Node(origin, index);

    public void SetClass(PlayerClass characterClass)
    {
        _class = characterClass;
        _displayedSkills = null;
        CancelTraining();
        var skills = Classes.Get(characterClass).Skills;
        _entries = new Entry[skills.Count];
        var previous = _classSprites.ToArray();
        _classSprites.Clear();
        for (var tier = 0; tier < 4; tier++)
        {
            _classSprites.Add(HudArt.JobTitle(characterClass, tier));
        }

        for (var index = 0; index < skills.Count; index++)
        {
            var skill = skills[index];
            if (skill.Code == 0)
            {
                _entries[index] = new Entry(skill, "", [], [], "", "", Status: "Unavailable in this version");
                continue;
            }

            _classSprites.Add(HudArt.SkillButton(characterClass, skill.IconKey, false));
            _classSprites.Add(HudArt.SkillButton(characterClass, skill.IconKey, true));
            var weapons = skill.AllowedWeaponsMask == 0 ? "Any weapon" : string.Join(", ", Enum.GetValues<ItemKind>().Where(skill.AllowsWeapon));
            var mouse = skill.MouseUse switch
            {
                SkillMouseUse.Left => "Left mouse",
                SkillMouseUse.Right => "Right mouse",
                SkillMouseUse.Both => "Left or right mouse",
                _ => "Passive skill"
            };
            var cost = skill.MouseUse == SkillMouseUse.None ? "No activation cost" : $"Rank 1: MP {skill.ManaForRank(1)} | SP {skill.StaminaForRank(1)}";
            _entries[index] = new Entry(skill, $"Lv {skill.RequiredLevel}",
                HudTextLayout.Wrap(font, skill.Description, TooltipWidth - 20, HudPainter.TextScale),
                HudTextLayout.Wrap(font, "Equipment: " + weapons, TooltipWidth - 20, HudPainter.TextScale), cost, mouse);
        }

        foreach (var path in previous.Except(_classSprites, StringComparer.OrdinalIgnoreCase))
        {
            sprites.Release(path);
        }

        sprites.Request(_classSprites);
    }

    public uint HoveredSkill(Point origin, Point pointer) =>
        HudSkillTreeLayout.BasicAttack(origin).Contains(pointer) ? HudSkillBindings.BasicAttackId : 0;

    public void HandleTrainingClick(Point origin, Point pointer, OwnerState state, uint npcId, HudProgressionController controller)
    {
        if (state is null || npcId == 0 || controller.Pending)
        {
            return;
        }

        if (_trainingIndex >= 0)
        {
            if (HudMessageBox.Accept.Contains(pointer))
            {
                controller.Submit(new ProgressionCommand(Guid.NewGuid(), state.ProgressionRevision, ProgressionAction.LearnSkill,
                    SkillCode: _entries[_trainingIndex].Skill.Code, NpcEntityId: npcId));
                _trainingIndex = -1;
            }
            else if (HudMessageBox.Reject.Contains(pointer))
            {
                _trainingIndex = -1;
            }

            return;
        }

        for (var index = 0; index < Math.Min(4, _entries.Length); index++)
        {
            if (!ClassRow(origin, index).Contains(pointer))
            {
                continue;
            }

            var entry = _entries[index];
            var rank = Rank(state, entry.Skill.Code);
            if (!CanTrain(index, state))
            {
                return;
            }

            _trainingIndex = index;
            _trainingName = entry.Skill.Name;
            _trainingPrice = $"Rank {rank + 1}: {SkillLearning.Price(index, rank):N0} gold";
        }
    }

    public void Draw(HudPainter painter, Point origin, Point pointer, OwnerState state, HudSkillBindings bindings, uint skillMaster, bool pressed,
        bool interactive)
    {
        RefreshRanks(state);
        _hovered = null;
        _hoveredBasic = false;
        painter.Sprite(HudArt.SkillWindow, origin.X, origin.Y + 400);
        for (var tier = 0; tier < 4; tier++)
        {
            var title = HudSkillTreeLayout.Title(origin, tier);
            painter.Sprite(HudArt.JobTitle(_class, tier), title.X, title.Y);
        }

        for (var index = 0; index < Math.Min(_entries.Length, HudSkillTreeLayout.NodeCount); index++)
        {
            var entry = _entries[index];
            var node = ClassRow(origin, index);
            if (entry.Skill.Code != 0)
            {
                var usable = entry.Rank > 0 || skillMaster != 0 && CanTrain(index, state);
                painter.Sprite(HudArt.SkillButton(_class, entry.Skill.IconKey, !usable), node.X, node.Y);
                if (index < 4)
                {
                    // Only the first job tier is available (no class change), as the original draws gauges for flagged skills.
                    var mastery = HudSkillTreeLayout.MasteryGauge(origin, index);
                    painter.Sprite(entry.Skill.MouseUse == SkillMouseUse.None ? HudArt.SkillGaugePassive : HudArt.SkillGaugeBack, mastery.X, mastery.Y);
                }

                var rank = HudSkillTreeLayout.RankGauge(origin, index);
                var length = Math.Clamp(entry.Rank, 0, 10) * rank.Height / 10;
                painter.Sprite(HudArt.SkillGauge, rank.X, rank.Bottom - length, new Rectangle(0, rank.Height - length, rank.Width, length));
                DrawShortcut(painter, node, entry.Skill.Code, bindings);
            }

            if (interactive && node.Contains(pointer))
            {
                _hovered = entry;
                painter.Sprite(HudArt.SkillSelect, node.X + (pressed ? 1 : 0), node.Y + (pressed ? 1 : 0));
            }
        }

        var basic = HudSkillTreeLayout.BasicAttack(origin);
        DrawShortcut(painter, basic, HudSkillBindings.BasicAttackId, bindings);
        if (interactive && basic.Contains(pointer))
        {
            _hoveredBasic = true;
            painter.Sprite(HudArt.SkillSelect, basic.X + (pressed ? 1 : 0), basic.Y + (pressed ? 1 : 0));
        }

        if (state is not null)
        {
            if (_points != state.SkillPoints)
            {
                _points = state.SkillPoints;
                _pointsText = _points.ToString();
            }

            painter.TextCentered(_pointsText, origin.X + 165, origin.X + 253, origin.Y + 578);
        }
    }

    public void DrawOverlay(HudPainter painter, HudLayout layout, Point pointer, uint skillMaster, string message)
    {
        if (_trainingIndex >= 0)
        {
            painter.Flush();
            HudMessageBox.Draw(painter, _trainingName, _trainingPrice, pointer);
            return;
        }

        if (_hovered is not null)
        {
            DrawDetails(painter, layout, pointer, _hovered);
        }
        else if (_hoveredBasic)
        {
            DrawLines(painter, layout, pointer, "Basic attack", ["Release LMB or RMB to select.", "Hold the button + F1-F8 to bind."]);
        }
        else if (skillMaster != 0)
        {
            DrawLines(painter, layout, new Point(layout.Width / 2, layout.BottomOffset + 400), "Skill master",
                [message.Length > 0 ? message : "Select a skill to train."]);
        }
    }

    private bool CanTrain(int index, OwnerState state)
    {
        if (state is null || index >= 4 || index >= _entries.Length)
        {
            return false;
        }

        var skill = _entries[index].Skill;
        var rank = Rank(state, skill.Code);
        return rank < 10 && state.SkillPoints > 0 && state.Level >= skill.RequiredLevel + rank * 2
            && (index == 0 || Rank(state, _entries[index - 1].Skill.Code) > 0)
            && state.Money >= SkillLearning.Price(index, rank);
    }

    private static int Rank(OwnerState state, uint code)
    {
        foreach (var skill in state.Skills)
        {
            if (skill.Code == code)
            {
                return skill.Rank;
            }
        }

        return 0;
    }

    private static void DrawLines(HudPainter painter, HudLayout layout, Point anchor, string title, string[] lines)
    {
        var width = Math.Max(painter.Measure(title), lines.Max(painter.Measure)) + 24;
        var height = 24 + lines.Length * HudPainter.LineHeight + 8;
        var size = HudPainter.InfoBoxBounds(0, 0, width, height);
        var place = layout.PlaceOverlay(new Point(anchor.X - size.Width / 2, anchor.Y - size.Height - 8), size.Width, size.Height);
        var bounds = HudPainter.InfoBoxBounds(place.X, place.Y, place.Width, place.Height);
        painter.Flush();
        painter.InfoBox(bounds);
        painter.Text(title, bounds.X + 10, bounds.Y + 8, new Color(255, 255, 180));
        for (var index = 0; index < lines.Length; index++)
        {
            painter.Text(lines[index], bounds.X + 10, bounds.Y + 26 + index * HudPainter.LineHeight, new Color(220, 220, 220));
        }
    }

    private static void DrawDetails(HudPainter painter, HudLayout layout, Point pointer, Entry entry)
    {
        if (entry.Skill.Code == 0)
        {
            DrawLines(painter, layout, pointer, entry.Skill.Name, [entry.Status]);
            return;
        }

        var lines = new List<string>(entry.Description)
        {
            $"{entry.Level}   {entry.Mouse}",
            entry.Cost
        };
        lines.AddRange(entry.Weapons);
        lines.Add(entry.Status);
        DrawLines(painter, layout, pointer, entry.Skill.Name, lines.ToArray());
    }

    private void RefreshRanks(OwnerState state)
    {
        if (state is null || ReferenceEquals(state.Skills, _displayedSkills))
        {
            return;
        }

        _displayedSkills = state.Skills;
        for (var index = 0; index < _entries.Length; index++)
        {
            var entry = _entries[index];
            if (entry.Skill.Code == 0)
            {
                continue;
            }

            var rank = Rank(state, entry.Skill.Code);
            var shownRank = Math.Max(1, rank);
            var cost = entry.Skill.Category == SkillCategory.Passive
                ? "No activation cost"
                : $"Rank {shownRank}: MP {entry.Skill.ManaForRank(shownRank)} | SP {entry.Skill.StaminaForRank(shownRank)}";
            var status = rank == 0 ? "Unlearned | Rank 1 preview" : $"Learned rank {rank} / 10";
            if (rank > 0 && entry.Skill.Category == SkillCategory.Passive)
            {
                status += " | Passive";
            }

            if (entry.Skill.MouseUse != SkillMouseUse.None)
            {
                status += " | Casting unavailable";
            }

            _entries[index] = entry with { Rank = rank, Cost = cost, Status = status };
        }
    }

    // Short keys: ShortKey\f<n>.bmp at the skill box left + 9, top - 6.
    private static void DrawShortcut(HudPainter painter, Rectangle node, uint skillCode, HudSkillBindings bindings)
    {
        for (var index = 0; index < 8; index++)
        {
            if (bindings.Shortcut(index).SkillId == skillCode)
            {
                painter.Sprite(HudArt.ShortKeys[index], node.X + 9, node.Y - 6);
            }
        }
    }
}
