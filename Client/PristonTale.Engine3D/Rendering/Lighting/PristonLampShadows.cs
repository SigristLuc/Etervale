using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Engine3D.Diagnostics;

namespace PristonTale.Engine3D.Rendering.Lighting;

// Omnidirectional shadows that actors cast from the stage lamps nearest the camera focus: each lamp keeps a row of an
// R32F atlas with its six cube faces (90-degree views along the axes, Shaders/Lighting/Shading.hlsli LampShadow),
// holding the distance to the lamp over its radius. A lamp keeps its row while it stays among the nearest, so its faces are redrawn only when
// it is new and then in turn with the others (one lamp every other frame), which keeps moving actors' shadows current at
// a fraction of the cost.
internal sealed class PristonLampShadows : IDisposable
{
    public const int MaxLamps = 4;
    public const int Faces = 6;
    private const float NearPlane = 10f;

    private static readonly Vector3[] FaceForward =
    [
        Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ
    ];

    private static readonly Vector3[] FaceUp =
    [
        Vector3.UnitY, Vector3.UnitY, -Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitY, Vector3.UnitY
    ];

    private readonly GraphicsDevice _device;
    private readonly IClientLog _log;
    private readonly Vector3[] _rowPositions = new Vector3[MaxLamps];
    private readonly float[] _rowRadii = new float[MaxLamps];
    private readonly bool[] _rowUsed = new bool[MaxLamps];
    private readonly bool[] _rowClaimed = new bool[MaxLamps];
    private RenderTarget2D _atlas;
    private int _nextRefresh;
    private uint _frame;

    public PristonLampShadows(GraphicsDevice device, IClientLog log)
    {
        _device = device;
        _log = log;
    }

    public int Lamps { get; private set; }
    public int FaceSize { get; private set; }
    public RenderTarget2D Atlas => _atlas;

    // Rows of the frame's local lights (-1: no shadow), parallel to the light list.
    public int[] Rows { get; } = new int[PristonShaderLibrary.MaxLocalLightCount];

    // Without a device (tests) only the row assignment works.
    public void Configure(int lamps, int faceSize)
    {
        lamps = Math.Clamp(lamps, 0, MaxLamps);
        if (lamps == Lamps && faceSize == FaceSize && (_atlas is not null || lamps == 0))
        {
            return;
        }

        _atlas?.Dispose();
        _atlas = null;
        Lamps = lamps;
        FaceSize = faceSize;
        Array.Clear(_rowUsed);
        if (lamps == 0)
        {
            return;
        }

        if (_device is null)
        {
            return;
        }

        try
        {
            _atlas = new RenderTarget2D(_device, faceSize * Faces, faceSize * lamps, false, SurfaceFormat.Single, DepthFormat.Depth24, 0,
                RenderTargetUsage.PreserveContents);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or OutOfMemoryException)
        {
            _log.Warn($"[Rendering] Lamp shadow atlas {faceSize * Faces}x{faceSize * lamps} allocation failed; lamp shadows off: {exception.Message}");
            Lamps = 0;
        }
    }

    // Gives the nearest lamps (lights from firstLamp on, nearest first) a row, keeping the rows of lamps that stay.
    // Returns the rows to draw this frame as a bit mask: new lamps, and one of the others in turn.
    public int Assign(IReadOnlyList<PristonPointLight> lights, int firstLamp)
    {
        Array.Fill(Rows, -1);
        if (Lamps == 0)
        {
            return 0;
        }

        Array.Clear(_rowClaimed);
        var lampCount = Math.Min(Lamps, Math.Max(lights.Count - firstLamp, 0));
        var redraw = 0;
        for (var i = 0; i < lampCount; i++)
        {
            var light = lights[firstLamp + i];
            var row = FindRow(light);
            if (row < 0)
            {
                row = FreeRow();
                _rowPositions[row] = light.Position;
                _rowRadii[row] = light.Radius;
                _rowUsed[row] = true;
                redraw |= 1 << row;
            }

            _rowClaimed[row] = true;
            Rows[firstLamp + i] = row;
        }

        for (var row = 0; row < Lamps; row++)
        {
            _rowUsed[row] = _rowClaimed[row];
        }

        // The rows in turn, one every other frame.
        _frame++;
        for (var step = 0; step < Lamps && _frame % 2 == 0; step++)
        {
            var row = (_nextRefresh + step) % Lamps;
            if (_rowClaimed[row] && (redraw & (1 << row)) == 0)
            {
                redraw |= 1 << row;
                _nextRefresh = row + 1;
                break;
            }
        }

        return redraw;
    }

    private int FindRow(PristonPointLight light)
    {
        for (var row = 0; row < Lamps; row++)
        {
            if (_rowUsed[row] && !_rowClaimed[row] && _rowPositions[row] == light.Position && _rowRadii[row] == light.Radius)
            {
                return row;
            }
        }

        return -1;
    }

    private int FreeRow()
    {
        for (var row = 0; row < Lamps; row++)
        {
            if (!_rowClaimed[row] && !_rowUsed[row])
            {
                return row;
            }
        }

        for (var row = 0; row < Lamps; row++)
        {
            if (!_rowClaimed[row])
            {
                return row;
            }
        }

        throw new InvalidOperationException("More lamps than shadow rows.");
    }

    public Vector3 RowPosition(int row) => _rowPositions[row];

    public float RowRadius(int row) => _rowRadii[row];

    public Rectangle FaceRectangle(int row, int face) => new(face * FaceSize, row * FaceSize, FaceSize, FaceSize);

    // The face's view-projection, as LampShadow in Shaders/Lighting/Shading.hlsli inverts it.
    public static Matrix FaceViewProjection(Vector3 position, float radius, int face)
    {
        var view = Matrix.CreateLookAt(position, position + FaceForward[face], FaceUp[face]);
        var projection = Matrix.CreatePerspectiveFieldOfView(MathHelper.PiOver2, 1f, NearPlane, MathF.Max(radius, NearPlane * 2f));
        return view * projection;
    }

    public void Dispose() => _atlas?.Dispose();
}
