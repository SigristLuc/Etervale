using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Rendering;

namespace PristonTale.Tests;

internal static class ModelNormalTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("skinned model normals follow posed faces instead of averaged or inverted authored normals", CubeAsync);
        yield return ("skinned model normals smooth gentle folds and keep hard edges", FoldAsync);
    }

    private static Task CubeAsync()
    {
        var corners = new Vector3[8];
        for (var i = 0; i < 8; i++)
        {
            corners[i] = new Vector3((i & 1) == 0 ? -10 : 10, (i & 2) == 0 ? -10 : 10, (i & 4) == 0 ? -10 : 10);
        }

        // The legacy defect: one vertex per corner whose normal averages three perpendicular faces, half of them inverted.
        var vertices = corners.Select((position, i) => new PristonModelVertex(position, Vector3.Normalize(i % 2 == 0 ? position : -position))).ToArray();
        int[][] quads = [[0, 1, 3, 2], [4, 6, 7, 5], [0, 4, 5, 1], [2, 3, 7, 6], [0, 2, 6, 4], [1, 5, 7, 3]];
        var faces = new List<PristonModelFace>();
        foreach (var quad in quads)
        {
            faces.Add(Outward(corners, quad[0], quad[1], quad[2]));
            faces.Add(Outward(corners, quad[0], quad[2], quad[3]));
        }

        var pose = Matrix.CreateRotationX(MathHelper.PiOver2) * Matrix.CreateRotationY(0.3f) * Matrix.CreateTranslation(100, 5, -40);
        var model = Model(vertices, faces);
        var normals = PristonModelNormals.Build(model, (_, _) => pose)[0];
        foreach (var face in faces)
        {
            var expected = PosedFaceNormal(vertices, face, pose);
            foreach (var corner in normals[face])
            {
                var shaded = Vector3.Normalize(Vector3.TransformNormal(corner, pose));
                Assert.True(Vector3.Dot(shaded, expected) > 0.9999f, $"cube corner normal {shaded} does not follow its posed face {expected}");
            }
        }

        return Task.CompletedTask;
    }

    private static Task FoldAsync()
    {
        // Two quads hinged on the x axis: a 20 degree fold is smoothed across the hinge, a 90 degree fold stays hard.
        foreach (var (degrees, smooth) in new[] { (20f, true), (90f, false) })
        {
            var tilt = MathHelper.ToRadians(degrees);
            var far = new Vector3(0, MathF.Sin(tilt) * 10, MathF.Cos(tilt) * 10);
            Vector3[] positions = [new(-10, 0, 0), new(10, 0, 0), new(-10, 0, -10), new(10, 0, -10), new Vector3(-10, 0, 0) + far, new Vector3(10, 0, 0) + far];
            var vertices = positions.Select(position => new PristonModelVertex(position, Vector3.Up)).ToArray();
            List<PristonModelFace> faces = [Upward(positions, 0, 2, 3), Upward(positions, 0, 3, 1), Upward(positions, 0, 1, 5), Upward(positions, 0, 5, 4)];
            var normals = PristonModelNormals.Build(Model(vertices, faces), (_, _) => Matrix.Identity)[0];
            var flat = normals[faces[0]][0];
            var folded = normals[faces[2]][0];
            if (smooth)
            {
                Assert.True(Vector3.Dot(flat, folded) > 0.9999f, "a gentle fold split its hinge normal");
                Assert.True(Vector3.Dot(flat, Vector3.Up) < 0.999f, "a gentle fold did not blend the hinge normal");
            }
            else
            {
                Assert.True(Vector3.Dot(flat, Vector3.Up) > 0.9999f, "a hard edge leaked into the flat face");
                Assert.True(Vector3.Dot(flat, folded) < 0.1f, "a hard edge was smoothed");
            }
        }

        return Task.CompletedTask;
    }

    private static PristonModel Model(PristonModelVertex[] vertices, List<PristonModelFace> faces)
    {
        var obj = new PristonModelObject("fixture", "", false, Matrix.Identity, Vector3.Zero,
            vertices, faces, [], [], [], [], [], [], [], [], []);
        return new PristonModel("fixture", [], [obj], [], default);
    }

    private static PristonModelFace Outward(Vector3[] positions, int a, int b, int c)
    {
        var centre = (positions[a] + positions[b] + positions[c]) / 3f;
        var normal = Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]);
        return Vector3.Dot(normal, centre) >= 0f ? new PristonModelFace(a, b, c, 0, -1) : new PristonModelFace(a, c, b, 0, -1);
    }

    private static PristonModelFace Upward(Vector3[] positions, int a, int b, int c)
    {
        var normal = Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]);
        return normal.Y >= 0f ? new PristonModelFace(a, b, c, 0, -1) : new PristonModelFace(a, c, b, 0, -1);
    }

    private static Vector3 PosedFaceNormal(PristonModelVertex[] vertices, PristonModelFace face, Matrix pose)
    {
        var a = Vector3.Transform(vertices[face.A].Position, pose);
        var b = Vector3.Transform(vertices[face.B].Position, pose);
        var c = Vector3.Transform(vertices[face.C].Position, pose);
        return Vector3.Normalize(Vector3.Cross(b - a, c - a));
    }
}
