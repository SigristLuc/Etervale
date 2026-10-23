using Microsoft.Xna.Framework;
using PristonTale.Gameplay.Effects;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Modifiers;
using PristonTale.Simulation.Skills;

namespace PristonTale.Gameplay.UI.Widgets;

// The skill sockets of the bottom bar (the selected left and right skills with their cooldown sweep) and the buff
// bar of the local player's active effects, with the original keep icons and the remaining time.
public sealed class HudSkillHud
{
    private const int BuffIcon = 26;
    private const int BuffSpacing = 30;
    private const int MaxBuffs = 12;
    private static readonly CharacterClassRegistry Classes = new();

    private readonly HudSprites _sprites;
    private readonly Dictionary<uint, string> _buttons = new();
    private readonly Dictionary<uint, string> _keepIcons = new();
    private readonly string[] _seconds = new string[600];
    private PlayerClass _class;

    public HudSkillHud(HudSprites sprites)
    {
        _sprites = sprites;
        for (var second = 0; second < _seconds.Length; second++)
        {
            _seconds[second] = second < 60 ? second.ToString(System.Globalization.CultureInfo.InvariantCulture) : $"{second / 60}m";
        }
    }

    public void SetClass(PlayerClass characterClass)
    {
        _class = characterClass;
        _buttons.Clear();
        var requests = new List<string>();
        foreach (var skill in Classes.Get(characterClass).Skills)
        {
            if (skill.Code == 0 || skill.IconKey.Length == 0)
            {
                continue;
            }

            var button = HudArt.SkillButton(characterClass, skill.IconKey, false);
            _buttons[skill.Code] = button;
            requests.Add(button);
            if (SkillRuleCatalog.TryGet(skill.Code, out var rule) && rule.KeepIcon.Length > 0)
            {
                var keep = $"image/Sinimage/skill/keep/{rule.KeepIcon}.tga";
                _keepIcons[skill.Code] = keep;
                requests.Add(keep);
            }
        }

        _sprites.Request(requests);
    }

    // Icons over the bottom bar's sockets; y is the bar's vertical offset in the design space.
    public void DrawSockets(HudPainter painter, int y, uint leftSkill, uint rightSkill, SkillCooldownBook cooldowns, double nowMs)
    {
        DrawSocket(painter, HudBar.Socket(false), y, leftSkill, cooldowns, nowMs);
        DrawSocket(painter, HudBar.Socket(true), y, rightSkill, cooldowns, nowMs);
    }

    // The status whose icon is under the pointer in the buff bar (0 for none).
    public static uint BuffAt(IReadOnlyList<StatusEntry> statuses, Point pointer)
    {
        var shown = Math.Min(statuses.Count, MaxBuffs);
        for (var index = 0; index < shown; index++)
        {
            if (new Rectangle(8 + index * BuffSpacing, 8, BuffIcon, BuffIcon).Contains(pointer))
            {
                return statuses[index].Code;
            }
        }

        return 0;
    }

    public void DrawBuffs(HudPainter painter, IReadOnlyList<StatusEntry> statuses, double nowMs)
    {
        var x = 8;
        var y = 8;
        var shown = 0;
        foreach (var status in statuses)
        {
            if (shown >= MaxBuffs)
            {
                break;
            }

            var bounds = new Rectangle(x, y, BuffIcon, BuffIcon);
            var icon = Icon(status.Code);
            painter.ColorBox(new Rectangle(x - 1, y - 1, BuffIcon + 2, BuffIcon + 2), status.Harmful ? 150 : 20, 20, status.Harmful ? 20 : 60, 180);
            if (icon is not null && painter.Texture(icon) is { } texture)
            {
                painter.Image(texture, bounds);
            }
            else
            {
                painter.ColorBox(bounds, status.Harmful ? 120 : 60, status.Harmful ? 40 : 90, status.Harmful ? 40 : 140, 220);
            }

            if (!status.Permanent && status.DurationMs > 0)
            {
                var remaining = status.RemainingMs(nowMs);
                var fraction = (float)Math.Clamp(1d - remaining / status.DurationMs, 0d, 1d);
                var shade = (int)(BuffIcon * fraction);
                if (shade > 0)
                {
                    painter.ColorBox(new Rectangle(x, y, BuffIcon, shade), 0, 0, 0, 120);
                }

                var seconds = (int)Math.Ceiling(remaining / 1000d);
                painter.TextCentered(_seconds[Math.Clamp(seconds, 0, _seconds.Length - 1)], x - 4, x + BuffIcon + 4, y + BuffIcon + 1,
                    seconds <= 5 ? new Color(255, 120, 100) : new Color(230, 230, 230));
            }

            x += BuffSpacing;
            shown++;
        }
    }

    private void DrawSocket(HudPainter painter, Rectangle socket, int y, uint skillCode, SkillCooldownBook cooldowns, double nowMs)
    {
        if (skillCode is 0 or HudSkillBindings.BasicAttackId || !_buttons.TryGetValue(skillCode, out var button) || painter.Texture(button) is not { } texture)
        {
            return;
        }

        var bounds = new Rectangle(socket.X + (socket.Width - 40) / 2, socket.Y + y + (socket.Height - 40) / 2, 40, 40);
        painter.Image(texture, bounds);
        var fraction = cooldowns?.Fraction(skillCode, nowMs) ?? 0f;
        if (fraction <= 0f)
        {
            return;
        }

        // The sweep drains from the top as the gauge refills, like the original's mastery gauge.
        var shade = (int)MathF.Ceiling(bounds.Height * fraction);
        painter.ColorBox(new Rectangle(bounds.X, bounds.Y, bounds.Width, shade), 0, 0, 0, 160);
        painter.ColorBox(new Rectangle(bounds.X, bounds.Y + shade - 1, bounds.Width, 1), 255, 220, 120, 200);
        var seconds = (int)Math.Ceiling(cooldowns.RemainingMs(skillCode, nowMs) / 1000d);
        painter.TextCentered(_seconds[Math.Clamp(seconds, 0, _seconds.Length - 1)], bounds.X, bounds.Right, bounds.Y + 13, new Color(255, 240, 200));
    }

    private string Icon(uint code)
    {
        // Buffs another class's skill put on the player (an ally's blessing) bring their own keep icon.
        if (!_keepIcons.ContainsKey(code) && SkillRuleCatalog.TryGet(code, out var rule) && rule.KeepIcon.Length > 0)
        {
            var path = $"image/Sinimage/skill/keep/{rule.KeepIcon}.tga";
            _keepIcons[code] = path;
            _sprites.Request([path]);
        }

        if (_keepIcons.TryGetValue(code, out var keep) && !_sprites.IsMissing(keep))
        {
            return keep;
        }

        return _buttons.GetValueOrDefault(code);
    }
}
