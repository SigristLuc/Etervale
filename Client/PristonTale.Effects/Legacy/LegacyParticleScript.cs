using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Xna.Framework;
using PristonTale.Effects.Rendering;

namespace PristonTale.Effects.Legacy;

public static partial class LegacyParticleScript
{
    public static IReadOnlyList<LegacyParticleSequence> Read(TextReader reader)
    {
        var result = new List<LegacyParticleSequence>();
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var events = new List<(string Key, string Value)>();
        bool system = false;
        bool sequence = false;
        bool closed = false;
        while (reader.ReadLine() is { } raw)
        {
            string line = raw.Split("//", 2, StringSplitOptions.None)[0].Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (closed)
            {
                throw new InvalidDataException("Content after particle system closure.");
            }

            if (!system && SystemHeader().IsMatch(line))
            {
                system = true;
                continue;
            }

            if (system && !sequence && SequenceHeader().IsMatch(line))
            {
                sequence = true;
                properties.Clear();
                events.Clear();
                continue;
            }

            if (line == "}" && system)
            {
                if (sequence)
                {
                    result.Add(Compile(properties, events));
                    if (result.Count > 32)
                    {
                        throw new InvalidDataException("Too many particle sequences.");
                    }

                    sequence = false;
                }
                else
                {
                    closed = true;
                }

                continue;
            }

            int equals = line.IndexOf('=');
            if (!system || equals <= 0)
            {
                throw new InvalidDataException($"Invalid particle directive: {line}");
            }

            string key = Whitespace().Replace(line[..equals].Trim(), " ").ToLowerInvariant();
            string value = line[(equals + 1)..].Trim();
            if (!sequence)
            {
                if (key != "position")
                {
                    throw new InvalidDataException("Unsupported particle system property.");
                }

                // The original particle manager sets the position after compilation, overriding this authored default.
                var position = Range(value, 3);
                if (position.Min != position.Max)
                {
                    throw new InvalidDataException("Random system positions require an explicit spawn policy.");
                }

                continue;
            }

            if (key.StartsWith("initial ", StringComparison.Ordinal) || key.StartsWith("at ", StringComparison.Ordinal)
                || key.StartsWith("fade so ", StringComparison.Ordinal) || key.StartsWith("final ", StringComparison.Ordinal))
            {
                events.Add((key, value));
                if (events.Count > 128)
                {
                    throw new InvalidDataException("Too many particle events.");
                }
            }
            else
            {
                properties.Add(key, value);
            }
        }

        if (!closed || sequence || result.Count == 0)
        {
            throw new InvalidDataException("Incomplete particle system.");
        }

        return result;
    }

    private static LegacyParticleSequence Compile(Dictionary<string, string> properties, List<(string Key, string Value)> sourceEvents)
    {
        foreach (string key in properties.Keys)
        {
            if (key is not ("sourceblendmode" or "loops" or "numparticles" or "gravity" or "emitrate" or "emitradius" or "lifetime" or "texture" or "delay" or "particletype"))
            {
                throw new InvalidDataException($"Unsupported particle property: {key}");
            }
        }

        var blend = properties["sourceblendmode"].ToUpperInvariant() switch
        {
            "BLEND_LAMP" => ParticleBlendMode.Additive,
            "BLEND_INVSHADOW" => ParticleBlendMode.InverseShadow,
            "BLEND_COLOR" => ParticleBlendMode.ColorBlend,
            _ => throw new InvalidDataException("Unsupported particle blend.")
        };
        float loops = Number(properties["loops"]);
        float delay = properties.TryGetValue("delay", out var delayValue) ? Number(delayValue) : 0;
        var gravity = Range(properties["gravity"], 3);
        string type = properties.GetValueOrDefault("particletype", "TYPE_ONE").ToUpperInvariant();
        if (gravity.Min != gravity.Max || type is not ("TYPE_ONE" or "TYPE_TWO") || delay is < 0 or > 60
            || loops is < 1 or > 256 || loops != MathF.Truncate(loops))
        {
            throw new InvalidDataException("Unsupported gravity, particle type, delay or loop count.");
        }

        float count = Number(properties["numparticles"]);
        float rate = Number(properties["emitrate"]);
        var lifetime = Range(properties["lifetime"], 1);
        if (count is < 1 or > 4096 || count != MathF.Truncate(count) || rate is <= 0 or > 100000 || count * loops > 65536
            || lifetime.Min.X < 0 || lifetime.Max.X is <= 0 or > 60)
        {
            throw new InvalidDataException("Particle sequence exceeds finite resource limits.");
        }

        string texture = properties["texture"].Trim('"').Replace('\\', '/');
        texture = Regex.Replace(texture, "/+", "/");
        if (!texture.StartsWith("effect/particle/", StringComparison.OrdinalIgnoreCase) || texture.Contains("..", StringComparison.Ordinal))
        {
            throw new InvalidDataException("Particle texture must remain inside Effect/Particle.");
        }

        var events = new List<LegacyParticleEvent>();
        foreach (var entry in sourceEvents)
        {
            string key = entry.Key;
            bool fade = key.StartsWith("fade so ", StringComparison.Ordinal);
            if (fade)
            {
                key = key[8..];
            }

            var parts = key.Split(' ');
            float time;
            string property;
            if (parts.Length == 2 && parts[0] is "initial" or "final")
            {
                time = parts[0] == "initial" ? 0 : lifetime.Max.X;
                property = parts[1];
                if (fade && parts[0] == "initial")
                {
                    throw new InvalidDataException("Initial events cannot fade.");
                }
            }
            else if (parts.Length == 3 && parts[0] == "at")
            {
                time = Number(parts[1]);
                property = parts[2];
            }
            else
            {
                throw new InvalidDataException($"Unsupported particle event: {entry.Key}");
            }

            if (property == "localanglez")
            {
                var local = Range(entry.Value, 1);
                if (local.Min.X != 0 || local.Max.X != 0 || fade || time != 0)
                {
                    throw new InvalidDataException("Animated local rotations are not supported.");
                }

                continue;
            }

            bool angleComponent = property is "partanglex" or "partangley" or "partanglez";
            string angleAxis = property;
            if (angleComponent)
            {
                property = "partangle";
            }

            if (time is < 0 or > 60 || !Enum.TryParse<LegacyParticleProperty>(property, true, out var kind) || !Enum.IsDefined(kind))
            {
                throw new InvalidDataException($"Invalid particle event: {entry.Key}");
            }

            int dimensions = kind == LegacyParticleProperty.Color ? 4 : 1;
            if (kind is LegacyParticleProperty.PartAngle or LegacyParticleProperty.Velocity)
            {
                dimensions = 3;
            }

            var value = Range(entry.Value, angleComponent ? 1 : dimensions);
            if (angleComponent)
            {
                if (angleAxis != "partanglez" && (value.Min.X != 0 || value.Max.X != 0))
                {
                    throw new InvalidDataException("Nonzero X/Y particle rotations are not supported.");
                }

                value = new LegacyParticleRange(new Vector4(0, 0, value.Min.X, 0), new Vector4(0, 0, value.Max.X, 0));
            }
            if ((kind == LegacyParticleProperty.Velocity && fade)
                || (kind == LegacyParticleProperty.PartAngle && (value.Min.X != 0 || value.Max.X != 0 || value.Min.Y != 0 || value.Max.Y != 0)))
            {
                throw new InvalidDataException("Velocity fades and non-billboard rotations require a separate implementation.");
            }

            if (events.Any(existing => existing.Property == kind && existing.Time == time))
            {
                throw new InvalidDataException("Duplicate particle event time for the same property.");
            }

            events.Add(new LegacyParticleEvent(kind, time, fade, value));
        }

        return new LegacyParticleSequence
        {
            Texture = texture,
            Count = (int)count,
            Loops = (int)loops,
            Delay = delay,
            Gravity = new Vector3(gravity.Min.X, gravity.Min.Y, gravity.Min.Z),
            GroundPlane = type == "TYPE_TWO",
            Blend = blend,
            EmitRate = rate,
            Lifetime = lifetime,
            Radius = Range(properties.GetValueOrDefault("emitradius", "xyz(0,0,0)"), 3),
            Events = events.OrderBy(static entry => entry.Time).ToArray()
        };
    }

    private static LegacyParticleRange Range(string expression, int dimensions)
    {
        string value = expression.Trim();
        if (dimensions > 1)
        {
            string prefix = dimensions == 4 ? "rgba(" : "xyz(";
            if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !value.EndsWith(')'))
            {
                throw new InvalidDataException($"Expected {prefix} expression: {expression}");
            }

            value = value[prefix.Length..^1];
        }

        var components = new List<string>();
        int depth = 0;
        int start = 0;
        for (int index = 0; index < value.Length; index++)
        {
            if (value[index] == '(')
            {
                depth++;
            }
            else if (value[index] == ')')
            {
                depth--;
            }
            else if (value[index] == ',' && depth == 0)
            {
                components.Add(value[start..index].Trim());
                start = index + 1;
            }
        }

        components.Add(value[start..].Trim());
        if (depth != 0 || components.Count != dimensions)
        {
            throw new InvalidDataException($"Invalid particle range: {expression}");
        }

        Span<float> min = stackalloc float[4];
        Span<float> max = stackalloc float[4];
        min.Clear();
        max.Clear();
        for (int index = 0; index < dimensions; index++)
        {
            string component = components[index];
            float low;
            float high;
            if (component.StartsWith("random(", StringComparison.OrdinalIgnoreCase) && component.EndsWith(')'))
            {
                var bounds = component[7..^1].Split(',');
                if (bounds.Length != 2)
                {
                    throw new InvalidDataException("Random ranges require two bounds.");
                }

                low = Number(bounds[0]);
                high = Number(bounds[1]);
            }
            else
            {
                low = Number(component);
                high = low;
            }

            if (low > high)
            {
                throw new InvalidDataException("Reversed particle range.");
            }

            min[index] = low;
            max[index] = high;
        }

        return new LegacyParticleRange(new Vector4(min[0], min[1], min[2], min[3]), new Vector4(max[0], max[1], max[2], max[3]));
    }

    private static float Number(string value)
    {
        if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float result) || !float.IsFinite(result))
        {
            throw new InvalidDataException($"Invalid finite particle number: {value}");
        }

        return result;
    }

    [GeneratedRegex("^particlesystem \\\"[^\\\"]+\\\" 1\\.00 \\{$", RegexOptions.IgnoreCase)]
    private static partial Regex SystemHeader();

    [GeneratedRegex("^eventsequence \\\"[^\\\"]+\\\" \\{$", RegexOptions.IgnoreCase)]
    private static partial Regex SequenceHeader();

    [GeneratedRegex("\\s+")]
    private static partial Regex Whitespace();
}
