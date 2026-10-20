using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Formats;

namespace PristonTale.Engine3D.Rendering;

// Legacy model files store vertex normals averaged across perpendicular faces and sometimes pointing inward. Strong
// sun and sky light turn those into bright-to-dark gradients across flat faces, so skinned and animated meshes get
// normals rebuilt from their posed geometry, like the static stage. A corner shares its normal only with faces at the
// same posed position within the crease angle, then the normal returns to its bone's local space, where the vertex
// shader's bone transform brings it back.
internal static class PristonModelNormals
{
    private const float CreaseCosine = 0.5f;
    private const float PositionWeld = 0.01f;

    public static Dictionary<PristonModelFace, Vector3[]>[] Build(PristonModel model, Func<int, int, Matrix> vertexPose)
    {
        var result = new Dictionary<PristonModelFace, Vector3[]>[model.Objects.Count];
        for (var objectIndex = 0; objectIndex < model.Objects.Count; objectIndex++)
        {
            result[objectIndex] = BuildObject(model.Objects[objectIndex], objectIndex, vertexPose);
        }

        return result;
    }

    private static Dictionary<PristonModelFace, Vector3[]> BuildObject(PristonModelObject obj, int objectIndex, Func<int, int, Matrix> vertexPose)
    {
        var poses = new Matrix[obj.Vertices.Count];
        var posed = new Vector3[obj.Vertices.Count];
        var welded = new int[obj.Vertices.Count];
        var welds = new Dictionary<(int X, int Y, int Z), int>();
        for (var vertexIndex = 0; vertexIndex < obj.Vertices.Count; vertexIndex++)
        {
            poses[vertexIndex] = vertexPose(objectIndex, vertexIndex);
            posed[vertexIndex] = Vector3.Transform(obj.Vertices[vertexIndex].Position, poses[vertexIndex]);
            var key = (Quantize(posed[vertexIndex].X), Quantize(posed[vertexIndex].Y), Quantize(posed[vertexIndex].Z));
            if (!welds.TryGetValue(key, out var weld))
            {
                weld = welds.Count;
                welds.Add(key, weld);
            }

            welded[vertexIndex] = weld;
        }

        var faces = new List<PristonModelFace>(obj.Faces.Count);
        var areaNormals = new List<Vector3>(obj.Faces.Count);
        var unitNormals = new List<Vector3>(obj.Faces.Count);
        var facesAtPosition = new List<int>[welds.Count];
        foreach (var face in obj.Faces)
        {
            if (!IsValid(face, obj.Vertices.Count))
            {
                continue;
            }

            var areaNormal = Vector3.Cross(posed[face.B] - posed[face.A], posed[face.C] - posed[face.A]);
            var faceIndex = faces.Count;
            faces.Add(face);
            areaNormals.Add(areaNormal);
            unitNormals.Add(areaNormal.LengthSquared() > 1e-12f ? Vector3.Normalize(areaNormal) : Vector3.Zero);
            foreach (var vertexIndex in (ReadOnlySpan<int>)[face.A, face.B, face.C])
            {
                (facesAtPosition[welded[vertexIndex]] ??= []).Add(faceIndex);
            }
        }

        var corners = new Dictionary<PristonModelFace, Vector3[]>(faces.Count, ReferenceEqualityComparer.Instance);
        for (var faceIndex = 0; faceIndex < faces.Count; faceIndex++)
        {
            var face = faces[faceIndex];
            var own = unitNormals[faceIndex];
            var normals = new Vector3[3];
            var vertices = (ReadOnlySpan<int>)[face.A, face.B, face.C];
            for (var corner = 0; corner < 3; corner++)
            {
                var vertexIndex = vertices[corner];
                var sum = Vector3.Zero;
                if (own != Vector3.Zero)
                {
                    foreach (var neighbour in facesAtPosition[welded[vertexIndex]])
                    {
                        if (Vector3.Dot(unitNormals[neighbour], own) >= CreaseCosine)
                        {
                            sum += areaNormals[neighbour];
                        }
                    }
                }

                normals[corner] = ToBoneSpace(sum, poses[vertexIndex], obj.Vertices[vertexIndex].Normal);
            }

            corners[face] = normals;
        }

        return corners;
    }

    private static Vector3 ToBoneSpace(Vector3 posedNormal, Matrix pose, Vector3 authored)
    {
        if (posedNormal.LengthSquared() > 1e-12f && MathF.Abs(pose.Determinant()) > 1e-12f)
        {
            var local = Vector3.TransformNormal(posedNormal, Matrix.Invert(pose));
            if (local.LengthSquared() > 1e-12f)
            {
                return Vector3.Normalize(local);
            }
        }

        return authored.LengthSquared() > 1e-6f ? Vector3.Normalize(authored) : Vector3.Up;
    }

    private static int Quantize(float value) => (int)MathF.Round(value / PositionWeld);

    private static bool IsValid(PristonModelFace face, int vertexCount) =>
        (uint)face.A < (uint)vertexCount && (uint)face.B < (uint)vertexCount && (uint)face.C < (uint)vertexCount;
}
