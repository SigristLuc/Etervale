using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Gameplay.World.Remote;

namespace PristonTale.Gameplay.World;

// The name plates of summons and crystal pets over the scene.
public sealed partial class GameScene
{
    private Texture2D _platePixel;

    public void DrawSummonPlates(SpriteBatch batch, SpriteFont font)
    {
        if (_remote is null || _camera is null || _playerBody is null)
        {
            return;
        }

        if (_platePixel is null)
        {
            _platePixel = new Texture2D(_graphicsDevice, 1, 1) { Name = "Summon plate" };
            _platePixel.SetData([Color.White]);
        }

        SummonPlates.Draw(batch, font, _platePixel, _graphicsDevice.Viewport, _camera.View, _camera.Projection, _remote, _remote.SelfEntityId,
            _playerBody.Position);
    }
}
