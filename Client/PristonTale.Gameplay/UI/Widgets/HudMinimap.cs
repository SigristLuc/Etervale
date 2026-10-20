using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Formats;
using PristonTale.Gameplay.UI.Rendering;

namespace PristonTale.Gameplay.UI.Widgets;

public readonly record struct HudWorldView(
    string FieldId,
    bool Underground,
    PristonIntRectangle MapRectangle,
    Vector3 PlayerPosition,
    Vector3 PlayerForward,
    float TimeOfDay,
    bool Running);

// Original field minimap: 128x128 box at (656,426), right and bottom anchored, black 50% backdrop, the
// field\map\<id>.tga region around the player, NPC dots, the heading arrow, MapBox.tga and the field title.
public sealed class HudMinimap
{
    private const float FixedScale = 256f;
    private string _field;

    public static Rectangle Box(HudLayout layout) => layout.BottomRight(new Rectangle(656, 426, 128, 128));

    // FieldMapDrawSize 16 (dungeon) or 24 (other fields) times 64 world units from the centre to the box edge.
    public static float ViewSize(bool underground) => (underground ? 16 : 24) * 64f;

    // Original stage coordinates are fixed point with Z mirrored relative to the engine (stage vertex Z = -z).
    public static Vector2 MapPoint(PristonIntRectangle rectangle, Vector3 position) =>
        new(position.X - rectangle.Left / FixedScale, rectangle.Bottom / FixedScale + position.Z);

    // Returns the texture source (normalized) and destination (relative to the 126x126 interior) after the original clipping.
    public static bool MapRegion(PristonIntRectangle rectangle, Vector3 position, bool underground, out Vector4 source, out Vector4 destination)
    {
        source = default;
        destination = default;
        var width = (rectangle.Right - rectangle.Left) / FixedScale;
        var height = (rectangle.Bottom - rectangle.Top) / FixedScale;
        if (width <= 0 || height <= 0)
        {
            return false;
        }

        var size = ViewSize(underground);
        var point = MapPoint(rectangle, position);
        var fpx = size / width;
        var fpy = size / height;
        var fx = point.X / width - fpx;
        var fw = point.X / width + fpx;
        var fy = point.Y / height - fpy;
        var fh = point.Y / height + fpy;
        if (fx < 0 && fw < 0 || fx > 1 && fw > 1 || fy < 0 && fh < 0 || fy > 1 && fh > 1)
        {
            return false;
        }

        float dx = 0;
        float dy = 0;
        float dw = 126;
        float dh = 126;
        if (fx < 0)
        {
            var clip = -fx * (64 / fpx);
            dx += clip;
            dw -= clip;
            fx = 0;
        }

        if (fw > 1)
        {
            dw -= (fw - 1) * (64 / fpx);
            fw = 1;
        }

        if (fy < 0)
        {
            var clip = -fy * (64 / fpy);
            dy += clip;
            dh -= clip;
            fy = 0;
        }

        if (fh > 1)
        {
            dh -= (fh - 1) * (64 / fpy);
            fh = 1;
        }

        source = new Vector4(fx, fy, fw - fx, fh - fy);
        destination = new Vector4(dx, dy, dw, dh);
        return dw > 0 && dh > 0;
    }

    // The minimap arrow bitmap points down and is turned half a circle before applying the heading.
    public static float ArrowRotation(Vector3 forward) =>
        forward.X == 0 && forward.Z == 0 ? MathF.PI : MathF.Atan2(forward.X, -forward.Z) + MathF.PI;

    public void Update(HudSprites sprites, string fieldId)
    {
        if (string.Equals(fieldId, _field, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (_field is not null)
        {
            sprites.Release(HudArt.FieldMap(_field));
            sprites.Release(HudArt.FieldTitle(_field));
        }

        _field = fieldId;
        if (fieldId is not null)
        {
            sprites.Request(HudArt.FieldMap(fieldId));
            sprites.Request(HudArt.FieldTitle(fieldId));
        }
    }

    public void Draw(HudPainter painter, HudLayout layout, HudWorldView world, IEnumerable<Vector3> npcs)
    {
        var box = Box(layout);
        painter.ColorBox(box, 0, 0, 0, 128);
        if (world.FieldId is not null)
        {
            var map = painter.Texture(HudArt.FieldMap(world.FieldId));
            if (map is not null && MapRegion(world.MapRectangle, world.PlayerPosition, world.Underground, out var source, out var destination))
            {
                var from = new Rectangle((int)(source.X * map.Width), (int)(source.Y * map.Height),
                    Math.Max(1, (int)(source.Z * map.Width)), Math.Max(1, (int)(source.W * map.Height)));
                var to = new Rectangle(box.X + 1 + (int)destination.X, box.Y + 1 + (int)destination.Y, (int)destination.Z, (int)destination.W);
                painter.Stretch(HudArt.FieldMap(world.FieldId), to, from);
            }
        }

        var size = ViewSize(world.Underground);
        var center = new Vector2(box.X + 64, box.Y + 64);
        foreach (var npc in npcs)
        {
            var dx = npc.X - world.PlayerPosition.X;
            var dz = npc.Z - world.PlayerPosition.Z;
            if (MathF.Abs(dx) < size && MathF.Abs(dz) < size)
            {
                var x = center.X + dx * 64 / size;
                var y = center.Y + dz * 64 / size;
                painter.Sprite(HudArt.MapNpc, (int)x - 3, (int)y - 3);
            }
        }

        painter.Rotated(HudArt.MapArrow, center, ArrowRotation(world.PlayerForward));
        painter.Sprite(HudArt.MapBox, box.X, box.Y);
        if (world.FieldId is not null)
        {
            painter.Stretch(HudArt.FieldTitle(world.FieldId), new Rectangle(box.X, box.Y - 16, 128, 16));
        }
    }
}
