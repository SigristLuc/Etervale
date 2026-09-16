using Microsoft.Xna.Framework.Graphics;

namespace PristonTale.Gameplay.UI.Rendering;

public static class HudTextLayout
{
    public static string[] Wrap(SpriteFont font, string text, float width, float scale)
    {
        var lines = new List<string>();
        var line = "";
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = line.Length == 0 ? word : line + " " + word;
            if (line.Length > 0 && font.MeasureString(candidate).X * scale > width)
            {
                lines.Add(line);
                line = word;
            }
            else
            {
                line = candidate;
            }
        }

        if (line.Length > 0)
        {
            lines.Add(line);
        }

        return lines.ToArray();
    }
}
