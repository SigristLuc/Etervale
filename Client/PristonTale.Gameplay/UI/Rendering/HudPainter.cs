using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace PristonTale.Gameplay.UI.Rendering;

// Draws original sprites in 800x600 logical units at an integer scale with point sampling.
// Text is queued and drawn in a linear-filtered pass because the UI font is a scaled vector bake.
public sealed class HudPainter : IDisposable
{
    // The original used 14-16 px Comic Sans through GDI TextOut, whose y is the top of a cell with ~2 px leading.
    public const float TextScale = 0.7f;
    public const int LineHeight = 16;
    private const float CapTop = 2f;

    private readonly record struct TextRun(string Value, Vector2 Position, Color Color, bool Shadow);

    private readonly SpriteBatch _batch;
    private readonly SpriteFont _font;
    private readonly HudSprites _sprites;
    private readonly Texture2D _white;
    private readonly List<TextRun> _text = new(128);
    private readonly float _textShift;
    private Matrix _transform = Matrix.Identity;
    private bool _active;

    public HudPainter(SpriteBatch batch, SpriteFont font, HudSprites sprites)
    {
        _batch = batch;
        _font = font;
        _sprites = sprites;
        _white = new Texture2D(batch.GraphicsDevice, 1, 1);
        _white.SetData([Color.White]);
        _textShift = font.GetGlyphs().TryGetValue('M', out var glyph) ? MathF.Round(CapTop - glyph.Cropping.Y * TextScale) : 0;
    }

    public HudSprites Sprites => _sprites;

    public void Begin(HudLayout layout)
    {
        _text.Clear();
        _transform = Matrix.CreateScale(Math.Max(1, layout.Scale));
        BeginSprites();
    }

    public void End()
    {
        if (!_active)
        {
            return;
        }

        Flush();
        _batch.End();
        _active = false;
    }

    public void Flush()
    {
        if (_text.Count == 0 || !_active)
        {
            return;
        }

        _batch.End();
        _batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp, DepthStencilState.None,
            RasterizerState.CullNone, transformMatrix: _transform);
        foreach (var run in _text)
        {
            if (run.Shadow)
            {
                _batch.DrawString(_font, run.Value, run.Position + Vector2.One, Color.Black, 0f, Vector2.Zero, TextScale, SpriteEffects.None, 0f);
            }

            _batch.DrawString(_font, run.Value, run.Position, run.Color, 0f, Vector2.Zero, TextScale, SpriteEffects.None, 0f);
        }

        _text.Clear();
        _batch.End();
        BeginSprites();
    }

    public Texture2D Texture(string path) => _sprites.Get(path);

    public void Sprite(string path, int x, int y, Color? tint = null)
    {
        var texture = _sprites.Get(path);
        if (texture is not null)
        {
            _batch.Draw(texture, new Vector2(x, y), tint ?? Color.White);
        }
    }

    public void Sprite(string path, int x, int y, Rectangle source, Color? tint = null)
    {
        var texture = _sprites.Get(path);
        if (texture is null)
        {
            return;
        }

        source = Rectangle.Intersect(source, texture.Bounds);
        if (source.Width > 0 && source.Height > 0)
        {
            _batch.Draw(texture, new Vector2(x, y), source, tint ?? Color.White);
        }
    }

    public void Stretch(string path, Rectangle destination, Rectangle? source = null, Color? tint = null)
    {
        var texture = _sprites.Get(path);
        if (texture is not null && destination.Width > 0 && destination.Height > 0)
        {
            _batch.Draw(texture, destination, source, tint ?? Color.White);
        }
    }

    public void Rotated(string path, Vector2 center, float rotation, Color? tint = null)
    {
        var texture = _sprites.Get(path);
        if (texture is not null)
        {
            var origin = new Vector2(texture.Width / 2f, texture.Height / 2f);
            _batch.Draw(texture, center, null, tint ?? Color.White, rotation, origin, 0.5f, SpriteEffects.None, 0f);
        }
    }

    public void Image(Texture2D texture, Rectangle destination, Color? tint = null)
    {
        if (texture is not null && destination.Width > 0 && destination.Height > 0)
        {
            _batch.Draw(texture, destination, tint ?? Color.White);
        }
    }

    public void Fill(Rectangle bounds, Color premultiplied)
    {
        if (bounds.Width > 0 && bounds.Height > 0)
        {
            _batch.Draw(_white, bounds, premultiplied);
        }
    }

    // Original colour box fills are straight RGBA.
    public void ColorBox(Rectangle bounds, int r, int g, int b, int a) => Fill(bounds, Color.FromNonPremultiplied(r, g, b, a));

    public void Text(string value, int x, int y, Color? color = null, bool shadow = true)
    {
        if (!string.IsNullOrEmpty(value))
        {
            _text.Add(new TextRun(value, new Vector2(x, y + _textShift), color ?? Color.White, shadow));
        }
    }

    public void TextCentered(string value, int left, int right, int y, Color? color = null, bool shadow = true)
    {
        if (!string.IsNullOrEmpty(value))
        {
            Text(value, left + (right - left - Measure(value)) / 2, y, color, shadow);
        }
    }

    public int Measure(string value) => string.IsNullOrEmpty(value) ? 0 : (int)MathF.Ceiling(_font.MeasureString(value).X * TextScale);

    public string Fit(string value, int width)
    {
        if (Measure(value) <= width)
        {
            return value;
        }

        for (var length = value.Length - 1; length > 0; length--)
        {
            var candidate = value[..length] + "...";
            if (Measure(candidate) <= width)
            {
                return candidate;
            }
        }

        return string.Empty;
    }

    // Item info box: nine 16x16 tiles repeated over a box measured in whole tiles.
    public void InfoBox(Rectangle bounds)
    {
        var columns = Math.Max(2, (bounds.Width + 15) / 16);
        var rows = Math.Max(2, (bounds.Height + 15) / 16);
        for (var column = 0; column < columns; column++)
        {
            for (var row = 0; row < rows; row++)
            {
                var horizontal = column == 0 ? 0 : column == columns - 1 ? 2 : 1;
                var vertical = row == 0 ? 0 : row == rows - 1 ? 2 : 1;
                Sprite(HudArt.InfoBox[vertical * 3 + horizontal], bounds.X + column * 16, bounds.Y + row * 16);
            }
        }
    }

    public static Rectangle InfoBoxBounds(int x, int y, int width, int height) =>
        new(x, y, Math.Max(2, (width + 15) / 16) * 16, Math.Max(2, (height + 15) / 16) * 16);

    // Help box: 64-pixel corners/sides, 32-pixel repeated edges and centre, Box_Line separator.
    public void HelpBox(int x, int y, int sizeW, int sizeH, bool separator = true)
    {
        sizeW = Math.Max(2, sizeW);
        sizeH = Math.Max(2, sizeH);
        for (var j = 0; j < sizeW - 2; j++)
        {
            for (var k = 0; k < sizeH - 2; k++)
            {
                Sprite(HudArt.HelpCenter, x + 64 + 32 * j, y + 32 + 32 * k);
            }
        }

        var right = x + (sizeW - 1) * 32 + 32;
        var bottom = y + (sizeH - 1) * 32;
        Sprite(HudArt.HelpTopLeft, x, y);
        Sprite(HudArt.HelpTopRight, right, y);
        Sprite(HudArt.HelpBottomLeft, x, bottom);
        Sprite(HudArt.HelpBottomRight, right, bottom);
        for (var j = 1; j < sizeW - 1; j++)
        {
            Sprite(HudArt.HelpTop, x + j * 32 + 32, y);
            Sprite(HudArt.HelpBottom, x + j * 32 + 32, bottom);
        }

        for (var k = 1; k < sizeH - 1; k++)
        {
            Sprite(HudArt.HelpLeft, x, y + k * 32);
            Sprite(HudArt.HelpRight, right, y + k * 32);
        }

        if (separator)
        {
            Stretch(HudArt.HelpLine, new Rectangle(x + 8, y + 48, sizeW * 32 + 50, 10));
        }
    }

    public static Rectangle HelpBoxBounds(int x, int y, int sizeW, int sizeH) => new(x, y, Math.Max(2, sizeW) * 32 + 64, Math.Max(2, sizeH) * 32 + 32);

    private void BeginSprites()
    {
        _batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None,
            RasterizerState.CullNone, transformMatrix: _transform);
        _active = true;
    }

    public void Dispose() => _white.Dispose();
}
