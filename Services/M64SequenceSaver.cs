using System.IO;

namespace Sm64DecompLevelViewer.Services;

internal readonly record struct M64ChannelStart(byte Channel, int PointerOffset, int Offset, int Tick);

// Preserve the original sequence, including all embedded scripts/tables, and
// append replacement streams only for edited channel segments. Absolute pointers
// in untouched scripts remain valid. Unsupported edits fail before any write.
internal sealed class M64SequenceSaver(byte[]? source, IReadOnlyList<M64ChannelStart> starts,
    IReadOnlyList<M64Track> baseline, int sourceEndTick)
{
    private sealed class LayerCapacityException : InvalidOperationException { }
    private byte[]? _expected = source;

    internal static M64Track Snapshot(M64Track track) => new()
    {
        ChannelIndex = track.ChannelIndex, Instrument = track.Instrument, Volume = track.Volume, Bank = track.Bank,
        Notes = track.Notes.Select(note => note.Clone()).ToList()
    };

    private static string Key(M64Note n) => $"{n.StartTick}/{n.DurationTicks}/{n.Pitch}/{n.Velocity}/{n.Instrument}/{n.Gate}/{n.CommandType}/{n.ChannelVolume}/{n.ChannelPan}/{n.Reverb}";
    private static bool SameNotes(IEnumerable<M64Note> a, IEnumerable<M64Note> b) =>
        a.Select(Key).OrderBy(k => k, StringComparer.Ordinal).SequenceEqual(b.Select(Key).OrderBy(k => k, StringComparer.Ordinal));

    private static List<M64Note>? AddedNotes(List<M64Note> original, List<M64Note> current)
    {
        var counts = original.GroupBy(Key).ToDictionary(g => g.Key, g => g.Count());
        var added = new List<M64Note>();
        foreach (var note in current)
        {
            string key = Key(note);
            if (counts.TryGetValue(key, out int remaining) && remaining > 0) counts[key]--;
            else added.Add(note);
        }
        return counts.Values.Any(count => count != 0) ? null : added;
    }

    public M64SaveResult Save(string path, List<M64Track> tracks, byte tempo, M64TempoMap tempoMap)
    {
        var result = new M64SaveResult();
        string? temporary = null;
        try
        {
            if (tracks.Any(t => t.ChannelIndex > 15) || tracks.Select(t => t.ChannelIndex).Distinct().Count() != tracks.Count)
                throw new InvalidOperationException("M64 channels must have unique indices from 0 to 15.");
            if (tracks.Sum(t => t.Notes.Count) == 0) throw new InvalidOperationException("No notes remain in the sequence. The file was preserved to avoid an accidental silent export.");
            foreach (var track in tracks)
            {
                var old = baseline.FirstOrDefault(t => t.ChannelIndex == track.ChannelIndex);
                if (source == null || old == null || !SameNotes(old.Notes, track.Notes)) M64LayerPlanner.Split(track.Notes);
                if ((source == null || old?.Volume != track.Volume) && track.Volume > 127)
                    throw new InvalidOperationException("Channel volumes must be between 0 and 127.");
            }
            if (_expected != null && File.Exists(path) && !File.ReadAllBytes(path).SequenceEqual(_expected))
                throw new InvalidOperationException("The M64 file changed outside this editor. Reload it before saving so those changes are preserved.");
            result.OriginalSize = File.Exists(path) ? checked((int)new FileInfo(path).Length) : 0;
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            if (source != null)
            {
                writer.Write(source);
                if (baseline.Any(old => tracks.All(t => t.ChannelIndex != old.ChannelIndex)) ||
                    tracks.Any(t => baseline.All(old => old.ChannelIndex != t.ChannelIndex)))
                    throw new InvalidOperationException("Adding or removing whole channels in an existing scripted sequence needs a separate export. The original file was preserved.");
                foreach (var track in tracks)
                {
                    var old = baseline.Single(t => t.ChannelIndex == track.ChannelIndex);
                    bool settingsChanged = old.Volume != track.Volume || old.Instrument != track.Instrument || old.Bank != track.Bank;
                    if (!settingsChanged && SameNotes(old.Notes, track.Notes)) continue;
                    var segments = starts.Where(s => s.Channel == track.ChannelIndex).OrderBy(s => s.Tick).ToList();
                    if (segments.Count == 0 || segments.GroupBy(s => s.Tick).Any(g => g.Count() > 1))
                        throw new InvalidOperationException($"Channel {track.ChannelIndex} has ambiguous script starts; it cannot safely be reconstructed.");
                    if (track.Notes.Any(n => n.StartTick < segments[0].Tick || (long)n.StartTick + n.DurationTicks > sourceEndTick))
                        throw new InvalidOperationException($"Channel {track.ChannelIndex}: edited notes must fit inside the original sequence timeline (ending at tick {sourceEndTick}).");
                    for (int i = 0; i < segments.Count; i++)
                    {
                        var segment = segments[i];
                        int end = i + 1 < segments.Count ? segments[i + 1].Tick : sourceEndTick;
                        var notes = track.Notes.Where(n => n.StartTick >= segment.Tick && n.StartTick < end).ToList();
                        var oldNotes = old.Notes.Where(n => n.StartTick >= segment.Tick && n.StartTick < end).ToList();
                        if (!settingsChanged && SameNotes(oldNotes, notes)) continue;
                        if (notes.Any(n => (long)n.StartTick + n.DurationTicks > end))
                            throw new InvalidOperationException($"Channel {track.ChannelIndex}: a note crosses a scripted channel restart at tick {end}. Shorten it or move it before saving.");
                        var added = AddedNotes(oldNotes, notes);
                        bool preserveLayers = added != null;
                        var layers = preserveLayers && added!.Count == 0 ? new List<List<M64Note>>() : M64LayerPlanner.Split(added ?? notes);
                        (int Offset, List<int> Pointers) replacement;
                        try { replacement = WriteReplacementChannel(writer, source, segment, layers.Count, track, old, preserveLayers); }
                        catch (LayerCapacityException)
                        {
                            // Reuse occupied slots only if the original layers contain
                            // fully understood note commands, without hidden effects.
                            layers = M64LayerPlanner.Split(notes);
                            replacement = WriteReplacementChannel(writer, source, segment, layers.Count, track, old, false);
                        }
                        var pointers = new List<int>();
                        for (int l = 0; l < layers.Count; l++)
                        {
                            pointers.Add(checked((int)stream.Position));
                            WriteLayer(writer, layers[l], segment.Tick, track.Instrument);
                        }
                        for (int l = 0; l < layers.Count; l++) PatchPointer(writer, replacement.Pointers[l], pointers[l]);
                        PatchPointer(writer, segment.PointerOffset, replacement.Offset);
                    }
                }
            }
            else WriteNewSequence(writer, tracks, tempo, tempoMap);

            if (stream.Length > ushort.MaxValue)
                throw new InvalidOperationException("The saved sequence exceeds 16-bit M64 pointer capacity. No file was replaced.");
            byte[] bytes = stream.ToArray();
            // Build everything first. A failed encoding must never truncate the target.
            string fullPath = Path.GetFullPath(path);
            string folder = Path.GetDirectoryName(fullPath)!;
            temporary = Path.Combine(folder, "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                output.Write(bytes);
                output.Flush(true);
            }
            if (File.Exists(path) && !File.Exists(path + ".bak")) File.Copy(path, path + ".bak");
            // Recheck after staging, since another editor could have saved meanwhile.
            if (_expected != null && File.Exists(path) && !File.ReadAllBytes(path).SequenceEqual(_expected))
                throw new InvalidOperationException("The M64 changed while saving. Reload it; the external version was preserved.");
            File.Move(temporary, fullPath, overwrite: true);
            temporary = null;
            _expected = bytes;
            result.Success = true;
            result.FileSize = bytes.Length;
            foreach (var track in tracks) result.SavedChannels.Add($"Channel {track.ChannelIndex}: {track.Notes.Count} notes");
            int simultaneous = 0, maximum = 0;
            foreach (var ev in tracks.SelectMany(t => t.Notes).SelectMany(n => new[] { (Tick: n.StartTick, Delta: 1), (Tick: n.StartTick + n.DurationTicks, Delta: -1) }).OrderBy(e => e.Tick).ThenBy(e => e.Delta))
            { simultaneous += ev.Delta; maximum = Math.Max(maximum, simultaneous); }
            if (maximum > 16) result.Warnings.Add($"Up to {maximum} notes overlap across all channels. The game's shared voice pool may steal notes, depending on its audio-session preset and sound effects.");
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
            result.Warnings.Add("Save failed: " + ex.Message);
        }
        finally
        {
            if (temporary != null)
            {
                try { File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        return result;
    }

    private static (int Offset, List<int> Pointers) WriteReplacementChannel(BinaryWriter writer, byte[] data, M64ChannelStart segment,
        int layerCount, M64Track track, M64Track old, bool preserveLayers)
    {
        var commands = new List<byte[]>();
        var originalLayers = new Dictionary<int, int>();
        int pos = segment.Offset, tick = 0;
        bool large = false;
        while (pos < data.Length)
        {
            int start = pos;
            byte cmd = data[pos++];
            if (cmd == 0xff) { commands.Add([cmd]); break; }
            if ((cmd & 0xf0) == 0x90)
            {
                if (tick != 0) throw new InvalidOperationException("This channel starts layers after a delay; its timed script cannot safely be flattened.");
                if (pos + 2 > data.Length || (cmd & 15) > 3 || originalLayers.ContainsKey(cmd & 15))
                    throw new InvalidOperationException("Invalid or repeated layer start in edited channel.");
                originalLayers[cmd & 15] = (data[pos] << 8) | data[pos + 1];
                pos += 2;
                continue;
            }
            if (cmd == 0xc4) large = true;
            else if (cmd == 0xfd)
            {
                if (pos >= data.Length) throw new InvalidOperationException("Truncated channel delay.");
                int delay = data[pos++];
                if ((delay & 128) != 0)
                { if (pos >= data.Length) throw new InvalidOperationException("Truncated channel delay."); delay = ((delay & 127) << 8) | data[pos++]; }
                tick += delay;
            }
            else if (cmd == 0xfe) tick++;
            else if (cmd is 0xc1 or 0xc6 or 0xdf or 0xe0 or 0xdd or 0xdc or 0xdb or 0xd9 or 0xd8 or 0xd7 or 0xd6 or 0xd4 or 0xd3 or 0xd2 or 0xd1 or 0xe3 or 0xe5 or 0xe6 or 0xeb) pos++;
            else if (cmd == 0xda) pos += 2; // envelope pointer remains valid in preserved source
            else if (cmd is 0xe1 or 0xe2) pos += 3;
            else if (cmd is >= 0x60 and <= 0x6f) { } // channel priority
            else throw new InvalidOperationException($"Channel script command 0x{cmd:X2} needs preservation that this note export does not yet support. The original file was kept.");
            if (pos > data.Length) throw new InvalidOperationException("Truncated channel script.");
            var bytes = data[start..pos];
            if (cmd == 0xdf && track.Volume != old.Volume) bytes[1] = track.Volume;
            if (cmd == 0xc1 && track.Instrument != old.Instrument) bytes[1] = track.Instrument;
            if (cmd == 0xc6 && track.Bank != old.Bank) bytes[1] = track.Bank;
            commands.Add(bytes);
        }
        if (!large || commands.Count == 0 || commands[^1][0] != 0xff)
            throw new InvalidOperationException("This channel does not use a complete large-note script; it cannot safely be reconstructed.");
        if (preserveLayers && originalLayers.Count + layerCount > 4)
            throw new LayerCapacityException();
        if (!preserveLayers)
            foreach (int layerOffset in originalLayers.Values) ValidateSimpleLayer(data, layerOffset);
        int offset = CheckedOffset(writer);
        writer.Write((byte)0xc4);
        if (preserveLayers)
            foreach (var layer in originalLayers)
            { writer.Write((byte)(0x90 | layer.Key)); writer.Write((byte)(layer.Value >> 8)); writer.Write((byte)layer.Value); }
        var pointers = new List<int>();
        var freeSlots = Enumerable.Range(0, 4).Where(slot => !preserveLayers || !originalLayers.ContainsKey(slot)).ToList();
        for (int l = 0; l < layerCount; l++)
        { writer.Write((byte)(0x90 | freeSlots[l])); pointers.Add(CheckedOffset(writer)); writer.Write((ushort)0); }
        if (track.Bank != old.Bank) writer.Write(new byte[] { 0xc6, track.Bank });
        if (track.Instrument != old.Instrument) writer.Write(new byte[] { 0xc1, track.Instrument });
        if (track.Volume != old.Volume) writer.Write(new byte[] { 0xdf, track.Volume });
        foreach (var command in commands) writer.Write(command);
        return (offset, pointers);
    }

    private static void ValidateSimpleLayer(byte[] data, int pos)
    {
        while (pos < data.Length)
        {
            byte cmd = data[pos++];
            if (cmd == 0xff) return;
            if (cmd is 0xc2 or 0xc6) pos++;
            else if (cmd <= 0xbf || cmd == 0xc0)
            {
                if (cmd < 0x80 || cmd == 0xc0)
                {
                    if (pos >= data.Length) break;
                    int delay = data[pos++];
                    if ((delay & 128) != 0) pos++;
                }
                if (cmd <= 0xbf) pos += cmd < 0x40 || cmd >= 0x80 ? 2 : 1;
            }
            else throw new InvalidOperationException($"An edited original layer uses script command 0x{cmd:X2}. Its effects/loops cannot safely be flattened. Adding notes in free layers is supported without changing that script.");
            if (pos > data.Length) break;
        }
        throw new InvalidOperationException("The original layer script is incomplete. No file was replaced.");
    }

    private static void WriteNewSequence(BinaryWriter writer, List<M64Track> tracks, byte tempo, M64TempoMap tempoMap)
    {
        if (tracks.Sum(t => t.Notes.Count) == 0) throw new InvalidOperationException("The sequence has no notes to export.");
        int mask = tracks.Aggregate(0, (value, t) => value | (1 << t.ChannelIndex));
        writer.Write(new byte[] { 0xd3, 0x80, 0xd7, (byte)(mask >> 8), (byte)mask, 0xdd, tempo, 0xdb, 0x50 });
        var channelPointers = new Dictionary<byte, int>();
        foreach (var track in tracks)
        { writer.Write((byte)(0x90 | track.ChannelIndex)); channelPointers[track.ChannelIndex] = CheckedOffset(writer); writer.Write((ushort)0); }
        int end = tracks.SelectMany(t => t.Notes).Max(n => checked(n.StartTick + n.DurationTicks));
        int lastTick = 0;
        foreach (var change in tempoMap.Changes.Where(c => c.Tick > 0 && c.Tick < end))
        {
            WriteDelay(writer, 0xfd, change.Tick - lastTick);
            if (change.Bpm < 1 || change.Bpm > 255 || change.Bpm != Math.Truncate(change.Bpm))
                throw new InvalidOperationException("This tempo cannot be represented by seq_settempo.");
            writer.Write((byte)0xdd); writer.Write((byte)change.Bpm); lastTick = change.Tick;
        }
        WriteDelay(writer, 0xfd, end - lastTick);
        writer.Write(new byte[] { 0xd6, (byte)(mask >> 8), (byte)mask, 0xff });
        foreach (var track in tracks)
        {
            var layers = M64LayerPlanner.Split(track.Notes);
            int channelOffset = CheckedOffset(writer);
            // Select the bank before the instrument, as the engine resolves it immediately.
            writer.Write(new byte[] { 0xc4, 0xc6, track.Bank, 0xc1, track.Instrument, 0xdf, track.Volume, 0xdd, 64 });
            var pointers = new List<int>();
            for (int l = 0; l < layers.Count; l++)
            { writer.Write((byte)(0x90 | l)); pointers.Add(CheckedOffset(writer)); writer.Write((ushort)0); }
            WriteDelay(writer, 0xfd, end);
            writer.Write((byte)0xff);
            for (int l = 0; l < layers.Count; l++)
            { int offset = CheckedOffset(writer); WriteLayer(writer, layers[l], 0, track.Instrument); PatchPointer(writer, pointers[l], offset); }
            PatchPointer(writer, channelPointers[track.ChannelIndex], channelOffset);
        }
    }

    private static void WriteLayer(BinaryWriter writer, List<M64Note> notes, int startTick, byte channelInstrument)
    {
        int tick = startTick;
        foreach (var note in notes)
        {
            if (note.Instrument > 127 || (note.Instrument == 127) != (channelInstrument == 127))
                throw new InvalidOperationException("Keep percussion and melodic notes in separate channels; this layer export cannot safely switch a channel between drums and melody.");
            WriteDelay(writer, 0xc0, note.StartTick - tick);
            int transpose = Math.Max(0, note.Pitch - 63);
            writer.Write(new byte[] { 0xc2, (byte)transpose, 0xc6, note.Instrument });
            // Always write an explicit delay. A compact note's inherited delay
            // is no longer reliable after notes have been redistributed to layers.
            writer.Write((byte)(note.Pitch - transpose));
            WriteVariable(writer, note.DurationTicks);
            writer.Write(note.Velocity);
            writer.Write(note.CommandType == 1 ? (byte)0 : note.Gate);
            tick = checked(note.StartTick + note.DurationTicks);
        }
        writer.Write((byte)0xff);
    }

    private static int CheckedOffset(BinaryWriter writer)
    {
        if (writer.BaseStream.Position > ushort.MaxValue) throw new InvalidOperationException("M64 pointer capacity exceeded.");
        return (int)writer.BaseStream.Position;
    }
    private static void PatchPointer(BinaryWriter writer, int location, int offset)
    {
        if ((uint)offset > ushort.MaxValue) throw new InvalidOperationException("M64 pointer capacity exceeded.");
        long position = writer.BaseStream.Position;
        writer.BaseStream.Position = location;
        writer.Write((byte)(offset >> 8)); writer.Write((byte)offset);
        writer.BaseStream.Position = position;
    }
    private static void WriteDelay(BinaryWriter writer, byte command, int ticks)
    {
        if (ticks < 0) throw new InvalidOperationException("A layer contains overlapping or out-of-order notes.");
        while (ticks > 0)
        { int part = Math.Min(ticks, 32767); writer.Write(command); WriteVariable(writer, part); ticks -= part; }
    }
    private static void WriteVariable(BinaryWriter writer, int value)
    {
        if (value < 0 || value > 32767) throw new InvalidOperationException("M64 note delay is outside 0–32767.");
        if (value >= 128) writer.Write((byte)(0x80 | (value >> 8)));
        writer.Write((byte)value);
    }
}
