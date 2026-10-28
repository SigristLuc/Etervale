using Microsoft.Xna.Framework;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Gameplay.World.Remote;

namespace PristonTale.Gameplay.UI.Widgets;

// Drop names over the scene, in the classic name box geometry the original used for them: the box 36 pixels above the
// drop's point, the text 9 pixels in, and a box that would leave the screen is not drawn. Labels draw in the order
// given (the hovered drop last, on top) and before the monster and NPC names, which cover them. The original's grey
// text is lavender here, brighter under the cursor.
public static class HudGroundLabels
{
    private const int Lift = 36;
    private static readonly Color Named = new(150, 144, 206);
    private static readonly Color Hovered = new(196, 186, 255);

    public static void Draw(HudPainter painter, HudLayout layout, IReadOnlyList<GroundLabel> labels, Action<Guid, Rectangle> placed)
    {
        var scale = Math.Max(1, layout.Scale);
        var lineHeight = Math.Max(16, painter.TextHeight);
        var height = 4 + lineHeight + 6 + 2;
        foreach (var label in labels)
        {
            var width = painter.Measure(label.Text) + 18;
            var x = (int)(label.Anchor.X / scale) - width / 2;
            var y = (int)(label.Anchor.Y / scale) - Lift;
            if (x < 0 || y < 0 || x + width > layout.Width || y + height > layout.Height)
            {
                continue;
            }

            painter.Flush();
            painter.NameBox(new Rectangle(x, y, width, height));
            painter.Flush();
            painter.Text(label.Text, x + 9, y + 5 + 2, label.Hovered ? Hovered : Named, false);
            placed?.Invoke(label.Id, new Rectangle(x * scale, y * scale, width * scale, height * scale));
        }
    }
}
