using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Rendering;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;

using StageMesh = SharpGLTF.Geometry.MeshBuilder<SharpGLTF.Geometry.VertexTypes.VertexPositionNormal, SharpGLTF.Geometry.VertexTypes.VertexColor1Texture1, SharpGLTF.Geometry.VertexTypes.VertexEmpty>;
using StageVB = SharpGLTF.Geometry.VertexBuilder<SharpGLTF.Geometry.VertexTypes.VertexPositionNormal, SharpGLTF.Geometry.VertexTypes.VertexColor1Texture1, SharpGLTF.Geometry.VertexTypes.VertexEmpty>;
using RigidMesh = SharpGLTF.Geometry.MeshBuilder<SharpGLTF.Geometry.VertexTypes.VertexPositionNormal, SharpGLTF.Geometry.VertexTypes.VertexTexture1, SharpGLTF.Geometry.VertexTypes.VertexEmpty>;
using RigidVB = SharpGLTF.Geometry.VertexBuilder<SharpGLTF.Geometry.VertexTypes.VertexPositionNormal, SharpGLTF.Geometry.VertexTypes.VertexTexture1, SharpGLTF.Geometry.VertexTypes.VertexEmpty>;
using SkinMesh = SharpGLTF.Geometry.MeshBuilder<SharpGLTF.Geometry.VertexTypes.VertexPositionNormal, SharpGLTF.Geometry.VertexTypes.VertexTexture1, SharpGLTF.Geometry.VertexTypes.VertexJoints4>;
using SkinVB = SharpGLTF.Geometry.VertexBuilder<SharpGLTF.Geometry.VertexTypes.VertexPositionNormal, SharpGLTF.Geometry.VertexTypes.VertexTexture1, SharpGLTF.Geometry.VertexTypes.VertexJoints4>;

namespace PristonTale.GltfExport;

internal sealed record ExportOptions(
    bool FlipWinding,
    bool FlipV,
    bool Recenter = false,
    float Scale = 1f,
    bool NoAnims = false)
{
    public bool HasRootTransform => Recenter || Scale != 1f;

    public Matrix RootTransform(BoundingBox bounds)
    {
        var center = Recenter ? (bounds.Min + bounds.Max) * 0.5f : Vector3.Zero;
        return Matrix.CreateTranslation(-center) * Matrix.CreateScale(Scale);
    }
}

internal static class GltfSceneExport
{
    internal const float TicksPerSecond = 160000f / 33f;

    private const int SampleStepTicks = 160;
    private const int MaxSurfacesPerMesh = 200;
    private const float TransformTolerance = 5e-3f;
    private const string ExportRootName = "__PT_EXPORT_ROOT__";
    private const string TransformCarrierSuffix = "__PT_SCALE_TRANSLATION";

    public static SceneBuilder BuildStage(
        PristonStage stage,
        MaterialFactory materials,
        ExportOptions options)
    {
        var scene = new SceneBuilder();
        var normals = BuildStageNormals(stage);
        var root = options.RootTransform(stage.Bounds).ToAffineNumerics();
        var meshes = new List<StageMesh>();
        var materialMeshes = new Dictionary<MaterialBuilder, StageMesh>();
        StageMesh? currentMesh = null;
        var primitiveCount = 0;

        StageMesh MeshFor(MaterialBuilder material)
        {
            if (materialMeshes.TryGetValue(material, out var existing))
            {
                return existing;
            }

            if (currentMesh is null || primitiveCount >= MaxSurfacesPerMesh)
            {
                currentMesh = new StageMesh($"stage{meshes.Count}");
                meshes.Add(currentMesh);
                primitiveCount = 0;
            }

            materialMeshes.Add(material, currentMesh);
            primitiveCount++;
            return currentMesh;
        }

        foreach (var face in stage.Faces)
        {
            if (!ValidFace(face.A, face.B, face.C, stage.Vertices.Count))
            {
                continue;
            }

            var sourceMaterial = face.MaterialIndex >= 0 && face.MaterialIndex < stage.Materials.Count
                ? stage.Materials[face.MaterialIndex]
                : null;
            if (sourceMaterial is null)
            {
                continue;
            }

            var material = materials.Create(sourceMaterial, face.MaterialIndex);
            if (material is null)
            {
                continue;
            }

            var textureCoordinates = TextureCoordinatesOf(stage.TexLinks, face.TexLinkIndex);
            var primitive = MeshFor(material).UsePrimitive(material);

            StageVB BuildVertex(int vertexIndex, Vector2 uv)
            {
                var vertex = stage.Vertices[vertexIndex];
                var color = vertex.Color;
                var exportedColor = new System.Numerics.Vector4(
                    color.R / 255f,
                    color.G / 255f,
                    color.B / 255f,
                    color.A / 255f);
                return new StageVB(
                    new VertexPositionNormal(
                        vertex.Position.ToNumerics(),
                        normals[vertexIndex].ToNumerics()),
                    new VertexColor1Texture1(exportedColor, ConvertUv(uv, options)));
            }

            AddTriangle(
                primitive,
                BuildVertex(face.A, textureCoordinates.A),
                BuildVertex(face.B, textureCoordinates.B),
                BuildVertex(face.C, textureCoordinates.C),
                options);
        }

        foreach (var mesh in meshes)
        {
            scene.AddRigidMesh(mesh, root);
        }

        return scene;
    }

    public static SceneBuilder BuildModel(
        PristonModel model,
        MaterialFactory materials,
        ExportOptions options)
    {
        var scene = new SceneBuilder();
        var animator = new PristonModelAnimator(model);
        var absoluteBind = Copy(animator.Evaluate(0f));
        var wrapper = options.HasRootTransform
            ? new NodeBuilder(ExportRootName)
            {
                LocalMatrix = options.RootTransform(model.Bounds).ToAffineNumerics()
            }
            : null;
        var tree = new NodeTree(model, absoluteBind, wrapper);
        var bindNormals = BuildSmoothNormals(model);

        for (var objectIndex = 0; objectIndex < model.Objects.Count; objectIndex++)
        {
            var modelObject = model.Objects[objectIndex];
            if (modelObject.Faces.Count == 0)
            {
                continue;
            }

            var mesh = new RigidMesh(tree.Names[objectIndex]);
            var emitted = false;
            foreach (var face in modelObject.Faces)
            {
                if (!ValidFace(face.A, face.B, face.C, modelObject.Vertices.Count))
                {
                    continue;
                }

                var sourceMaterial = face.MaterialIndex >= 0 && face.MaterialIndex < model.Materials.Count
                    ? model.Materials[face.MaterialIndex]
                    : null;
                if (sourceMaterial is null)
                {
                    continue;
                }

                var material = materials.Create(sourceMaterial, face.MaterialIndex);
                if (material is null)
                {
                    continue;
                }

                var textureCoordinates = TextureCoordinatesOf(modelObject.TexLinks, face.TexLinkIndex);
                var primitive = mesh.UsePrimitive(material);

                RigidVB BuildVertex(int vertexIndex, Vector2 uv)
                {
                    return new RigidVB(
                        new VertexPositionNormal(
                            modelObject.Vertices[vertexIndex].Position.ToNumerics(),
                            bindNormals[objectIndex][vertexIndex].ToNumerics()),
                        new VertexTexture1(ConvertUv(uv, options)));
                }

                AddTriangle(
                    primitive,
                    BuildVertex(face.A, textureCoordinates.A),
                    BuildVertex(face.B, textureCoordinates.B),
                    BuildVertex(face.C, textureCoordinates.C),
                    options);
                emitted = true;
            }

            if (emitted)
            {
                scene.AddRigidMesh(mesh, tree.Nodes[objectIndex]);
            }
        }

        if (model.IsAnimated && !options.NoAnims && model.LastFrame > 0)
        {
            tree.AddInclusiveClip("Animation", animator, 0, model.LastFrame);
        }

        return scene;
    }

    public static SceneBuilder BuildActor(
        PristonActor actor,
        MaterialFactory materials,
        ExportOptions options)
    {
        var skeleton = actor.Skeleton;
        if (skeleton.Objects.Count == 0)
        {
            throw new InvalidDataException("The actor skeleton has no bones.");
        }

        var scene = new SceneBuilder();
        var animator = new PristonModelAnimator(skeleton);
        var absoluteBind = Copy(animator.Evaluate(0f));
        var armatureRoot = new NodeBuilder(ExportRootName);
        var tree = new NodeTree(
            skeleton,
            absoluteBind,
            armatureRoot,
            options.Scale);
        var skeletonByName = BuildObjectNameIndex(skeleton);
        var lastSkeletonIndex = skeleton.Objects.Count - 1;
        var renderFilter = actor.RenderObjectNames.Count > 0
            ? new HashSet<string>(actor.RenderObjectNames, StringComparer.OrdinalIgnoreCase)
            : null;
        var bindNormals = BuildSmoothNormals(actor.Mesh);
        var mesh = new SkinMesh("mesh");
        var emitted = false;

        for (var objectIndex = 0; objectIndex < actor.Mesh.Objects.Count; objectIndex++)
        {
            var modelObject = actor.Mesh.Objects[objectIndex];
            if (renderFilter is not null && !renderFilter.Contains(modelObject.Name))
            {
                continue;
            }

            foreach (var face in modelObject.Faces)
            {
                if (!ValidFace(face.A, face.B, face.C, modelObject.Vertices.Count))
                {
                    continue;
                }

                var sourceMaterial = face.MaterialIndex >= 0 && face.MaterialIndex < actor.Mesh.Materials.Count
                    ? actor.Mesh.Materials[face.MaterialIndex]
                    : null;
                if (sourceMaterial is null)
                {
                    continue;
                }

                var material = materials.Create(sourceMaterial, face.MaterialIndex);
                if (material is null)
                {
                    continue;
                }

                var textureCoordinates = TextureCoordinatesOf(modelObject.TexLinks, face.TexLinkIndex);
                var primitive = mesh.UsePrimitive(material);

                SkinVB BuildVertex(int vertexIndex, Vector2 uv)
                {
                    var bone = EffectiveBone(
                        modelObject,
                        vertexIndex,
                        skeletonByName,
                        objectIndex,
                        lastSkeletonIndex);
                    var bind = absoluteBind[bone];
                    var position = Vector3.Transform(modelObject.Vertices[vertexIndex].Position, bind)
                        * options.Scale;
                    var normal = Vector3.TransformNormal(bindNormals[objectIndex][vertexIndex], bind);
                    if (normal.LengthSquared() > 1e-8f)
                    {
                        normal.Normalize();
                    }

                    return new SkinVB(
                        new VertexPositionNormal(position.ToNumerics(), normal.ToNumerics()),
                        new VertexTexture1(ConvertUv(uv, options)),
                        new VertexJoints4(bone));
                }

                AddTriangle(
                    primitive,
                    BuildVertex(face.A, textureCoordinates.A),
                    BuildVertex(face.B, textureCoordinates.B),
                    BuildVertex(face.C, textureCoordinates.C),
                    options);
                emitted = true;
            }
        }

        if (!emitted)
        {
            throw new InvalidDataException("The actor contains no renderable triangles.");
        }

        scene.AddSkinnedMesh(
            mesh,
            System.Numerics.Matrix4x4.Identity,
            tree.Nodes);

        if (!options.NoAnims)
        {
            foreach (var clip in EnumerateClips(actor))
            {
                tree.AddExclusiveClip(
                    clip.Name,
                    animator,
                    clip.StartTicks,
                    clip.EndTicks);
            }
        }

        return scene;
    }

    private static Dictionary<string, int> BuildObjectNameIndex(PristonModel model)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < model.Objects.Count; index++)
        {
            var name = model.Objects[index].Name;
            if (name.Length > 0 && !result.ContainsKey(name))
            {
                result.Add(name, index);
            }
        }

        return result;
    }

    private static int EffectiveBone(
        PristonModelObject modelObject,
        int vertexIndex,
        IReadOnlyDictionary<string, int> skeletonByName,
        int objectIndex,
        int lastSkeletonIndex)
    {
        if (modelObject.HasPhysique && vertexIndex < modelObject.PhysiqueBoneNames.Count)
        {
            var boneName = modelObject.PhysiqueBoneNames[vertexIndex];
            if (!string.IsNullOrWhiteSpace(boneName)
                && skeletonByName.TryGetValue(boneName, out var boneIndex))
            {
                return Math.Min(boneIndex, lastSkeletonIndex);
            }
        }

        return Math.Min(objectIndex, lastSkeletonIndex);
    }

    private static IEnumerable<MotionClip> EnumerateClips(PristonActor actor)
    {
        var usedNames = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var actorInfo = actor.Info;
        for (var index = PristonFormatConstants.CharacterMotionBase; index < actorInfo.Motions.Count; index++)
        {
            var motion = actorInfo.Motions[index];
            if (motion.State == 0 || motion.EndFrame <= motion.StartFrame)
            {
                continue;
            }

            var offset = MotionFrameOffsetTicks(actor, motion) / SampleStepTicks;
            var startTicks = checked(((int)motion.StartFrame + offset) * SampleStepTicks);
            var endTicks = checked(((int)motion.EndFrame + offset) * SampleStepTicks);
            if (endTicks <= startTicks)
            {
                continue;
            }

            var baseName = Enum.IsDefined(typeof(PristonActorMotion), motion.State)
                ? ((PristonActorMotion)motion.State).ToString()
                : $"State_0x{motion.State:X}";
            var name = baseName;
            if (usedNames.TryGetValue(baseName, out var duplicateCount))
            {
                name = $"{baseName}.{index:000}";
            }

            usedNames[baseName] = duplicateCount + 1;
            yield return new MotionClip(name, startTicks, endTicks);
        }
    }

    private static int MotionFrameOffsetTicks(
        PristonActor actor,
        PristonMotionInfo motion)
    {
        var rangeIndex = motion.MotionFrame - 1;
        if (rangeIndex < 0 || rangeIndex >= actor.Skeleton.FrameRanges.Count)
        {
            return 0;
        }

        return actor.Skeleton.FrameRanges[rangeIndex].StartFrame;
    }

    private readonly record struct MotionClip(
        string Name,
        int StartTicks,
        int EndTicks);

    private sealed class NodeTree
    {
        private readonly PristonModel _model;
        private readonly int[] _parents;
        private readonly NodeBuilder? _wrapper;
        private readonly float _scale;
        private readonly NodeBuilder?[] _transformCarriers;

        public NodeTree(
            PristonModel model,
            Matrix[] absoluteBind,
            NodeBuilder? wrapper = null,
            float scale = 1f)
        {
            _model = model;
            _wrapper = wrapper;
            _scale = scale;

            var count = model.Objects.Count;
            Names = BuildUniqueNames(model, wrapper is null ? null : ExportRootName);
            Nodes = new NodeBuilder[count];
            _parents = new int[count];
            _transformCarriers = new NodeBuilder?[count];
            var carrierNames = BuildCarrierNames(Names, wrapper is null ? null : ExportRootName);

            var objectsByName = BuildObjectNameIndex(model);
            for (var index = 0; index < count; index++)
            {
                var parentName = model.Objects[index].ParentName;
                _parents[index] = parentName.Length > 0
                    && objectsByName.TryGetValue(parentName, out var parentIndex)
                    && parentIndex != index
                        ? parentIndex
                        : -1;
            }

            var creating = new bool[count];
            for (var index = 0; index < count; index++)
            {
                CreateNode(index, absoluteBind, creating, carrierNames);
            }
        }

        public string[] Names { get; }
        public NodeBuilder[] Nodes { get; }

        public void AddInclusiveClip(
            string name,
            PristonModelAnimator animator,
            int startTicks,
            int endTicks)
        {
            var rotations = new RotationContinuityState(Nodes.Length);
            foreach (var sourceTicks in SampleTicks(startTicks, endTicks))
            {
                var seconds = (sourceTicks - startTicks) / TicksPerSecond;
                AddSample(name, animator, sourceTicks, seconds, rotations);
            }

            var duration = (endTicks - startTicks) / TicksPerSecond;
            AddSample(name, animator, endTicks, duration, rotations);
        }

        public void AddExclusiveClip(
            string name,
            PristonModelAnimator animator,
            int startTicks,
            int endTicks)
        {
            if (endTicks <= startTicks)
            {
                throw new ArgumentOutOfRangeException(nameof(endTicks));
            }

            var rotations = new RotationContinuityState(Nodes.Length);
            var lastSourceTick = endTicks - 1;
            foreach (var sourceTicks in SampleTicks(startTicks, lastSourceTick))
            {
                var seconds = (sourceTicks - startTicks) / TicksPerSecond;
                AddSample(name, animator, sourceTicks, seconds, rotations);
            }

            var duration = (endTicks - startTicks) / TicksPerSecond;
            AddSample(name, animator, lastSourceTick, duration, rotations);
        }

        private IEnumerable<int> SampleTicks(int startTicks, int terminalTicks)
        {
            var ticks = new SortedSet<int>
            {
                startTicks
            };
            for (var sample = (long)startTicks + SampleStepTicks;
                 sample < terminalTicks;
                 sample += SampleStepTicks)
            {
                ticks.Add((int)sample);
            }

            foreach (var modelObject in _model.Objects)
            {
                AddKeyTicks(ticks, modelObject.RotationKeys, startTicks, terminalTicks);
                AddKeyTicks(ticks, modelObject.PositionKeys, startTicks, terminalTicks);
                AddKeyTicks(ticks, modelObject.ScaleKeys, startTicks, terminalTicks);
            }

            return ticks;
        }

        private static void AddKeyTicks<T>(
            ISet<int> ticks,
            IReadOnlyList<T> keys,
            int startTicks,
            int terminalTicks)
        {
            foreach (var key in keys)
            {
                var frame = key switch
                {
                    PristonRotationKey rotation => rotation.Frame,
                    PristonPositionKey position => position.Frame,
                    PristonScaleKey scale => scale.Frame,
                    _ => throw new InvalidOperationException(
                        $"Unsupported animation key type {typeof(T).Name}.")
                };
                if (frame > startTicks && frame < terminalTicks)
                {
                    ticks.Add(frame);
                }
            }
        }

        private NodeBuilder CreateNode(
            int index,
            Matrix[] absoluteBind,
            bool[] creating,
            string[] carrierNames)
        {
            if (Nodes[index] is not null)
            {
                return Nodes[index];
            }

            if (creating[index])
            {
                throw new InvalidDataException(
                    $"Model hierarchy contains a cycle at object '{_model.Objects[index].Name}'.");
            }

            creating[index] = true;
            var parentIndex = _parents[index];
            NodeBuilder? parent;
            Matrix local;
            if (parentIndex >= 0)
            {
                parent = CreateNode(parentIndex, absoluteBind, creating, carrierNames);
                local = RelativeToParent(
                    absoluteBind[index],
                    absoluteBind[parentIndex],
                    _model.Objects[index].Name);
            }
            else
            {
                parent = _wrapper;
                local = absoluteBind[index];
            }

            NodeBuilder node;
            if (_model.Objects[index].IsAnimated)
            {
                DecomposeRotationScale(
                    local,
                    "BindPose",
                    Names[index],
                    0,
                    out var translation,
                    out var rotation,
                    out var scale);
                if (_scale != 1f)
                {
                    translation *= _scale;
                }

                if (RequiresRotationScaleCarrier(_model.Objects[index]))
                {
                    var carrier = parent is null
                        ? new NodeBuilder(carrierNames[index])
                        : parent.CreateNode(carrierNames[index]);
                    carrier.LocalMatrix = CreateScaleTranslation(scale, translation);
                    node = carrier.CreateNode(Names[index]);
                    node.LocalMatrix = System.Numerics.Matrix4x4.CreateFromQuaternion(rotation);
                    _transformCarriers[index] = carrier;
                }
                else
                {
                    node = parent is null
                        ? new NodeBuilder(Names[index])
                        : parent.CreateNode(Names[index]);
                    node.LocalMatrix = CreateScaleRotationTranslation(
                        scale,
                        rotation,
                        translation);
                }
            }
            else
            {
                node = parent is null
                    ? new NodeBuilder(Names[index])
                    : parent.CreateNode(Names[index]);
                if (_scale != 1f)
                {
                    local.Translation *= _scale;
                }

                node.LocalMatrix = local.ToAffineNumerics();
            }

            Nodes[index] = node;
            creating[index] = false;
            return node;
        }

        private void AddSample(
            string clipName,
            PristonModelAnimator animator,
            int sourceTicks,
            float outputSeconds,
            RotationContinuityState rotations)
        {
            var absolute = animator.Evaluate(sourceTicks);
            for (var index = 0; index < Nodes.Length; index++)
            {
                if (!_model.Objects[index].IsAnimated)
                {
                    continue;
                }

                var parentIndex = _parents[index];
                var local = parentIndex >= 0
                    ? RelativeToParent(
                        absolute[index],
                        absolute[parentIndex],
                        _model.Objects[index].Name)
                    : absolute[index];
                DecomposeRotationScale(
                    local,
                    clipName,
                    Names[index],
                    sourceTicks,
                    out var translation,
                    out var rotation,
                    out var scale);

                if (_scale != 1f)
                {
                    translation *= _scale;
                }

                rotation = rotations.MakeContinuous(index, rotation);
                var carrier = _transformCarriers[index];
                if (carrier is null)
                {
                    Nodes[index].UseTranslation(clipName).WithPoint(outputSeconds, translation);
                    Nodes[index].UseScale(clipName).WithPoint(outputSeconds, scale);
                }
                else
                {
                    carrier.UseTranslation(clipName).WithPoint(outputSeconds, translation);
                    carrier.UseScale(clipName).WithPoint(outputSeconds, scale);
                }

                Nodes[index].UseRotation(clipName).WithPoint(outputSeconds, rotation);
            }
        }
    }

    private sealed class RotationContinuityState
    {
        private readonly bool[] _hasValue;
        private readonly System.Numerics.Quaternion[] _values;

        public RotationContinuityState(int count)
        {
            _hasValue = new bool[count];
            _values = new System.Numerics.Quaternion[count];
        }

        public System.Numerics.Quaternion MakeContinuous(
            int index,
            System.Numerics.Quaternion value)
        {
            if (_hasValue[index]
                && System.Numerics.Quaternion.Dot(_values[index], value) < 0f)
            {
                value = new System.Numerics.Quaternion(
                    -value.X,
                    -value.Y,
                    -value.Z,
                    -value.W);
            }

            _hasValue[index] = true;
            _values[index] = value;
            return value;
        }
    }

    private static Matrix RelativeToParent(
        Matrix absolute,
        Matrix parentAbsolute,
        string objectName)
    {
        var determinant = parentAbsolute.Determinant();
        if (!float.IsFinite(determinant) || MathF.Abs(determinant) <= 1e-8f)
        {
            throw new InvalidDataException(
                $"Object '{objectName}' has a non-invertible parent transform.");
        }

        return absolute * Matrix.Invert(parentAbsolute);
    }

    private static void DecomposeRotationScale(
        Matrix source,
        string clipName,
        string nodeName,
        int sourceTicks,
        out System.Numerics.Vector3 translation,
        out System.Numerics.Quaternion rotation,
        out System.Numerics.Vector3 scale)
    {
        var matrix = source.ToAffineNumerics();
        translation = matrix.Translation;
        scale = new System.Numerics.Vector3(
            MathF.Sqrt((matrix.M11 * matrix.M11) + (matrix.M21 * matrix.M21) + (matrix.M31 * matrix.M31)),
            MathF.Sqrt((matrix.M12 * matrix.M12) + (matrix.M22 * matrix.M22) + (matrix.M32 * matrix.M32)),
            MathF.Sqrt((matrix.M13 * matrix.M13) + (matrix.M23 * matrix.M23) + (matrix.M33 * matrix.M33)));
        if (scale.X <= 1e-8f || scale.Y <= 1e-8f || scale.Z <= 1e-8f)
        {
            throw NonDecomposableAnimation(clipName, nodeName, sourceTicks);
        }

        if (matrix.GetDeterminant() < 0f)
        {
            scale.X = -scale.X;
        }

        var rotationMatrix = new System.Numerics.Matrix4x4(
            matrix.M11 / scale.X,
            matrix.M12 / scale.Y,
            matrix.M13 / scale.Z,
            0f,
            matrix.M21 / scale.X,
            matrix.M22 / scale.Y,
            matrix.M23 / scale.Z,
            0f,
            matrix.M31 / scale.X,
            matrix.M32 / scale.Y,
            matrix.M33 / scale.Z,
            0f,
            0f,
            0f,
            0f,
            1f);
        rotation = System.Numerics.Quaternion.CreateFromRotationMatrix(rotationMatrix);
        rotation = System.Numerics.Quaternion.Normalize(rotation);
        var reconstructed = System.Numerics.Matrix4x4.CreateFromQuaternion(rotation)
            * System.Numerics.Matrix4x4.CreateScale(scale)
            * System.Numerics.Matrix4x4.CreateTranslation(translation);
        if (!MatricesNearlyEqual(matrix, reconstructed))
        {
            throw NonDecomposableAnimation(clipName, nodeName, sourceTicks);
        }
    }

    private static InvalidDataException NonDecomposableAnimation(
        string clipName,
        string nodeName,
        int sourceTicks)
    {
        return new InvalidDataException(
            $"Animation '{clipName}' node '{nodeName}' at tick {sourceTicks} contains shear or another transform that glTF TRS channels cannot represent.");
    }

    private static System.Numerics.Matrix4x4 CreateScaleTranslation(
        System.Numerics.Vector3 scale,
        System.Numerics.Vector3 translation)
    {
        var matrix = System.Numerics.Matrix4x4.CreateScale(scale);
        matrix.Translation = translation;
        return matrix;
    }

    private static System.Numerics.Matrix4x4 CreateScaleRotationTranslation(
        System.Numerics.Vector3 scale,
        System.Numerics.Quaternion rotation,
        System.Numerics.Vector3 translation)
    {
        return System.Numerics.Matrix4x4.CreateScale(scale)
            * System.Numerics.Matrix4x4.CreateFromQuaternion(rotation)
            * System.Numerics.Matrix4x4.CreateTranslation(translation);
    }

    private static bool RequiresRotationScaleCarrier(PristonModelObject modelObject)
    {
        foreach (var key in modelObject.ScaleKeys)
        {
            var largestMagnitude = MathF.Max(
                1f,
                MathF.Max(
                    MathF.Abs(key.Scale.X),
                    MathF.Max(MathF.Abs(key.Scale.Y), MathF.Abs(key.Scale.Z))));
            var largestDifference = MathF.Max(
                MathF.Abs(key.Scale.X - key.Scale.Y),
                MathF.Max(
                    MathF.Abs(key.Scale.X - key.Scale.Z),
                    MathF.Abs(key.Scale.Y - key.Scale.Z)));
            if (largestDifference > largestMagnitude * TransformTolerance)
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatricesNearlyEqual(
        System.Numerics.Matrix4x4 left,
        System.Numerics.Matrix4x4 right)
    {
        var leftValues = MatrixValues(left);
        var rightValues = MatrixValues(right);
        var largestMagnitude = 1f;
        var largestDifference = 0f;

        for (var index = 0; index < leftValues.Length; index++)
        {
            if (!float.IsFinite(leftValues[index]) || !float.IsFinite(rightValues[index]))
            {
                return false;
            }

            largestMagnitude = MathF.Max(largestMagnitude, MathF.Abs(leftValues[index]));
            largestDifference = MathF.Max(
                largestDifference,
                MathF.Abs(leftValues[index] - rightValues[index]));
        }

        return largestDifference <= largestMagnitude * TransformTolerance;
    }

    private static float[] MatrixValues(System.Numerics.Matrix4x4 matrix)
    {
        return
        [
            matrix.M11,
            matrix.M12,
            matrix.M13,
            matrix.M14,
            matrix.M21,
            matrix.M22,
            matrix.M23,
            matrix.M24,
            matrix.M31,
            matrix.M32,
            matrix.M33,
            matrix.M34,
            matrix.M41,
            matrix.M42,
            matrix.M43,
            matrix.M44
        ];
    }

    private static string[] BuildUniqueNames(
        PristonModel model,
        string? reservedName)
    {
        var names = new string[model.Objects.Count];
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (reservedName is not null)
        {
            used.Add(reservedName);
        }

        for (var index = 0; index < names.Length; index++)
        {
            var baseName = SafeName(model.Objects[index].Name, index);
            var name = baseName;
            var suffix = 1;
            while (!used.Add(name))
            {
                name = $"{baseName}.{suffix:000}";
                suffix++;
            }

            names[index] = name;
        }

        return names;
    }

    private static string[] BuildCarrierNames(
        IReadOnlyList<string> nodeNames,
        string? reservedName)
    {
        var names = new string[nodeNames.Count];
        var used = new HashSet<string>(nodeNames, StringComparer.OrdinalIgnoreCase);
        if (reservedName is not null)
        {
            used.Add(reservedName);
        }

        for (var index = 0; index < names.Length; index++)
        {
            var baseName = $"{nodeNames[index]}{TransformCarrierSuffix}";
            var name = baseName;
            var suffix = 1;
            while (!used.Add(name))
            {
                name = $"{baseName}.{suffix:000}";
                suffix++;
            }

            names[index] = name;
        }

        return names;
    }

    private static void AddTriangle<TVertexGeometry, TVertexMaterial, TVertexSkinning>(
        IPrimitiveBuilder primitive,
        VertexBuilder<TVertexGeometry, TVertexMaterial, TVertexSkinning> first,
        VertexBuilder<TVertexGeometry, TVertexMaterial, TVertexSkinning> second,
        VertexBuilder<TVertexGeometry, TVertexMaterial, TVertexSkinning> third,
        ExportOptions options)
        where TVertexGeometry : struct, IVertexGeometry
        where TVertexMaterial : struct, IVertexMaterial
        where TVertexSkinning : struct, IVertexSkinning
    {
        var builder = (PrimitiveBuilder<MaterialBuilder, TVertexGeometry, TVertexMaterial, TVertexSkinning>)primitive;
        if (options.FlipWinding)
        {
            builder.AddTriangle(first, third, second);
        }
        else
        {
            builder.AddTriangle(first, second, third);
        }
    }

    private static System.Numerics.Vector2 ConvertUv(
        Vector2 uv,
        ExportOptions options)
    {
        return new System.Numerics.Vector2(
            uv.X,
            options.FlipV ? 1f - uv.Y : uv.Y);
    }

    private static PristonTexLink TextureCoordinatesOf(
        IReadOnlyList<PristonTexLink> links,
        int index)
    {
        return index >= 0 && index < links.Count
            ? links[index]
            : PristonTexLink.Empty;
    }

    private static bool ValidFace(
        int first,
        int second,
        int third,
        int vertexCount)
    {
        return first >= 0
            && second >= 0
            && third >= 0
            && first < vertexCount
            && second < vertexCount
            && third < vertexCount
            && first != second
            && second != third
            && first != third;
    }

    private static Matrix[] Copy(Matrix[] source)
    {
        var destination = new Matrix[source.Length];
        Array.Copy(source, destination, source.Length);
        return destination;
    }

    private static string SafeName(string name, int index)
    {
        return string.IsNullOrWhiteSpace(name)
            ? $"obj{index}"
            : name;
    }

    private static Vector3[][] BuildSmoothNormals(PristonModel model)
    {
        var result = new Vector3[model.Objects.Count][];
        for (var objectIndex = 0; objectIndex < model.Objects.Count; objectIndex++)
        {
            var modelObject = model.Objects[objectIndex];
            var normals = new Vector3[modelObject.Vertices.Count];
            var needsFallback = false;
            for (var vertexIndex = 0; vertexIndex < normals.Length; vertexIndex++)
            {
                var authored = modelObject.Vertices[vertexIndex].Normal;
                if (authored.LengthSquared() > 1e-6f)
                {
                    normals[vertexIndex] = authored;
                }
                else
                {
                    needsFallback = true;
                }
            }

            if (needsFallback)
            {
                var averaged = new Vector3[modelObject.Vertices.Count];
                foreach (var face in modelObject.Faces)
                {
                    if (!ValidFace(face.A, face.B, face.C, modelObject.Vertices.Count))
                    {
                        continue;
                    }

                    var normal = Vector3.Cross(
                        modelObject.Vertices[face.B].Position - modelObject.Vertices[face.A].Position,
                        modelObject.Vertices[face.C].Position - modelObject.Vertices[face.A].Position);
                    if (normal.LengthSquared() < 1e-6f)
                    {
                        continue;
                    }

                    normal.Normalize();
                    averaged[face.A] += normal;
                    averaged[face.B] += normal;
                    averaged[face.C] += normal;
                }

                for (var vertexIndex = 0; vertexIndex < normals.Length; vertexIndex++)
                {
                    if (normals[vertexIndex].LengthSquared() <= 1e-6f)
                    {
                        normals[vertexIndex] = averaged[vertexIndex].LengthSquared() < 1e-6f
                            ? Vector3.Up
                            : Vector3.Normalize(averaged[vertexIndex]);
                    }
                }
            }

            result[objectIndex] = normals;
        }

        return result;
    }

    private static Vector3[] BuildStageNormals(PristonStage stage)
    {
        var normals = new Vector3[stage.Vertices.Count];
        foreach (var face in stage.Faces)
        {
            if (!ValidFace(face.A, face.B, face.C, stage.Vertices.Count))
            {
                continue;
            }

            var normal = Vector3.Cross(
                stage.Vertices[face.B].Position - stage.Vertices[face.A].Position,
                stage.Vertices[face.C].Position - stage.Vertices[face.A].Position);
            if (normal.LengthSquared() < 1e-6f)
            {
                continue;
            }

            normal.Normalize();
            normals[face.A] += normal;
            normals[face.B] += normal;
            normals[face.C] += normal;
        }

        for (var index = 0; index < normals.Length; index++)
        {
            normals[index] = normals[index].LengthSquared() < 1e-6f
                ? Vector3.Up
                : Vector3.Normalize(normals[index]);
        }

        return normals;
    }
}
