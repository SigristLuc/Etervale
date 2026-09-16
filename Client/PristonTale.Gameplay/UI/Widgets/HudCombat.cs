using Microsoft.Xna.Framework;
using PristonTale.Client.Net.Presence;
using PristonTale.Client.Net.WorldCatalog;
using PristonTale.Gameplay.UI.Rendering;

namespace PristonTale.Gameplay.UI.Widgets;

public sealed class HudCombat
{
    private sealed class Entity(EntitySpawnInfo spawn)
    {
        public EntitySpawnInfo Spawn { get; } = spawn;
        public int Hp { get; set; } = spawn.Hp;
    }

    private readonly Dictionary<uint, Entity> _entities = new();
    private readonly string[] _history = new string[64];
    private WorldRenderCatalog _catalog = WorldRenderCatalog.Empty;
    private uint _self;
    private int _historyCount;
    private int _historyEnd;
    private uint _lastTarget;
    private int _lastHp;
    private int _lastMaximum;
    private string _targetHealth = "";

    public int HistoryCount => _historyCount;
    public string LatestMessage => _historyCount > 0 ? _history[(_historyEnd + 63) % 64] : "";
    public void SetCatalog(WorldRenderCatalog catalog) => _catalog = catalog;

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
            }
        }

        foreach (var combat in update.Events)
        {
            if (combat.Kind != RemoteCombatEventKind.Hit || combat.AttackerId != _self && combat.TargetId != _self)
            {
                continue;
            }

            var prefix = combat.Critical ? "Critical! " : "";
            var text = combat.AttackerId == _self
                ? $"{prefix}{Name(combat.TargetId)}: -{combat.Damage} HP"
                : $"{prefix}You received {combat.Damage} damage";
            if (combat.Killed)
            {
                text += " (defeated)";
            }

            _history[_historyEnd] = text;
            _historyEnd = (_historyEnd + 1) % _history.Length;
            _historyCount = Math.Min(_historyCount + 1, _history.Length);
        }

        foreach (var id in update.Removed)
        {
            _entities.Remove(id);
        }
    }

    public void Draw(HudCanvas canvas, HudLayout layout, uint target, Rectangle occupiedPanel)
    {
        var width = (int)(layout.ViewportWidth / layout.Scale);
        var height = (int)(layout.ViewportHeight / layout.Scale);
        if (target != 0 && _entities.TryGetValue(target, out var entity))
        {
            var bounds = new Rectangle((width - 280) / 2, 18, 280, 67);
            if (bounds.Intersects(occupiedPanel))
            {
                bounds.X = Math.Max(8, (occupiedPanel.Left - bounds.Width) / 2);
            }

            canvas.Frame(bounds, "Panel", 15);
            canvas.Text(Name(target), new Rectangle(bounds.X + 14, bounds.Y + 9, 252, 23), scale: 0.8f, centered: true);
            var maximum = entity.Spawn.MaxHp;
            if (target != _lastTarget || entity.Hp != _lastHp || maximum != _lastMaximum)
            {
                _lastTarget = target;
                _lastHp = entity.Hp;
                _lastMaximum = maximum;
                _targetHealth = $"{entity.Hp} / {maximum}";
            }

            canvas.Bar(new Rectangle(bounds.X + 14, bounds.Y + 36, 252, 20), entity.Hp, maximum, new Color(177, 55, 57), _targetHealth);
        }

        if (_historyCount == 0)
        {
            return;
        }

        var log = new Rectangle(16, height - 108, Math.Min(300, layout.Resources.X - 24), 92);
        if (log.Width < 190)
        {
            log.Width = 250;
            log.Y = layout.Resources.Y - 100;
        }
        canvas.Frame(log, "Inset", 12);
        canvas.Text("COMBAT", new Rectangle(log.X + 12, log.Y + 7, log.Width - 24, 17), scale: 0.55f);
        var count = Math.Min(3, _historyCount);
        for (var index = 0; index < count; index++)
        {
            var entry = (_historyEnd - count + index + _history.Length) % _history.Length;
            canvas.Text(_history[entry], new Rectangle(log.X + 12, log.Y + 29 + index * 18, log.Width - 24, 17), HudCanvas.Muted, 0.6f);
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
