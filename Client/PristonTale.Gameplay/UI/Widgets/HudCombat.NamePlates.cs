using Microsoft.Xna.Framework;
using PristonTale.Client.Net.Presence;
using PristonTale.Gameplay.UI.Chat;
using PristonTale.Gameplay.UI.Rendering;

namespace PristonTale.Gameplay.UI.Widgets;

// Name boxes over the scene: an NPC's name always within 768 units of the camera, a monster's only while it is under the
// cursor or is the clicked target. Summons carry their own plates (SummonPlates). Farther boxes draw first; the hovered
// and the clicked one draw last. Classic (classicNamePlates) is the original's: the speech bubble frame (highlighted
// under the cursor), the HUD text size and the life bar while the cursor is on the monster. Otherwise the same frame
// holds smaller text, and the bar shows once the monster was clicked or the local player swung at it, not for the
// cursor alone.
public sealed partial class HudCombat
{
    private const float NpcPlateDepth = 12 * 64;
    // Classic: the frame's top this far above the head point. Compact: the fill's bottom this far above it.
    private const int ClassicPlateLift = 36;
    private const int PlateLift = 12;
    private const int PlatePadY = 1;
    private const float PlateTextScale = 0.6f;
    // The original's 100x16 life bar drawn at 0.6 x 0.7.
    private const int PlateBarWidth = 60;
    private const int PlateBarHeight = 11;
    private static readonly Color PlateMonster = new(255, 210, 210);
    private static readonly Color PlateTracedMonster = new(255, 230, 200);
    private static readonly Color PlateNpc = new(180, 180, 255);
    private readonly List<(ulong Id, HudOverheadPoint Point, bool Focus)> _plates = new();

    // Whether a monster's name box carries its life bar.
    public static bool ShowsPlateBar(bool classic, bool hovered, bool traced, bool attacked) => classic ? hovered : traced || attacked;

    public void DrawNamePlates(HudPainter painter, HudLayout layout, Func<ulong, HudOverheadPoint?> project, ulong hovered, ulong traced, bool classic)
    {
        _plates.Clear();
        foreach (var (id, entity) in _entities)
        {
            var focus = id == hovered || id == traced;
            var kind = entity.Spawn.Kind;
            var shown = kind == RemoteEntityKind.Npc || (kind == RemoteEntityKind.Monster && focus && entity.Hp > 0 && entity.Spawn.OwnerId == 0);
            if (shown && project(id) is { } point && (focus || point.Depth < NpcPlateDepth))
            {
                _plates.Add((id, point, focus));
            }
        }

        _plates.Sort(static (left, right) => left.Focus != right.Focus ? (left.Focus ? 1 : -1) : right.Point.Depth.CompareTo(left.Point.Depth));
        var scale = Math.Max(1, layout.Scale);
        foreach (var (id, point, _) in _plates)
        {
            var entity = _entities[id];
            var monster = entity.Spawn.Kind == RemoteEntityKind.Monster;
            var color = !monster ? PlateNpc : id == traced ? PlateTracedMonster : PlateMonster;
            var x = (int)(point.Screen.X / scale);
            var y = (int)(point.Screen.Y / scale);
            if (classic)
            {
                DrawClassicPlate(painter, layout, Name(id), x, y, color, id == hovered, monster && ShowsPlateBar(true, id == hovered, id == traced, entity.Attacked) ? entity : null);
            }
            else
            {
                DrawPlate(painter, layout, Name(id), x, y, color, id == hovered, monster && ShowsPlateBar(false, id == hovered, id == traced, entity.Attacked) ? entity : null);
            }
        }
    }

    // A box that would leave the screen is not drawn, as in the original.
    private static void DrawClassicPlate(HudPainter painter, HudLayout layout, string name, int anchorX, int anchorY, Color color, bool hovered, Entity bar)
    {
        var width = painter.Measure(name);
        var lineHeight = Math.Max(16, painter.TextHeight);
        var frameHeight = 4 + lineHeight + 6 + 2;
        var x = anchorX - width / 2 - 9;
        var y = anchorY - ClassicPlateLift;
        if (x < 0 || y < 0 || x + width + 18 > layout.Width || y + frameHeight + PlateBarHeight > layout.Height)
        {
            return;
        }

        painter.Flush();
        HudChatBubbles.DrawFrame(painter, x, y, width, lineHeight, 1, tail: false, selected: hovered);
        painter.Flush();
        painter.Text(name, x + 9, y + 5 + 2, color, false);
        if (bar is not null)
        {
            DrawHealthBar(painter, new Rectangle(anchorX - PlateBarWidth / 2, y + frameHeight - 2, PlateBarWidth, PlateBarHeight), bar.Hp, bar.Spawn.MaxHp);
        }
    }

    // The original's speech bubble frame around the smaller text, the capitals centred inside with room for descenders.
    private static void DrawPlate(HudPainter painter, HudLayout layout, string name, int anchorX, int anchorY, Color color, bool hovered, Entity bar)
    {
        var textWidth = painter.MeasureAt(name, PlateTextScale);
        var (capitals, descent) = painter.LetterHeightsAt(PlateTextScale);
        var pad = (int)MathF.Ceiling(descent) + PlatePadY;
        var content = (int)MathF.Ceiling(capitals) + pad * 2;
        // The frame: a 4-pixel top and 2-pixel bottom border around the content, 9 pixels each side of the text.
        var width = textWidth + 18;
        var height = content + 6;
        var x = anchorX - width / 2;
        var y = anchorY - PlateLift - height;
        if (x < 0 || y < 0 || x + width > layout.Width || y + height + 1 + PlateBarHeight > layout.Height)
        {
            return;
        }

        painter.Flush();
        HudChatBubbles.DrawFrame(painter, x, y, textWidth, content - 6, 1, tail: false, selected: hovered);
        painter.Flush();
        painter.Text(name, x + 9, (int)(y + 4 + pad - HudPainter.CapTop), color, PlateTextScale);
        if (bar is not null)
        {
            DrawHealthBar(painter, new Rectangle(anchorX - PlateBarWidth / 2, y + height + 1, PlateBarWidth, PlateBarHeight), bar.Hp, bar.Spawn.MaxHp);
        }
    }
}
