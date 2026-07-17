using System;
using System.Collections.Generic;
using System.IO;

namespace Sm64DecompLevelViewer.Services
{
    public class M64Note
    {
        public int StartTick { get; set; }
        public int DurationTicks { get; set; }
        public byte Pitch { get; set; }
        public byte Velocity { get; set; }
        public byte Instrument { get; set; }
        public byte LayerIndex { get; set; } = 0;
        public byte Gate { get; set; } = 204;
        public byte CommandType { get; set; } = 0;
        public byte ChannelVolume { get; set; } = 127;
        public byte ChannelPan { get; set; } = 64;
        public byte Reverb { get; set; } = 0;

        public override string ToString()
        {
            return $"Note {Pitch} at {StartTick} (Duration: {DurationTicks})";
        }
    }

    public class ChannelStateEvent
    {
        public int Tick { get; set; }
        public byte Volume { get; set; } = 127;
        public byte Pan { get; set; } = 64;
        public byte Reverb { get; set; } = 0;
    }

    public class M64Track
    {
        public byte ChannelIndex { get; set; }
        public byte Instrument { get; set; } = 0;
        public byte Volume { get; set; } = 127;
        public byte Bank { get; set; } = 0;
        public List<M64Note> Notes { get; set; } = new();
    }

    public class M64SaveResult
    {
        public int FileSize { get; set; }
        public int OriginalSize { get; set; }
        public List<string> SavedChannels { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
    }

    public class M64Service
    {
        public byte Tempo { get; set; } = 120;
        public int LoopStartTick { get; set; } = 0;

        private byte[] _originalHeader = null;
        private Dictionary<byte, int> _chanPointerLocations = new();
        private Dictionary<string, int> _layerPointerLocations = new();
        private Dictionary<byte, List<int>> _chanLayers = new();
        private Dictionary<byte, int> _chanVolumeOffsets = new();
        private Dictionary<byte, int> _chanInstrumentOffsets = new();
        private Dictionary<byte, int> _chanBankOffsets = new();

        public List<string> LoadWarnings { get; } = new();

        public int GetChannelLayerCount(byte ch)
        {
            if (_chanLayers.TryGetValue(ch, out var layers))
            {
                return layers.Count;
            }
            return 0;
        }

        private int GetSeqCmdSize(byte cmd, byte[] data, ref int pos)
        {
            // 0 parameter bytes
            if (cmd == 0xff || cmd == 0xfe || cmd == 0xf7 || cmd == 0xf1 || 
                cmd == 0xd4 || cmd == 0xc6)
            {
                return 0;
            }

            // Variable length integer (m64_read_compressed_u16)
            if (cmd == 0xfd)
            {
                ReadVarInt(data, ref pos);
                return 0;
            }

            // 1 parameter byte
            if (cmd == 0xf8 || cmd == 0xf2 || cmd == 0xdc || cmd == 0xda || 
                cmd == 0xd5 || cmd == 0xdf || cmd == 0xde || cmd == 0xdd || 
                cmd == 0xdb || cmd == 0xd3 || cmd == 0xd0 || cmd == 0xcc || 
                cmd == 0xc9 || cmd == 0xc8 || cmd == 0xd9)
            {
                return 1;
            }

            // 2 parameter bytes
            if (cmd == 0xfc || cmd == 0xfb || cmd == 0xfa || cmd == 0xf9 || 
                cmd == 0xf5 || cmd == 0xd7 || cmd == 0xd6 || cmd == 0xd2 || 
                cmd == 0xd1)
            {
                return 2;
            }

            // 3 parameter bytes
            if (cmd == 0xc7)
            {
                return 3;
            }

            // Default for sub-commands or channel starts
            if ((cmd & 0xF0) == 0x90) return 2;
            if ((cmd & 0xF0) == 0x00) return 0;
            if ((cmd & 0xF0) == 0x50) return 0;
            if ((cmd & 0xF0) == 0x70) return 0;
            if ((cmd & 0xF0) == 0x80) return 0;

            return 0;
        }

        private int GetChanCmdSize(byte cmd, byte[] data, ref int pos)
        {
            // 0 parameter bytes
            if (cmd == 0xff || cmd == 0xfe || cmd == 0xf7 || cmd == 0xf6 || 
                cmd == 0xf1 || cmd == 0xc5 || cmd == 0xc3 || cmd == 0xc4 || 
                cmd == 0xe4 || cmd == 0xea || cmd == 0xec)
            {
                return 0;
            }

            // Variable length integer (m64_read_compressed_u16)
            if (cmd == 0xfd)
            {
                ReadVarInt(data, ref pos);
                return 0;
            }

            // 1 parameter byte
            if (cmd == 0xf8 || cmd == 0xf4 || cmd == 0xf3 || cmd == 0xf2 || 
                cmd == 0xc6 || cmd == 0xc1 || cmd == 0xdf || cmd == 0xe0 || 
                cmd == 0xdd || cmd == 0xdc || cmd == 0xdb || cmd == 0xd9 || 
                cmd == 0xd8 || cmd == 0xd7 || cmd == 0xd6 || cmd == 0xd4 || 
                cmd == 0xd3 || cmd == 0xd2 || cmd == 0xd1 || cmd == 0xe3 || 
                cmd == 0xe5 || cmd == 0xe6 || cmd == 0xeb)
            {
                return 1;
            }

            // 2 parameter bytes
            if (cmd == 0xfc || cmd == 0xfb || cmd == 0xfa || cmd == 0xf9 || 
                cmd == 0xf5 || cmd == 0xc2 || cmd == 0xda || cmd == 0xe7)
            {
                return 2;
            }

            // 3 parameter bytes
            if (cmd == 0xe2 || cmd == 0xe1 || cmd == 0xc7)
            {
                return 3;
            }

            // 8 parameter bytes
            if (cmd == 0xe8)
            {
                return 8;
            }

            // Default for any sub-commands or note events that fall into channel parsing
            if ((cmd & 0xF0) == 0x90) return 2;
            if ((cmd & 0xF0) == 0x10) return 2;
            if ((cmd & 0xF0) == 0x20) return 0;
            if ((cmd & 0xF0) == 0x30) return 1;
            if ((cmd & 0xF0) == 0x40) return 1;
            if ((cmd & 0xF0) == 0x50) return 0;
            if ((cmd & 0xF0) == 0x60) return 0;
            if ((cmd & 0xF0) == 0x70) return 0;
            if ((cmd & 0xF0) == 0x80) return 0;

            return 0;
        }

        private class ChannelSegment
        {
            public byte ChannelIndex { get; set; }
            public int Offset { get; set; }
            public int StartTick { get; set; }
        }

        public List<M64Track> LoadM64(string filePath)
        {
            var tracks = new List<M64Track>();
            Tempo = 120;

            LoadWarnings.Clear();
            _originalHeader = null;
            _chanPointerLocations.Clear();
            _layerPointerLocations.Clear();
            _chanLayers.Clear();
            _chanVolumeOffsets.Clear();
            _chanInstrumentOffsets.Clear();
            _chanBankOffsets.Clear();

            try
            {
                if (!File.Exists(filePath))
                {
                    Console.WriteLine($"M64 file not found: {filePath}");
                    return tracks;
                }

                byte[] data = File.ReadAllBytes(filePath);
                if (data.Length < 6) return tracks;

                int seqStartOffset = 0;
                if (data[0] == 0x00 && data[1] == 0x02)
                {
                    seqStartOffset = (data[4] << 8) | data[5];
                }
                if (seqStartOffset >= data.Length) return tracks;

                int pos = seqStartOffset;
                var channelSegments = new List<ChannelSegment>();
                var visitedSeqOffsets = new Dictionary<int, int>();
                int seqTick = 0;
                LoopStartTick = 0;

                // Parse sequence commands to find all channel segments over the timeline
                while (pos < data.Length)
                {
                    if (visitedSeqOffsets.TryGetValue(pos, out int firstVisitTick))
                    {
                        LoopStartTick = firstVisitTick;
                        break;
                    }
                    visitedSeqOffsets[pos] = seqTick;

                    byte cmd = data[pos++];
                    if (cmd == 0xff) break; // end of seq commands

                    if ((cmd & 0xF0) == 0x90) // seq_startchannel
                    {
                        byte ch = (byte)(cmd & 0x0F);
                        _chanPointerLocations[ch] = pos; // points to the 16-bit offset
                        int chOffset = (data[pos++] << 8) | data[pos++];
                        channelSegments.Add(new ChannelSegment { ChannelIndex = ch, Offset = chOffset, StartTick = seqTick });
                    }
                    else if (cmd == 0xfd) // seq_delay
                    {
                        seqTick += ReadVarInt(data, ref pos);
                    }
                    else if (cmd == 0xfe) // seq_delay1
                    {
                        seqTick += 1;
                    }
                    else if (cmd == 0xfb) // seq_jump
                    {
                        int jumpOffset = (data[pos++] << 8) | data[pos++];
                        pos = jumpOffset;
                    }
                    else
                    {
                        if (cmd == 0xdd)
                        {
                            Tempo = data[pos];
                        }
                        int argSize = GetSeqCmdSize(cmd, data, ref pos);
                        pos += argSize;
                    }
                }

                // Parse segments grouped by channel index
                for (byte ch = 0; ch < 16; ch++)
                {
                    var chSegments = channelSegments.Where(s => s.ChannelIndex == ch).ToList();
                    if (chSegments.Count == 0) continue;

                    var track = new M64Track { ChannelIndex = ch };
                    tracks.Add(track);

                    _chanLayers[ch] = new List<int>();

                    foreach (var segment in chSegments)
                    {
                        pos = segment.Offset;
                        if (pos >= data.Length) continue;

                        var layerOffsets = new List<int>();
                        int lIndex = 0;

                        // Trace channel events to build a timeline of volume and panning changes
                        var channelEvents = new List<ChannelStateEvent>();
                        int currentChTick = segment.StartTick;
                        byte currentVol = track.Volume;
                        byte currentPan = 64;
                        byte currentReverb = 0;

                        channelEvents.Add(new ChannelStateEvent { Tick = currentChTick, Volume = currentVol, Pan = currentPan, Reverb = currentReverb });

                        int tracePos = pos;
                        while (tracePos < data.Length)
                        {
                            byte cmd = data[tracePos++];
                            if (cmd == 0xff) break; // end of channel
                            if (cmd == 0xfb) break; // jump

                            if (cmd == 0xc1) // instrument
                            {
                                tracePos++;
                            }
                            else if (cmd == 0xdf) // volume
                            {
                                currentVol = Math.Min((byte)127, data[tracePos++]);
                                channelEvents.Add(new ChannelStateEvent { Tick = currentChTick, Volume = currentVol, Pan = currentPan, Reverb = currentReverb });
                            }
                            else if (cmd == 0xda) // pan
                            {
                                currentPan = data[tracePos++];
                                channelEvents.Add(new ChannelStateEvent { Tick = currentChTick, Volume = currentVol, Pan = currentPan, Reverb = currentReverb });
                            }
                            else if (cmd == 0xd4) // reverb
                            {
                                currentReverb = data[tracePos++];
                                channelEvents.Add(new ChannelStateEvent { Tick = currentChTick, Volume = currentVol, Pan = currentPan, Reverb = currentReverb });
                            }
                            else if (cmd == 0xc0) // delay
                            {
                                currentChTick += ReadVarInt(data, ref tracePos);
                            }
                            else if (cmd == 0xfe) // delay1
                            {
                                currentChTick += 1;
                            }
                            else
                            {
                                int argSize = GetChanCmdSize(cmd, data, ref tracePos);
                                tracePos += argSize;
                            }
                        }

                        // Linear channel parsing as before to extract layer offsets
                        while (pos < data.Length)
                        {
                            byte cmd = data[pos++];
                            if (cmd == 0xff) break; // end of channel
                            if (cmd == 0xfb) break; // jump - end of linear channel segment

                            if (cmd == 0xc1)
                            {
                                _chanInstrumentOffsets[ch] = pos;
                                track.Instrument = data[pos++];
                            }
                            else if (cmd == 0xdf)
                            {
                                _chanVolumeOffsets[ch] = pos;
                                track.Volume = Math.Min((byte)127, data[pos++]);
                            }
                            else if (cmd == 0xc6)
                            {
                                _chanBankOffsets[ch] = pos;
                                track.Bank = data[pos++];
                            }
                            else if ((cmd & 0xF0) == 0x90) // chan_setlayer
                            {
                                _layerPointerLocations[ch + "_" + lIndex] = pos; // points to the 16-bit offset
                                int lOffset = (data[pos++] << 8) | data[pos++];
                                layerOffsets.Add(lOffset);
                                _chanLayers[ch].Add(lOffset);
                                lIndex++;
                            }
                            else
                            {
                                int argSize = GetChanCmdSize(cmd, data, ref pos);
                                pos += argSize;
                            }
                        }

                        // Parse notes for each layer in this segment
                        for (int l = 0; l < layerOffsets.Count; l++)
                        {
                            int layerOffset = layerOffsets[l];
                            if (layerOffset < 0 || layerOffset >= data.Length) continue;

                            int currentTick = segment.StartTick;
                            int currentTranspose = 0;
                            int lastPlayPercentage = 48; // Default initial play percentage
                            var visitedOffsets = new HashSet<int>();
                            ParseLayerEvents(data, layerOffset, track, l, ref currentTick, visitedOffsets, ref currentTranspose, ref lastPlayPercentage, channelEvents);
                        }
                    }
                }

                // Copy original header up to the first note layer offset
                int minLayerOffset = data.Length;
                foreach (var list in _chanLayers.Values)
                {
                    foreach (var offset in list)
                    {
                        if (offset < minLayerOffset && offset > 0) minLayerOffset = offset;
                    }
                }

                if (minLayerOffset < data.Length && minLayerOffset > 0)
                {
                    _originalHeader = new byte[minLayerOffset];
                    Array.Copy(data, 0, _originalHeader, 0, minLayerOffset);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error parsing M64:\n{ex.Message}\n\n{ex.StackTrace}", "M64 Parser Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }

            return tracks;
        }

        public M64SaveResult SaveM64(string filePath, List<M64Track> tracks, bool isNotesModified = true)
        {
            var result = new M64SaveResult();
            try
            {
                // Create backup if not exists
                string backupPath = filePath + ".bak";
                if (!File.Exists(backupPath) && File.Exists(filePath))
                {
                    File.Copy(filePath, backupPath, true);
                }

                result.OriginalSize = File.Exists(backupPath) ? (int)new FileInfo(backupPath).Length : (File.Exists(filePath) ? (int)new FileInfo(filePath).Length : 0);

                var warnings = new List<string>();

                if (!isNotesModified)
                {
                    try
                    {
                        string sourcePath = File.Exists(backupPath) ? backupPath : filePath;
                        byte[] fileBytes = File.ReadAllBytes(sourcePath);

                        foreach (var track in tracks)
                        {
                            if (_chanVolumeOffsets.TryGetValue(track.ChannelIndex, out int volOffset) && volOffset < fileBytes.Length)
                            {
                                fileBytes[volOffset] = track.Volume;
                            }
                            if (_chanInstrumentOffsets.TryGetValue(track.ChannelIndex, out int instOffset) && instOffset < fileBytes.Length)
                            {
                                fileBytes[instOffset] = track.Instrument;
                            }
                            if (_chanBankOffsets.TryGetValue(track.ChannelIndex, out int bankOffset) && bankOffset < fileBytes.Length)
                            {
                                fileBytes[bankOffset] = track.Bank;
                            }
                        }

                        File.WriteAllBytes(filePath, fileBytes);
                        result.FileSize = fileBytes.Length;

                        foreach (var track in tracks)
                        {
                            result.SavedChannels.Add($"Channel {track.ChannelIndex} (Volume {track.Volume}, Instrument {track.Instrument})");
                        }

                        return result;
                    }
                    catch (Exception ex)
                    {
                        warnings.Add($"Direct patch fallback failed: {ex.Message}. Reconstructing layers instead.");
                    }
                }

                // Check active channels limit
                int activeChannelsCount = 0;
                foreach (var track in tracks)
                {
                    if (track.Notes.Count > 0) activeChannelsCount++;
                }
                if (activeChannelsCount > 12)
                {
                    warnings.Add($"* Active Channels Overflow: {activeChannelsCount} channels contain notes. The SM64 audio driver allocates at most 16 total note channels across the entire game. Using more than 12 channels for a single sequence will likely cause note clipping or total silence due to voice starvation.");
                }

                // Check empty sequence
                int totalNotes = 0;
                foreach (var track in tracks) totalNotes += track.Notes.Count;
                if (totalNotes == 0)
                {
                    warnings.Add("* Empty Sequence: The sequence contains no note events. It will play silently in-game.");
                }

                bool hasNewChannels = false;
                foreach (var track in tracks)
                {
                    if (track.Notes.Count > 0 && !_chanPointerLocations.ContainsKey(track.ChannelIndex))
                    {
                        hasNewChannels = true;
                        break;
                    }
                }

                if (hasNewChannels)
                {
                    _originalHeader = null;
                }

                if (_originalHeader != null)
                {
                    // Patch volume, instrument, and bank in _originalHeader
                    foreach (var track in tracks)
                    {
                        if (_chanVolumeOffsets.TryGetValue(track.ChannelIndex, out int volOffset) && volOffset < _originalHeader.Length)
                        {
                            _originalHeader[volOffset] = track.Volume;
                        }
                        if (_chanInstrumentOffsets.TryGetValue(track.ChannelIndex, out int instOffset) && instOffset < _originalHeader.Length)
                        {
                            _originalHeader[instOffset] = track.Instrument;
                        }
                        if (_chanBankOffsets.TryGetValue(track.ChannelIndex, out int bankOffset) && bankOffset < _originalHeader.Length)
                        {
                            _originalHeader[bankOffset] = track.Bank;
                        }
                    }

                    using (var ms = new MemoryStream())
                    using (var bw = new BinaryWriter(ms))
                    {
                        // Write the preserved original header byte-for-byte
                        bw.Write(_originalHeader);

                        var layerStartOffsets = new Dictionary<string, int>();

                        // Group track notes into layers dynamically to avoid overlaps, using the available layers of each channel
                        var trackLayers = new Dictionary<byte, Dictionary<byte, List<M64Note>>>();
                        foreach (var track in tracks)
                        {
                            int numLayers = _chanLayers.ContainsKey(track.ChannelIndex) ? _chanLayers[track.ChannelIndex].Count : 0;
                            var layers = new Dictionary<byte, List<M64Note>>();
                            for (byte l = 0; l < numLayers; l++)
                            {
                                layers[l] = new List<M64Note>();
                            }

                            if (numLayers > 0)
                            {
                                var sortedNotes = track.Notes.OrderBy(n => n.StartTick).ToList();
                                var lastPlayPercentages = new Dictionary<byte, int>();
                                for (byte l = 0; l < numLayers; l++)
                                {
                                    lastPlayPercentages[l] = 0;
                                }

                                foreach (var note in sortedNotes)
                                {
                                    bool assigned = false;
                                    for (byte l = 0; l < numLayers; l++)
                                    {
                                        int lastNoteEnd = 0;
                                        if (layers[l].Count > 0)
                                        {
                                            var lastNote = layers[l][layers[l].Count - 1];
                                            int lastStepSize = lastNote.CommandType == 2 ? lastPlayPercentages[l] : lastNote.DurationTicks;
                                            lastNoteEnd = lastNote.StartTick + lastStepSize;
                                        }
                                        if (note.StartTick >= lastNoteEnd)
                                        {
                                            layers[l].Add(note);
                                            if (note.CommandType != 2)
                                            {
                                                lastPlayPercentages[l] = note.DurationTicks;
                                            }
                                            assigned = true;
                                            break;
                                        }
                                    }
                                    if (!assigned)
                                    {
                                        // Skip note to prevent timing accumulation and sequence corruption.
                                        // Since M64 layers are strictly monophonic, overlapping notes must play on different layers.
                                        // We skip them if the channel has no more free/available layers.
                                    }
                                }
                            }
                            trackLayers[track.ChannelIndex] = layers;
                        }

                        // Write layer note events
                        foreach (var track in tracks)
                        {
                            int numLayers = _chanLayers.ContainsKey(track.ChannelIndex) ? _chanLayers[track.ChannelIndex].Count : 0;
                            for (byte l = 0; l < numLayers; l++)
                            {
                                int layerDataStart = (int)ms.Position;
                                layerStartOffsets[track.ChannelIndex + "_" + l] = layerDataStart;

                                var notes = trackLayers.ContainsKey(track.ChannelIndex) && trackLayers[track.ChannelIndex].ContainsKey(l)
                                    ? trackLayers[track.ChannelIndex][l]
                                    : new List<M64Note>();

                                bool layerOverlapWarning = false;
                                int lastNoteEnd = 0;
                                int lastTick = 0;
                                int lastPlayPercentage = 0;

                                for (int idx = 0; idx < notes.Count; idx++)
                                {
                                    var note = notes[idx];
                                    int stepSize = note.DurationTicks;
                                    if (note.CommandType == 2)
                                    {
                                        stepSize = lastPlayPercentage;
                                    }
                                    else
                                    {
                                        lastPlayPercentage = note.DurationTicks;
                                    }

                                    if (note.StartTick < lastNoteEnd)
                                    {
                                        layerOverlapWarning = true;
                                    }
                                    lastNoteEnd = note.StartTick + stepSize;

                                    int delay = note.StartTick - lastTick;
                                    if (delay > 0)
                                    {
                                        bw.Write((byte)0xc0); // delay command
                                        WriteVarInt(bw, delay);
                                    }

                                    int t_type = note.CommandType;
                                    if (t_type == 2)
                                    {
                                        bw.Write((byte)(0x80 | (note.Pitch & 0x3f)));
                                        bw.Write(note.Velocity);
                                        bw.Write((byte)note.Gate);
                                    }
                                    else if (t_type == 1)
                                    {
                                        bw.Write((byte)(0x40 | (note.Pitch & 0x3f)));
                                        WriteVarInt(bw, note.DurationTicks);
                                        bw.Write(note.Velocity);
                                    }
                                    else
                                    {
                                        bw.Write((byte)(0x00 | (note.Pitch & 0x3f)));
                                        WriteVarInt(bw, note.DurationTicks);
                                        bw.Write(note.Velocity);
                                        bw.Write(note.Gate);
                                    }

                                    lastTick = note.StartTick + stepSize;
                                }

                                if (layerOverlapWarning)
                                {
                                    warnings.Add($"* Channel {track.ChannelIndex} Layer {l} Overlap: Notes in this layer overlap, which will cause them to cut each other off or play out-of-order in-game.");
                                }

                                // Layer end
                                bw.Write((byte)0xff);
                            }
                        }

                        // Patch layer pointers in the headers
                        long currentPos = ms.Position;
                        foreach (var track in tracks)
                        {
                            int numLayers = _chanLayers.ContainsKey(track.ChannelIndex) ? _chanLayers[track.ChannelIndex].Count : 0;
                            for (byte l = 0; l < numLayers; l++)
                            {
                                string key = track.ChannelIndex + "_" + l;
                                if (_layerPointerLocations.ContainsKey(key))
                                {
                                    int start = layerStartOffsets[key];
                                    int placeholder = _layerPointerLocations[key];
                                    ms.Position = placeholder;
                                    bw.Write((byte)((start >> 8) & 0xff));
                                    bw.Write((byte)(start & 0xff));
                                }
                            }
                        }
                        ms.Position = currentPos;

                        byte[] savedBytes = ms.ToArray();
                        File.WriteAllBytes(filePath, savedBytes);
                        Console.WriteLine($"Successfully patched and compiled M64: {filePath}");

                        result.FileSize = savedBytes.Length;
                        result.Warnings = warnings;
                        foreach (var track in tracks)
                        {
                            if (track.Notes.Count > 0)
                            {
                                result.SavedChannels.Add($"Channel {track.ChannelIndex}: {track.Notes.Count} notes (Instrument {track.Instrument}, Volume {track.Volume})");
                            }
                        }
                        return result;
                    }
                }

                // Fallback compilation if original header is not present
                using (var ms = new MemoryStream())
                using (var bw = new BinaryWriter(ms))
                {
                    // Sequence commands
                    ushort channelMask = 0;
                    foreach (var track in tracks)
                    {
                        channelMask |= (ushort)(1 << track.ChannelIndex);
                    }

                    bw.Write((byte)0xd3); // seq_setmutebhv
                    bw.Write((byte)0x80);

                    bw.Write((byte)0xdd); // seq_settempo
                    bw.Write(Tempo);

                    bw.Write((byte)0xd7); // init channels
                    bw.Write((byte)((channelMask >> 8) & 0xff));
                    bw.Write((byte)(channelMask & 0xff));

                    // Write channel pointers placeholder
                    var chanOffsetPlaceholders = new Dictionary<byte, int>();
                    foreach (var track in tracks)
                    {
                        bw.Write((byte)(0x90 | track.ChannelIndex)); // seq_startchannel
                        chanOffsetPlaceholders[track.ChannelIndex] = (int)ms.Position;
                        bw.Write((byte)0x00); // offset placeholder
                        bw.Write((byte)0x00);
                    }

                    // Set master volume and end sequence commands
                    bw.Write((byte)0xdb); // seq_setvol
                    bw.Write((byte)127);

                    // Write seq_delay of maxSongTick to keep the sequence alive
                    int maxSongTick = 0;
                    foreach (var track in tracks)
                    {
                        if (track.Notes.Count > 0)
                        {
                            int trackEnd = track.Notes.Max(n => n.StartTick + n.DurationTicks);
                            if (trackEnd > maxSongTick) maxSongTick = trackEnd;
                        }
                    }
                    if (maxSongTick == 0) maxSongTick = 480;

                    bw.Write((byte)0xfd); // seq_delay
                    WriteVarInt(bw, maxSongTick);

                    bw.Write((byte)0xd6); // seq_disablechannels
                    bw.Write((byte)((channelMask >> 8) & 0xff));
                    bw.Write((byte)(channelMask & 0xff));

                    bw.Write((byte)0xff); // seq_end

                    // Write channel headers and layer blocks
                    var chanOffsets = new Dictionary<byte, int>();
                    var layerOffsetsPlaceholders = new Dictionary<string, int>();
                    var layersGroupedCache = new Dictionary<byte, List<List<M64Note>>>();

                    foreach (var track in tracks)
                    {
                        chanOffsets[track.ChannelIndex] = (int)ms.Position;

                        // Group track notes into layers dynamically to avoid overlaps
                        var layers = new List<List<M64Note>>();
                        var sortedNotes = track.Notes.OrderBy(n => n.StartTick).ToList();
                        var lastPlayPercentages = new List<int>();
                        bool polyphonyWarning = false;
                        
                        foreach (var note in sortedNotes)
                        {
                            bool assigned = false;
                            for (int i = 0; i < layers.Count; i++)
                            {
                                int lastNoteEnd = 0;
                                if (layers[i].Count > 0)
                                {
                                    var lastNote = layers[i][layers[i].Count - 1];
                                    int lastStepSize = lastNote.CommandType == 2 ? lastPlayPercentages[i] : lastNote.DurationTicks;
                                    lastNoteEnd = lastNote.StartTick + lastStepSize;
                                }
                                if (note.StartTick >= lastNoteEnd)
                                {
                                    layers[i].Add(note);
                                    if (note.CommandType != 2)
                                    {
                                        lastPlayPercentages[i] = note.DurationTicks;
                                    }
                                    assigned = true;
                                    break;
                                }
                            }
                            if (!assigned)
                            {
                                if (layers.Count < 4)
                                {
                                    layers.Add(new List<M64Note> { note });
                                    lastPlayPercentages.Add(note.CommandType != 2 ? note.DurationTicks : 0);
                                    assigned = true;
                                }
                                else
                                {
                                    polyphonyWarning = true;
                                    // Skip to avoid timing corruption
                                }
                            }
                        }
                        if (layers.Count == 0)
                        {
                            layers.Add(new List<M64Note>());
                        }
                        layersGroupedCache[track.ChannelIndex] = layers;

                        if (polyphonyWarning)
                        {
                            warnings.Add($"* Polyphony limit exceeded on Channel {track.ChannelIndex}. A single M64 channel can play at most 4 notes simultaneously. Extra notes were skipped to prevent timing corruption.");
                        }

                        // Write channel header commands
                        bw.Write((byte)0xc4); // chan_largenoteson
                        bw.Write((byte)0xc1); // chan_setinstr
                        bw.Write(track.Instrument);
                        bw.Write((byte)0xdf); // chan_setvol
                        bw.Write(track.Volume);
                        bw.Write((byte)0xc6); // chan_setbank
                        bw.Write(track.Bank);

                        // Set layers placeholders
                        for (int i = 0; i < layers.Count; i++)
                        {
                            bw.Write((byte)(0x90 | i)); // chan_setlayer
                            string key = track.ChannelIndex + "_" + i;
                            layerOffsetsPlaceholders[key] = (int)ms.Position;
                            bw.Write((byte)0x00); // 16-bit offset placeholder
                            bw.Write((byte)0x00);
                        }
                        bw.Write((byte)0xff); // end channel
                    }

                    // Write notes for each layer
                    var layerStartOffsets = new Dictionary<string, int>();
                    foreach (var track in tracks)
                    {
                        var layers = layersGroupedCache[track.ChannelIndex];
                        for (int i = 0; i < layers.Count; i++)
                        {
                            string key = track.ChannelIndex + "_" + i;
                            layerStartOffsets[key] = (int)ms.Position;

                            var notes = layers[i];
                            int lastTick = 0;
                            int lastPlayPercentage = 0;
                            for (int idx = 0; idx < notes.Count; idx++)
                            {
                                var note = notes[idx];
                                int stepSize = note.DurationTicks;
                                if (note.CommandType == 2)
                                {
                                    stepSize = lastPlayPercentage;
                                }
                                else
                                {
                                    lastPlayPercentage = note.DurationTicks;
                                }

                                int delay = note.StartTick - lastTick;
                                if (delay > 0)
                                {
                                    bw.Write((byte)0xc0); // delay
                                    WriteVarInt(bw, delay);
                                }

                                int t_type = note.CommandType;
                                if (t_type == 2)
                                {
                                    bw.Write((byte)(0x80 | (note.Pitch & 0x3f)));
                                    bw.Write(note.Velocity);
                                    bw.Write((byte)note.Gate);
                                }
                                else if (t_type == 1)
                                {
                                    bw.Write((byte)(0x40 | (note.Pitch & 0x3f)));
                                    WriteVarInt(bw, note.DurationTicks);
                                    bw.Write(note.Velocity);
                                }
                                else
                                {
                                    bw.Write((byte)(0x00 | (note.Pitch & 0x3f)));
                                    WriteVarInt(bw, note.DurationTicks);
                                    bw.Write(note.Velocity);
                                    bw.Write(note.Gate);
                                }

                                lastTick = note.StartTick + stepSize;
                            }
                            bw.Write((byte)0xff); // end layer
                        }
                    }

                    // Patch placeholders
                    foreach (var track in tracks)
                    {
                        var layers = layersGroupedCache[track.ChannelIndex];
                        for (int i = 0; i < layers.Count; i++)
                        {
                            string key = track.ChannelIndex + "_" + i;
                            int start = layerStartOffsets[key];
                            int placeholder = layerOffsetsPlaceholders[key];

                            ms.Position = placeholder;
                            bw.Write((byte)((start >> 8) & 0xff));
                            bw.Write((byte)(start & 0xff));
                        }

                        int chOff = chanOffsets[track.ChannelIndex];
                        ms.Position = chanOffsetPlaceholders[track.ChannelIndex];
                        bw.Write((byte)((chOff >> 8) & 0xff));
                        bw.Write((byte)(chOff & 0xff));
                    }

                    byte[] savedBytes = ms.ToArray();
                    File.WriteAllBytes(filePath, savedBytes);
                    Console.WriteLine($"Successfully compiled M64: {filePath}");

                    result.FileSize = savedBytes.Length;
                    result.Warnings = warnings;
                    foreach (var track in tracks)
                    {
                        if (track.Notes.Count > 0)
                        {
                            result.SavedChannels.Add($"Channel {track.ChannelIndex}: {track.Notes.Count} notes (Instrument {track.Instrument}, Volume {track.Volume})");
                        }
                    }
                    return result;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error compiling M64: {ex.Message}");
                result.Warnings.Add($"Compilation Error: {ex.Message}");
            }
            return result;
        }

        private int ReadVarInt(byte[] data, ref int pos)
        {
            byte b1 = data[pos++];
            if (b1 >= 0x80)
            {
                byte b2 = data[pos++];
                return ((b1 & 0x7F) << 8) | b2;
            }
            return b1;
        }

        private void WriteVarInt(BinaryWriter bw, int value)
        {
            if (value >= 0x80)
            {
                bw.Write((byte)(0x80 | ((value >> 8) & 0x7f)));
                bw.Write((byte)(value & 0xff));
            }
            else
            {
                bw.Write((byte)value);
            }
        }

        private struct LoopState
        {
            public int StartPos;
            public int RemIters;
        }

        private void ParseLayerEvents(byte[] data, int startOffset, M64Track track, int layerIndex, ref int currentTick, HashSet<int> visitedOffsets, ref int currentTranspose, ref int lastPlayPercentage, List<ChannelStateEvent> channelEvents, int depth = 0)
        {
            const int MIN_NOTE_PITCH = 0;
            const int MAX_NOTE_PITCH = 127;

            if (depth > 50)
            {
                if (!LoadWarnings.Contains("Sequence script contains infinite jump/call loops. This will freeze the SM64 audio driver thread."))
                {
                    LoadWarnings.Add("Sequence script contains infinite jump/call loops. This will freeze the SM64 audio driver thread.");
                }
                return;
            }
            if (visitedOffsets.Contains(startOffset)) return;

            // Track visited offsets along the current execution path to detect infinite loops/recursions
            // while allowing subroutines (layer_calls) to be traversed multiple times sequentially.
            var pathVisited = new HashSet<int>(visitedOffsets);
            pathVisited.Add(startOffset);

            try
            {
                var loopStack = new Stack<LoopState>();
                byte currentInstrument = track.Instrument;
                int eventCount = 0;

                int pos = startOffset;
                while (pos < data.Length)
                {
                    if (eventCount++ > 50000) break; // Safety limit to prevent infinite loops
                    if (currentTick > 60000)
                    {
                        if (!LoadWarnings.Contains("Sequence duration limit exceeded (60,000 ticks). Notes beyond this limit were ignored."))
                        {
                            LoadWarnings.Add("Sequence duration limit exceeded (60,000 ticks). Notes beyond this limit were ignored.");
                        }
                        break;
                    }

                    byte cmd = data[pos++];
                    if (cmd == 0xff) break; // end of layer or return from call

                    if (cmd == 0xc0) // delay
                    {
                        currentTick += ReadVarInt(data, ref pos);
                    }
                    else if (cmd == 0xc2) // layer_transpose
                    {
                        currentTranspose = (sbyte)data[pos++];
                    }
                    else if (cmd == 0xc6) // layer_setinstr
                    {
                        currentInstrument = data[pos++];
                    }
                    else if (cmd == 0xc1 || cmd == 0xc9 || cmd == 0xca) // layer velocity, duration, pan
                    {
                        pos++;
                    }
                    else if (cmd == 0xcb) // layer envelope / ADSR (takes 3 bytes)
                    {
                        pos += 3;
                    }
                    else if (cmd == 0xf4) // relative jump (takes 1 byte)
                    {
                        pos++;
                    }
                    else if (cmd == 0xf8) // layer_loop start
                    {
                        byte remIters = data[pos++];
                        loopStack.Push(new LoopState { StartPos = pos, RemIters = remIters == 0 ? 256 : remIters });
                    }
                    else if (cmd == 0xf7) // layer_loopend
                    {
                        if (loopStack.Count > 0)
                        {
                            var loop = loopStack.Pop();
                            loop.RemIters--;
                            if (loop.RemIters > 0)
                            {
                                pos = loop.StartPos;
                                loopStack.Push(loop);
                            }
                        }
                    }
                    else if (cmd == 0xc3)
                    {
                        ReadVarInt(data, ref pos);
                    }
                    else if (cmd == 0xfb) // layer_jump
                    {
                        int jumpOffset = (data[pos++] << 8) | data[pos++];
                        ParseLayerEvents(data, jumpOffset, track, layerIndex, ref currentTick, pathVisited, ref currentTranspose, ref lastPlayPercentage, channelEvents, depth + 1);
                        break;
                    }
                    else if (cmd == 0xfc) // layer_call
                    {
                        int callOffset = (data[pos++] << 8) | data[pos++];
                        ParseLayerEvents(data, callOffset, track, layerIndex, ref currentTick, pathVisited, ref currentTranspose, ref lastPlayPercentage, channelEvents, depth + 1);
                    }
                    else if (cmd == 0xc7) // portamento (dynamic size)
                    {
                        if (pos < data.Length)
                        {
                            byte mode = data[pos++];
                            pos++; // Skip target note
                            if ((mode & 0x80) != 0)
                            {
                                pos++;
                            }
                            else
                            {
                                ReadVarInt(data, ref pos);
                            }
                        }
                    }
                    else if (cmd >= 0x00 && cmd <= 0x3f) // note0 (large)
                    {
                        byte pitch = (byte)(cmd & 0x3f);
                        int duration = ReadVarInt(data, ref pos);
                        byte vel = data[pos++];
                        byte gate = data[pos++];
                        
                        lastPlayPercentage = duration;
                        
                        byte finalPitch = (byte)Math.Clamp(pitch + currentTranspose, MIN_NOTE_PITCH, MAX_NOTE_PITCH);
                        byte noteVol = 127;
                        byte notePan = 64;
                        byte noteReverb = 0;
                        int localTick = currentTick;
                        if (channelEvents != null && channelEvents.Count > 0)
                        {
                            var ev = channelEvents.FindLast(e => e.Tick <= localTick) ?? channelEvents[0];
                            noteVol = ev.Volume;
                            notePan = ev.Pan;
                            noteReverb = ev.Reverb;
                        }

                        track.Notes.Add(new M64Note { 
                            StartTick = currentTick, 
                            DurationTicks = duration, 
                            Pitch = finalPitch, 
                            Velocity = vel, 
                            Instrument = currentInstrument, 
                            LayerIndex = (byte)layerIndex,
                            Gate = gate,
                            CommandType = 0,
                            ChannelVolume = noteVol,
                            ChannelPan = notePan,
                            Reverb = noteReverb
                        });
                        currentTick += duration;
                    }
                    else if (cmd >= 0x40 && cmd <= 0x7f) // note1 (medium)
                    {
                        byte pitch = (byte)(cmd & 0x3f);
                        int duration = ReadVarInt(data, ref pos);
                        byte vel = data[pos++];
                        
                        lastPlayPercentage = duration;
                        
                        byte finalPitch = (byte)Math.Clamp(pitch + currentTranspose, MIN_NOTE_PITCH, MAX_NOTE_PITCH);
                        byte noteVol = 127;
                        byte notePan = 64;
                        byte noteReverb = 0;
                        int localTick = currentTick;
                        if (channelEvents != null && channelEvents.Count > 0)
                        {
                            var ev = channelEvents.FindLast(e => e.Tick <= localTick) ?? channelEvents[0];
                            noteVol = ev.Volume;
                            notePan = ev.Pan;
                            noteReverb = ev.Reverb;
                        }

                        track.Notes.Add(new M64Note { 
                            StartTick = currentTick, 
                            DurationTicks = duration, 
                            Pitch = finalPitch, 
                            Velocity = vel, 
                            Instrument = currentInstrument, 
                            LayerIndex = (byte)layerIndex,
                            Gate = 250,
                            CommandType = 1,
                            ChannelVolume = noteVol,
                            ChannelPan = notePan,
                            Reverb = noteReverb
                        });
                        currentTick += duration;
                    }
                    else if (cmd >= 0x80 && cmd <= 0xbf) // note2 (small)
                    {
                        byte pitch = (byte)(cmd & 0x3f);
                        byte vel = data[pos++];
                        int duration = data[pos++]; // noteDuration (gate)
                        
                        int delay = lastPlayPercentage;
                        
                        byte finalPitch = (byte)Math.Clamp(pitch + currentTranspose, MIN_NOTE_PITCH, MAX_NOTE_PITCH);
                        byte noteVol = 127;
                        byte notePan = 64;
                        byte noteReverb = 0;
                        int localTick = currentTick;
                        if (channelEvents != null && channelEvents.Count > 0)
                        {
                            var ev = channelEvents.FindLast(e => e.Tick <= localTick) ?? channelEvents[0];
                            noteVol = ev.Volume;
                            notePan = ev.Pan;
                            noteReverb = ev.Reverb;
                        }

                        track.Notes.Add(new M64Note { 
                            StartTick = currentTick, 
                            DurationTicks = delay, 
                            Pitch = finalPitch, 
                            Velocity = vel, 
                            Instrument = currentInstrument, 
                            LayerIndex = (byte)layerIndex,
                            Gate = (byte)duration,
                            CommandType = 2,
                            ChannelVolume = noteVol,
                            ChannelPan = notePan,
                            Reverb = noteReverb
                        });
                        currentTick += delay;
                    }
                }
            }
            finally
            {
                // Persistent visited set to avoid exponential path traversal on corrupted sequences
            }
        }

        public List<M64Track> ImportMidi(string filePath)
        {
            return MidiParser.ParseMidi(filePath, 48);
        }
    }

    public static class AiffWavTranscoder
    {
        public static byte[] SafeReadAllBytes(string filePath)
        {
            try
            {
                // Force cache invalidation on Windows for UNC paths (WSL mounts) by refreshing FileInfo
                var fi = new FileInfo(filePath);
                fi.Refresh();

                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    byte[] bytes = new byte[fs.Length];
                    int bytesRead = 0;
                    int numToRead = bytes.Length;
                    while (numToRead > 0)
                    {
                        int n = fs.Read(bytes, bytesRead, numToRead);
                        if (n == 0) break;
                        bytesRead += n;
                        numToRead -= n;
                    }
                    return bytes;
                }
            }
            catch
            {
                return File.ReadAllBytes(filePath);
            }
        }

        public static byte[] ConvertAiffToWav(byte[] aiffData)
        {
            try
            {
                if (aiffData.Length < 12) return Array.Empty<byte>();
                int pos = 12; // Skip 'FORM' size 'AIFF'
                int numChannels = 0;
                int numSampleFrames = 0;
                int sampleSize = 0;
                double sampleRate = 0;
                byte[] soundData = null;

                while (pos + 8 <= aiffData.Length)
                {
                    string chunkName = System.Text.Encoding.ASCII.GetString(aiffData, pos, 4);
                    pos += 4;
                    int chunkSize = (aiffData[pos] << 24) | (aiffData[pos + 1] << 16) | (aiffData[pos + 2] << 8) | aiffData[pos + 3];
                    pos += 4;

                    if (chunkName == "COMM")
                    {
                        numChannels = (aiffData[pos] << 8) | aiffData[pos + 1];
                        numSampleFrames = (aiffData[pos + 2] << 24) | (aiffData[pos + 3] << 16) | (aiffData[pos + 4] << 8) | aiffData[pos + 5];
                        sampleSize = (aiffData[pos + 6] << 8) | aiffData[pos + 7];
                        
                        // Parse 80-bit float exponent and mantissa
                        int exp = ((aiffData[pos + 8] & 0x7F) << 8) | aiffData[pos + 9];
                        uint hiMant = ((uint)aiffData[pos + 10] << 24) | ((uint)aiffData[pos + 11] << 16) | ((uint)aiffData[pos + 12] << 8) | aiffData[pos + 13];
                        double rate = hiMant * Math.Pow(2, exp - 16383 - 31);
                        sampleRate = (double.IsNaN(rate) || double.IsInfinity(rate) || rate <= 0) ? 32000 : rate;

                        pos += (chunkSize + 1) & ~1;
                    }
                    else if (chunkName == "SSND")
                    {
                        int offset = (aiffData[pos] << 24) | (aiffData[pos + 1] << 16) | (aiffData[pos + 2] << 8) | aiffData[pos + 3];
                        int dataOffset = 8 + offset;
                        int dataSize = chunkSize - dataOffset;
                        if (dataSize > 0 && pos + dataOffset + dataSize <= aiffData.Length)
                        {
                            soundData = new byte[dataSize];
                            Array.Copy(aiffData, pos + dataOffset, soundData, 0, dataSize);
                        }
                        pos += (chunkSize + 1) & ~1;
                    }
                    else
                    {
                        pos += (chunkSize + 1) & ~1;
                    }
                }

                if (soundData == null || numChannels == 0 || sampleSize == 0) return Array.Empty<byte>();

                // Convert Big-Endian to Little-Endian
                if (sampleSize == 16)
                {
                    for (int i = 0; i < soundData.Length - 1; i += 2)
                    {
                        byte temp = soundData[i];
                        soundData[i] = soundData[i + 1];
                        soundData[i + 1] = temp;
                    }
                }

                using (var ms = new MemoryStream())
                using (var bw = new BinaryWriter(ms))
                {
                    bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
                    bw.Write(36 + soundData.Length);
                    bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
                    bw.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
                    bw.Write(16);
                    bw.Write((short)1); // PCM
                    bw.Write((short)numChannels);
                    bw.Write((int)sampleRate);
                    bw.Write((int)(sampleRate * numChannels * (sampleSize / 8)));
                    bw.Write((short)(numChannels * (sampleSize / 8)));
                    bw.Write((short)sampleSize);
                    bw.Write(System.Text.Encoding.ASCII.GetBytes("data"));
                    bw.Write(soundData.Length);
                    bw.Write(soundData);

                    return ms.ToArray();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error converting AIFF to WAV: {ex.Message}");
                return Array.Empty<byte>();
            }
        }

        public static byte[] ConvertWavToAiff(byte[] wavData)
        {
            try
            {
                if (wavData.Length < 12) return Array.Empty<byte>();
                int pos = 12;
                int numChannels = 0;
                int sampleRate = 0;
                int sampleSize = 0;
                byte[] soundData = null;

                while (pos + 8 <= wavData.Length)
                {
                    string chunkName = System.Text.Encoding.ASCII.GetString(wavData, pos, 4);
                    pos += 4;
                    int chunkSize = BitConverter.ToInt32(wavData, pos);
                    pos += 4;

                    if (chunkName == "fmt ")
                    {
                        numChannels = BitConverter.ToInt16(wavData, pos + 2);
                        sampleRate = BitConverter.ToInt32(wavData, pos + 4);
                        sampleSize = BitConverter.ToInt16(wavData, pos + 14);
                        pos += chunkSize;
                    }
                    else if (chunkName == "data")
                    {
                        soundData = new byte[chunkSize];
                        Array.Copy(wavData, pos, soundData, 0, chunkSize);
                        pos += chunkSize;
                    }
                    else
                    {
                        pos += chunkSize;
                    }
                }

                if (soundData == null || numChannels == 0 || sampleSize == 0) return Array.Empty<byte>();

                // Convert Little-Endian to Big-Endian
                if (sampleSize == 16)
                {
                    for (int i = 0; i < soundData.Length - 1; i += 2)
                    {
                        byte temp = soundData[i];
                        soundData[i] = soundData[i + 1];
                        soundData[i + 1] = temp;
                    }
                }

                using (var ms = new MemoryStream())
                using (var bw = new BinaryWriter(ms))
                {
                    bw.Write(System.Text.Encoding.ASCII.GetBytes("FORM"));
                    int formSize = 46 + soundData.Length;
                    bw.Write(new byte[] { (byte)(formSize >> 24), (byte)(formSize >> 16), (byte)(formSize >> 8), (byte)formSize });
                    bw.Write(System.Text.Encoding.ASCII.GetBytes("AIFF"));
                    
                    bw.Write(System.Text.Encoding.ASCII.GetBytes("COMM"));
                    bw.Write(new byte[] { 0, 0, 0, 18 });
                    bw.Write(new byte[] { (byte)(numChannels >> 8), (byte)numChannels });
                    bw.Write(new byte[] { (byte)(soundData.Length / (numChannels * (sampleSize / 8)) >> 24), (byte)(soundData.Length / (numChannels * (sampleSize / 8)) >> 16), (byte)(soundData.Length / (numChannels * (sampleSize / 8)) >> 8), (byte)(soundData.Length / (numChannels * (sampleSize / 8))) });
                    bw.Write(new byte[] { (byte)(sampleSize >> 8), (byte)sampleSize });
                    
                    byte[] rateBytes = EncodeDoubleTo80Bit((double)sampleRate);
                    bw.Write(rateBytes);

                    bw.Write(System.Text.Encoding.ASCII.GetBytes("SSND"));
                    int ssndSize = 8 + soundData.Length;
                    bw.Write(new byte[] { (byte)(ssndSize >> 24), (byte)(ssndSize >> 16), (byte)(ssndSize >> 8), (byte)ssndSize });
                    bw.Write(new byte[] { 0, 0, 0, 0 });
                    bw.Write(new byte[] { 0, 0, 0, 0 });
                    bw.Write(soundData);

                    return ms.ToArray();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error converting WAV to AIFF: {ex.Message}");
                return Array.Empty<byte>();
            }
        }

        public static byte[] EncodeDoubleTo80Bit(double value)
        {
            byte[] bytes = new byte[10];
            if (value == 0) return bytes;

            long bits = BitConverter.DoubleToInt64Bits(value);
            bool sign = (bits < 0);
            int exp = (int)((bits >> 52) & 0x7FF);
            ulong mantissa = (ulong)(bits & 0xFFFFFFFFFFFFFL);

            if (exp == 0)
            {
                exp = 0;
            }
            else if (exp == 0x7FF)
            {
                exp = 0x7FFF;
                mantissa = 0x8000000000000000UL;
            }
            else
            {
                exp = exp - 1023 + 16383;
                mantissa = (mantissa | 0x10000000000000UL) << 11;
            }

            bytes[0] = (byte)((sign ? 0x80 : 0) | (exp >> 8));
            bytes[1] = (byte)exp;
            
            bytes[2] = (byte)(mantissa >> 56);
            bytes[3] = (byte)(mantissa >> 48);
            bytes[4] = (byte)(mantissa >> 40);
            bytes[5] = (byte)(mantissa >> 32);
            bytes[6] = (byte)(mantissa >> 24);
            bytes[7] = (byte)(mantissa >> 16);
            bytes[8] = (byte)(mantissa >> 8);
            bytes[9] = (byte)mantissa;

            return bytes;
        }
    }

    public static class MidiParser
    {
        public static List<M64Track> ParseMidi(string filePath, int ticksPerBeat)
        {
            var tracks = new List<M64Track>();
            if (!File.Exists(filePath)) return tracks;

            try
            {
                byte[] data = File.ReadAllBytes(filePath);
                int pos = 0;

                if (data.Length < 14) return tracks;
                if (data[pos++] != 0x4D || data[pos++] != 0x54 || data[pos++] != 0x68 || data[pos++] != 0x64) return tracks;
                pos += 4; // Skip length (should be 6)
                int format = (data[pos++] << 8) | data[pos++];
                int numTracks = (data[pos++] << 8) | data[pos++];
                int division = (data[pos++] << 8) | data[pos++]; // ticks per quarter note

                double divisionRatio = (double)ticksPerBeat / division;

                for (int t = 0; t < numTracks; t++)
                {
                    if (pos + 8 >= data.Length) break;
                    if (data[pos++] != 0x4D || data[pos++] != 0x54 || data[pos++] != 0x72 || data[pos++] != 0x6B) break;
                    int trackLength = (data[pos++] << 24) | (data[pos++] << 16) | (data[pos++] << 8) | data[pos++];
                    int trackEnd = pos + trackLength;

                    var channelTracks = new Dictionary<byte, M64Track>();
                    var activeNotes = new Dictionary<int, (int startTick, byte velocity)>();

                    int currentTick = 0;
                    byte runningStatus = 0;

                    while (pos < trackEnd && pos < data.Length)
                    {
                        int deltaTime = ReadVarInt(data, ref pos);
                        currentTick += deltaTime;

                        byte status = data[pos];
                        if ((status & 0x80) != 0)
                        {
                            runningStatus = status;
                            pos++;
                        }
                        else
                        {
                            status = runningStatus;
                        }

                        byte eventType = (byte)(status & 0xF0);
                        byte channel = (byte)(status & 0x0F);

                        if (eventType == 0x90) // Note On
                        {
                            byte pitch = data[pos++];
                            byte velocity = data[pos++];
                            int noteKey = (channel << 8) | pitch;

                            if (velocity > 0)
                            {
                                activeNotes[noteKey] = ((int)(currentTick * divisionRatio), velocity);
                            }
                            else
                            {
                                if (activeNotes.TryGetValue(noteKey, out var startInfo))
                                {
                                    int noteStart = startInfo.startTick;
                                    int noteDuration = (int)(currentTick * divisionRatio) - noteStart;
                                    if (noteDuration <= 0) noteDuration = ticksPerBeat / 2;

                                    if (!channelTracks.TryGetValue(channel, out var chTrack))
                                    {
                                        chTrack = new M64Track { ChannelIndex = channel };
                                        channelTracks[channel] = chTrack;
                                    }

                                    chTrack.Notes.Add(new M64Note
                                    {
                                        StartTick = noteStart,
                                        DurationTicks = noteDuration,
                                        Pitch = pitch,
                                        Velocity = startInfo.velocity,
                                        Instrument = chTrack.Instrument
                                    });
                                    activeNotes.Remove(noteKey);
                                }
                            }
                        }
                        else if (eventType == 0x80) // Note Off
                        {
                            byte pitch = data[pos++];
                            byte velocity = data[pos++];
                            int noteKey = (channel << 8) | pitch;

                            if (activeNotes.TryGetValue(noteKey, out var startInfo))
                            {
                                int noteStart = startInfo.startTick;
                                int noteDuration = (int)(currentTick * divisionRatio) - noteStart;
                                if (noteDuration <= 0) noteDuration = ticksPerBeat / 2;

                                if (!channelTracks.TryGetValue(channel, out var chTrack))
                                {
                                    chTrack = new M64Track { ChannelIndex = channel };
                                    channelTracks[channel] = chTrack;
                                }

                                chTrack.Notes.Add(new M64Note
                                {
                                    StartTick = noteStart,
                                    DurationTicks = noteDuration,
                                    Pitch = pitch,
                                    Velocity = startInfo.velocity,
                                    Instrument = chTrack.Instrument
                                });
                                activeNotes.Remove(noteKey);
                            }
                        }
                        else if (eventType == 0xA0 || eventType == 0xB0 || eventType == 0xE0)
                        {
                            pos += 2;
                        }
                        else if (eventType == 0xC0 || eventType == 0xD0)
                        {
                            byte val = data[pos++];
                            if (eventType == 0xC0)
                            {
                                if (!channelTracks.TryGetValue(channel, out var chTrack))
                                {
                                    chTrack = new M64Track { ChannelIndex = channel, Instrument = val };
                                    channelTracks[channel] = chTrack;
                                }
                                else
                                {
                                    chTrack.Instrument = val;
                                }
                            }
                        }
                        else if (status == 0xFF)
                        {
                            byte metaType = data[pos++];
                            int metaLen = ReadVarInt(data, ref pos);
                            pos += metaLen;
                        }
                        else if (status == 0xF0 || status == 0xF7)
                        {
                            int sysexLen = ReadVarInt(data, ref pos);
                            pos += sysexLen;
                        }
                    }

                    foreach (var chTrack in channelTracks.Values)
                    {
                        if (chTrack.Notes.Count > 0)
                        {
                            var existing = tracks.FirstOrDefault(t => t.ChannelIndex == chTrack.ChannelIndex);
                            if (existing == null)
                            {
                                tracks.Add(chTrack);
                            }
                            else
                            {
                                existing.Notes.AddRange(chTrack.Notes);
                            }
                        }
                    }
                    
                    pos = trackEnd;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing MIDI: {ex.Message}");
            }

            foreach (var track in tracks)
            {
                track.Notes = track.Notes.OrderBy(n => n.StartTick).ToList();
            }

            return tracks;
        }

        private static int ReadVarInt(byte[] data, ref int pos)
        {
            int value = 0;
            byte b;
            do
            {
                b = data[pos++];
                value = (value << 7) | (b & 0x7F);
            } while ((b & 0x80) != 0);
            return value;
        }
    }
}
