using PristonTale.Simulation.Skills;
using Microsoft.Xna.Framework.Input;

namespace PristonTale.Gameplay.UI.Widgets;

public sealed record HudSkillChoice(uint Id, string Name, int Rank, SkillMouseUse MouseUse);
public readonly record struct HudSkillShortcut(uint SkillId, SkillMouseUse Side);

public sealed class HudSkillBindings
{
    public const uint BasicAttackId = 0x11111111;
    public static readonly HudSkillChoice BasicAttack = new(BasicAttackId, "Basic attack", 1, SkillMouseUse.Both);
    private readonly Dictionary<uint, HudSkillChoice> _choices = new() { [BasicAttackId] = BasicAttack };
    private readonly HudSkillShortcut[] _shortcuts = new HudSkillShortcut[8];
    private KeyboardState _previousKeys;
    private MouseState _previousMouse;
    private uint _pressedSkill;
    private SkillMouseUse _pressedSide;
    private bool _boundDuringPress;

    // The saved record restores both sockets from the selected skill, which is 0 (the normal attack) until a skill is chosen.
    public uint LeftSkillId { get; private set; } = BasicAttackId;
    public uint RightSkillId { get; private set; } = BasicAttackId;
    public HudSkillChoice Left => _choices.GetValueOrDefault(LeftSkillId);
    public HudSkillChoice Right => _choices.GetValueOrDefault(RightSkillId);
    public HudSkillShortcut Shortcut(int index) => _shortcuts[index];

    public bool Assign(int index, uint skillId, SkillMouseUse side)
    {
        if (index < 0 || index >= _shortcuts.Length || !CanSelect(skillId, side))
        {
            return false;
        }

        // Original keeps one shortcut per skill and one skill per function key.
        for (var other = 0; other < _shortcuts.Length; other++)
        {
            if (_shortcuts[other].SkillId == skillId)
            {
                _shortcuts[other] = default;
            }
        }

        _shortcuts[index] = new HudSkillShortcut(skillId, side);
        return true;
    }

    public bool Select(uint skillId, SkillMouseUse side)
    {
        if (!CanSelect(skillId, side))
        {
            return false;
        }

        if (side == SkillMouseUse.Left)
        {
            LeftSkillId = skillId;
        }
        else
        {
            RightSkillId = skillId;
        }

        return true;
    }

    public void Update(KeyboardState keyboard, MouseState mouse, uint hoveredSkill)
    {
        if (mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released)
        {
            BeginPress(hoveredSkill, SkillMouseUse.Left);
        }
        else if (mouse.RightButton == ButtonState.Pressed && _previousMouse.RightButton == ButtonState.Released)
        {
            BeginPress(hoveredSkill, SkillMouseUse.Right);
        }

        var control = keyboard.IsKeyDown(Keys.LeftControl) || keyboard.IsKeyDown(Keys.RightControl);
        for (var index = 0; index < _shortcuts.Length && !control; index++)
        {
            var key = Keys.F1 + index;
            if (!keyboard.IsKeyDown(key) || _previousKeys.IsKeyDown(key))
            {
                continue;
            }

            if (hoveredSkill != 0)
            {
                var held = _pressedSide == SkillMouseUse.Left ? mouse.LeftButton : mouse.RightButton;
                if (_pressedSkill == hoveredSkill && held == ButtonState.Pressed && Assign(index, hoveredSkill, _pressedSide))
                {
                    _boundDuringPress = true;
                }
            }
            else
            {
                var shortcut = _shortcuts[index];
                Select(shortcut.SkillId, shortcut.Side);
            }
        }

        var released = _pressedSide == SkillMouseUse.Left ? mouse.LeftButton == ButtonState.Released : mouse.RightButton == ButtonState.Released;
        if (_pressedSkill != 0 && released)
        {
            if (!_boundDuringPress && hoveredSkill == _pressedSkill)
            {
                Select(_pressedSkill, _pressedSide);
            }

            _pressedSkill = 0;
            _pressedSide = SkillMouseUse.None;
        }

        _previousKeys = keyboard;
        _previousMouse = mouse;
    }

    private void BeginPress(uint skillId, SkillMouseUse side)
    {
        _pressedSkill = skillId;
        _pressedSide = side;
        _boundDuringPress = false;
    }

    private bool CanSelect(uint skillId, SkillMouseUse side)
    {
        return side is SkillMouseUse.Left or SkillMouseUse.Right
            && _choices.TryGetValue(skillId, out var skill) && skill.Rank > 0 && (skill.MouseUse & side) == side;
    }
}
