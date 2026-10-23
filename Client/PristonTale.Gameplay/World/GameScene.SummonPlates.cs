using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace PristonTale.Gameplay.World;

// Name plates over monsters fighting for players: the name in pale green, then a life bar for the player's own and the
// owner's name in parentheses for anyone else's.
public sealed partial class GameScene
{
    private static readonly Color PlateName = new(220, 255, 160);
    private static readonly Color PlateOwner = new(255, 255, 200);
    private const float PlateHeight = 70f;
    private const float PlateRange = 900f;
    private const int BarWidth = 40;
    private const int BarHeight = 4;

    private Texture2D _platePixel;

    public void DrawSummonPlates(SpriteBatch batch, SpriteFont font)
    {
        if (_remote is null || _camera is null || _playerBody is null)
        {
            return;
        }

        var viewport = _graphicsDevice.Viewport;
        foreach (var (entityId, ownerId, plate) in _remote.SummonPlates)
        {
            if (!_remote.TryGetDrawnPosition(entityId, out var position) || plate.Hp <= 0
                || Vector3.DistanceSquared(position, _playerBody.Position) > PlateRange * PlateRange)
            {
                continue;
            }

            var point = viewport.Project(position + Vector3.Up * PlateHeight, _camera.Projection, _camera.View, Matrix.Identity);
            if (point.Z is < 0f or > 1f)
            {
                continue;
            }

            var size = font.MeasureString(plate.Name);
            var origin = new Vector2(MathF.Round(point.X - size.X / 2f), MathF.Round(point.Y - size.Y));
            batch.DrawString(font, plate.Name, origin + Vector2.One, Color.Black);
            batch.DrawString(font, plate.Name, origin, PlateName);
            var below = new Vector2(point.X, origin.Y + size.Y + 1f);
            if (ownerId == _remote.SelfEntityId)
            {
                DrawPlateBar(batch, below, plate.MaxHp > 0 ? Math.Clamp(plate.Hp / (float)plate.MaxHp, 0f, 1f) : 0f);
            }
            else if (_remote.PlayerName(ownerId) is { Length: > 0 } owner)
            {
                var text = $"({owner})";
                var width = font.MeasureString(text).X;
                var at = new Vector2(MathF.Round(below.X - width / 2f), below.Y);
                batch.DrawString(font, text, at + Vector2.One, Color.Black);
                batch.DrawString(font, text, at, PlateOwner);
            }
        }
    }

    private void DrawPlateBar(SpriteBatch batch, Vector2 top, float fraction)
    {
        if (_platePixel is null)
        {
            _platePixel = new Texture2D(_graphicsDevice, 1, 1) { Name = "Summon plate" };
            _platePixel.SetData([Color.White]);
        }

        var bounds = new Rectangle((int)(top.X - BarWidth / 2f), (int)top.Y, BarWidth, BarHeight);
        batch.Draw(_platePixel, new Rectangle(bounds.X - 1, bounds.Y - 1, bounds.Width + 2, bounds.Height + 2), Color.Black);
        batch.Draw(_platePixel, new Rectangle(bounds.X, bounds.Y, (int)(bounds.Width * fraction), bounds.Height), new Color(220, 40, 40));
    }
}
