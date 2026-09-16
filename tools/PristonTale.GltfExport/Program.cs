using System.Globalization;
using PristonTale.Engine3D.Formats;
using PristonTale.GltfExport;
using SharpGLTF.Scenes;

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    Console.WriteLine(
        "ptgltf <asset-path> [output.glb|output.gltf] [--asset-root <dir>] [--recenter] [--scale <f>] [--flip-winding] [--flip-v] [--no-anims]\n"
        + "  Exports a PristonTale .smd (stage/model), .smb (model) or .inx (skinned actor) to glTF/GLB.\n"
        + "  --recenter moves geometry to the origin; --scale <f> applies an explicit unit conversion.");
    return args.Length == 0 ? 1 : 0;
}

var input = args[0];
if (!File.Exists(input))
{
    Console.Error.WriteLine($"error: file not found: {input}");
    return 1;
}

string? temporaryDirectory = null;
try
{
    var remaining = new List<string>(args.Skip(1));
    var flipWinding = TakeFlag(remaining, "--flip-winding");
    var flipV = TakeFlag(remaining, "--flip-v");
    var recenter = TakeFlag(remaining, "--recenter");
    var noAnimations = TakeFlag(remaining, "--no-anims");
    var assetRootOption = TakeOption(remaining, "--asset-root");
    var scaleOption = TakeOption(remaining, "--scale");
    var scale = ParseScale(scaleOption);

    var unknownOption = remaining.FirstOrDefault(
        static argument => argument.StartsWith("--", StringComparison.Ordinal));
    if (unknownOption is not null)
    {
        throw new ArgumentException($"unknown option: {unknownOption}");
    }

    if (remaining.Count > 1)
    {
        throw new ArgumentException("only one output path may be supplied.");
    }

    var output = remaining.Count == 1
        ? remaining[0]
        : Path.ChangeExtension(input, ".glb");
    var outputExtension = Path.GetExtension(output);
    if (!outputExtension.Equals(".glb", StringComparison.OrdinalIgnoreCase)
        && !outputExtension.Equals(".gltf", StringComparison.OrdinalIgnoreCase))
    {
        throw new ArgumentException("the output extension must be .glb or .gltf.");
    }

    output = Path.GetFullPath(output);
    var outputDirectory = Path.GetDirectoryName(output)
        ?? throw new InvalidOperationException("the output path has no parent directory.");
    Directory.CreateDirectory(outputDirectory);

    var options = new ExportOptions(
        flipWinding,
        flipV,
        recenter,
        scale,
        noAnimations);
    temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"ptgltf_{Environment.ProcessId}_{Guid.NewGuid():N}");
    Directory.CreateDirectory(temporaryDirectory);

    var extension = Path.GetExtension(input);
    SceneBuilder scene;
    string summary;

    if (extension.Equals(".inx", StringComparison.OrdinalIgnoreCase))
    {
        var assetRoot = assetRootOption ?? FindAssetRoot(input)
            ?? throw new InvalidOperationException(
                "could not locate an 'assets' root; pass --asset-root <dir>.");
        if (!Directory.Exists(assetRoot))
        {
            throw new DirectoryNotFoundException($"asset root not found: {assetRoot}");
        }

        if (options.Recenter)
        {
            Console.Error.WriteLine(
                "note: --recenter is ignored for skinned actors because translation above the armature cancels during skinning; --scale is applied.");
        }

        var actor = new PristonActorLoader().Load(assetRoot, input);
        var meshDirectory = Path.GetDirectoryName(Path.GetFullPath(actor.Mesh.Path))
            ?? throw new InvalidOperationException("the actor mesh path has no parent directory.");
        var materials = new MaterialFactory(
            meshDirectory,
            temporaryDirectory,
            forceDoubleSided: true);
        scene = GltfSceneExport.BuildActor(actor, materials, options);
        summary = $"actor: {actor.Mesh.Objects.Count} mesh objects, {actor.Skeleton.Objects.Count} bones, "
            + $"{materials.GeneratedPngs} textures ({materials.MissingTextures} missing or invalid)";
    }
    else
    {
        if (assetRootOption is not null)
        {
            throw new ArgumentException("--asset-root is only valid for .inx inputs.");
        }

        if (!extension.Equals(".smd", StringComparison.OrdinalIgnoreCase)
            && !extension.Equals(".smb", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"unsupported input extension '{extension}'; expected .smd, .smb or .inx.");
        }

        var inputDirectory = Path.GetDirectoryName(Path.GetFullPath(input))
            ?? throw new InvalidOperationException("the input path has no parent directory.");
        var materials = new MaterialFactory(inputDirectory, temporaryDirectory);
        scene = BuildSmdScene(
            input,
            materials,
            options,
            out summary);
    }

    var gltf = scene.ToGltf2();
    if (outputExtension.Equals(".glb", StringComparison.OrdinalIgnoreCase))
    {
        gltf.SaveGLB(output);
    }
    else
    {
        gltf.SaveGLTF(output);
    }

    Console.WriteLine($"wrote {output}");
    Console.WriteLine($"  {summary}");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"error exporting {input}: {exception.Message}");
    if (Environment.GetEnvironmentVariable("PTGLTF_DEBUG") == "1")
    {
        Console.Error.WriteLine(exception);
    }

    return 1;
}
finally
{
    if (temporaryDirectory is not null)
    {
        try
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            if (Environment.GetEnvironmentVariable("PTGLTF_DEBUG") == "1")
            {
                Console.Error.WriteLine(
                    $"temporary directory cleanup failed for '{temporaryDirectory}': {exception.Message}");
            }
        }
    }
}

static SceneBuilder BuildSmdScene(
    string input,
    MaterialFactory materials,
    ExportOptions options,
    out string summary)
{
    var reader = new PristonSmdReader();
    PristonStage stage;
    try
    {
        stage = reader.ReadStage(input);
    }
    catch (InvalidDataException stageException)
    {
        PristonModel model;
        try
        {
            model = reader.ReadModel(input);
        }
        catch (InvalidDataException modelException)
        {
            throw new InvalidDataException(
                $"the file is neither a supported stage nor model. Stage parser: {stageException.Message} Model parser: {modelException.Message}",
                modelException);
        }

        var modelScene = GltfSceneExport.BuildModel(model, materials, options);
        summary = $"model: {model.Objects.Count} objects, animated={model.IsAnimated}, "
            + $"{materials.GeneratedPngs} textures ({materials.MissingTextures} missing or invalid)";
        return modelScene;
    }

    var stageScene = GltfSceneExport.BuildStage(stage, materials, options);
    summary = $"stage: {stage.Vertices.Count} verts, {stage.Faces.Count} faces, "
        + $"{materials.GeneratedPngs} textures ({materials.MissingTextures} missing or invalid)";
    return stageScene;
}

static float ParseScale(string? value)
{
    if (value is null)
    {
        return 1f;
    }

    if (!float.TryParse(
            value,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var scale)
        || !float.IsFinite(scale)
        || scale <= 0f)
    {
        throw new ArgumentException($"invalid --scale value '{value}'; expected a positive finite number.");
    }

    return scale;
}

static bool TakeFlag(List<string> arguments, string name)
{
    var found = false;
    for (var index = arguments.Count - 1; index >= 0; index--)
    {
        if (string.Equals(arguments[index], name, StringComparison.OrdinalIgnoreCase))
        {
            arguments.RemoveAt(index);
            found = true;
        }
    }

    return found;
}

static string? TakeOption(List<string> arguments, string name)
{
    var index = arguments.FindIndex(
        argument => string.Equals(argument, name, StringComparison.OrdinalIgnoreCase));
    if (index < 0)
    {
        return null;
    }

    if (index + 1 >= arguments.Count
        || arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
    {
        throw new ArgumentException($"option {name} requires a value.");
    }

    var duplicateIndex = arguments.FindIndex(
        index + 1,
        argument => string.Equals(argument, name, StringComparison.OrdinalIgnoreCase));
    if (duplicateIndex >= 0)
    {
        throw new ArgumentException($"option {name} may only be supplied once.");
    }

    var value = arguments[index + 1];
    arguments.RemoveAt(index + 1);
    arguments.RemoveAt(index);
    return value;
}

static string? FindAssetRoot(string assetPath)
{
    var startingDirectory = Path.GetDirectoryName(Path.GetFullPath(assetPath))
        ?? throw new InvalidOperationException("the asset path has no parent directory.");
    for (var directory = new DirectoryInfo(startingDirectory);
         directory is not null;
         directory = directory.Parent)
    {
        if (string.Equals(directory.Name, "assets", StringComparison.OrdinalIgnoreCase))
        {
            return directory.FullName;
        }
    }

    return null;
}
