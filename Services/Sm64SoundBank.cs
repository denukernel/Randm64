using System.IO;
using System.Text.Json;

namespace Sm64DecompLevelViewer.Services;

public readonly record struct Sm64EnvelopePoint(int Delay, int Target);

public sealed record Sm64Instrument(string SamplePath, double Tuning, bool IsPercussion,
    byte? Pan, byte ReleaseRate, Sm64EnvelopePoint[] Envelope);

// Uses the same JSON layout consumed by SM64's tools/assemble_sound.py.
public sealed class Sm64SoundBank(string projectRoot)
{
    private readonly Dictionary<string, (DateTime Modified, JsonElement Root)> _banks = new();

    public Sm64Instrument? Resolve(string bankId, byte instrumentId, byte pitch, string version = "us")
    {
        string path = Path.Combine(projectRoot, "sound", "sound_banks", bankId + ".json");
        if (!File.Exists(path)) return null;
        string define = version.StartsWith("VERSION_", StringComparison.OrdinalIgnoreCase)
            ? version.ToUpperInvariant() : "VERSION_" + version.ToUpperInvariant();
        string cacheKey = path + "|" + define;
        DateTime modified = File.GetLastWriteTimeUtc(path);
        if (!_banks.TryGetValue(cacheKey, out var cached) || cached.Modified != modified)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
                WriteResolved(document.RootElement, define, writer);
            using var resolved = JsonDocument.Parse(stream.ToArray());
            cached = (modified, resolved.RootElement.Clone());
            _banks[cacheKey] = cached;
        }
        JsonElement root = cached.Root;
        if (!root.TryGetProperty("instruments", out var instruments)) return null;
        bool percussion = instrumentId == 127;
        JsonElement instrument;
        if (percussion)
        {
            // Drum indices are sequence semitones, not General MIDI drum numbers.
            if (!instruments.TryGetProperty("percussion", out var drums) &&
                !root.TryGetProperty("percussion", out drums)) return null;
            if (drums.ValueKind != JsonValueKind.Array || drums.GetArrayLength() == 0) return null;
            instrument = drums[Math.Min((int)pitch, drums.GetArrayLength() - 1)];
        }
        else
        {
            if (!root.TryGetProperty("instrument_list", out var list) || instrumentId >= list.GetArrayLength()) return null;
            if (list[instrumentId].ValueKind != JsonValueKind.String) return null;
            if (!instruments.TryGetProperty(list[instrumentId].GetString()!, out instrument)) return null;
        }
        if (instrument.ValueKind != JsonValueKind.Object) return null;
        int low = instrument.TryGetProperty("normal_range_lo", out var lo) ? lo.GetInt32() : 0;
        int high = instrument.TryGetProperty("normal_range_hi", out var hi) ? hi.GetInt32() : 127;
        string soundKey = !percussion && pitch < low ? "sound_lo" : !percussion && pitch > high ? "sound_hi" : "sound";
        if (!instrument.TryGetProperty(soundKey, out var sound) || sound.ValueKind == JsonValueKind.Null) return null;
        double tuning = 0;
        string? sampleName = sound.ValueKind == JsonValueKind.String ? sound.GetString() :
            sound.TryGetProperty("sample", out var sample) ? sample.GetString() : null;
        if (sound.ValueKind == JsonValueKind.Object && sound.TryGetProperty("tuning", out var tune)) tuning = tune.GetDouble();
        if (string.IsNullOrEmpty(sampleName)) return null;
        string sampleBank = "instruments";
        if (root.TryGetProperty("sample_bank", out var sb))
        {
            if (sb.ValueKind == JsonValueKind.String) sampleBank = sb.GetString()!;
        }
        string samplePath = Path.Combine(projectRoot, "sound", "samples", sampleBank, sampleName + ".aiff");
        if (!File.Exists(samplePath)) samplePath = Path.Combine(projectRoot, "sound", "samples", "instruments", sampleName + ".aiff");
        if (!File.Exists(samplePath)) return null;
        var envelope = new List<Sm64EnvelopePoint>();
        if (instrument.TryGetProperty("envelope", out var name) && root.TryGetProperty("envelopes", out var envelopes) &&
            envelopes.TryGetProperty(name.GetString()!, out var points))
        {
            foreach (var point in points.EnumerateArray())
            {
                if (point.ValueKind == JsonValueKind.Array)
                    envelope.Add(new(point[0].GetInt32(), point[1].GetInt32()));
                else if (point.ValueKind == JsonValueKind.String)
                    envelope.Add(new(point.GetString() switch { "hang" => -1, "restart" => -3, _ => 0 }, 0));
            }
        }
        return new(samplePath, tuning, percussion,
            percussion && instrument.TryGetProperty("pan", out var pan) ? (byte)Math.Clamp(pan.GetInt32(), 0, 127) : null,
            instrument.TryGetProperty("release_rate", out var release) ? (byte)release.GetInt32() : (byte)32,
            envelope.ToArray());
    }

    public static string GetVersionForSequence(string sequencePath)
    {
        string folder = Path.GetFileName(Path.GetDirectoryName(sequencePath) ?? "").ToLowerInvariant();
        return folder is "us" or "jp" or "eu" or "sh" or "cn" ? folder : "us";
    }

    // Match assemble_sound.py's apply_ifs: any matching define selects "then".
    // Resolve recursively, since conditionals can wrap whole sounds or appear
    // inside sample names, tuning, sample banks, envelopes, and arrays.
    private static void WriteResolved(JsonElement value, string define, Utf8JsonWriter writer)
    {
        if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("ifdef", out var conditions) &&
            value.TryGetProperty("then", out var whenTrue) && value.TryGetProperty("else", out var whenFalse))
        {
            bool matches = conditions.EnumerateArray().Any(condition => condition.GetString() == define);
            WriteResolved(matches ? whenTrue : whenFalse, define, writer);
        }
        else if (value.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (var property in value.EnumerateObject())
            {
                writer.WritePropertyName(property.Name);
                WriteResolved(property.Value, define, writer);
            }
            writer.WriteEndObject();
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (var element in value.EnumerateArray()) WriteResolved(element, define, writer);
            writer.WriteEndArray();
        }
        else value.WriteTo(writer);
    }
}
