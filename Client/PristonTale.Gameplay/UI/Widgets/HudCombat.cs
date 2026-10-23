using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Client.Net.Presence;
using PristonTale.Client.Net.WorldCatalog;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Simulation.World.Monsters;

namespace PristonTale.Gameplay.UI.Widgets;

// The target frame regions in logical HUD pixels. Portrait is the viewport inside cw.tga that holds the live 3D portrait;
// Brood is the 16x16 race icon of monsters; StatsY is the text line under the life bar (level and life percentage).
public readonly record struct HudTargetFrame(Rectangle Box, Rectangle Portrait, Rectangle HealthBar, Rectangle Brood, int NameY, int StatsY);

// Target inspection box (centred at 0.92/0.1 of the screen with cw.tga, the name 16 pixels above its bottom edge, the
// monster race icon in its top right corner and the original life bar under it). Hits are kept as history lines; the
// original shows them as floating numbers only, so the chat window does not list them.
public sealed class HudCombat
{
    private const int StateBarWidth = 100;
    private const int StateBarSize = 92;
    private const int StateBarHeight = 16;
    private const int NameLift = 21;
    private static readonly Color LevelColor = new(220, 220, 220);
    private static readonly Color MonsterColor = new(255, 160, 120);
    private static readonly Color NpcColor = new(255, 255, 180);
    // The original name shadow sits one pixel right and one pixel up.
    private static readonly Point NameShadow = new(1, -1);

    private sealed class Entity(EntitySpawnInfo spawn)
    {
        public EntitySpawnInfo Spawn { get; } = spawn;
        public int Hp { get; set; } = spawn.Hp;
        public Vector3 Position { get; set; } = new(spawn.Position.X, spawn.Position.Y, spawn.Position.Z);
        // Set when the local player starts attacking this monster at full life, as in the original.
        public bool StateBarEnabled { get; set; }
    }

    private readonly Dictionary<uint, Entity> _entities = new();
    private readonly (string Text, Color Color)[] _history = new (string, Color)[64];
    private WorldRenderCatalog _catalog = WorldRenderCatalog.Empty;
    private uint _self;
    private int _levelValue = -1;
    private string _levelText = "";
    private int _percentValue = -1;
    private string _percentText = "";
    private int _historyCount;
    private int _historyEnd;

    public int HistoryCount => _historyCount;
    public string LatestMessage => _historyCount > 0 ? _history[(_historyEnd + 63) % 64].Text : "";
    public void SetCatalog(WorldRenderCatalog catalog) => _catalog = catalog;

    // What the client knows of an entity in view: its kind and a summon's owner (Unspecified when not in view).
    public RemoteEntityKind KindOf(uint id, out uint ownerId)
    {
        ownerId = 0;
        if (!_entities.TryGetValue(id, out var entity))
        {
            return RemoteEntityKind.Unspecified;
        }

        ownerId = entity.Spawn.OwnerId;
        return entity.Spawn.Kind;
    }

    // Fills the caller's list (cleared first) so the minimap reads the NPC positions without allocating.
    public void CollectNpcPositions(List<Vector3> positions)
    {
        positions.Clear();
        foreach (var entity in _entities.Values)
        {
            if (entity.Spawn.Kind == RemoteEntityKind.Npc)
            {
                positions.Add(entity.Position);
            }
        }
    }

    public void Observe(PresenceUpdate update)
    {
        foreach (var spawn in update.Spawns)
        {
            var enabled = _entities.TryGetValue(spawn.EntityId, out var previous) && previous.StateBarEnabled;
            _entities[spawn.EntityId] = new Entity(spawn) { StateBarEnabled = enabled };
            if (spawn.IsSelf)
            {
                _self = spawn.EntityId;
            }
        }

        foreach (var state in update.States)
        {
            if (_entities.TryGetValue(state.EntityId, out var entity))
            {
                entity.Hp = state.Hp;
                entity.Position = new Vector3(state.Position.X, state.Position.Y, state.Position.Z);
            }
        }

        foreach (var combat in update.Events)
        {
            if (combat.Kind == RemoteCombatEventKind.SwingBegin && combat.AttackerId == _self && _self != 0)
            {
                NoteAttack(combat.TargetId);
            }

            if (combat.Kind != RemoteCombatEventKind.Hit || combat.AttackerId != _self && combat.TargetId != _self)
            {
                continue;
            }

            var prefix = combat.Critical ? "Critical! " : "";
            var outgoing = combat.AttackerId == _self;
            var text = outgoing ? $"{prefix}{Name(combat.TargetId)}: -{combat.Damage} HP" : $"{prefix}You received {combat.Damage} damage";
            if (combat.Killed)
            {
                text += " (defeated)";
            }

            Add(text, outgoing ? Color.White : MonsterColor);
        }

        foreach (var id in update.Removed)
        {
            _entities.Remove(id);
        }
    }

    public void Add(string text, Color color)
    {
        _history[_historyEnd] = (text, color);
        _historyEnd = (_historyEnd + 1) % _history.Length;
        _historyCount = Math.Min(_historyCount + 1, _history.Length);
    }

    // As in the original, only an attack that finds the monster at full life enables the classic bar.
    public void NoteAttack(uint target)
    {
        if (_entities.TryGetValue(target, out var entity) && entity.Spawn.Kind == RemoteEntityKind.Monster && entity.Spawn.MaxHp > 0
            && entity.Hp == entity.Spawn.MaxHp)
        {
            entity.StateBarEnabled = true;
        }
    }

    public static Rectangle TargetBox(HudLayout layout)
    {
        var height = layout.Height / 6 + 10;
        var width = (int)(layout.Height * 1.34f / 8) + 10;
        var centerX = (int)(layout.Width * 0.92f);
        var centerY = (int)(layout.Height * 0.1f);
        return new Rectangle(centerX - (width >> 1), centerY - (height >> 1), width, height);
    }

    // The life bar starts 5 pixels in and 2 pixels below the box, 10 pixels narrower; the text line under it is where the
    // original printed the target's statistics, 22 pixels below the box.
    public static HudTargetFrame TargetFrame(HudLayout layout)
    {
        var box = TargetBox(layout);
        var centerX = box.X + (box.Width >> 1);
        var centerY = box.Y + (box.Height >> 1);
        var portrait = new Rectangle(box.X + 4, box.Y + 4, box.Width - 8, box.Height - 8);
        var bar = new Rectangle(box.X + 5, box.Bottom + 2, box.Width - 10, StateBarHeight);
        var brood = new Rectangle(centerX + box.Width / 2 - 24, centerY - box.Height / 2 + 8, 16, 16);
        // The UI font is taller than the original's; the name sits higher so its descenders end inside the frame's border.
        return new HudTargetFrame(box, portrait, bar, brood, box.Bottom - NameLift, box.Bottom + 22);
    }

    public static int HealthPercent(int life, int maximum)
    {
        if (maximum <= 0 || life <= 0)
        {
            return 0;
        }

        return (int)Math.Clamp((long)Math.Min(life, maximum) * 100 / maximum, 1, 100);
    }

    public bool ShowsHealthBar(uint target, bool classic)
    {
        // NPCs show only their name, as in the original: they cannot be fought, so a life bar would mislead.
        if (!_entities.TryGetValue(target, out var entity) || entity.Spawn.MaxHp <= 0 || entity.Spawn.Kind == RemoteEntityKind.Npc)
        {
            return false;
        }

        return !classic || entity.StateBarEnabled;
    }

    // Original life bar: red to yellow to green over the squared life fraction.
    public static Color StateColor(int life, int maximum)
    {
        if (maximum <= 0)
        {
            return Color.Red;
        }

        var percent = (int)((long)Math.Clamp(life, 0, maximum) * 511 / maximum);
        percent = percent * percent / 512;
        var low = percent & 0xFF;
        return percent < 256 ? new Color(255, low, 0) : new Color(255 - low, 255, 0);
    }

    public bool TryGetPortraitSubject(uint target, out HudPortraitSubject subject)
    {
        subject = default;
        if (target == 0 || !_entities.TryGetValue(target, out var entity))
        {
            return false;
        }

        var (y, z) = entity.Spawn.Kind switch
        {
            RemoteEntityKind.Monster when _catalog.GetMonster(entity.Spawn.TemplateId) is { } monster => (monster.PortraitOffsetY, monster.PortraitOffsetZ),
            RemoteEntityKind.Npc when _catalog.GetNpc(entity.Spawn.TemplateId) is { } npc => (npc.PortraitOffsetY, npc.PortraitOffsetZ),
            _ => (0, 0)
        };
        subject = new HudPortraitSubject(target, y, z);
        return true;
    }

    // The race icon of monsters; the brood of player-like monsters has none.
    public static string BroodIcon(MonsterBrood brood) => brood switch
    {
        MonsterBrood.Normal => HudArt.BroodNormal,
        MonsterBrood.Mutant => HudArt.BroodMutant,
        MonsterBrood.Undead => HudArt.BroodUndead,
        MonsterBrood.Demon => HudArt.BroodDemon,
        MonsterBrood.Mechanic => HudArt.BroodMechanic,
        _ => null
    };

    // Modern mode (default) always shows the bar with the level and life percentage under it; classic mode draws only
    // what the original shows: the name, the race icon and the life bar once StateBarEnabled.
    public void DrawTarget(HudPainter painter, HudLayout layout, uint target, bool classicHealthBar = false, Texture2D portrait = null)
    {
        if (target == 0 || !_entities.TryGetValue(target, out var entity))
        {
            return;
        }

        var frame = TargetFrame(layout);
        var box = frame.Box;
        // cw.tga's centre is a black gradient at roughly half opacity; the slate fill behind it shows through as the
        // frame's grey backdrop, and the actor is drawn over both.
        painter.ColorBox(frame.Portrait, 60, 68, 74, 255);
        painter.Stretch(HudArt.TargetBox, box);
        painter.Image(portrait, frame.Portrait);
        if (entity.Spawn.Kind == RemoteEntityKind.Monster && BroodIcon(_catalog.GetMonster(entity.Spawn.TemplateId)?.Brood ?? MonsterBrood.Normal) is { } icon)
        {
            painter.Stretch(icon, frame.Brood);
        }

        var color = entity.Spawn.Kind switch
        {
            RemoteEntityKind.Monster => MonsterColor,
            RemoteEntityKind.Npc => NpcColor,
            _ => Color.White
        };
        // The original centres the name on the frame without clipping it; it only has to stay on screen.
        var centerX = box.X + (box.Width >> 1);
        var room = Math.Max(box.Width - 8, 2 * Math.Min(centerX, layout.Width - centerX) - 8);
        painter.TextCentered(painter.Fit(Name(target), room), centerX - room / 2, centerX + room / 2, frame.NameY, color, true, NameShadow);
        if (!ShowsHealthBar(target, classicHealthBar))
        {
            return;
        }

        var bar = frame.HealthBar;
        DrawHealthBar(painter, bar, entity.Hp, entity.Spawn.MaxHp);
        if (classicHealthBar)
        {
            return;
        }

        var percent = PercentText(HealthPercent(entity.Hp, entity.Spawn.MaxHp));
        painter.Text(LevelText(Level(entity)), bar.X, frame.StatsY, LevelColor);
        painter.Text(percent, bar.Right - painter.Measure(percent), frame.StatsY, Color.White);
    }

    private static void DrawHealthBar(HudPainter painter, Rectangle bar, int life, int maximum)
    {
        painter.Stretch(HudArt.EnergyBack, bar, new Rectangle(0, 0, StateBarWidth, 15));
        if (life > 0)
        {
            var percent = (int)((long)Math.Min(life, maximum) * StateBarSize / maximum);
            painter.Stretch(HudArt.EnergyFill, new Rectangle(bar.X, bar.Y, (percent + 3) * bar.Width / StateBarWidth, bar.Height),
                new Rectangle(0, 0, percent + 3, 15), StateColor(life, maximum));
        }
    }

    private static int Level(Entity entity) => entity.Spawn.Level > 0 ? entity.Spawn.Level : entity.Spawn.Player?.Level ?? 0;

    // The frame redraws every frame; its numbers are formatted only when they change.
    private string LevelText(int level)
    {
        if (level != _levelValue)
        {
            _levelValue = level;
            _levelText = level > 0 ? $"Lv. {level}" : "";
        }

        return _levelText;
    }

    private string PercentText(int percent)
    {
        if (percent != _percentValue)
        {
            _percentValue = percent;
            _percentText = $"{percent}%";
        }

        return _percentText;
    }

    private string Name(uint id)
    {
        if (!_entities.TryGetValue(id, out var entity))
        {
            return "Target";
        }

        if (entity.Spawn.Player is { } player)
        {
            return player.Name;
        }

        if (!string.IsNullOrEmpty(entity.Spawn.Name))
        {
            return entity.Spawn.Name;
        }

        return entity.Spawn.Kind == RemoteEntityKind.Npc
            ? _catalog.GetNpc(entity.Spawn.TemplateId)?.Name ?? "NPC"
            : _catalog.GetMonster(entity.Spawn.TemplateId)?.Name ?? "Monster";
    }
}
