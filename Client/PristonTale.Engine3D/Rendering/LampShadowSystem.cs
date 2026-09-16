using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace PristonTale.Engine3D.Rendering;

internal sealed class LampShadowSystem : IDisposable
{
    // Atlas layout: MaxLamps tiles in a TilesX × TilesY grid. Only the selected lamps are drawn
    // each frame, so GPU cost scales with how many lamps are actually near you, not with MaxLamps.
    public const int MaxLamps = 8;
    private const int TilesX = 4;
    private const int TilesY = 2;
    public int TileSize { get; }
    private int AtlasWidth => TileSize * TilesX;
    private int AtlasHeight => TileSize * TilesY;

    private const float FieldOfView = 2.4f;
    private const float NearPlane = 5f;
    // A lamp is relevant if its lit circle reaches within this distance of the focus — the same
    // ground extent the sun shadow frustum covers (so we only shadow lamps you can actually see).
    private const float VisibleExtent = 1400f;

    private readonly RenderTarget2D _atlas;
    private readonly Matrix[] _view = new Matrix[MaxLamps];
    private readonly Matrix[] _projection = new Matrix[MaxLamps];
    private readonly Vector4[] _posRadius = new Vector4[MaxLamps];
    private readonly Vector4[] _tile = new Vector4[MaxLamps];

    // Scratch for adaptive selection (lamp index + distance), reused each frame.
    private readonly int[] _candIndex = new int[MaxLamps];
    private readonly float[] _candDist = new float[MaxLamps];

    public LampShadowSystem(GraphicsDevice graphicsDevice, int tileSize = 1024)
    {
        TileSize = tileSize;
        _atlas = new RenderTarget2D(graphicsDevice, AtlasWidth, AtlasHeight, false, SurfaceFormat.Single, DepthFormat.Depth24);
    }

    public int Count { get; private set; }

    public Texture2D Atlas => _atlas;
    public Matrix ViewProjection(int i) => _view[i] * _projection[i];
    public Vector4 PositionRadius(int i) => _posRadius[i];
    public Vector4 Tile(int i) => _tile[i];
    public Vector2 TexelSize => new(1f / AtlasWidth, 1f / AtlasHeight);

    public void Clear()
    {
        Count = 0;
    }

    public void Select(IReadOnlyList<PristonPointLight> lights, Vector3 focus, float lampFactor)
    {
        Count = 0;
        if (lampFactor <= 0.05f || lights.Count == 0)
            return;

        // Insertion-sort the relevant lamps (lit circle within VisibleExtent of the focus) by
        // distance, keeping only the nearest MaxLamps. A lamp whose edge is farther than the
        // visible extent lights nothing you can see, so it never gets a tile.
        var kept = 0;
        for (var i = 0; i < lights.Count; i++)
        {
            var radius = MathF.Max(lights[i].Radius, 200f);
            var dist = Vector3.Distance(lights[i].Position, focus);
            if (dist - radius > VisibleExtent)
                continue;

            var slot = kept < MaxLamps ? kept : MaxLamps;
            for (var s = 0; s < kept && s < MaxLamps; s++)
            {
                if (dist < _candDist[s]) { slot = s; break; }
            }
            if (slot >= MaxLamps)
                continue;

            var limit = Math.Min(kept, MaxLamps - 1);
            for (var k = limit; k > slot; k--)
            {
                _candDist[k] = _candDist[k - 1];
                _candIndex[k] = _candIndex[k - 1];
            }
            _candDist[slot] = dist;
            _candIndex[slot] = i;
            if (kept < MaxLamps)
                kept++;
        }

        for (var i = 0; i < kept; i++)
        {
            var lamp = lights[_candIndex[i]];
            var pos = lamp.Position;
            var radius = MathF.Max(lamp.Radius, 200f);
            // Look straight down; up = Forward (any axis not parallel to the view direction).
            // A lamp is a POINT light, so its shadows must radiate from the lamp point — this needs
            // a PERSPECTIVE frustum (an orthographic one would project every caster straight down in
            // parallel, which is wrong for a point light). The hard square-edge cut the perspective
            // frustum can leave on the ground is softened in the shader (border fade), not here.
            _view[i] = Matrix.CreateLookAt(pos, pos + new Vector3(0f, -1f, 0f) * radius, Vector3.Forward);
            _projection[i] = Matrix.CreatePerspectiveFieldOfView(FieldOfView, 1f, NearPlane, radius * 2f);
            _posRadius[i] = new Vector4(pos, lamp.Radius);

            var tx = i % TilesX;
            var ty = i / TilesX;
            _tile[i] = new Vector4(
                (float)tx / TilesX,
                (float)ty / TilesY,
                1f / TilesX,
                1f / TilesY);
        }
        Count = kept;
    }

    public void Render(GraphicsDevice graphicsDevice, Action<Matrix, Matrix> drawCasters)
    {
        if (Count == 0)
            return;

        var previousViewport = graphicsDevice.Viewport;
        graphicsDevice.SetRenderTarget(_atlas);
        graphicsDevice.Clear(Color.White);
        graphicsDevice.DepthStencilState = DepthStencilState.Default;
        graphicsDevice.BlendState = BlendState.Opaque;
        graphicsDevice.SamplerStates[0] = SamplerState.LinearWrap;

        for (var i = 0; i < Count; i++)
        {
            var tx = i % TilesX;
            var ty = i / TilesX;
            graphicsDevice.Viewport = new Viewport(tx * TileSize, ty * TileSize, TileSize, TileSize);
            drawCasters(_view[i], _projection[i]);
        }

        graphicsDevice.SetRenderTarget(null);
        graphicsDevice.Viewport = previousViewport;
    }

    public void Dispose()
    {
        _atlas?.Dispose();
    }
}
