using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Engine3D.Formats;

namespace PristonTale.Engine3D.Rendering;

public sealed class PristonMeshEffect : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly BasicEffect _effect;
    private readonly List<Batch>[] _frames;
    public int FrameCount => _frames.Length;

    public PristonMeshEffect(GraphicsDevice device, PristonTextureLoader textures, PristonModel mesh, PristonModel skeleton, int frameCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(frameCount, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(frameCount, 60);
        _device = device;
        _effect = new BasicEffect(device) { TextureEnabled = true, VertexColorEnabled = true, LightingEnabled = false };
        _frames = new List<Batch>[frameCount];
        try
        {
            var directory = Path.GetDirectoryName(mesh.Path) ?? ".";
            for (int frame = 0; frame < frameCount; frame++)
            {
                var batches = new List<Batch>();
                _frames[frame] = batches;
                var groups = PristonModelGeometry.BuildGroups(mesh, Matrix.Identity, Color.White, skeleton, frame * 160);
                foreach (var group in groups)
                {
                    if (group.Key < 0 || group.Key >= mesh.Materials.Count)
                    {
                        throw new InvalidDataException("Effect mesh references an invalid material.");
                    }

                    var material = mesh.Materials[group.Key];
                    if (material.IsInvisible || material.HasNoRenderableTexture || group.Value.Count == 0)
                    {
                        continue;
                    }

                    var texture = textures.LoadTexture(directory, material.TextureName, material.OpacityName);
                    var vertices = group.Value.ToArray();
                    var buffer = new VertexBuffer(device, VertexPositionColorTexture.VertexDeclaration, vertices.Length, BufferUsage.WriteOnly);
                    batches.Add(new Batch(buffer, texture, vertices.Length / 3));
                    buffer.SetData(vertices);
                }
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Draw(int frame, Matrix world, Matrix view, Matrix projection, float opacity)
    {
        _effect.World = world;
        _effect.View = view;
        _effect.Projection = projection;
        _effect.Alpha = MathHelper.Clamp(opacity, 0, 1);
        _effect.DiffuseColor = new Vector3(_effect.Alpha);
        _device.BlendState = BlendState.Additive;
        _device.DepthStencilState = DepthStencilState.DepthRead;
        _device.RasterizerState = RasterizerState.CullNone;
        _device.SamplerStates[0] = SamplerState.LinearWrap;
        foreach (var batch in _frames[Math.Clamp(frame, 0, FrameCount - 1)])
        {
            _effect.Texture = batch.Texture;
            _device.SetVertexBuffer(batch.Vertices);
            foreach (var pass in _effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                _device.DrawPrimitives(PrimitiveType.TriangleList, 0, batch.Primitives);
            }
        }
    }

    public void Dispose()
    {
        foreach (var frame in _frames)
        {
            if (frame is null)
            {
                continue;
            }

            foreach (var batch in frame)
            {
                batch.Vertices.Dispose();
            }
        }

        _effect.Dispose();
    }

    private readonly record struct Batch(VertexBuffer Vertices, Texture2D Texture, int Primitives);
}
