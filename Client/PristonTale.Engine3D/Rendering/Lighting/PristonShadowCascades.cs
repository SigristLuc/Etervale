using Microsoft.Xna.Framework;

namespace PristonTale.Engine3D.Rendering.Lighting;

// Stabilized cascaded shadow maps of the sun (or the moon): the view frustum split by view depth (the practical
// scheme: a blend of logarithmic and uniform splits), each slice enclosed in its bounding sphere, so a cascade's size
// does not change as the camera turns, and its light-space position snapped to whole texels, so shadow edges do not
// crawl as the camera moves. The cascades share one atlas (a 2x2 grid of tiles from two cascades up). Far cascades
// refresh less often than near ones (UpdateMask).
internal sealed class PristonShadowCascades
{
    public const int MaxCascades = 4;
    // View depth covered by the shadows and the share of logarithmic splits.
    public const float ShadowDistance = 9000f;
    public const float NearDepth = 60f;
    private const float SplitLambda = 0.8f;
    // Casters up to this far beyond a cascade's sphere toward the light still cast into it (towers, cliffs).
    private const float CasterReach = 4000f;

    private readonly Matrix[] _viewProjections = new Matrix[MaxCascades];
    private readonly Vector4[] _atlasRects = new Vector4[MaxCascades];
    private readonly float[] _texelWorldSizes = new float[MaxCascades];
    private readonly float[] _depthRanges = new float[MaxCascades];
    private readonly float[] _splits = new float[MaxCascades];
    private uint _refreshCount;

    public int Count { get; private set; } = MaxCascades;
    public int AtlasSize { get; private set; }
    public int TileSize => Count == 1 ? AtlasSize : AtlasSize / 2;
    public IReadOnlyList<Matrix> ViewProjections => _viewProjections;
    public IReadOnlyList<Vector4> AtlasRects => _atlasRects;
    public IReadOnlyList<float> TexelWorldSizes => _texelWorldSizes;
    public IReadOnlyList<float> DepthRanges => _depthRanges;

    // Far view depth of each cascade.
    public IReadOnlyList<float> Splits => _splits;

    public void Configure(int count, int atlasSize)
    {
        Count = Math.Clamp(count, 1, MaxCascades);
        AtlasSize = atlasSize;
        for (var cascade = 0; cascade < MaxCascades; cascade++)
        {
            _atlasRects[cascade] = Count == 1
                ? new Vector4(0f, 0f, 1f, 1f)
                : new Vector4(cascade % 2 * 0.5f, cascade / 2 * 0.5f, 0.5f, 0.5f);
        }

        for (var cascade = 0; cascade < Count; cascade++)
        {
            _splits[cascade] = SplitDepth(cascade + 1, Count);
        }

        for (var cascade = Count; cascade < MaxCascades; cascade++)
        {
            _splits[cascade] = _splits[Count - 1];
        }

        _refreshCount = 0;
    }

    // Practical split scheme (Zhang et al. 2006): lambda * logarithmic + (1 - lambda) * uniform.
    public static float SplitDepth(int index, int count)
    {
        var t = (float)index / count;
        var logarithmic = NearDepth * MathF.Pow(ShadowDistance / NearDepth, t);
        var uniform = NearDepth + (ShadowDistance - NearDepth) * t;
        return MathHelper.Lerp(uniform, logarithmic, SplitLambda);
    }

    // The cascades this refresh updates: the first two every time, the third every second and the fourth every
    // fourth refresh. A full refresh (teleports, a new light direction) updates all.
    public int UpdateMask(bool full)
    {
        var refresh = _refreshCount++;
        if (full)
        {
            return (1 << Count) - 1;
        }

        var mask = 0b0011;
        if (refresh % 2 == 0)
        {
            mask |= 0b0100;
        }

        if (refresh % 4 == 0)
        {
            mask |= 0b1000;
        }

        return mask & ((1 << Count) - 1);
    }

    // Fits a cascade to the camera slice [near split, far split] for a light travelling along lightDirection.
    public void Fit(int cascade, Matrix cameraView, Matrix cameraProjection, Vector3 lightDirection)
    {
        var near = cascade == 0 ? NearDepth * 0.5f : _splits[cascade - 1];
        var far = _splits[cascade];
        var inverseView = Matrix.Invert(cameraView);
        var eye = inverseView.Translation;
        var forward = Vector3.Normalize(-new Vector3(inverseView.M31, inverseView.M32, inverseView.M33));
        var tanX = 1f / cameraProjection.M11;
        var tanY = 1f / cameraProjection.M22;
        var k2 = tanX * tanX + tanY * tanY;
        // The smallest sphere around the slice, centred on the view axis.
        var t = MathF.Min((near + far) * 0.5f * (1f + k2), far);
        var radius = MathF.Sqrt(MathF.Max((far - t) * (far - t) + far * far * k2, (t - near) * (t - near) + near * near * k2));
        // Quantized so the cascade's texel size never changes between frames.
        radius = MathF.Ceiling(radius / 16f) * 16f;
        var centre = eye + forward * t;
        var direction = Vector3.Normalize(lightDirection);
        var up = MathF.Abs(Vector3.Dot(direction, Vector3.Up)) > 0.95f ? Vector3.Forward : Vector3.Up;
        var depthRange = 2f * radius + CasterReach;
        var view = Matrix.CreateLookAt(centre - direction * (radius + CasterReach), centre, up);
        var projection = Matrix.CreateOrthographic(2f * radius, 2f * radius, 0f, depthRange);
        projection = StabilizeProjection(view, projection, TileSize);
        _viewProjections[cascade] = view * projection;
        _texelWorldSizes[cascade] = 2f * radius / TileSize;
        _depthRanges[cascade] = depthRange;
    }

    // Snaps a fixed world anchor to whole texels: the moving frustum centre always maps to clip zero and cannot reveal
    // the sub-texel motion to cancel.
    internal static Matrix StabilizeProjection(Matrix view, Matrix projection, int mapSize)
    {
        var originClip = Vector4.Transform(Vector4.UnitW, view * projection);
        var texelX = (originClip.X * 0.5f + 0.5f) * mapSize;
        var texelY = (originClip.Y * 0.5f + 0.5f) * mapSize;
        var clipPerTexel = 2f / mapSize;
        projection.M41 += (MathF.Round(texelX) - texelX) * clipPerTexel;
        projection.M42 += (MathF.Round(texelY) - texelY) * clipPerTexel;
        return projection;
    }

    // The pixel rectangle of a cascade's tile in the atlas.
    public Rectangle TileRectangle(int cascade)
    {
        var rect = _atlasRects[cascade];
        return new Rectangle((int)(rect.X * AtlasSize), (int)(rect.Y * AtlasSize), TileSize, TileSize);
    }
}
