using static Sm64DecompLevelViewer.Services.Audio.M64CommandReader;
using System;
using System.Collections.Generic;
using System.IO;

namespace Sm64DecompLevelViewer.Services.Audio
{

    internal class M64SequenceParser
    {
        public byte Tempo { get; set; } = 100;
        public M64TempoMap TempoMap { get; } = new(100);
        public int LoopStartTick { get; set; } = 0;
        public bool HasSequenceLoop { get; private set; }
        public int SequenceEndTick { get; private set; }

        private M64SequenceSaver? _sequenceSaver;
        private bool _loadFailed;
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

        private class ChannelSegment
        {
            public byte ChannelIndex { get; set; }
            public int Offset { get; set; }
            public int StartTick { get; set; }
        }

        public List<M64Track> LoadM64(string filePath, byte[]? previewData = null, bool strict = false)
        {
            var tracks = new List<M64Track>();
            byte[]? loadedData = null;
            var saveStarts = new List<M64ChannelStart>();
            int saveEndTick = 0;
            bool parsedCompletely = false;
            _sequenceSaver = null;
            _loadFailed = true;
            Tempo = 120; // SM64 sequence-player default, before seq_settempo.
            TempoMap.Reset(Tempo);
            HasSequenceLoop = false;
            SequenceEndTick = 0;

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
                if (previewData == null && !File.Exists(filePath))
                {
                    Console.WriteLine($"M64 file not found: {filePath}");
                    return tracks;
                }

                byte[] data = previewData == null ? File.ReadAllBytes(filePath) : (byte[])previewData.Clone();
                string region = Sm64SoundBank.GetVersionForSequence(filePath);
                loadedData = data;
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
                        HasSequenceLoop = seqTick > firstVisitTick;
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
                        saveStarts.Add(new(ch, pos - 2, chOffset, seqTick));
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
                        if (cmd == 0xdd || cmd == 0xdc)
                        {
                            // US/EU: settempo is unsigned; addtempo is signed.
                            // Keep the event's tick instead of applying the final BPM to every section.
                            double bpm = cmd == 0xdd ? data[pos] : TempoMap.GetTempoAt(seqTick) + (sbyte)data[pos];
                            TempoMap.SetTempo(seqTick, Math.Max(1.0 / 48, bpm));
                        }
                        int argSize = GetSeqCmdSize(cmd, data, ref pos);
                        pos += argSize;
                    }
                }

                saveEndTick = seqTick;
                SequenceEndTick = seqTick;
                Tempo = (byte)Math.Clamp(TempoMap.GetTempoAt(0), 1, 255);

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
                        // Channel initialization resets freqScale to 1.0.
                        track.PitchChanges.Add(new(currentChTick, 1));
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
                                currentVol = data[tracePos++];
                                channelEvents.Add(new ChannelStateEvent { Tick = currentChTick, Volume = currentVol, Pan = currentPan, Reverb = currentReverb });
                            }
                            else if (cmd == 0xdd) // pan
                            {
                                currentPan = data[tracePos++];
                                channelEvents.Add(new ChannelStateEvent { Tick = currentChTick, Volume = currentVol, Pan = currentPan, Reverb = currentReverb });
                            }
                            else if (cmd == 0xd4) // reverb
                            {
                                currentReverb = data[tracePos++];
                                channelEvents.Add(new ChannelStateEvent { Tick = currentChTick, Volume = currentVol, Pan = currentPan, Reverb = currentReverb });
                            }
                            else if (cmd == 0xd3) // chan_pitchbend: signed octave fraction
                            {
                                int bend = (sbyte)data[tracePos++];
                                // data.c: gPitchBendFrequencyScale[k] = 0.5 * 2^(k/127).
                                // EU inserts a leading 0.5 entry but keeps the +127 index.
                                // SH/CN insert that entry and use +128, retaining neutral zero.
                                int tableBend = Math.Clamp(bend - (region == "eu" ? 1 : 0), -127, 127);
                                track.PitchChanges.Add(new(currentChTick, Math.Pow(2, tableBend / 127.0)));
                            }
                            else if (cmd == 0xde) // chan_freqscale: unsigned Q15 multiplier
                            {
                                int scale = (data[tracePos++] << 8) | data[tracePos++];
                                track.PitchChanges.Add(new(currentChTick, scale / 32768.0));
                            }
                            else if (cmd == 0xfd) // channel delay
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
                                track.Volume = data[pos++];
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
                            byte currentInstrument = track.Instrument;
                            var visitedOffsets = new HashSet<int>();
                            ParseLayerEvents(data, layerOffset, track, l, ref currentTick, visitedOffsets, ref currentTranspose, ref lastPlayPercentage, channelEvents, ref currentInstrument);
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
                parsedCompletely = true;
            }
            catch (Exception ex)
            {
                if (strict) throw new InvalidDataException("M64 parsing failed: " + ex.Message, ex);
                System.Windows.MessageBox.Show($"Error parsing M64:\n{ex.Message}\n\n{ex.StackTrace}", "M64 Parser Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }

            if (loadedData != null && saveStarts.Count > 0 && parsedCompletely)
            {
                _sequenceSaver = new M64SequenceSaver(loadedData, saveStarts,
                    tracks.Select(M64SequenceSaver.Snapshot).ToList(), saveEndTick);
                _loadFailed = false;
            }
            return tracks;
        }

        public M64SaveResult SaveM64(string filePath, List<M64Track> tracks, bool isNotesModified = true)
        {
            if (_loadFailed) return new M64SaveResult { Error = "Reload a valid M64 before saving; the loaded sequence could not be parsed completely." };
            // Derive changes from the loaded snapshot, not an unreliable UI dirty
            // flag, and never restore stale .bak bytes over subsequent edits.
            _sequenceSaver ??= new M64SequenceSaver(null, [], [], 0);
            return _sequenceSaver.Save(filePath, tracks, Tempo, TempoMap);
        }

        internal M64SaveResult EncodeM64(List<M64Track> tracks, out byte[]? data, bool allowInheritedLarge = false)
        {
            data = null;
            if (_loadFailed || _sequenceSaver == null)
                return new M64SaveResult { Error = "The sequence could not be parsed completely." };
            return _sequenceSaver.Encode(tracks, Tempo, TempoMap, out data, allowInheritedLarge);
        }

        private struct LoopState
        {
            public int StartPos;
            public int RemIters;
        }

        private void ParseLayerEvents(byte[] data, int startOffset, M64Track track, int layerIndex, ref int currentTick, HashSet<int> visitedOffsets, ref int currentTranspose, ref int lastPlayPercentage, List<ChannelStateEvent> channelEvents, ref byte currentInstrument, int depth = 0)
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
                        ParseLayerEvents(data, jumpOffset, track, layerIndex, ref currentTick, pathVisited, ref currentTranspose, ref lastPlayPercentage, channelEvents, ref currentInstrument, depth + 1);
                        break;
                    }
                    else if (cmd == 0xfc) // layer_call
                    {
                        int callOffset = (data[pos++] << 8) | data[pos++];
                        ParseLayerEvents(data, callOffset, track, layerIndex, ref currentTick, pathVisited, ref currentTranspose, ref lastPlayPercentage, channelEvents, ref currentInstrument, depth + 1);
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
                            Gate = 0,
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
            // Imported MIDI currently uses a constant preview tempo; do not
            // inherit tempo changes from the previously opened M64 sequence.
            TempoMap.Reset(Tempo);
            _sequenceSaver = null;
            _loadFailed = false;
            return MidiParser.ParseMidi(filePath, 48);
        }
    }

}
