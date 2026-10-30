using System.IO;
using Sm64DecompLevelViewer.Services.Audio;

namespace Sm64DecompLevelViewer.Services.Chaos;

// Encode an additive edit through the same layer and channel planner as the editor.
internal static class HarmonyBuilder
{
    internal static byte[] Apply(byte[] source, string region, double intensity, Random random, M64MutationSettings settings, int[] offsets)
    {
        if (settings.MaxPitchDisplacement is < 3)
            throw new InvalidDataException("Minor third harmony needs a pitch displacement limit of at least 3 semitones.");
        var values = M64CommandScanner.Scan(source, region);
        if (values.Any(v => v.Kind == M64ValueKind.Pitch && (!v.Large || v.Shared)))
            throw new InvalidDataException("Cannot safely add harmony to short-note or ambiguously shared scripts.");
        string identity = Path.Combine(region, "preview.m64");
        var parser = new M64SequenceParser();
        var tracks = parser.LoadM64(identity, source, strict: true);
        if (parser.LoadWarnings.Count != 0)
            throw new InvalidDataException("Cannot safely add harmony: " + parser.LoadWarnings[0]);
        if (tracks.Count == 0)
            throw new InvalidDataException("Cannot safely add harmony: no playable channels were parsed.");

        int added = 0;
        foreach (var track in tracks.Where(t => settings.Channels.Contains(t.ChannelIndex)))
        {
            foreach (var note in track.Notes.ToArray())
            {
                if (settings.ProtectPercussion && (track.Instrument == 127 || note.Instrument == 127)) continue;
                foreach (int offset in offsets)
                {
                    if (random.NextDouble() >= intensity / 100) continue;
                    int pitch = note.Pitch + offset;
                    if (pitch < 0 || pitch > 127)
                        throw new InvalidDataException($"Cannot add a {offset:+0;-0} harmony outside pitches 0–127; the sequence was left unchanged.");
                    var copy = note.Clone();
                    copy.Pitch = (byte)pitch;
                    track.Notes.Add(copy);
                    added++;
                }
            }
        }
        if (added == 0) return source;
        var result = parser.EncodeM64(tracks, out byte[]? encoded, allowInheritedLarge: true);
        if (!result.Success || encoded == null)
            throw new InvalidDataException("Cannot safely add harmony: " + result.Error);

        // Refuse an output if the editor parser cannot recover every original
        // and added note. Overflow channels may legitimately change ownership.
        var check = new M64SequenceParser();
        var decoded = check.LoadM64(identity, encoded, strict: true);
        static string Key(M64Note n) => $"{n.StartTick}/{n.DurationTicks}/{n.Pitch}/{n.Velocity}/{n.Instrument}/{n.Gate}";
        var expected = tracks.SelectMany(t => t.Notes).Select(Key).OrderBy(x => x, StringComparer.Ordinal);
        var actual = decoded.SelectMany(t => t.Notes).Select(Key).OrderBy(x => x, StringComparer.Ordinal);
        if (check.LoadWarnings.Count != 0 || !expected.SequenceEqual(actual))
            throw new InvalidDataException("Cannot safely add harmony: encoded notes did not round-trip correctly.");
        return encoded;
    }
}
