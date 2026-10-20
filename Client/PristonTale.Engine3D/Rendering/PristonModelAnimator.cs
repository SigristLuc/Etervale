using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Formats;

namespace PristonTale.Engine3D.Rendering;

public sealed class PristonModelAnimator
{
    private static readonly ConditionalWeakTable<PristonModel, PreparedRig> PreparedRigs = new();

    private readonly PristonModel _model;
    private readonly PreparedRig _rig;
    private readonly Matrix[] _transforms;
    private readonly int[] _rotationSegments;
    private readonly int[] _positionSegments;
    private readonly int[] _scaleSegments;

    public PristonModelAnimator(PristonModel model)
    {
        _model = model;
        var count = model.Objects.Count;
        _rig = PreparedRigs.GetValue(model, static source => new PreparedRig(source));
        _transforms = new Matrix[count];
        _rotationSegments = new int[count];
        _positionSegments = new int[count];
        _scaleSegments = new int[count];
    }

    public int ObjectCount => _transforms.Length;

    public Matrix[] Evaluate(float frame)
    {
        for (var orderIndex = 0; orderIndex < _rig.EvaluationOrder.Length; orderIndex++)
        {
            var index = _rig.EvaluationOrder[orderIndex];
            var obj = _model.Objects[index];
            var local = obj.IsAnimated ? SampleObjectTransform(index, obj, frame) : obj.Transform;
            var parent = _rig.ParentIndices[index];
            if (parent < 0)
            {
                _transforms[index] = local;
                continue;
            }

            // Static objects store their transform in absolute space, so strip the parent's
            // bind transform before re-applying the (possibly animated) resolved parent.
            if (!obj.IsAnimated)
            {
                local *= _rig.ParentInverses[index];
            }

            _transforms[index] = local * _transforms[parent];
        }

        return _transforms;
    }

    private Matrix SampleObjectTransform(int objectIndex, PristonModelObject obj, float frame)
    {
        var rotation = SampleRotationMatrix(objectIndex, obj, frame);
        var scale = SampleScale(objectIndex, obj.ScaleKeys, frame);
        var position = SamplePosition(objectIndex, obj.PositionKeys, frame, obj.BasePosition);
        var matrix = rotation * Matrix.CreateScale(scale);
        matrix.Translation = position;
        return matrix;
    }

    // The previous-rotation matrices hold absolute cumulative orientations; interpolating converted deltas would drift.
    private Matrix SampleRotationMatrix(int objectIndex, PristonModelObject obj, float frame)
    {
        var keys = obj.RotationKeys;
        var prev = obj.PreviousRotationMatrices;
        var quaternions = _rig.RotationQuaternions[objectIndex];
        if (prev.Count == 0)
        {
            return quaternions.Length > 0 ? Matrix.CreateFromQuaternion(quaternions[0]) : obj.BaseRotation;
        }

        if (keys.Count <= 1 || frame <= keys[0].Frame)
        {
            _rotationSegments[objectIndex] = 0;
            return prev[0];
        }

        var segment = SeekRotationSegment(keys, frame, _rotationSegments[objectIndex]);
        _rotationSegments[objectIndex] = segment;
        var next = segment + 1;
        if (next < keys.Count && frame < keys[next].Frame)
        {
            var a = keys[segment];
            var b = keys[next];
            var amount = (frame - a.Frame) / (b.Frame - a.Frame);
            var quaternionIndex = Math.Min(segment, quaternions.Length - 1);
            var qa = quaternions[quaternionIndex];
            var qb = next < quaternions.Length ? quaternions[next] : qa;
            return Matrix.CreateFromQuaternion(Quaternion.Slerp(qa, qb, amount));
        }

        return prev[Math.Min(keys.Count - 1, prev.Count - 1)];
    }

    private Vector3 SamplePosition(int objectIndex, IReadOnlyList<PristonPositionKey> keys, float frame, Vector3 fallback)
    {
        if (keys.Count == 0)
        {
            return fallback;
        }

        if (keys.Count == 1 || frame <= keys[0].Frame)
        {
            _positionSegments[objectIndex] = 0;
            return keys[0].Position;
        }

        var segment = SeekPositionSegment(keys, frame, _positionSegments[objectIndex]);
        _positionSegments[objectIndex] = segment;
        var next = segment + 1;
        if (next < keys.Count && frame < keys[next].Frame)
        {
            var a = keys[segment];
            var b = keys[next];
            var amount = (frame - a.Frame) / (b.Frame - a.Frame);
            return Vector3.Lerp(a.Position, b.Position, amount);
        }

        return keys[^1].Position;
    }

    private Vector3 SampleScale(int objectIndex, IReadOnlyList<PristonScaleKey> keys, float frame)
    {
        if (keys.Count == 0)
        {
            return Vector3.One;
        }

        if (keys.Count == 1 || frame <= keys[0].Frame)
        {
            _scaleSegments[objectIndex] = 0;
            return keys[0].Scale;
        }

        var segment = SeekScaleSegment(keys, frame, _scaleSegments[objectIndex]);
        _scaleSegments[objectIndex] = segment;
        var next = segment + 1;
        if (next < keys.Count && frame < keys[next].Frame)
        {
            var a = keys[segment];
            var b = keys[next];
            var amount = (frame - a.Frame) / (b.Frame - a.Frame);
            return Vector3.Lerp(a.Scale, b.Scale, amount);
        }

        return keys[^1].Scale;
    }

    private static int SeekRotationSegment(IReadOnlyList<PristonRotationKey> keys, float frame, int hint)
    {
        var segment = Math.Clamp(hint, 0, keys.Count - 2);
        while (segment > 0 && frame < keys[segment].Frame)
        {
            segment--;
        }

        while (segment < keys.Count - 2 && frame >= keys[segment + 1].Frame)
        {
            segment++;
        }

        return segment;
    }

    private static int SeekPositionSegment(IReadOnlyList<PristonPositionKey> keys, float frame, int hint)
    {
        var segment = Math.Clamp(hint, 0, keys.Count - 2);
        while (segment > 0 && frame < keys[segment].Frame)
        {
            segment--;
        }

        while (segment < keys.Count - 2 && frame >= keys[segment + 1].Frame)
        {
            segment++;
        }

        return segment;
    }

    private static int SeekScaleSegment(IReadOnlyList<PristonScaleKey> keys, float frame, int hint)
    {
        var segment = Math.Clamp(hint, 0, keys.Count - 2);
        while (segment > 0 && frame < keys[segment].Frame)
        {
            segment--;
        }

        while (segment < keys.Count - 2 && frame >= keys[segment + 1].Frame)
        {
            segment++;
        }

        return segment;
    }

    private sealed class PreparedRig
    {
        public PreparedRig(PristonModel model)
        {
            var count = model.Objects.Count;
            ParentIndices = new int[count];
            ParentInverses = new Matrix[count];
            RotationQuaternions = new Quaternion[count][];
            var byName = new Dictionary<string, int>(count, StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < count; index++)
            {
                var name = model.Objects[index].Name;
                if (name.Length > 0 && !byName.ContainsKey(name))
                {
                    byName.Add(name, index);
                }
            }

            for (var index = 0; index < count; index++)
            {
                var obj = model.Objects[index];
                RotationQuaternions[index] = PrepareRotationQuaternions(obj);
                if (obj.ParentName.Length > 0 && byName.TryGetValue(obj.ParentName, out var parent))
                {
                    ParentIndices[index] = parent;
                    ParentInverses[index] = Matrix.Invert(model.Objects[parent].Transform);
                }
                else
                {
                    ParentIndices[index] = -1;
                    ParentInverses[index] = Matrix.Identity;
                }
            }

            EvaluationOrder = BuildEvaluationOrder(ParentIndices, model.Path);
        }

        public int[] ParentIndices { get; }
        public Matrix[] ParentInverses { get; }
        public Quaternion[][] RotationQuaternions { get; }
        public int[] EvaluationOrder { get; }

        private static Quaternion[] PrepareRotationQuaternions(PristonModelObject obj)
        {
            if (obj.PreviousRotationMatrices.Count == 0)
            {
                return obj.RotationKeys.Count == 0 ? [] : [obj.RotationKeys[0].Rotation];
            }

            var quaternions = new Quaternion[obj.PreviousRotationMatrices.Count];
            for (var index = 0; index < quaternions.Length; index++)
            {
                quaternions[index] = Quaternion.CreateFromRotationMatrix(obj.PreviousRotationMatrices[index]);
            }

            return quaternions;
        }

        private static int[] BuildEvaluationOrder(ReadOnlySpan<int> parents, string modelPath)
        {
            var order = new int[parents.Length];
            var state = new byte[parents.Length];
            var count = 0;
            for (var index = 0; index < parents.Length; index++)
            {
                Visit(index, parents, state, order, ref count, modelPath);
            }

            return order;
        }

        private static void Visit(
            int index,
            ReadOnlySpan<int> parents,
            Span<byte> state,
            Span<int> order,
            ref int count,
            string modelPath)
        {
            if (state[index] == 2)
            {
                return;
            }

            if (state[index] == 1)
            {
                throw new InvalidDataException($"Animation hierarchy cycle at object {index} in '{modelPath}'.");
            }

            state[index] = 1;
            var parent = parents[index];
            if (parent >= 0)
            {
                Visit(parent, parents, state, order, ref count, modelPath);
            }

            state[index] = 2;
            order[count] = index;
            count++;
        }
    }
}
