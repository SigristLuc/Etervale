using Microsoft.Xna.Framework;

namespace PristonTale.Engine3D.Rendering;

internal sealed class RenderQueueBuilder
{
    private readonly PristonRenderQueue<RenderBatch> _queue = new();
    private readonly BoundingFrustum _frustum = new(Matrix.Identity);

    public PristonRenderQueue<RenderBatch> Queue => _queue;

    public int Build(IReadOnlyList<RenderBatch> batches, PristonFrameContext frame)
    {
        return Build(batches, [], frame, includeShadowCasters: true);
    }

    public int Build(
        IReadOnlyList<RenderBatch> batches,
        IReadOnlyList<RenderBatch> shadowBatches,
        PristonFrameContext frame,
        bool includeShadowCasters = true)
    {
        _queue.Clear();
        _frustum.Matrix = frame.View * frame.Projection;
        var culled = 0;
        for (var i = 0; i < batches.Count; i++)
        {
            var batch = batches[i];
            var sortKey = batch.Material.SortKey;
            var sortDepth = SortDepth(frame.CameraPosition, batch.Bounds);
            if (includeShadowCasters
                && !batch.ShadowCoveredByAggregate
                && (batch.Material.CastsShadow || batch.ForceCastsShadow))
            {
                _queue.AddShadowCaster(batch, sortKey, sortDepth);
            }

            if (!_frustum.Intersects(batch.Bounds))
            {
                culled++;
                continue;
            }

            var pass = RenderPass(batch);
            _queue.Add(batch, pass, sortKey, sortDepth);
        }

        if (includeShadowCasters)
        {
            for (var i = 0; i < shadowBatches.Count; i++)
            {
                var batch = shadowBatches[i];
                _queue.AddShadowCaster(
                    batch,
                    batch.Material.SortKey,
                    SortDepth(frame.CameraPosition, batch.Bounds));
            }
        }

        _queue.Sort(includeShadowCasters);
        return culled;
    }

    private static PristonRenderPass RenderPass(RenderBatch batch)
    {
        if (batch.Material.Transparent)
            return PristonRenderPass.Transparent;

        return batch.Material.AlphaTest ? PristonRenderPass.AlphaTest : PristonRenderPass.Opaque;
    }

    private static float SortDepth(Vector3 cameraPosition, BoundingBox bounds)
    {
        var center = (bounds.Min + bounds.Max) * 0.5f;
        return Vector3.DistanceSquared(cameraPosition, center);
    }

}
