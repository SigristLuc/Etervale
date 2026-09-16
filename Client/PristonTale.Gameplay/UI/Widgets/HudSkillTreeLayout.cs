using Microsoft.Xna.Framework;

namespace PristonTale.Gameplay.UI.Widgets;

public static class HudSkillTreeLayout
{
    public static Rectangle Column(Rectangle panel, int tier)
    {
        var width = (panel.Width - 32) / 4;
        return new Rectangle(panel.X + 16 + tier * width, panel.Y + 68, width, 158);
    }

    public static Rectangle Node(Rectangle panel, int index)
    {
        var column = Column(panel, index / 4);
        var position = (index % 4) switch
        {
            0 => new Point(4, 53),
            1 => new Point(50, 106),
            2 => new Point(65, 2),
            _ => new Point(128, 28)
        };
        return new Rectangle(column.X + position.X, column.Y + position.Y, 46, 46);
    }

    public static Rectangle BasicAttack(Rectangle panel)
    {
        var column = Column(panel, 0);
        return new Rectangle(column.X + 131, column.Y + 109, 40, 40);
    }

    public static Rectangle Confirmation(Rectangle panel) => new(panel.Center.X - 208, panel.Y + 42, 416, 174);

    public static Rectangle ConfirmButton(Rectangle panel, bool accept)
    {
        var box = Confirmation(panel);
        return new Rectangle(box.X + (accept ? 220 : 20), box.Bottom - 48, 176, 32);
    }
}
