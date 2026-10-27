using System;
using System.Collections.Generic;
using System.IO;

namespace Sm64DecompLevelViewer.Services;
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
