using Microsoft.Xna.Framework;
using PristonTale.Client.Net.Presence;
using PristonTale.Client.Net.WorldCatalog;
using PristonTale.Gameplay.UI.Rendering;

namespace PristonTale.Gameplay.UI.Widgets;

// The target frame regions. Portrait is the viewport inside cw.tga reserved for the live 3D portrait.
public readonly record struct HudTargetFrame(Rectangle Box, Rectangle Portrait, Rectangle HealthBar, int NameY, int LevelY);

// Target inspection box (at 0.92/0.1 of the screen with cw.tga and the original life bar)
// and combat lines in the bottom-left chat area (chat messages at x=16).
public sealed class HudCombat
{
    private const int StateBarWidth = 100;
    private const int StateBarSize = 92;
    private const int StateBarHeight = 16;
    private const int LogLines = 6;

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
    private int _historyCount;
    private int _historyEnd;

    public int HistoryCount => _historyCount;
    public string LatestMessage => _historyCount > 0 ? _history[(_historyEnd + 63) % 64].Text : "";
    public void SetCatalog(WorldRenderCatalog catalog) => _catalog = catalog;

    public IEnumerable<Vector3> NpcPositions
    {
        get
        {
            foreach (var entity in _entities.Values)
            {
                if (entity.Spawn.Kind == RemoteEntityKind.Npc)
                {
                    yield return entity.Position;
                }
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

            Add(text, outgoing ? Color.White : new Color(255, 160, 120));
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

    public static HudTargetFrame TargetFrame(HudLayout layout)
    {
        var box = TargetBox(layout);
        var portrait = new Rectangle(box.X + 4, box.Y + 4, box.Width - 8, box.Height - 8);
        var bar = new Rectangle(box.X + 5, box.Bottom + 2, box.Width - 10, StateBarHeight);
        return new HudTargetFrame(box, portrait, bar, box.Bottom - 16, bar.Bottom + 1);
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
        if (!_entities.TryGetValue(target, out var entity) || entity.Spawn.MaxHp <= 0)
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

    // Modern mode (default) always shows the bar with a percentage; classic mode follows StateBarEnabled and, like
    // the original life bar, shows the bar alone.
    public void DrawTarget(HudPainter painter, HudLayout layout, uint target, bool classicHealthBar = false)
    {
        if (target == 0 || !_entities.TryGetValue(target, out var entity))
        {
            return;
        }

        var frame = TargetFrame(layout);
        var box = frame.Box;
        // cw.tga has a translucent centre; the original fills the portrait viewport behind it.
        painter.ColorBox(frame.Portrait, 8, 10, 12, 255);
        painter.Stretch(HudArt.TargetBox, box);
        var color = entity.Spawn.Kind switch
        {
            RemoteEntityKind.Monster => new Color(255, 160, 120),
            RemoteEntityKind.Npc => new Color(255, 255, 180),
            _ => Color.White
        };
        painter.TextCentered(painter.Fit(Name(target), box.Width - 8), box.Left, box.Right, frame.NameY, color);

        var level = Level(entity);
        var levelText = level > 0 ? $"Lv. {level}" : "";
        var levelColor = new Color(220, 220, 220);
        if (!ShowsHealthBar(target, classicHealthBar))
        {
            painter.TextCentered(levelText, box.Left, box.Right, frame.HealthBar.Y, levelColor);
            return;
        }

        var bar = frame.HealthBar;
        DrawHealthBar(painter, bar, entity.Hp, entity.Spawn.MaxHp);
        if (classicHealthBar)
        {
            painter.TextCentered(levelText, box.Left, box.Right, frame.LevelY, levelColor);
            return;
        }

        // The percentage sits under the bar's right end; the bar art is too thin to hold text.
        var percent = $"{HealthPercent(entity.Hp, entity.Spawn.MaxHp)}%";
        painter.Text(levelText, bar.X, frame.LevelY, levelColor);
        painter.Text(percent, bar.Right - painter.Measure(percent), frame.LevelY, Color.White);
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

    public void DrawLog(HudPainter painter, HudLayout layout)
    {
        var count = Math.Min(LogLines, _historyCount);
        for (var index = 0; index < count; index++)
        {
            var entry = _history[(_historyEnd - 1 - index + _history.Length) % _history.Length];
            painter.Text(painter.Fit(entry.Text, 270), 16, layout.BottomOffset + 565 - (index + 1) * HudPainter.LineHeight, entry.Color);
        }
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
