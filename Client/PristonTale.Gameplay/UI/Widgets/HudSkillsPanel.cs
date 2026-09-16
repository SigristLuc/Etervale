using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Skills;
using PristonTale.Simulation.Progression;

namespace PristonTale.Gameplay.UI.Widgets;

public sealed class HudSkillsPanel(SpriteFont font, HudIconCache icons)
{
    private sealed record Entry(Skill Skill, string Level, string[] Description, string[] Weapons, string Cost, string Mouse,
        int Rank = 0, string Status = "Unlearned");
    private Entry[] _entries = [];
    private int _points = -1;
    private string _pointsText = "";
    private static readonly CharacterClassRegistry Classes = new();
    private static readonly string[] TierNumbers = ["I", "II", "III", "IV"];
    private static readonly string[] Shortcuts = ["F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8"];
    private IReadOnlyList<string> _jobTitles = Array.Empty<string>();
    private int _trainingIndex = -1;
    private string _trainingName = "";
    private string _trainingPrice = "";
    private IReadOnlyList<OwnedSkillView> _displayedSkills;
    public void CancelTraining() => _trainingIndex = -1;

    private static Rectangle ConfirmButton(Rectangle panel, bool accept) => HudSkillTreeLayout.ConfirmButton(panel, accept);

    public void HandleTrainingClick(Rectangle panel, Point pointer, OwnerState state, uint npcId, HudProgressionController controller)
    {
        if (state is null || npcId == 0 || controller.Pending)
        {
            return;
        }

        if (_trainingIndex >= 0)
        {
            if (ConfirmButton(panel, true).Contains(pointer))
            {
                controller.Submit(new ProgressionCommand(Guid.NewGuid(), state.ProgressionRevision, ProgressionAction.LearnSkill,
                    SkillCode: _entries[_trainingIndex].Skill.Code, NpcEntityId: npcId));
                _trainingIndex = -1;
            }
            else if (ConfirmButton(panel, false).Contains(pointer))
            {
                _trainingIndex = -1;
            }

            return;
        }

        for (var index = 0; index < Math.Min(4, _entries.Length); index++)
        {
            if (!ClassRow(panel, index).Contains(pointer))
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
            _trainingName = $"{entry.Skill.Name} | Rank {rank + 1}";
            _trainingPrice = $"{SkillLearning.Price(index, rank):N0} gold + 1 skill point";
        }
    }

    private bool CanTrain(int index, OwnerState state)
    {
        if (state is null || index >= 4)
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

    public void SetClass(PlayerClass characterClass)
    {
        _displayedSkills = null;
        CancelTraining();
        var skills = Classes.Get(characterClass).Skills;
        _jobTitles = HudJobTitles.For(characterClass);
        _entries = new Entry[skills.Count];
        for (var index = 0; index < skills.Count; index++)
        {
            var skill = skills[index];
            if (skill.Code == 0)
            {
                _entries[index] = new Entry(skill, "", [], [], "", "", Status: "Unavailable in this version");
                continue;
            }

            var weapons = skill.AllowedWeaponsMask == 0 ? "Any weapon" : string.Join(", ",
                Enum.GetValues<ItemKind>().Where(skill.AllowsWeapon));
            var mouse = skill.MouseUse switch
            {
                SkillMouseUse.Left => "Left mouse",
                SkillMouseUse.Right => "Right mouse",
                SkillMouseUse.Both => "Left or right mouse",
                _ => "Passive skill"
            };
            var cost = skill.MouseUse == SkillMouseUse.None ? "No activation cost" : $"Rank 1: MP {skill.ManaForRank(1)} | SP {skill.StaminaForRank(1)}";
            _entries[index] = new Entry(skill, $"Lv {skill.RequiredLevel}",
                HudTextLayout.Wrap(font, skill.Description, 312, 0.68f),
                HudTextLayout.Wrap(font, "Equipment: " + weapons, 312, 0.62f), cost, mouse);
        }

        icons.Request(skills.Select(skill => skill.IconKey));
    }

    public static Rectangle ClassRow(Rectangle panel, int index) => HudSkillTreeLayout.Node(panel, index);

    public void Draw(HudCanvas canvas, HudLayout layout, Point pointer, OwnerState state, HudSkillBindings bindings,
        uint skillMaster = 0, string message = "")
    {
        RefreshRanks(state);
        var panel = layout.Panel;
        for (var tier = 0; tier < _jobTitles.Count; tier++)
        {
            var column = HudSkillTreeLayout.Column(panel, tier);
            var heading = new Rectangle(column.X, panel.Y + 42, column.Width - 6, 22);
            canvas.Frame(heading, "Inset", 4);
            canvas.Text(TierNumbers[tier], new Rectangle(heading.X + 5, heading.Y, 22, 22), scale: 0.65f);
            canvas.Text(_jobTitles[tier], new Rectangle(heading.X + 30, heading.Y, heading.Width - 34, 22),
                tier == 0 ? HudCanvas.Gold : HudCanvas.Muted, 0.67f, centered: true);
        }

        var basic = HudActions.SkillRow(panel);
        canvas.Frame(basic, basic.Contains(pointer) ? "Selected" : "Slot", 5);
        canvas.Image("Attack", new Rectangle(basic.X + 4, basic.Y + 4, basic.Width - 8, basic.Height - 8));
        DrawShortcut(canvas, basic, HudSkillBindings.BasicAttackId, bindings);
        Entry hovered = null;
        for (var index = 0; index < _entries.Length; index++)
        {
            var entry = _entries[index];
            var bounds = ClassRow(panel, index);
            var hover = bounds.Contains(pointer);
            canvas.Frame(bounds, hover ? "Selected" : "Slot", 5);
            var texture = icons.Find(entry.Skill.IconKey);
            if (texture is not null)
            {
                var tint = entry.Rank > 0 || skillMaster != 0 && CanTrain(index, state) ? Color.White : new Color(130, 140, 149);
                canvas.Icon(texture, new Rectangle(bounds.X + 2, bounds.Y + 2, bounds.Width - 4, bounds.Height - 4), tint);
            }

            if (entry.Skill.Code != 0)
            {
                DrawRank(canvas, bounds, entry.Rank);
                DrawShortcut(canvas, bounds, entry.Skill.Code, bindings);
            }

            if (hover)
            {
                hovered = entry;
            }
        }

        if (state is not null)
        {
            if (_points != state.SkillPoints)
            {
                _points = state.SkillPoints;
                _pointsText = $"Points: {_points}";
            }

            canvas.Text(_pointsText, new Rectangle(panel.X + 132, panel.Y + 17, 130, 22), HudCanvas.Muted, 0.7f);
        }

        canvas.Text(skillMaster == 0 ? "Select: LMB / RMB     Bind: hold + F1-F8" : "Skill master: select a skill to train",
            new Rectangle(panel.X + 270, panel.Y + 17, panel.Width - 324, 22), HudCanvas.Muted, 0.62f);
        if (_trainingIndex >= 0)
        {
            DrawConfirmation(canvas, panel);
        }
        else if (hovered is not null)
        {
            DrawDetails(canvas, layout, pointer, hovered);
        }
        else if (basic.Contains(pointer))
        {
            DrawNotice(canvas, layout, "Basic attack", "Release LMB or RMB to select. Hold + F1-F8 to bind.");
        }
        else if (skillMaster != 0 && message.Length > 0)
        {
            DrawNotice(canvas, layout, "Skill master", message);
        }
    }

    private void DrawConfirmation(HudCanvas canvas, Rectangle panel)
    {
        var box = HudSkillTreeLayout.Confirmation(panel);
        canvas.FlushText();
        canvas.Frame(box);
        canvas.Text("CONFIRM TRAINING", new Rectangle(box.X + 20, box.Y + 12, box.Width - 40, 26), scale: 0.82f);
        canvas.Text(_trainingName, new Rectangle(box.X + 20, box.Y + 50, box.Width - 40, 28), scale: 0.75f);
        canvas.Text(_trainingPrice, new Rectangle(box.X + 20, box.Y + 88, box.Width - 40, 26), scale: 0.7f);
        canvas.Frame(ConfirmButton(panel, false), "Button", 8);
        canvas.Frame(ConfirmButton(panel, true), "Button", 8);
        canvas.Text("Cancel", ConfirmButton(panel, false), centered: true);
        canvas.Text("Learn", ConfirmButton(panel, true), centered: true);
    }

    private static void DrawNotice(HudCanvas canvas, HudLayout layout, string title, string message)
    {
        var bounds = layout.PlaceOverlay(new Point(layout.Panel.Center.X - 200, layout.Panel.Y - 78), 400, 72);
        canvas.FlushText();
        canvas.Frame(bounds, "Panel", 14);
        canvas.Text(title, new Rectangle(bounds.X + 14, bounds.Y + 10, bounds.Width - 28, 24), scale: 0.75f);
        canvas.Text(message, new Rectangle(bounds.X + 14, bounds.Y + 37, bounds.Width - 28, 22), HudCanvas.Muted, 0.62f);
    }

    private static void DrawRank(HudCanvas canvas, Rectangle bounds, int rank)
    {
        var track = new Rectangle(bounds.Right + 1, bounds.Y + 3, 7, bounds.Height - 6);
        canvas.Frame(track, "Inset", 2);
        var height = (track.Height - 4) * rank / 10;
        if (height > 0)
        {
            canvas.Image("Fill", new Rectangle(track.X + 2, track.Bottom - 2 - height, 3, height), new Color(117, 187, 98));
        }
    }

    private static void DrawDetails(HudCanvas canvas, HudLayout layout, Point pointer, Entry entry)
    {
        if (entry.Skill.Code == 0)
        {
            DrawNotice(canvas, layout, entry.Skill.Name, entry.Status);
            return;
        }

        var height = 168 + (entry.Description.Length + entry.Weapons.Length) * 22;
        var bounds = layout.PlaceOverlay(new Point(pointer.X - 170, layout.Panel.Y - height - 8), 340, height);
        canvas.FlushText();
        canvas.Frame(bounds, "Panel", 16);
        var y = bounds.Y + 12;
        canvas.Text(entry.Skill.Name, new Rectangle(bounds.X + 14, y, 312, 28), scale: 0.83f);
        y += 32;
        foreach (var line in entry.Description)
        {
            canvas.Text(line, new Rectangle(bounds.X + 14, y, 312, 22), Color.White, 0.68f);
            y += 22;
        }

        y += 8;
        canvas.Text(entry.Level, new Rectangle(bounds.X + 14, y, 65, 22), HudCanvas.Muted, 0.65f);
        canvas.Text(entry.Mouse, new Rectangle(bounds.X + 88, y, 228, 22), HudCanvas.Muted, 0.65f);
        y += 24;
        canvas.Text(entry.Cost, new Rectangle(bounds.X + 14, y, 312, 22), scale: 0.68f);
        y += 24;
        foreach (var line in entry.Weapons)
        {
            canvas.Text(line, new Rectangle(bounds.X + 14, y, 312, 22), HudCanvas.Muted, 0.62f);
            y += 22;
        }

        canvas.Text(entry.Status, new Rectangle(bounds.X + 14, y + 8, 312, 22), HudCanvas.Muted, 0.6f);
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

    private static void DrawShortcut(HudCanvas canvas, Rectangle node, uint skillCode, HudSkillBindings bindings)
    {
        for (var index = 0; index < Shortcuts.Length; index++)
        {
            if (bindings.Shortcut(index).SkillId != skillCode)
            {
                continue;
            }

            var badge = new Rectangle(node.X, node.Y, 22, 15);
            canvas.Frame(badge, "Inset", 2);
            canvas.Text(Shortcuts[index], badge, Color.White, 0.5f, centered: true);
        }
    }
}
