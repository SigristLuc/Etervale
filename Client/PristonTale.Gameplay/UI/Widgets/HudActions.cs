using PristonTale.Simulation.Skills;
using Microsoft.Xna.Framework;
using PristonTale.Gameplay.UI.Rendering;

namespace PristonTale.Gameplay.UI.Widgets;

public sealed class HudActions
{
    public HudSkillBindings Bindings { get; } = new();
    public bool RightAttackEnabled => Bindings.RightSkillId == HudSkillBindings.BasicAttackId;

    public static Rectangle Socket(Rectangle resources, bool right)
    {
        var scale = resources.Width / 500f;
        var center = resources.X + (right ? 451 : 48) * scale;
        return new Rectangle((int)center - 22, resources.Y + (int)(49 * scale) - 22, 44, 44);
    }

    public static Rectangle SkillRow(Rectangle panel) => HudSkillTreeLayout.BasicAttack(panel);

    public void Draw(HudCanvas canvas, Rectangle resources)
    {
        var left = Socket(resources, false);
        var right = Socket(resources, true);
        canvas.Frame(left, "Slot", 5);
        canvas.Frame(right, "Slot", 5);
        canvas.Image("Attack", new Rectangle(left.X + 5, left.Y + 5, 34, 34));
        if (RightAttackEnabled)
        {
            canvas.Image("Attack", new Rectangle(right.X + 5, right.Y + 5, 34, 34));
        }
    }

    public string HoverText(Rectangle resources, Point pointer)
    {
        if (Socket(resources, false).Contains(pointer))
        {
            return "Left mouse: Basic attack. Select skills in S.";
        }

        if (Socket(resources, true).Contains(pointer))
        {
            return RightAttackEnabled ? "Right mouse: Basic attack. Select skills in S." : "Right mouse: no skill selected. Open S.";
        }

        return "";
    }

}
