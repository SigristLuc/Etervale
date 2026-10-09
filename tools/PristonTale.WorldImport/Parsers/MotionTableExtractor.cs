using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.IO;
using PristonTale.Simulation.World.Motion;

namespace PristonTale.WorldImport.Parsers;

/// <summary>One motion table's usable clip rows, ready for the <c>motion_slots</c> table.</summary>
public sealed record ExtractedMotionSlot(
    int Slot, int State, int StartFrame, int EndFrame,
    long Event0, long Event1, long Event2, long Event3, bool Repeat);

/// <summary>
/// The <c>ptworld</c> INX motion-timing pass (combat-fidelity F2): read each distinct model's INX from the
/// game assets and extract its motion table — the raw clip windows + ×160 event offsets the authoritative
/// world host schedules combat from. Mirrors the client's resolution EXACTLY: read via
/// <see cref="PristonInxReader"/> (all file access through <see cref="PristonFileResolver"/> — the data
/// uses Windows casing + <c>\</c>); a model whose INX carries no usable motions (player class bodies)
/// borrows its <c>MotionFile</c> → <c>.inx</c> table (the <c>PristonActorLoader.ResolveMotionInfo</c>
/// convention); usable slot = <c>State != 0 &amp;&amp; EndFrame &gt; StartFrame</c>, scanning from the
/// character motion base — slot order is preserved (first slot per state is the clip the client plays).
/// Keys are <see cref="MotionTimingCatalog.NormalizeKey"/>-normalized so the runtime lookup matches.
/// </summary>
public sealed class MotionTableExtractor
{
    private readonly string _assetRoot;
    private readonly PristonInxReader _reader = new();

    // motion_table_key → rows (extracted once per distinct table; player bodies share one biped table).
    private readonly Dictionary<string, IReadOnlyList<ExtractedMotionSlot>> _tables = new();
    // model_key → motion_table_key.
    private readonly Dictionary<string, string> _models = new();

    public MotionTableExtractor(string assetRoot) => _assetRoot = assetRoot;

    public IReadOnlyDictionary<string, string> Models => _models;
    public IReadOnlyDictionary<string, IReadOnlyList<ExtractedMotionSlot>> Tables => _tables;

    /// <summary>
    /// Extract one model's motion table (idempotent per model). <paramref name="modelPath"/> is the raw
    /// config value (monster <c>.inf *모델</c> / player class body path — any extension/casing). Returns
    /// false (and reports why) when the INX cannot be resolved — the runtime falls back to F1 constants.
    /// </summary>
    public bool TryExtract(string modelPath, out string failure)
    {
        failure = null!;
        if (string.IsNullOrWhiteSpace(modelPath))
        {
            failure = "empty model path";
            return false;
        }

        var modelKey = MotionTimingCatalog.NormalizeKey(modelPath);
        if (_models.ContainsKey(modelKey))
            return true;

        var modelDiskPath = Path.Combine(_assetRoot, modelKey);
        if (!PristonFileResolver.Exists(modelDiskPath))
        {
            failure = $"inx not found: {modelKey}";
            return false;
        }

        if (!TryReadInfo(modelDiskPath, out var info, out failure))
        {
            return false;
        }
        var tableKey = modelKey;

        if (!HasUsableMotions(info))
        {
            // Borrow the MotionFile table (player class bodies → the shared biped INX).
            if (string.IsNullOrWhiteSpace(info.MotionFile))
            {
                failure = $"no motions and no MotionFile link: {modelKey}";
                return false;
            }

            tableKey = MotionTimingCatalog.NormalizeKey(info.MotionFile);
            if (!_tables.ContainsKey(tableKey))
            {
                var linkDiskPath = Path.Combine(_assetRoot, tableKey);
                if (!PristonFileResolver.Exists(linkDiskPath))
                {
                    failure = $"motion table inx not found: {tableKey} (for {modelKey})";
                    return false;
                }

                if (!TryReadInfo(linkDiskPath, out info, out failure))
                {
                    return false;
                }
                if (!HasUsableMotions(info))
                {
                    failure = $"motion table has no usable motions: {tableKey} (for {modelKey})";
                    return false;
                }
            }
        }

        if (!_tables.ContainsKey(tableKey))
            _tables[tableKey] = ExtractSlots(info);

        _models[modelKey] = tableKey;
        return true;
    }

    private bool TryReadInfo(string path, out PristonModelInfo info, out string failure)
    {
        try
        {
            info = _reader.Read(path);
            failure = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException)
        {
            info = null!;
            failure = $"invalid INX {path}: {exception.Message}";
            return false;
        }
    }

    private static bool HasUsableMotions(PristonModelInfo info)
    {
        for (var i = PristonFormatConstants.CharacterMotionBase; i < info.Motions.Count; i++)
        {
            var m = info.Motions[i];
            if (m.State != 0 && m.EndFrame > m.StartFrame)
                return true;
        }

        return false;
    }

    private static IReadOnlyList<ExtractedMotionSlot> ExtractSlots(PristonModelInfo info)
    {
        var slots = new List<ExtractedMotionSlot>();
        for (var i = PristonFormatConstants.CharacterMotionBase; i < info.Motions.Count; i++)
        {
            var m = info.Motions[i];
            if (m.State == 0 || m.EndFrame <= m.StartFrame)
                continue;

            var ev = m.EventFrames;
            slots.Add(new ExtractedMotionSlot(
                Slot: i,
                State: (int)m.State,
                StartFrame: (int)m.StartFrame,
                EndFrame: (int)m.EndFrame,
                Event0: ev.Count > 0 ? ev[0] : 0,
                Event1: ev.Count > 1 ? ev[1] : 0,
                Event2: ev.Count > 2 ? ev[2] : 0,
                Event3: ev.Count > 3 ? ev[3] : 0,
                Repeat: m.Repeat != 0));
        }

        return slots;
    }
}
