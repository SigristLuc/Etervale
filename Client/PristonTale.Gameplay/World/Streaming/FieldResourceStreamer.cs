using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Physics;
using PristonTale.Engine3D.Rendering;
using PristonTale.Simulation.World;

namespace PristonTale.Gameplay.World.Streaming;

internal sealed class FieldResourceStreamer : IDisposable
{
    private readonly string _assetRoot;
    private readonly PristonSmdReader _reader;
    private readonly PristonRenderer _renderer;
    private readonly IClientLog _log;
    private readonly Action<FieldLighting> _configureLighting;

    public FieldResourceStreamer(
        string assetRoot,
        PristonSmdReader reader,
        PristonRenderer renderer,
        IClientLog log,
        Action<FieldLighting> configureLighting)
    {
        _assetRoot = assetRoot;
        _reader = reader;
        _renderer = renderer;
        _log = log;
        _configureLighting = configureLighting;
    }

    private sealed record PreparedField(
        FieldDef Definition,
        PristonStage Stage,
        PristonModel[] Models,
        PristonCollisionWorld Collision,
        long Started);
    private readonly Dictionary<string, Task<PreparedField>> _fieldLoads = new();
    private readonly HashSet<string> _failedFieldLoads = new();

    public void Request(FieldDef field)
    {
        if (_fieldLoads.Count + (_stageUpload is null ? 0 : 1) >= FieldStreamingPolicy.MaximumPendingFields
            || _fieldLoads.ContainsKey(field.Id)
            || _failedFieldLoads.Contains(field.Id)
            || _uploadingField?.Definition.Id == field.Id)
        {
            return;
        }

        _log.Info($"[Stream] preparing {field.Id}.");
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var root = _assetRoot;
        var renderer = _renderer;
        _fieldLoads.Add(field.Id, Task.Run(async () =>
        {
            var stage = _reader.ReadStage(AssetPath(root, field.StageSmd));
            var models = field.StageObjectSmds.Select(path => _reader.ReadModel(AssetPath(root, path))).ToArray();
            var collision = new PristonCollisionWorld(stage);
            await renderer.PrepareStageTexturesAsync(stage, models);
            var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            _log.Info($"[Stream] CPU ready {field.Id}: {elapsed:F1} ms; {stage.Faces.Count} faces, {models.Length} models.");
            return new PreparedField(field, stage, models, collision, started);
        }));
    }

    private PreparedField _uploadingField;
    private IEnumerator<RenderStageHandle> _stageUpload;
    private double _maximumFieldUploadMilliseconds;
    private int _fieldUploadFrames;

    public LoadedField Update()
    {
        if (_stageUpload is null)
        {
            string completed = null;
            foreach (var (id, preparation) in _fieldLoads)
            {
                if (!preparation.IsCompleted)
                {
                    continue;
                }

                completed = id;
                if (preparation.IsCompletedSuccessfully)
                {
                    _uploadingField = preparation.Result;
                    _configureLighting(_uploadingField.Definition.Lighting);
                    _maximumFieldUploadMilliseconds = 0d;
                    _fieldUploadFrames = 0;
                    _stageUpload = _renderer.AddStageIncrementally(_uploadingField.Stage, _uploadingField.Models);
                }
                else
                {
                    _failedFieldLoads.Add(id);
                    _log.Error($"[Stream] field preparation failed for {id}: {preparation.Exception}");
                }

                break;
            }

            if (completed is not null)
            {
                _fieldLoads.Remove(completed);
            }
        }

        if (_stageUpload is null)
        {
            return null;
        }

        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        _fieldUploadFrames++;
        try
        {
            // A soft budget between indivisible GPU calls, plus a hard cap on steps per frame.
            for (var step = 0; step < FieldStreamingPolicy.MaximumUploadSteps; step++)
            {
                if (!_stageUpload.MoveNext())
                {
                    throw new InvalidOperationException("Stage upload ended without publishing its handle.");
                }

                var handle = _stageUpload.Current;
                if (handle is not null)
                {
                    var loaded = new LoadedField(_uploadingField.Definition, _uploadingField.Stage, _uploadingField.Collision, handle);
                    _maximumFieldUploadMilliseconds = Math.Max(
                        _maximumFieldUploadMilliseconds,
                        System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                    var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(_uploadingField.Started).TotalMilliseconds;
                    _log.Info($"[Stream] resident {_uploadingField.Definition.Id}: {elapsed:F1} ms total; " +
                        $"{_fieldUploadFrames} upload frames; max Update {_maximumFieldUploadMilliseconds:F2} ms.");
                    _stageUpload.Dispose();
                    _stageUpload = null;
                    _uploadingField = null;
                    return loaded;
                }

                if (System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds >= FieldStreamingPolicy.UploadBudgetMilliseconds)
                {
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            _failedFieldLoads.Add(_uploadingField.Definition.Id);
            _log.Error($"[Stream] GPU publication failed for {_uploadingField.Definition.Id}: {ex}");
            _stageUpload.Dispose();
            _stageUpload = null;
            _uploadingField = null;
        }
        finally
        {
            _maximumFieldUploadMilliseconds = Math.Max(
                _maximumFieldUploadMilliseconds,
                System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }

        return null;
    }

    private static string AssetPath(string assetRoot, string relativePath)
    {
        var normalized = relativePath.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        return Path.Combine(assetRoot, normalized);
    }

    private async Task ObservePreparationAsync(Task preparation)
    {
        try
        {
            await preparation;
        }
        catch (Exception ex)
        {
            _log.Error($"[Stream] abandoned preparation failed: {ex}");
        }
    }

    public void Dispose()
    {
        _stageUpload?.Dispose();
        _stageUpload = null;
        foreach (var preparation in _fieldLoads.Values)
        {
            _ = ObservePreparationAsync(preparation);
        }

        _fieldLoads.Clear();
    }
}
