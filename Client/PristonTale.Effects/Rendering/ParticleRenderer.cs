using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Engine3D.Rendering;
using PristonTale.Effects.Effects;
using PristonTale.Effects.Particles;

namespace PristonTale.Effects.Rendering;

public sealed class ParticleRenderer : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly PristonUnlitEffect _effect;

    // Reusable batch buffers keyed by (texture, blend). Each batch grows its vertex array as needed and
    // is cleared (count reset to 0) — not freed — at the start of every Draw, so no per-frame alloc.
    private readonly Dictionary<BatchKey, Batch> _batches = new();
    private readonly List<DrawCommand> _commands = new();
    private readonly ParticleBlendStates _blendStates = new();

    public ParticleRenderer(GraphicsDevice device, ContentManager content)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _effect = new PristonUnlitEffect(content);
    }

    public bool DisplayOutput
    {
        get => _effect.DisplayOutput;
        set => _effect.DisplayOutput = value;
    }

    public void Draw(IReadOnlyList<EffectInstance> instances, Matrix view, Matrix projection, Vector3 cameraPosition)
    {
        _ = cameraPosition;
        BeginBatches();

        var right = Vector3.Normalize(new Vector3(view.M11, view.M21, view.M31));
        var up = Vector3.Normalize(new Vector3(view.M12, view.M22, view.M32));

        for (var ii = 0; ii < instances.Count; ii++)
        {
            var emitters = instances[ii].Emitters;
            for (var ei = 0; ei < emitters.Count; ei++)
            {
                var v = emitters[ei].GetRenderView();
                if (v.Texture is null || v.Particles.Length == 0)
                    continue;
                AppendEmitter(v, right, up);
            }
        }

        if (_commands.Count == 0)
            return;

        FlushBatches(view, projection);
    }

    private void BeginBatches()
    {
        _commands.Clear();
        foreach (var batch in _batches.Values)
            batch.Count = 0;
    }

    private void AppendEmitter(in EmitterRenderView view, Vector3 right, Vector3 up)
    {
        if (view.Definition.LegacySequence?.GroundPlane == true)
        {
            right = Vector3.Right;
            up = Vector3.Forward;
        }

        var frames = view.Definition.Frames;
        var frameCount = frames.Count;
        var blend = view.Blend;
        var particles = view.Particles;

        for (var i = 0; i < particles.Length; i++)
        {
            ref readonly var p = ref particles[i];
            var frameIndex = p.Frame;
            if (frameIndex < 0)
                frameIndex = 0;
            else if (frameIndex >= frameCount)
                frameIndex = frameCount - 1;

            var texture = frames[frameIndex];
            var key = new BatchKey(texture, blend);
            var batch = GetBatch(key);
            int start = batch.Count;

            var cos = (float)Math.Cos(p.Rotation);
            var sin = (float)Math.Sin(p.Rotation);
            var rRight = right * (cos * p.Size) + up * (sin * p.Size);
            float height = view.Definition.LegacySequence is null ? p.Size : p.Height;
            var rUp = up * (cos * height) - right * (sin * height);
            var pos = p.Position;
            var col = p.Color;

            var tl = new ParticleVertex(pos - rRight + rUp, col, new Vector2(0f, 0f));
            var tr = new ParticleVertex(pos + rRight + rUp, col, new Vector2(1f, 0f));
            var br = new ParticleVertex(pos + rRight - rUp, col, new Vector2(1f, 1f));
            var bl = new ParticleVertex(pos - rRight - rUp, col, new Vector2(0f, 1f));

            batch.Add(tl, tr, br);
            batch.Add(tl, br, bl);
            if (_commands.Count > 0 && _commands[^1].Key == key)
            {
                var previous = _commands[^1];
                _commands[^1] = previous with { Count = previous.Count + 6 };
            }
            else
            {
                _commands.Add(new DrawCommand(key, start, 6));
            }
        }
    }

    private Batch GetBatch(BatchKey key)
    {
        if (!_batches.TryGetValue(key, out var batch))
        {
            batch = new Batch();
            _batches[key] = batch;
        }
        return batch;
    }

    private void FlushBatches(Matrix view, Matrix projection)
    {
        _effect.View = view;
        _effect.Projection = projection;

        // Shared render state for all particle batches: occluded by prior scene depth (DepthRead =
        // test on, write off), no culling so both quad faces draw, linear-clamp sampling.
        _device.DepthStencilState = DepthStencilState.DepthRead;
        _device.RasterizerState = RasterizerState.CullNone;

        for (var k = 0; k < _commands.Count; k++)
        {
            var command = _commands[k];
            var key = command.Key;
            var batch = _batches[key];
            if (batch.Count == 0)
                continue;

            _device.BlendState = _blendStates.Get(key.Blend);
            _effect.Texture = key.Texture;

            var primitiveCount = command.Count / 3;
            _effect.Apply();
            _device.DrawUserPrimitives(PrimitiveType.TriangleList, batch.Vertices, command.Start, primitiveCount);
        }
    }

    public void Dispose()
    {
        _effect.Dispose();
        _blendStates.Dispose();
    }

    private readonly record struct DrawCommand(BatchKey Key, int Start, int Count);

    private readonly record struct BatchKey(Texture2D Texture, ParticleBlendMode Blend);

    private sealed class Batch
    {
        public ParticleVertex[] Vertices = new ParticleVertex[6 * 64];
        public int Count;

        public void Add(in ParticleVertex a, in ParticleVertex b, in ParticleVertex c)
        {
            EnsureCapacity(Count + 3);
            Vertices[Count++] = a;
            Vertices[Count++] = b;
            Vertices[Count++] = c;
        }

        private void EnsureCapacity(int needed)
        {
            if (Vertices.Length >= needed)
                return;
            var size = Vertices.Length;
            while (size < needed)
                size *= 2;
            Array.Resize(ref Vertices, size);
        }
    }
}
