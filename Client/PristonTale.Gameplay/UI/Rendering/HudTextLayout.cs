using Microsoft.Xna.Framework.Graphics;

namespace PristonTale.Gameplay.UI.Rendering;

// Word wrap: words join a line while it measures within the width; a single word wider than the width keeps a line of
// its own. Runs of spaces collapse.
public static class HudTextLayout
{
    public static string[] Wrap(SpriteFont font, string text, float width, float scale) =>
        Wrap(text, width, candidate => font.MeasureString(candidate).X * scale).ToArray();

    public static List<string> Wrap(string text, float width, Func<string, float> measure)
    {
        var lines = new List<string>();
        var line = "";
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = line.Length == 0 ? word : line + " " + word;
            if (line.Length > 0 && measure(candidate) > width)
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

        return lines;
    }
}
