using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Effects.Particles;
using PristonTale.Engine3D.Rendering;
using PristonTale.Engine3D.Rendering.Lighting;

namespace PristonTale.Gameplay.Effects;

public sealed class LegacyReturnEffect : IDisposable
{
    private const int LegacyTicksPerSecond = 70;
    private const float FixedScale = 1f / 256f;
    private const int FullTurn = 4096;
    private const int ParticleCount = 40;
    private const int RayCount = 45;
    private const int VerticesPerQuad = 6;

    private readonly GraphicsDevice _device;
    private readonly PristonTransientLights _lights;
    private readonly LegacyBillboardAnimation _particleAnimation;
    private readonly LegacyBillboardAnimation _rayAnimation;
    private readonly PristonUnlitEffect _effect;
    private readonly ParticleState[] _particles = new ParticleState[ParticleCount];
    private readonly RayState[] _rays = new RayState[RayCount];
    private readonly VertexPositionColorTexture[][] _particleVertices;
    private readonly VertexPositionColorTexture[][] _rayVertices;
    private readonly int[] _particleVertexCounts;
    private readonly int[] _rayVertexCounts;
    private Vector3 _origin;
    private float _ageSeconds;
    private int _previousTick;
    private int _endTick;
    private bool _secondLight;
    private bool _thirdLight;

    public LegacyReturnEffect(
        GraphicsDevice device,
        LegacyEffectLibrary library,
        PristonTransientLights lights)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(lights);
        _device = device;
        _lights = lights;
        _particleAnimation = library.ReturnParticle;
        _rayAnimation = library.ReturnRay;
        _effect = new PristonUnlitEffect(library.Content);
        _particleVertices = CreateVertexBuffers(_particleAnimation.Textures.Count, ParticleCount);
        _rayVertices = CreateVertexBuffers(_rayAnimation.Textures.Count, RayCount);
        _particleVertexCounts = new int[_particleAnimation.Textures.Count];
        _rayVertexCounts = new int[_rayAnimation.Textures.Count];
    }

    public bool Active { get; private set; }

    public void Start(Vector3 playerPosition)
    {
        if (!IsFinite(playerPosition))
        {
            throw new ArgumentOutOfRangeException(nameof(playerPosition));
        }

        _origin = playerPosition + Vector3.Up * (48f - 9000f * FixedScale);
        _ageSeconds = 0f;
        _previousTick = 0;
        _endTick = 0;
        _secondLight = false;
        _thirdLight = false;
        InitializeParticles();
        InitializeRays();
        _lights.Add(_origin, new Color(180, 180, 180), 200, 1);
        Active = true;
    }

    public void Update(float seconds)
    {
        if (!float.IsFinite(seconds) || seconds < 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(seconds));
        }

        if (!Active)
        {
            return;
        }

        _ageSeconds += seconds;
        var tick = (int)MathF.Floor(_ageSeconds * LegacyTicksPerSecond);
        if (!_secondLight && _previousTick < 100 && tick >= 100)
        {
            _lights.Add(_origin + Vector3.Up * (5000f * FixedScale), new Color(250, 250, 250), 200, 1);
            _secondLight = true;
        }

        if (!_thirdLight && _previousTick < 160 && tick >= 160)
        {
            _lights.Add(_origin + Vector3.Up * (5000f * FixedScale), new Color(250, 250, 250), 170, 1);
            _thirdLight = true;
        }

        _previousTick = tick;
        if (tick > _endTick)
        {
            Active = false;
        }
    }

    public void Draw(Matrix view, Matrix projection, Vector3 cameraPosition)
    {
        if (!Active)
        {
            return;
        }

        Array.Clear(_particleVertexCounts);
        Array.Clear(_rayVertexCounts);
        var right = Vector3.Normalize(new Vector3(view.M11, view.M21, view.M31));
        var up = Vector3.Normalize(new Vector3(view.M12, view.M22, view.M32));
        BuildParticleVertices(right, up);
        BuildRayVertices(cameraPosition, right);

        _effect.View = view;
        _effect.Projection = projection;
        _device.BlendState = BlendState.Additive;
        _device.DepthStencilState = DepthStencilState.DepthRead;
        _device.RasterizerState = RasterizerState.CullNone;
        DrawBuffers(_particleAnimation.Textures, _particleVertices, _particleVertexCounts);
        DrawBuffers(_rayAnimation.Textures, _rayVertices, _rayVertexCounts);
    }

    public void Clear()
    {
        Active = false;
        _ageSeconds = 0f;
        _previousTick = 0;
    }

    private static VertexPositionColorTexture[][] CreateVertexBuffers(int textureCount, int quadCapacity)
    {
        var buffers = new VertexPositionColorTexture[textureCount][];
        for (var index = 0; index < buffers.Length; index++)
        {
            buffers[index] = new VertexPositionColorTexture[quadCapacity * VerticesPerQuad];
        }

        return buffers;
    }

    private void InitializeParticles()
    {
        var exitTicks = (int)MathF.Ceiling(_particleAnimation.Animation.ExitDuration * LegacyTicksPerSecond);
        for (var index = 0; index < ParticleCount; index++)
        {
            var upper = index < ParticleCount / 2;
            var sequenceIndex = upper ? index : index - ParticleCount / 2;
            var angle = Random.Shared.Next(FullTurn);
            var radians = angle * MathHelper.TwoPi / FullTurn;
            var velocity = new Vector3(
                (int)(MathF.Cos(radians) * 70f),
                200f,
                (int)(MathF.Sin(radians) * 70f));
            var life = Random.Shared.Next(20) + (upper ? 60 : 80);
            var delay = upper ? sequenceIndex * 2 : sequenceIndex * 4 + 60;
            var gravity = upper ? -Random.Shared.Next(3) - 6 : Random.Shared.Next(3) + 2;
            var destinationAngle = upper ? FullTurn / 2 : FullTurn / 3;
            _particles[index] = new ParticleState(
                upper ? 10000f : -5000f,
                velocity,
                gravity,
                delay,
                life,
                upper ? 10f : 16f,
                destinationAngle / life);
            _endTick = Math.Max(_endTick, delay + life + exitTicks);
        }
    }

    private void InitializeRays()
    {
        var exitTicks = (int)MathF.Ceiling(_rayAnimation.Animation.ExitDuration * LegacyTicksPerSecond);
        for (var index = 0; index < RayCount; index++)
        {
            var stretch = index < 25;
            var sequenceIndex = stretch ? index : index - 25;
            var angleStep = stretch ? FullTurn / 30 : FullTurn / 20;
            var angle = angleStep * sequenceIndex;
            var radians = angle * MathHelper.TwoPi / FullTurn;
            var direction = new Vector3(MathF.Cos(radians), 0f, MathF.Sin(radians));
            var start = stretch
                ? direction * 3000f
                : direction * 2000f + Vector3.Up * 5000f;
            var destination = stretch
                ? direction * 3100f + Vector3.Up * 12000f
                : direction * 2000f + Vector3.Up * 9000f;
            var delay = sequenceIndex * Random.Shared.Next(32768) % 35 + (stretch ? 100 : 130);
            var velocity = IntegerVelocity(start, destination, 800f);
            var travelTicks = TravelTicks(start, destination, velocity, 800f);
            _rays[index] = new RayState(start, velocity, delay, travelTicks, stretch, stretch ? 900f : 800f);
            _endTick = Math.Max(_endTick, delay + travelTicks + exitTicks);
        }
    }

    private void BuildParticleVertices(Vector3 right, Vector3 up)
    {
        var ticks = _ageSeconds * LegacyTicksPerSecond;
        for (var index = 0; index < _particles.Length; index++)
        {
            ref readonly var particle = ref _particles[index];
            var localTicks = ticks - particle.Delay;
            if (localTicks < 0f)
            {
                continue;
            }

            var age = localTicks / LegacyTicksPerSecond;
            var exitAt = particle.Life / (float)LegacyTicksPerSecond;
            if (age > exitAt + _particleAnimation.Animation.ExitDuration)
            {
                continue;
            }

            var motionTicks = Math.Min((int)MathF.Floor(localTicks) + 1, particle.Life);
            var offset = particle.Velocity * motionTicks;
            offset.Y += particle.InitialY + particle.Gravity * motionTicks * (motionTicks + 1) * 0.5f;
            var accumulatedAngle = particle.AngleStep * motionTicks * (motionTicks + 1) / 2;
            offset = RotateLegacyY(offset, accumulatedAngle);
            var position = _origin + ToWorld(offset);
            var sample = _particleAnimation.Animation.Sample(age, exitAt);
            var color = AnimationColor(sample.Alpha);
            var halfSize = particle.Size * 0.5f;
            AppendQuad(
                _particleVertices[sample.Image],
                ref _particleVertexCounts[sample.Image],
                position,
                right * halfSize,
                up * halfSize,
                color);
        }
    }

    private void BuildRayVertices(Vector3 cameraPosition, Vector3 fallbackRight)
    {
        var ticks = _ageSeconds * LegacyTicksPerSecond;
        for (var index = 0; index < _rays.Length; index++)
        {
            ref readonly var ray = ref _rays[index];
            var localTicks = ticks - ray.Delay;
            if (localTicks < 0f)
            {
                continue;
            }

            var age = localTicks / LegacyTicksPerSecond;
            var exitAt = ray.TravelTicks / (float)LegacyTicksPerSecond;
            if (age > exitAt + _rayAnimation.Animation.ExitDuration)
            {
                continue;
            }

            var motionTicks = Math.Min((int)MathF.Floor(localTicks) + 1, ray.TravelTicks);
            var currentFixed = ray.Start + ray.Velocity * motionTicks;
            var startFixed = ray.Stretch ? ray.Start : currentFixed - ray.Velocity * 30f;
            var start = _origin + ToWorld(startFixed);
            var end = _origin + ToWorld(currentFixed);
            var segment = end - start;
            var toCamera = cameraPosition - (start + end) * 0.5f;
            var side = Vector3.Cross(segment, toCamera);
            if (side.LengthSquared() < 0.000001f)
            {
                side = fallbackRight;
            }
            else
            {
                side.Normalize();
            }

            var sample = _rayAnimation.Animation.Sample(age, exitAt);
            AppendRibbon(
                _rayVertices[sample.Image],
                ref _rayVertexCounts[sample.Image],
                start,
                end,
                side * (ray.Width * FixedScale),
                AnimationColor(sample.Alpha));
        }
    }

    private void DrawBuffers(
        IReadOnlyList<Texture2D> textures,
        VertexPositionColorTexture[][] buffers,
        int[] counts)
    {
        for (var index = 0; index < textures.Count; index++)
        {
            var count = counts[index];
            if (count == 0)
            {
                continue;
            }

            _effect.Texture = textures[index];
            _effect.Apply();
            _device.DrawUserPrimitives(PrimitiveType.TriangleList, buffers[index], 0, count / 3);
        }
    }

    private static Vector3 IntegerVelocity(Vector3 start, Vector3 destination, float speed)
    {
        var direction = Vector3.Normalize(destination - start);
        return new Vector3(
            (int)(direction.X * speed),
            (int)(direction.Y * speed),
            (int)(direction.Z * speed));
    }

    private static int TravelTicks(Vector3 start, Vector3 destination, Vector3 velocity, float speed)
    {
        var current = start;
        for (var tick = 1; tick <= 64; tick++)
        {
            var remaining = Vector3.Distance(current, destination);
            current += velocity;
            if (remaining < speed)
            {
                return tick;
            }
        }

        throw new InvalidOperationException("Return ray did not converge within its bounded legacy lifetime.");
    }

    private static Vector3 RotateLegacyY(Vector3 value, int angle)
    {
        var radians = angle * MathHelper.TwoPi / FullTurn;
        var sine = MathF.Sin(radians);
        var cosine = MathF.Cos(radians);
        return new Vector3(
            value.X * cosine - value.Z * sine,
            value.Y,
            value.X * sine + value.Z * cosine);
    }

    private static Vector3 ToWorld(Vector3 legacyFixed)
    {
        return new Vector3(legacyFixed.X, legacyFixed.Y, -legacyFixed.Z) * FixedScale;
    }

    private static Color AnimationColor(float alpha)
    {
        var value = (byte)Math.Clamp((int)MathF.Round(alpha * 255f), 0, 255);
        return new Color(byte.MaxValue, byte.MaxValue, byte.MaxValue, value);
    }

    private static void AppendQuad(
        VertexPositionColorTexture[] vertices,
        ref int count,
        Vector3 center,
        Vector3 right,
        Vector3 up,
        Color color)
    {
        var topLeft = center - right + up;
        var topRight = center + right + up;
        var bottomRight = center + right - up;
        var bottomLeft = center - right - up;
        vertices[count++] = new VertexPositionColorTexture(topLeft, color, new Vector2(0f, 0f));
        vertices[count++] = new VertexPositionColorTexture(topRight, color, new Vector2(1f, 0f));
        vertices[count++] = new VertexPositionColorTexture(bottomRight, color, new Vector2(1f, 1f));
        vertices[count++] = new VertexPositionColorTexture(topLeft, color, new Vector2(0f, 0f));
        vertices[count++] = new VertexPositionColorTexture(bottomRight, color, new Vector2(1f, 1f));
        vertices[count++] = new VertexPositionColorTexture(bottomLeft, color, new Vector2(0f, 1f));
    }

    private static void AppendRibbon(
        VertexPositionColorTexture[] vertices,
        ref int count,
        Vector3 start,
        Vector3 end,
        Vector3 side,
        Color color)
    {
        var startLeft = start - side;
        var startRight = start + side;
        var endLeft = end - side;
        var endRight = end + side;
        vertices[count++] = new VertexPositionColorTexture(endLeft, color, new Vector2(0f, 0f));
        vertices[count++] = new VertexPositionColorTexture(endRight, color, new Vector2(1f, 0f));
        vertices[count++] = new VertexPositionColorTexture(startRight, color, new Vector2(1f, 1f));
        vertices[count++] = new VertexPositionColorTexture(endLeft, color, new Vector2(0f, 0f));
        vertices[count++] = new VertexPositionColorTexture(startRight, color, new Vector2(1f, 1f));
        vertices[count++] = new VertexPositionColorTexture(startLeft, color, new Vector2(0f, 1f));
    }

    private static bool IsFinite(Vector3 value)
    {
        return float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
    }

    public void Dispose()
    {
        _effect.Dispose();
    }

    private readonly record struct ParticleState(
        float InitialY,
        Vector3 Velocity,
        float Gravity,
        int Delay,
        int Life,
        float Size,
        int AngleStep);

    private readonly record struct RayState(
        Vector3 Start,
        Vector3 Velocity,
        int Delay,
        int TravelTicks,
        bool Stretch,
        float Width);
}
