using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Formats;

namespace PristonTale.Engine3D.Rendering;

internal static class PristonModelGeometry
{
    internal static Dictionary<int, List<PristonCornerVertex>> BuildGroups(
        PristonModel model, Matrix world, Color color, PristonModel skeleton = null, float frame = 0)
    {
        var groups = new Dictionary<int, List<PristonCornerVertex>>();
        var transforms = new PristonModelAnimator(skeleton ?? model).Evaluate(frame);
        Dictionary<string, int> bones = null;
        if (skeleton is not null)
        {
            bones = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < skeleton.Objects.Count; index++)
            {
                bones.TryAdd(skeleton.Objects[index].Name, index);
            }
        }

        for (var objectIndex = 0; objectIndex < model.Objects.Count; objectIndex++)
        {
            var obj = model.Objects[objectIndex];
            var transform = transforms[Math.Min(objectIndex, transforms.Length - 1)] * world;
            Vector3 Position(int vertexIndex)
            {
                int bone = bones is null ? -1 : ResolvePhysiqueBone(obj, vertexIndex, bones);
                var pose = bone >= 0 ? transforms[bone] * world : transform;
                return Vector3.Transform(obj.Vertices[vertexIndex].Position, pose);
            }

            foreach (var face in obj.Faces)
            {
                if (!ValidFace(face.A, face.B, face.C, obj.Vertices.Count))
                {
                    continue;
                }

                PristonCornerVertex.AddTriangle(
                    GetGroup(groups, face.MaterialIndex),
                    Position(face.A),
                    Position(face.B),
                    Position(face.C),
                    color,
                    color,
                    color,
                    obj.TexLinks,
                    face.TexLinkIndex);
            }
        }

        return groups;
    }

    internal static int ResolvePhysiqueBone(PristonModelObject obj, int vertexIndex, Dictionary<string, int> boneLookup)
    {
        if (!obj.HasPhysique || vertexIndex < 0 || vertexIndex >= obj.PhysiqueBoneNames.Count)
        {
            return -1;
        }

        string name = obj.PhysiqueBoneNames[vertexIndex];
        return name.Length > 0 && boneLookup.TryGetValue(name, out int index) ? index : -1;
    }

    private static List<PristonCornerVertex> GetGroup(Dictionary<int, List<PristonCornerVertex>> groups, int material)
    {
        if (!groups.TryGetValue(material, out var list))
        {
            list = [];
            groups.Add(material, list);
        }

        return list;
    }

    private static bool ValidFace(int a, int b, int c, int vertexCount) =>
        a >= 0 && b >= 0 && c >= 0 && a < vertexCount && b < vertexCount && c < vertexCount;

}
