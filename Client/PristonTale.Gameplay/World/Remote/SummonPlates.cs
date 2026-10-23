using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Engine3D.Rendering;

namespace PristonTale.Gameplay.World.Remote;

// Name plates over monsters fighting for players: the name in pale green, then a life bar for the viewer's own and the
// owner's name in parentheses for anyone else's.
public static class SummonPlates
{
    private static readonly Color PlateName = new(220, 255, 160);
    private static readonly Color PlateOwner = new(255, 255, 200);
    // The plate sits over the model's own height (this one while its geometry is still loading).
    private const float LoadingHeight = 60f;
    private const float PlateRange = 900f;
    private const int BarWidth = 40;
    private const int BarHeight = 4;

    public static void Draw(SpriteBatch batch, SpriteFont font, Texture2D pixel, Viewport viewport, Matrix view, Matrix projection, RemoteEntityManager remote,
        PristonRenderer renderer, uint viewerId, Vector3 viewer)
    {
        foreach (var (entityId, ownerId, plate) in remote.SummonPlates)
        {
            if (!remote.TryGetDrawnPosition(entityId, out var position) || plate.Hp <= 0
                || Vector3.DistanceSquared(position, viewer) > PlateRange * PlateRange)
            {
                continue;
            }

            var height = remote.TryGetBodyActor(entityId, out var actor) && renderer.TryGetActorHeight(actor, out var modelHeight)
                ? modelHeight
                : LoadingHeight;
            var point = viewport.Project(position + Vector3.Up * height, projection, view, Matrix.Identity);
            if (point.Z is < 0f or > 1f)
            {
                continue;
            }

            var size = font.MeasureString(plate.Name);
            var origin = new Vector2(MathF.Round(point.X - size.X / 2f), MathF.Round(point.Y - size.Y));
            batch.DrawString(font, plate.Name, origin + Vector2.One, Color.Black);
            batch.DrawString(font, plate.Name, origin, PlateName);
            var below = new Vector2(point.X, origin.Y + size.Y + 1f);
            if (ownerId == viewerId)
            {
                var bounds = new Rectangle((int)(below.X - BarWidth / 2f), (int)below.Y, BarWidth, BarHeight);
                var fraction = plate.MaxHp > 0 ? Math.Clamp(plate.Hp / (float)plate.MaxHp, 0f, 1f) : 0f;
                batch.Draw(pixel, new Rectangle(bounds.X - 1, bounds.Y - 1, bounds.Width + 2, bounds.Height + 2), Color.Black);
                batch.Draw(pixel, new Rectangle(bounds.X, bounds.Y, (int)(bounds.Width * fraction), bounds.Height), new Color(220, 40, 40));
            }
            else if (remote.PlayerName(ownerId) is { Length: > 0 } owner)
            {
                var text = $"({owner})";
                var width = font.MeasureString(text).X;
                var at = new Vector2(MathF.Round(below.X - width / 2f), below.Y);
                batch.DrawString(font, text, at + Vector2.One, Color.Black);
                batch.DrawString(font, text, at, PlateOwner);
            }
        }
    }
}
