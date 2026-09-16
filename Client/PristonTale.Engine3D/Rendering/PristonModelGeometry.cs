using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Engine3D.Formats;

namespace PristonTale.Engine3D.Rendering;

internal static class PristonModelGeometry
{
    internal static Dictionary<int, List<VertexPositionColorTexture>> BuildGroups(
        PristonModel model, Matrix world, Color color, PristonModel skeleton = null, float frame = 0)
    {
        var groups = new Dictionary<int, List<VertexPositionColorTexture>>();
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

                var tex = face.TexLinkIndex >= 0 && face.TexLinkIndex < obj.TexLinks.Count ? obj.TexLinks[face.TexLinkIndex] : PristonTexLink.Empty;
                var list = GetGroup(groups, face.MaterialIndex);
                AddTriangle(
                    list,
                    Position(face.A),
                    Position(face.B),
                    Position(face.C),
                    color,
                    color,
                    color,
                    tex);
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

    private static List<VertexPositionColorTexture> GetGroup(Dictionary<int, List<VertexPositionColorTexture>> groups, int material)
    {
        if (!groups.TryGetValue(material, out var list))
        {
            list = [];
            groups.Add(material, list);
        }

        return list;
    }

    private static void AddTriangle(
        List<VertexPositionColorTexture> vertices,
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Color ca,
        Color cb,
        Color cc,
        PristonTexLink texLink)
    {
        var ta = texLink.A;
        var tb = texLink.B;
        var tc = texLink.C;
        vertices.Add(new VertexPositionColorTexture(a, ca, ta));
        vertices.Add(new VertexPositionColorTexture(b, cb, tb));
        vertices.Add(new VertexPositionColorTexture(c, cc, tc));
    }

    private static bool ValidFace(int a, int b, int c, int vertexCount) =>
        a >= 0 && b >= 0 && c >= 0 && a < vertexCount && b < vertexCount && c < vertexCount;

}
