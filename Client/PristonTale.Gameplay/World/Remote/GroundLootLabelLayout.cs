using Microsoft.Xna.Framework;

namespace PristonTale.Gameplay.World.Remote;

public sealed class GroundLootLabelLayout
{
    private const int CellWidth = 8;
    private bool[] _occupied = [];
    private Rectangle _viewport;
    private int _columns;
    private int _rows;
    private int _lineHeight;

    public void Begin(Rectangle viewport, int lineHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(lineHeight);
        _viewport = viewport;
        _lineHeight = lineHeight;
        _columns = Math.Max(0, viewport.Width / CellWidth);
        _rows = Math.Max(0, viewport.Height / lineHeight);
        int capacity = _columns * _rows;
        if (_occupied.Length < capacity)
        {
            _occupied = new bool[capacity];
        }
        else
        {
            Array.Clear(_occupied, 0, capacity);
        }
    }

    public bool TryPlace(Vector2 anchor, int width, out Rectangle bounds)
    {
        bounds = Rectangle.Empty;
        int cells = (width + CellWidth - 1) / CellWidth;
        if (cells <= 0 || cells > _columns || _rows == 0)
        {
            return false;
        }

        int column = Math.Clamp((int)(anchor.X - _viewport.X - width * 0.5f) / CellWidth, 0, _columns - cells);
        int startRow = Math.Clamp((int)(anchor.Y - _viewport.Y) / _lineHeight, 0, _rows - 1);
        for (int distance = 0; distance < _rows; distance++)
        {
            int row = startRow - distance;
            if (row >= 0 && TryOccupy(row, column, cells, out bounds))
            {
                return true;
            }

            row = startRow + distance;
            if (distance > 0 && row < _rows && TryOccupy(row, column, cells, out bounds))
            {
                return true;
            }
        }

        return false;
    }

    private bool TryOccupy(int row, int column, int cells, out Rectangle bounds)
    {
        bounds = Rectangle.Empty;
        int offset = row * _columns + column;
        for (int cell = 0; cell < cells; cell++)
        {
            if (_occupied[offset + cell])
            {
                return false;
            }
        }

        Array.Fill(_occupied, true, offset, cells);
        bounds = new Rectangle(_viewport.X + column * CellWidth, _viewport.Y + row * _lineHeight, cells * CellWidth, _lineHeight);
        return true;
    }
}
