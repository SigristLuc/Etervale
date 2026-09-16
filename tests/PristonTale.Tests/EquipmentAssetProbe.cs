using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.World.Remote;
using PristonTale.Simulation.Characters;

namespace PristonTale.Tests;

internal static class EquipmentAssetProbe
{
    public static void ExportTimings(string root, string output)
    {
        var registry = new CharacterClassRegistry();
        var loader = new PristonActorLoader();
        var text = new System.Text.StringBuilder();
        text.AppendLine("// Generated from the shipped INX tables by --export-equipment-timings; do not edit individual timings.");
        text.AppendLine("namespace PristonTale.Simulation.World.Motion;");
        text.AppendLine("public static class PlayerEquipmentTiming");
        text.AppendLine("{");
        text.AppendLine("    private static readonly Dictionary<(int Job, string Key), MotionClip> Clips = Build();");
        text.AppendLine("    public static int Count => Clips.Count;");
        text.AppendLine("    public static bool TryGet(int job, string key, out MotionClip clip) => Clips.TryGetValue((job, key), out clip);");
        text.AppendLine("    private static Dictionary<(int, string), MotionClip> Build()");
        text.AppendLine("    {");
        text.AppendLine("        var result = new Dictionary<(int, string), MotionClip>();");
        for (int job = 1; job <= 8; job++)
        {
            var definition = registry.Get((PlayerClass)job).Definition;
            var actor = loader.Load(root, Path.Combine(root, definition.DefaultVisuals.Body.InxExtensionPath.Replace('\\', '/')));
            var groups = new Dictionary<(uint Start, uint End, uint Hit), List<string>>();
            foreach (string key in EquipmentVisuals.CatalogKeys.Order(StringComparer.Ordinal))
            {
                if (!key.StartsWith('W'))
                {
                    continue;
                }

                var clip = PristonMotionSelection.Select(actor.Info, PristonActorMotion.Attack,
                    new(EquipmentVisuals.MotionIndex(key), definition.MotionJobMask, 2));
                var timing = (clip.StartFrame, clip.EndFrame, clip.EventFrames[0]);
                if (!groups.TryGetValue(timing, out var keys))
                {
                    keys = [];
                    groups.Add(timing, keys);
                }

                keys.Add(key);
            }

            foreach (var (timing, keys) in groups)
            {
                text.AppendLine("        foreach (string key in new[]");
                text.AppendLine("        {");
                foreach (string key in keys)
                {
                    text.AppendLine($"            \"{key}\",");
                }

                text.AppendLine("        })");
                text.AppendLine("        {");
                text.AppendLine($"            result.Add(({job}, key), new MotionClip({timing.Start}, {timing.End}, {timing.Hit}u, 0, 0, 0, false));");
                text.AppendLine("        }");
            }
        }

        text.AppendLine("        return result;");
        text.AppendLine("    }");
        text.AppendLine("}");
        File.WriteAllText(output, text.ToString());
    }

    public static void Run(string root)
    {
        var registry = new CharacterClassRegistry();
        var loader = new PristonActorLoader();
        string[] weapons = ["WA101", "WC101", "WS101", "WP101", "WT101", "WS201", "WM101", "WM101"];
        for (int index = 0; index < 8; index++)
        {
            var definition = registry.Get((PlayerClass)(index + 1)).Definition;
            var path = Path.Combine(root, definition.DefaultVisuals.Body.InxExtensionPath.Replace('\\', '/'));
            var actor = loader.Load(root, path);
            int item = EquipmentVisuals.MotionIndex(weapons[index]);
            var armed = PristonMotionSelection.Select(actor.Info, PristonActorMotion.Attack, new(item, definition.MotionJobMask, 2));
            var bare = PristonMotionSelection.Select(actor.Info, PristonActorMotion.Attack, new(-1, definition.MotionJobMask, 2));
            if (armed is null || bare is null)
            {
                string candidates = string.Join(", ", actor.Info.Motions.Where(motion => motion.State == (uint)PristonActorMotion.Attack)
                    .Select(motion => $"{motion.StartFrame}-{motion.EndFrame}: job={motion.JobCodeBit:X}, map={motion.MapPosition}"));
                throw new InvalidDataException($"No attack for {definition.Name}, job={definition.MotionJobMask:X}: {candidates}");
            }

            Console.WriteLine($"{definition.Name} {weapons[index]} index={item}: armed={armed.StartFrame}-{armed.EndFrame} hit={armed.EventFrames[0]}; bare={bare.StartFrame}-{bare.EndFrame} hit={bare.EventFrames[0]}");
            foreach (string key in EquipmentVisuals.CatalogKeys.Where(key => key.StartsWith('W')))
            {
                var clip = PristonMotionSelection.Select(actor.Info, PristonActorMotion.Attack,
                    new(EquipmentVisuals.MotionIndex(key), definition.MotionJobMask, 2));
                Assert.True(PristonTale.Simulation.World.Motion.PlayerEquipmentTiming.TryGet(index + 1, key, out var shared), "missing authoritative equipped timing");
                Assert.True(shared.Frames == clip.EndFrame - clip.StartFrame && shared.Event0 == clip.EventFrames[0], "authoritative timing differs from rendered INX");
            }
        }
    }
}
