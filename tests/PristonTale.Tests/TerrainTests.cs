using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using PristonTale.Application.World;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Physics;
using PristonTale.Geometry;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.World;

namespace PristonTale.Tests;

internal static class TerrainTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("server stage collision reader matches the client SMD reader in Priston axes", ReaderParityAsync);
        yield return ("field terrain blocks walls and high steps, settles drops and spawns on the floor", TerrainStepAsync);
        yield return ("movement reports off the floor or through walls are rejected and take the floor height", ReportTerrainAsync);
        yield return ("many small movement reports in one tick share one distance budget", ReportBudgetAsync);
        yield return ("small movement reports cannot pass a wall but may cut a corner", ReportWallSegmentsAsync);
    }

    private static Task ReaderParityAsync()
    {
        var bytes = Stage(
            [new(0, 10, 0), new(200, 10, 0), new(200, 30, 200), new(0, 30, 200), new(100, 0, 100), new(100, 120, 100), new(100, 0, 140)],
            [(0, 1, 2), (0, 2, 3), (4, 5, 6)]);
        var path = Path.Combine(Path.GetTempPath(), $"terrain-{Guid.NewGuid():N}.smd");
        File.WriteAllBytes(path, bytes);
        try
        {
            var server = StageCollisionReader.Read(bytes);
            var clientStage = new PristonSmdReader().ReadStage(path);
            Assert.True(clientStage.Materials.Single().MeshState == StageCollisionReader.CheckFace, "fixture MeshState offset disagrees with the client reader");
            var client = new PristonCollisionWorld(clientStage);
            Assert.True(server.CollisionTriangleCount == 3 && server.FloorTriangleCount == 2, "check-face triangles were not classified");
            foreach (var (x, z) in new[] { (10f, 10f), (100f, 50f), (190f, 190f), (50f, 150f) })
            {
                Assert.True(server.TryGetFloorHeight(x, z, out var serverHeight), "server lost the floor");
                Assert.True(client.TryGetFloorHeight(new Microsoft.Xna.Framework.Vector3(x, 0, -z), out var clientHeight), "client lost the floor");
                Assert.Near(clientHeight, serverHeight, 0.001f);
            }

            Assert.True(!server.TryGetFloorHeight(500, 500, out _), "floor reported outside the stage");
            Assert.True(StageCollisionReader.Read(Stage([new(0, 0, 0), new(1, 0, 0), new(0, 0, 1)], [(0, 1, 2)], meshState: 0)).CollisionTriangleCount == 0,
                "a face without the check-face flag became collision");
        }
        finally
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    private static Task TerrainStepAsync()
    {
        var terrain = new FieldTerrain(StageCollisionReader.Read(Stage(
            [new(0, 0, 100), new(400, 0, 100), new(400, 0, 400), new(0, 0, 400), new(200, 0, 0), new(200, 200, 0), new(200, 0, 400),
             new(300, 50, 0), new(400, 50, 0), new(400, 50, 100), new(300, 50, 100)],
            [(0, 1, 2), (0, 2, 3), (4, 5, 6), (7, 8, 9), (7, 9, 10)])));
        Assert.True(terrain.TryStep(new Vector3(50, 0, 150), new Vector3(60, 5, 160), out var walked) && walked == new Vector3(60, 0, 160),
            "open floor step was blocked or kept the client height");
        Assert.True(!terrain.TryStep(new Vector3(190, 0, 300), new Vector3(210, 0, 300), out var blocked) && blocked == new Vector3(190, 0, 300),
            "a wall did not block the step");
        Assert.True(!terrain.TryStep(new Vector3(350, 0, 150), new Vector3(350, 0, 90), out _), "a 50-unit ledge was climbed in one step");
        Assert.True(terrain.TryStep(new Vector3(350, 50, 90), new Vector3(350, 50, 150), out var fallen) && fallen.Y == 0, "stepping off a ledge did not fall");
        Assert.True(terrain.PlaceOnFloor(new Vector3(350, 0, 50)).Y == 50, "spawns did not use the highest floor");
        Assert.True(terrain.SettleNear(new Vector3(100, 20, 150), FieldTerrain.DropTolerance).Y == 0, "a drop did not settle on the floor below");
        Assert.True(FieldTerrain.None.TryStep(Vector3.Zero, new Vector3(5, 7, 5), out var free) && free == new Vector3(5, 7, 5),
            "a field without geometry changed movement");
        return Task.CompletedTask;
    }

    private static Task ReportTerrainAsync()
    {
        var terrain = new FieldTerrain(StageCollisionReader.Read(Stage(
            [new(0, 4, 0), new(400, 4, 0), new(400, 4, 400), new(0, 4, 400), new(200, 0, 0), new(200, 200, 0), new(200, 0, 400)],
            [(0, 1, 2), (0, 2, 3), (4, 5, 6)])));
        var character = new CharacterClassRegistry().CreatePlayer(PlayerClass.Fighter, "Ground");
        var player = new WorldPlayer(1, new Vector3(150, 0, 100), character, false, default);
        Assert.True(player.ReportMovement(new Vector3(170, 99, 100), 0f, true, true, 0, 500, terrain) == MovementVerdict.Accepted
            && player.Position == new Vector3(170, 4, 100), "an on-floor report was rejected or kept the client height");
        Assert.True(player.ReportMovement(new Vector3(190, 4, 100), 0f, true, true, 0, 800, terrain) == MovementVerdict.Accepted,
            "a short report beside a wall was rejected");
        Assert.True(player.ReportMovement(new Vector3(260, 4, 100), 0f, true, true, 0, 1500, terrain) == MovementVerdict.Rejected
            && player.Position.X == 190, "a long report through a wall was accepted");
        var edge = new WorldPlayer(2, new Vector3(390, 4, 100), character, false, default);
        Assert.True(edge.ReportMovement(new Vector3(450, 4, 100), 0f, true, true, 0, 500, terrain) == MovementVerdict.Rejected,
            "a report off the stage floor was accepted");
        return Task.CompletedTask;
    }

    private static Task ReportBudgetAsync()
    {
        var character = new CharacterClassRegistry().CreatePlayer(PlayerClass.Fighter, "Budget");
        var player = new WorldPlayer(1, Vector3.Zero, character, false, default);
        var budget = Locomotion.RunUnitsPerSecond(character.Derived.MoveSpeed) * 1.35f * 0.5f + 32f;
        var rejected = 0;
        for (var report = 1; report <= 40; report++)
        {
            if (player.ReportMovement(new Vector3(player.Position.X + 20, 0, 0), 0f, true, true, 0, 1000) == MovementVerdict.Rejected)
            {
                rejected++;
            }
        }

        Assert.True(rejected > 0 && player.Position.X <= budget + 0.01f, $"one tick of small reports moved {player.Position.X} units, budget {budget}");
        var held = player.Position.X;
        Assert.True(player.ReportMovement(new Vector3(held + 20, 0, 0), 0f, true, true, 0, 2000) == MovementVerdict.Accepted,
            "the budget did not refill with time");
        return Task.CompletedTask;
    }

    private static Task ReportWallSegmentsAsync()
    {
        var vertices = new List<Vector3> { new(0, 4, 0), new(400, 4, 0), new(400, 4, 400), new(0, 4, 400) };
        var faces = new List<(int, int, int)> { (0, 1, 2), (0, 2, 3) };
        void Wall(float x0, float z0, float x1, float z1)
        {
            var first = vertices.Count;
            vertices.AddRange([new(x0, 0, z0), new(x1, 0, z1), new(x1, 300, z1), new(x0, 300, z0)]);
            faces.Add((first, first + 1, first + 2));
            faces.Add((first, first + 2, first + 3));
        }

        Wall(100, 250, 100, 400);
        Wall(250, 150, 300, 150);
        Wall(300, 150, 300, 200);
        Wall(300, 200, 250, 200);
        Wall(250, 200, 250, 150);
        var terrain = new FieldTerrain(StageCollisionReader.Read(Stage(vertices.ToArray(), faces.ToArray())));
        var character = new CharacterClassRegistry().CreatePlayer(PlayerClass.Fighter, "Segments");
        var player = new WorldPlayer(1, new Vector3(90, 4, 300), character, false, default);
        var now = 1000L;
        foreach (var x in new[] { 95f, 103f, 108f, 113f, 118f })
        {
            now += 50;
            player.ReportMovement(new Vector3(x, 4, 300), 0f, true, false, 0, now, terrain);
            Assert.True(player.Position.X < 100f, $"small reports carried the player through a wall to {player.Position.X}");
        }

        var cutter = new WorldPlayer(2, new Vector3(240, 4, 160), character, false, default);
        Assert.True(cutter.ReportMovement(new Vector3(256, 4, 146), 0f, true, false, 0, 500, terrain) == MovementVerdict.Accepted,
            "a report cutting a pillar corner by a unit was rejected");
        Assert.True(cutter.Position.X < 250f || cutter.Position.Z < 150f, $"the server kept a point inside the pillar: {cutter.Position}");
        return Task.CompletedTask;
    }

    // Minimal stage SMD: file header (556), stage header (262260), one material group with one unused material record,
    // then 28-byte vertices and faces, matching the reader offsets of both client and server.
    internal static byte[] Stage(Vector3[] vertices, (int A, int B, int C)[] faces, uint meshState = StageCollisionReader.CheckFace)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        var signature = new byte[24];
        Encoding.ASCII.GetBytes("SMD Stage data Ver 0.72").CopyTo(signature, 0);
        writer.Write(signature);
        writer.Write(0);
        writer.Write(1);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(new byte[32 * 16]);
        writer.Write(0);
        writer.Write(new byte[256 * 256 * 4]);
        writer.Write(new byte[12 * 4]);
        writer.Write(vertices.Length);
        writer.Write(faces.Length);
        writer.Write(new byte[5 * 4 + 3 * 4 + 2 * 4 + 4 * 4]);
        writer.Write(0);
        writer.Write(0);
        writer.Write(1u);
        writer.Write(0);
        writer.Write(1);
        writer.Write(0);
        writer.Write(new byte[64]);
        var material = new byte[320];
        BinaryPrimitives.WriteUInt32LittleEndian(material.AsSpan(168), meshState);
        writer.Write(material);
        foreach (var vertex in vertices)
        {
            writer.Write(0u);
            writer.Write(0u);
            writer.Write((int)(vertex.X * 256));
            writer.Write((int)(vertex.Y * 256));
            writer.Write((int)(vertex.Z * 256));
            writer.Write(new byte[8]);
        }

        foreach (var (a, b, c) in faces)
        {
            writer.Write(0u);
            writer.Write(0);
            writer.Write((ushort)a);
            writer.Write((ushort)b);
            writer.Write((ushort)c);
            writer.Write((ushort)0);
            writer.Write(0u);
            writer.Write(new byte[8]);
        }

        return stream.ToArray();
    }
}
