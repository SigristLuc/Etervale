using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Rendering;
using PristonTale.GltfExport;

namespace PristonTale.Tests;

internal static class RenderTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("unused material catalogs do not enter streaming texture preparation", MaterialUsageAsync);
        yield return ("off-camera geometry remains eligible to cast shadows", ShadowAsync);
        yield return ("static mesh preparation computes topology and normals without a GPU", MeshAsync);
        yield return ("CPU image decoding handles invalid data without GPU resources", DecodeAsync);
        yield return ("PNG export cache separates opacity variants and basename collisions", ExportAsync);
    }

    private static Task MaterialUsageAsync()
    {
        var used = new PristonMaterial(true, 1, 0, 1, false, Vector3.One, 0, 0,
            PristonMaterialSurfaceFlags.None, "visible.bmp", "", [], [], 0, 0, 0);
        var unused = used with { TextureName = "nonexistent-unused.bmp" };
        var stage = new PristonStage("fixture.smd", [used, unused],
            [new(Vector3.Zero, Color.White), new(Vector3.UnitX, Color.White), new(Vector3.UnitZ, Color.White)],
            [new(0, 1, 2, 0, -1), new(0, 1, 2, 0, -1)], [], [], default);
        var selected = PristonMaterialUsage.ForStage(stage).ToArray();
        Assert.True(selected.Length == 1 && selected[0] == used, "unused/duplicate material slots generated streaming work");
        var high = new PristonModelObject("High", "", false, Matrix.Identity, Vector3.Zero,
            [], [new(0, 1, 2, 0, -1)], [], [], [], [], [], [], [], [], []);
        var low = high with { Name = "Low", Faces = new[] { new PristonModelFace(0, 1, 2, 1, -1) } };
        var model = new PristonModel("fixture", [used, unused], [high, low], [], default);
        var actor = new PristonActor(null!, model, model, ["HIGH"]);
        var actorMaterials = PristonMaterialUsage.ForActor(actor).ToArray();
        Assert.True(actorMaterials.Length == 1 && actorMaterials[0] == used, "actor prepared textures belonging to another LOD/armour");
        var allObjects = new PristonActor(null!, model, model, []);
        Assert.True(PristonMaterialUsage.ForActor(allObjects).Count() == 2, "actor without a render filter lost materials");
        return Task.CompletedTask;
    }

    private static Task ShadowAsync()
    {
        var material = new PristonMaterialRuntime(
            [],
            false,
            false,
            false,
            0,
            0,
            0,
            0,
            0,
            0,
            false,
            false,
            false,
            false,
            0,
            0);
        var batch = new RenderBatch(
            [],
            [],
            null!,
            material,
            ForceCastsShadow: true,
            ShadowCoveredByAggregate: true,
            IsSkinned: true,
            SkinnedBounds: new BoundingBox(new Vector3(100, 100, 100), new Vector3(101, 101, 101)));
        var aggregate = new RenderBatch(
            [],
            [],
            null!,
            material,
            ForceCastsShadow: true,
            IsSkinned: true,
            SkinnedBounds: new BoundingBox(new Vector3(100, 100, 100), new Vector3(101, 101, 101)));
        var frame = new PristonFrameContext(
            Matrix.Identity,
            Matrix.Identity,
            Vector3.Zero,
            0,
            PristonLighting.Default,
            Matrix.Identity,
            null!,
            true,
            1);
        var builder = new RenderQueueBuilder();
        Assert.True(builder.Build([batch], [aggregate], frame) == 1 && builder.Queue.Count == 0, "off-camera geometry entered colour pass");
        Assert.True(builder.Queue.ShadowCasters.Count == 1, "aggregate duplicated or lost the covered shadow caster");
        builder.Build([batch], [aggregate], frame, includeShadowCasters: false);
        Assert.True(builder.Queue.ShadowCasters.Count == 0, "shadow queue was rebuilt on a frame that reuses the previous maps");
        return Task.CompletedTask;
    }

    private static Task MeshAsync()
    {
        var a = new PristonCornerVertex(Vector3.Zero, Color.White, Vector2.Zero, Vector2.One, Vector2.Zero);
        var b = new PristonCornerVertex(Vector3.UnitZ, Color.White, Vector2.UnitY, Vector2.One, Vector2.Zero);
        var c = new PristonCornerVertex(Vector3.UnitX, Color.White, Vector2.UnitX, Vector2.One, Vector2.Zero);
        var builder = new PristonGpuMeshBuilder(null!);
        var prepared = builder.PrepareStaticMesh([a, b, c, a, b, c]);
        Assert.True(prepared.Vertices.Length == 3 && prepared.Indices.Length == 6, "mesh topology/deduplication changed");
        Assert.True(prepared.Vertices.All(vertex => vertex.Normal == Vector3.Up), "prepared normals changed");
        Assert.True(prepared.Vertices.All(vertex => vertex.TextureCoordinate1 == Vector2.One), "stage 1 UVs were lost");
        Assert.True(prepared.Bounds.Max == new Vector3(1, 0, 1), "prepared bounds changed");
        var otherLayerUv = a with { Uv1 = Vector2.Zero };
        Assert.True(builder.PrepareStaticMesh([a, b, c, otherLayerUv, b, c]).Vertices.Length == 4, "corners with different stage UVs were welded");
        return Task.CompletedTask;
    }

    private static Task DecodeAsync()
    {
        Assert.True(!PristonImageDecoder.Decode([], ".tga").IsValid, "empty TGA accepted");
        var image = PristonImageDecoder.Decode(Tga(20, 40, 60), ".tga");
        Assert.True(image.IsValid && image.Width == 1 && image.Height == 1, "valid TGA rejected");
        Assert.True(image.Pixels[0] == new Color(20, 40, 60, 255), "TGA channel order changed");
        var huge = Tga(0, 0, 0);
        huge[12] = 255;
        huge[13] = 255;
        huge[14] = 255;
        huge[15] = 255;
        Assert.True(!PristonImageDecoder.Decode(huge, ".tga").IsValid, "oversized/truncated TGA accepted");
        return Task.CompletedTask;
    }

    private static Task ExportAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "priston-export-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllBytes(Path.Combine(directory, "base.tga"), Tga(200, 180, 160));
            File.WriteAllBytes(Path.Combine(directory, "white.tga"), Tga(255, 255, 255));
            File.WriteAllBytes(Path.Combine(directory, "black.tga"), Tga(0, 0, 0));
            var material = new PristonMaterial(
                true,
                1,
                0,
                1,
                false,
                Vector3.One,
                0,
                0,
                PristonMaterialSurfaceFlags.None,
                "base.tga",
                "white.tga",
                [],
                [],
                0,
                0,
                0);
            var factory = new MaterialFactory(directory, directory);
            factory.Create(material, 0);
            factory.Create(material with { OpacityName = "black.tga" }, 1);
            Assert.True(factory.GeneratedPngs == 2, "opacity variants reused one PNG");
            var images = Directory.GetFiles(directory, "*.png");
            Assert.True(images.Length == 2, "variant filenames collided");
            var alphas = images.Select(path => PristonImageDecoder.Decode(File.ReadAllBytes(path), ".png").Pixels[0].A).Order().ToArray();
            Assert.True(alphas.SequenceEqual(new byte[] { 0, 255 }), "different opacity variants have identical pixels");
            factory.Create(material, 2);
            Assert.True(factory.GeneratedPngs == 2, "identical image was regenerated");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }

        return Task.CompletedTask;
    }

    private static byte[] Tga(byte red, byte green, byte blue)
    {
        var bytes = new byte[21];
        bytes[2] = 2;
        bytes[12] = 1;
        bytes[14] = 1;
        bytes[16] = 24;
        bytes[17] = 0x20;
        bytes[18] = blue;
        bytes[19] = green;
        bytes[20] = red;
        return bytes;
    }
}
