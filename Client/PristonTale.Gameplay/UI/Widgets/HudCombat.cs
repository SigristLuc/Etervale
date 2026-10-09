using Microsoft.Xna.Framework;
using PristonTale.Client.Net.Presence;
using PristonTale.Client.Net.WorldCatalog;
using PristonTale.Gameplay.UI.Rendering;

namespace PristonTale.Gameplay.UI.Widgets;

// Target inspection box (playmain.cpp DrawEachPlayer at 0.92/0.1 with cw.tga and ActorHud.cpp DrawStateBar)
// and combat lines in the bottom-left chat area (sinInterFace.cpp DisplayChatMessage at x=16).
public sealed class HudCombat
{
    private const int StateBarWidth = 100;
    private const int StateBarSize = 92;
    private const int LogLines = 6;

    private sealed class Entity(EntitySpawnInfo spawn)
    {
        public EntitySpawnInfo Spawn { get; } = spawn;
        public int Hp { get; set; } = spawn.Hp;
        public Vector3 Position { get; set; } = new(spawn.Position.X, spawn.Position.Y, spawn.Position.Z);
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
            _entities[spawn.EntityId] = new Entity(spawn);
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

    public static Rectangle TargetBox(HudLayout layout)
    {
        var height = layout.Height / 6 + 10;
        var width = (int)(layout.Height * 1.34f / 8) + 10;
        var centerX = (int)(layout.Width * 0.92f);
        var centerY = (int)(layout.Height * 0.1f);
        return new Rectangle(centerX - (width >> 1), centerY - (height >> 1), width, height);
    }

    // ActorHud.cpp DrawStateBar: red to yellow to green over the squared life fraction.
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

    public void DrawTarget(HudPainter painter, HudLayout layout, uint target)
    {
        if (target == 0 || !_entities.TryGetValue(target, out var entity))
        {
            return;
        }

        var box = TargetBox(layout);
        // cw.tga has a translucent centre; the original fills the portrait viewport behind it.
        painter.ColorBox(new Rectangle(box.X + 4, box.Y + 4, box.Width - 8, box.Height - 8), 8, 10, 12, 255);
        painter.Stretch(HudArt.TargetBox, box);
        var name = Name(target);
        var color = entity.Spawn.Kind switch
        {
            RemoteEntityKind.Monster => new Color(255, 160, 120),
            RemoteEntityKind.Npc => new Color(255, 255, 180),
            _ => Color.White
        };
        painter.TextCentered(painter.Fit(name, box.Width - 8), box.Left, box.Right, box.Bottom - 16, color);

        var maximum = entity.Spawn.MaxHp;
        if (maximum <= 0)
        {
            return;
        }

        var x = box.X + 5;
        var y = box.Bottom + 2;
        var width = box.Width - 10;
        painter.Stretch(HudArt.EnergyBack, new Rectangle(x, y, width, 16), new Rectangle(0, 0, StateBarWidth, 15));
        if (entity.Hp > 0)
        {
            var percent = (int)((long)Math.Min(entity.Hp, maximum) * StateBarSize / maximum);
            painter.Stretch(HudArt.EnergyFill, new Rectangle(x, y, (percent + 3) * width / StateBarWidth, 16), new Rectangle(0, 0, percent + 3, 15),
                StateColor(entity.Hp, maximum));
        }
    }

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

        return entity.Spawn.Player?.Name ?? _catalog.GetMonster(entity.Spawn.TemplateId)?.Name ?? "Monster";
    }
}
