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
    private Dictionary<byte, byte> _overflowOrigins = new();

    internal static M64Track Snapshot(M64Track track) => new()
    {
        ChannelIndex = track.ChannelIndex, Instrument = track.Instrument, Volume = track.Volume, Bank = track.Bank,
        Notes = track.Notes.Select(note => note.Clone()).ToList(),
        PitchChanges = track.PitchChanges.ToList()
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

    private List<M64Track> PlanOverflow(List<M64Track> tracks, Dictionary<byte, byte> origins, List<string> moves,
        Dictionary<byte, byte> channelSources)
    {
        var staged = tracks.Select(Snapshot).ToList();
        var used = baseline.Select(t => t.ChannelIndex).Concat(starts.Select(s => s.Channel))
            .Concat(tracks.Where(t => t.Notes.Count > 0).Select(t => t.ChannelIndex)).ToHashSet();
        foreach (var track in staged.ToList())
        {
            var layers = M64LayerPlanner.Split(track.Notes, int.MaxValue);
            if (layers.Count <= 4) continue;
            var old = baseline.FirstOrDefault(t => t.ChannelIndex == track.ChannelIndex);
            var added = old == null ? null : AddedNotes(old.Notes, track.Notes);
            List<List<M64Note>> extraLayers;
            if (added != null && M64LayerPlanner.Split(old!.Notes, int.MaxValue).Count <= 4)
            {
                // Keep every original note in its scripted channel. Route the added harmony
                // separately so the original layer scripts and effects remain byte-identical.
                var extras = added.ToHashSet();
                track.Notes = track.Notes.Where(n => !extras.Contains(n)).ToList();
                extraLayers = M64LayerPlanner.Split(added, int.MaxValue);
            }
            else
            {
                track.Notes = layers.Take(4).SelectMany(l => l).ToList();
                extraLayers = layers.Skip(4).ToList();
            }
            for (int first = 0; first < extraLayers.Count; first += 4)
            {
                int available = Enumerable.Range(0, 16).FirstOrDefault(i => !used.Contains((byte)i), -1);
                if (available < 0)
                    throw new InvalidOperationException($"Channel {track.ChannelIndex} needs another channel for its harmony, but all 16 channels are in use. No notes were discarded and the file was preserved.");
                byte channel = (byte)available;
                used.Add(channel);
                var overflow = Snapshot(track);
                overflow.ChannelIndex = channel;
                overflow.Notes = extraLayers.Skip(first).Take(4).SelectMany(l => l).ToList();
                staged.RemoveAll(t => t.ChannelIndex == channel);
                staged.Add(overflow);
                if (source != null && (old != null || origins.ContainsKey(track.ChannelIndex)))
                    origins[channel] = origins.GetValueOrDefault(track.ChannelIndex, track.ChannelIndex);
                moves.Add($"moved {overflow.Notes.Count} notes from channel {track.ChannelIndex} to unused channel {channel}");
                channelSources[channel] = track.ChannelIndex;
            }
        }
        return staged;
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
            var originalTracks = tracks;
            var origins = new Dictionary<byte, byte>(_overflowOrigins);
            var moves = new List<string>();
            var channelSources = new Dictionary<byte, byte>();
            var planned = PlanOverflow(tracks, origins, moves, channelSources);
            if (moves.Count > 0) tracks = planned;
            foreach (var track in tracks)
            {
                var old = baseline.FirstOrDefault(t => t.ChannelIndex == track.ChannelIndex);
                if (source == null || old == null || !SameNotes(old.Notes, track.Notes))
                {
                    try { M64LayerPlanner.Split(track.Notes); }
                    catch (InvalidOperationException ex)
                    {
                        throw new InvalidOperationException($"Channel {track.ChannelIndex}: {ex.Message}", ex);
                    }
                }
            }
            if (_expected != null && File.Exists(path) && !File.ReadAllBytes(path).SequenceEqual(_expected))
                throw new InvalidOperationException("The M64 file changed outside this editor. Reload it before saving so those changes are preserved.");
            result.OriginalSize = File.Exists(path) ? checked((int)new FileInfo(path).Length) : 0;
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            if (source != null)
            {
                writer.Write(source);
                if (baseline.Any(old => tracks.All(t => t.ChannelIndex != old.ChannelIndex)))
                    throw new InvalidOperationException("Removing scripted channels is not supported. The original file was preserved.");
                var addedTracks = tracks.Where(t => t.Notes.Count > 0 && baseline.All(old => old.ChannelIndex != t.ChannelIndex)).ToList();
                var relocatedPointers = addedTracks.Count > 0 ? WriteAddedChannels(writer, source, addedTracks, origins) : new Dictionary<int, int>();
                foreach (var track in tracks.Where(t => baseline.Any(old => old.ChannelIndex == t.ChannelIndex)))
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
                        PatchPointer(writer, relocatedPointers.GetValueOrDefault(segment.PointerOffset, segment.PointerOffset), replacement.Offset);
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
            _overflowOrigins = origins;
            if (moves.Count > 0)
            {
                originalTracks.Clear();
                originalTracks.AddRange(tracks);
                result.ChannelMoves.AddRange(moves);
                result.ChannelSources = channelSources;
            }
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

    private Dictionary<int, int> WriteAddedChannels(BinaryWriter writer, byte[] data, List<M64Track> tracks, Dictionary<byte, byte> origins)
    {
        var first = starts.OrderBy(s => s.Tick).ThenBy(s => s.PointerOffset).First();
        if (first.Tick != 0 || sourceEndTick <= 0 ||
            tracks.SelectMany(t => t.Notes).Any(n => (long)n.StartTick + n.DurationTicks > sourceEndTick))
            throw new InvalidOperationException("New channels must fit inside an existing sequence that starts at tick zero.");
        var injections = new Dictionary<M64ChannelStart, List<(M64Track Track, bool Overflow)>>();
        injections[first] = new();
        foreach (var track in tracks)
        {
            if (!origins.TryGetValue(track.ChannelIndex, out byte origin))
            {
                if (!injections.ContainsKey(first)) injections[first] = new();
                injections[first].Add((track, false));
                continue;
            }
            var segments = starts.Where(s => s.Channel == origin).OrderBy(s => s.Tick).ToList();
            if (segments.Count == 0 || segments.Select(s => s.PointerOffset).Distinct().Count() != segments.Count
                || segments.Select(s => s.Tick).Distinct().Count() != segments.Count)
                throw new InvalidOperationException($"Channel {origin}: ambiguous channel restarts cannot safely receive an automatic harmony.");
            for (int i = 0; i < segments.Count; i++)
            {
                var segment = segments[i];
                int end = i + 1 < segments.Count ? segments[i + 1].Tick : sourceEndTick;
                var part = Snapshot(track);
                part.Notes = track.Notes.Where(n => n.StartTick >= segment.Tick && n.StartTick < end).ToList();
                if (part.Notes.Any(n => (long)n.StartTick + n.DurationTicks > end))
                    throw new InvalidOperationException($"Channel {origin}: a harmony note crosses a scripted restart at tick {end}.");
                if (!injections.ContainsKey(segment)) injections[segment] = new();
                injections[segment].Add((part, true));
            }
        }
        var relocated = new Dictionary<int, int>();
        foreach (var injection in injections.OrderBy(pair => pair.Key.PointerOffset))
        {
            var anchor = injection.Key;
            int commandOffset = anchor.PointerOffset - 1;
            if (commandOffset < 0 || data[commandOffset] != (0x90 | anchor.Channel))
                throw new InvalidOperationException("The sequence entry cannot safely start additional channels.");
            // Add each companion at the original channel start, including later restarts.
            // Original addresses, tempo events, loop targets and channel controls survive.
            int entry = CheckedOffset(writer);
            if (anchor == first)
            {
                // Allocate once. Reallocating at each restart would reset the inherited
                // channel state that sequence_channel_enable deliberately preserves.
                int mask = tracks.Aggregate(0, (value, track) => value | (1 << track.ChannelIndex));
                writer.Write(new byte[] { 0xd7, (byte)(mask >> 8), (byte)mask });
            }
            var pointers = new Dictionary<byte, int>();
            foreach (var item in injection.Value)
            {
                writer.Write((byte)(0x90 | item.Track.ChannelIndex));
                pointers[item.Track.ChannelIndex] = CheckedOffset(writer);
                writer.Write((ushort)0);
            }
            relocated[anchor.PointerOffset] = CheckedOffset(writer) + 1;
            writer.Write(data, commandOffset, 3);
            writer.Write((byte)0xfb);
            int resume = commandOffset + 3;
            writer.Write((byte)(resume >> 8)); writer.Write((byte)resume);
            foreach (var item in injection.Value)
            {
                int offset = item.Overflow ? WriteOverflowChannel(writer, data, item.Track, anchor)
                    : WriteNewChannel(writer, item.Track, sourceEndTick);
                PatchPointer(writer, pointers[item.Track.ChannelIndex], offset);
            }
            long end = writer.BaseStream.Position;
            writer.BaseStream.Position = commandOffset;
            writer.Write((byte)0xfb);
            writer.Write((byte)(entry >> 8)); writer.Write((byte)entry);
            writer.BaseStream.Position = end;
        }
        return relocated;
    }

    private int WriteOverflowChannel(BinaryWriter writer, byte[] data, M64Track track, M64ChannelStart segment)
    {
        var old = baseline.Single(t => t.ChannelIndex == segment.Channel);
        var layers = track.Notes.Count == 0 ? new List<List<M64Note>>() : M64LayerPlanner.Split(track.Notes);
        // Copy timed bank, pan, volume, reverb and pitch controls at the same sequence tick.
        // Unknown layer effects are rejected before any file or editor model is changed.
        var replacement = WriteReplacementChannel(writer, data, segment, layers.Count, track, old, false, requireLarge: false);
        for (int layer = 0; layer < layers.Count; layer++)
        {
            int offset = CheckedOffset(writer);
            WriteLayer(writer, layers[layer], segment.Tick, track.Instrument);
            PatchPointer(writer, replacement.Pointers[layer], offset);
        }
        return replacement.Offset;
    }
    private static (int Offset, List<int> Pointers) WriteReplacementChannel(BinaryWriter writer, byte[] data, M64ChannelStart segment,
        int layerCount, M64Track track, M64Track old, bool preserveLayers, bool requireLarge = true)
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
            else if (cmd is 0xda or 0xde) pos += 2; // envelope pointer or raw frequency scale
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
        if ((requireLarge && !large) || commands.Count == 0 || commands[^1][0] != 0xff)
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
        // Interpret only control flow and note-only state. Calls and counted
        // loops are safe to expand, but effects must never be silently removed.
        var stack = new Stack<(int Position, int Remaining)>();
        var visited = new HashSet<string>();
        int steps = 0, ticks = 0, lastDelay = 0;
        int ReadByte()
        {
            if ((uint)pos >= data.Length) throw new InvalidOperationException("Truncated original layer script. No file was replaced.");
            return data[pos++];
        }
        int ReadDelay()
        {
            int value = ReadByte();
            return (value & 128) == 0 ? value : ((value & 127) << 8) | ReadByte();
        }
        while (++steps <= 50000)
        {
            string state = pos + ":" + string.Join(";", stack.Select(frame => $"{frame.Position},{frame.Remaining}"));
            if (!visited.Add(state))
                throw new InvalidOperationException("An edited layer has an unbounded loop. Its script cannot safely be expanded.");
            int cmd = ReadByte();
            if (cmd == 0xff)
            {
                if (stack.Count == 0) return;
                var frame = stack.Pop();
                if (frame.Remaining != -1) throw new InvalidOperationException("A layer ends inside an unfinished counted loop.");
                pos = frame.Position;
            }
            else if (cmd == 0xfc)
            {
                int target = (ReadByte() << 8) | ReadByte();
                if (stack.Count >= 4) throw new InvalidOperationException("Layer calls and loops exceed the game stack capacity of four.");
                stack.Push((pos, -1));
                pos = target;
            }
            else if (cmd == 0xf8)
            {
                int count = ReadByte();
                if (stack.Count >= 4) throw new InvalidOperationException("Layer calls and loops exceed the game stack capacity of four.");
                stack.Push((pos, count == 0 ? 256 : count));
            }
            else if (cmd == 0xf7)
            {
                if (stack.Count == 0 || stack.Peek().Remaining < 1)
                    throw new InvalidOperationException("A layer loop ends without a matching loop start.");
                var frame = stack.Pop();
                if (frame.Remaining > 1)
                {
                    pos = frame.Position;
                    stack.Push((pos, frame.Remaining - 1));
                }
            }
            else if (cmd is 0xc2 or 0xc6) ReadByte();
            else if (cmd <= 0xbf || cmd == 0xc0)
            {
                int delay = cmd < 0x80 || cmd == 0xc0 ? ReadDelay() : lastDelay;
                if (cmd <= 0xbf)
                {
                    if (cmd < 0x80) lastDelay = delay;
                    ReadByte(); // velocity
                    if (cmd < 0x40 || cmd >= 0x80) ReadByte(); // gate
                }
                ticks += delay;
                if (ticks > 60000) throw new InvalidOperationException("The original layer exceeds the editor timeline limit. Its remaining notes cannot safely be replaced.");
            }
            else throw new InvalidOperationException($"An edited original layer uses script command 0x{cmd:X2}. Its effects/loops cannot safely be flattened. Adding notes in free layers is supported without changing that script.");
        }
        throw new InvalidOperationException("The original layer exceeds the safe script expansion limit. No file was replaced.");
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
            int channelOffset = WriteNewChannel(writer, track, end);
            PatchPointer(writer, channelPointers[track.ChannelIndex], channelOffset);
        }
    }

    private static int WriteNewChannel(BinaryWriter writer, M64Track track, int end)
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
        return channelOffset;
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
