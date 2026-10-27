using System;
using System.Collections.Generic;
using System.IO;
using static Sm64DecompLevelViewer.Services.Audio.M64CommandReader;
namespace Sm64DecompLevelViewer.Services.Audio;
internal static class M64Diagnostics
{
        public static List<string> DiagnoseM64(string filePath)
        {
            var reports = new List<string>();
            try
            {
                if (!File.Exists(filePath))
                {
                    reports.Add($"ERROR: File not found at path: {filePath}");
                    return reports;
                }

                byte[] data = File.ReadAllBytes(filePath);
                reports.Add($"Diagnosing M64: {Path.GetFileName(filePath)} ({data.Length} bytes)");

                if (data.Length < 6)
                {
                    reports.Add("ERROR: M64 file is too short (minimum size is 6 bytes).");
                    return reports;
                }

                // 1. Scan Sequence Header
                int pos = 0;
                int seqStartOffset = 0;
                if (data[0] == 0x00 && data[1] == 0x02)
                {
                    reports.Add("Info: Found M64 sequence wrapper header (first 2 bytes are 0x00 0x02).");
                    int length = (data[2] << 8) | data[3];
                    reports.Add($"Info: Sequence wrapper length specified as {length} bytes.");
                    seqStartOffset = 4;
                    pos = 4;
                }

                List<int> channelOffsets = new List<int>();
                bool channelsInitialized = false;

                // Scan master sequence commands
                var seqVisited = new HashSet<int>();
                bool endSeq = false;

                while (pos < data.Length && !endSeq)
                {
                    if (seqVisited.Contains(pos))
                    {
                        reports.Add($"WARNING: Infinite loop detected in sequence control structure at offset 0x{pos:X}");
                        break;
                    }
                    seqVisited.Add(pos);

                    byte cmd = data[pos++];
                    if (cmd == 0xff)
                    {
                        reports.Add($"Info: Sequence End command (0xFF) reached at offset 0x{pos-1:X}");
                        break;
                    }

                    if (cmd == 0xfb) // Jump
                    {
                        int jumpOffset = (data[pos++] << 8) | data[pos++];
                        reports.Add($"Info: Sequence JUMP to 0x{jumpOffset:X} found at offset 0x{pos-3:X}");
                        if (jumpOffset < 0 || jumpOffset >= data.Length)
                        {
                            reports.Add($"CRITICAL: Sequence Jump points to out-of-bounds offset 0x{jumpOffset:X}");
                            break;
                        }
                        pos = jumpOffset;
                    }
                    else if (cmd == 0xfc) // Call
                    {
                        int callOffset = (data[pos++] << 8) | data[pos++];
                        reports.Add($"Info: Sequence CALL to 0x{callOffset:X} found at offset 0x{pos-3:X}");
                        if (callOffset < 0 || callOffset >= data.Length)
                        {
                            reports.Add($"CRITICAL: Sequence Call points to out-of-bounds offset 0x{callOffset:X}");
                            break;
                        }
                        pos = callOffset;
                    }
                    else if (cmd >= 0x90 && cmd <= 0x9f)
                    {
                        // seq_startchannel
                        int chIndex = cmd & 0x0f;
                        int chanOffset = (data[pos++] << 8) | data[pos++];
                        reports.Add($"Info: Start Channel {chIndex} pointing to offset 0x{chanOffset:X} at offset 0x{pos-3:X}");
                        if (chanOffset < 0 || chanOffset >= data.Length)
                        {
                            reports.Add($"CRITICAL: Channel {chIndex} start offset 0x{chanOffset:X} is out of bounds!");
                        }
                        else
                        {
                            if (!channelOffsets.Contains(chanOffset)) channelOffsets.Add(chanOffset);
                        }
                    }
                    else if (cmd == 0xd3) // seq_setmutebhv
                    {
                        byte muteBhv = data[pos++];
                        reports.Add($"Info: Set mute behavior to 0x{muteBhv:X2} at offset 0x{pos-2:X}");
                    }
                    else if (cmd == 0xd7) // seq_initchannels
                    {
                        int mask = (data[pos++] << 8) | data[pos++];
                        channelsInitialized = true;
                        reports.Add($"Info: Init channels mask 0x{mask:X4} at offset 0x{pos-3:X}");
                    }
                    else
                    {
                        int size = GetSeqCmdSize(cmd, data, ref pos);
                        pos += size;
                    }
                }

                // 2. Scan Channels
                var layerOffsets = new Dictionary<int, List<int>>();
                var chanVisited = new HashSet<int>();

                foreach (int chanOffset in channelOffsets)
                {
                    reports.Add($"--- Diagnosing Channel at offset 0x{chanOffset:X} ---");
                    int chanPos = chanOffset;
                    bool endChan = false;

                    while (chanPos < data.Length && !endChan)
                    {
                        if (chanVisited.Contains(chanPos))
                        {
                            reports.Add($"WARNING: Infinite loop detected in Channel parsing at offset 0x{chanPos:X}");
                            break;
                        }
                        chanVisited.Add(chanPos);

                        byte cmd = data[chanPos++];
                        if (cmd == 0xff)
                        {
                            break;
                        }

                        if (cmd == 0xfb) // Channel jump
                        {
                            int jumpOffset = (data[chanPos++] << 8) | data[chanPos++];
                            if (jumpOffset < 0 || jumpOffset >= data.Length)
                            {
                                reports.Add($"CRITICAL: Channel JUMP points to out-of-bounds offset 0x{jumpOffset:X}");
                                break;
                            }
                            chanPos = jumpOffset;
                        }
                        else if (cmd == 0xd9) // set decay release
                        {
                            byte decayRelease = data[chanPos++];
                            reports.Add($"Info: Channel set decay release to {decayRelease} at offset 0x{chanPos-2:X}");
                            if (decayRelease == 0)
                            {
                                reports.Add($"WARNING: Decay release value is 0 (may cause sound cutoffs or pops) at offset 0x{chanPos-1:X}");
                            }
                        }
                        else if ((cmd & 0xf0) == 0x90) // set layer
                        {
                            int layerIndex = cmd & 0x0f;
                            int layerOffset = (data[chanPos++] << 8) | data[chanPos++];
                            reports.Add($"Info: Set Layer {layerIndex} pointing to offset 0x{layerOffset:X} at offset 0x{chanPos-3:X}");
                            if (layerOffset < 0 || layerOffset >= data.Length)
                            {
                                reports.Add($"CRITICAL: Layer {layerIndex} points to out-of-bounds offset 0x{layerOffset:X}");
                            }
                            else
                            {
                                if (!layerOffsets.ContainsKey(layerIndex)) layerOffsets[layerIndex] = new List<int>();
                                if (!layerOffsets[layerIndex].Contains(layerOffset)) layerOffsets[layerIndex].Add(layerOffset);
                            }
                        }
                        else
                        {
                            int size = GetChanCmdSize(cmd, data, ref chanPos);
                            chanPos += size;
                        }
                    }
                }

                // 3. Scan Layers
                reports.Add("--- Diagnosing Layers & Note Commands ---");
                foreach (var kvp in layerOffsets)
                {
                    int layerIndex = kvp.Key;
                    foreach (int layerOffset in kvp.Value)
                    {
                        reports.Add($"Scanning Layer {layerIndex} at offset 0x{layerOffset:X}");
                        int layerPos = layerOffset;
                        bool endLayer = false;
                        int nestedCallDepth = 0;
                        int totalDelaysOrNotes = 0;
                        int loopStarts = 0;
                        int loopEnds = 0;

                        var layerVisited = new HashSet<int>();

                        while (layerPos < data.Length && !endLayer)
                        {
                            if (layerVisited.Contains(layerPos))
                            {
                                if (totalDelaysOrNotes == 0)
                                {
                                    reports.Add($"CRITICAL: Infinite loop with ZERO tick delay/notes detected at offset 0x{layerPos:X}! This WILL hang the N64 audio driver CPU thread!");
                                }
                                else
                                {
                                    reports.Add($"WARNING: Layer jump back (loop) to 0x{layerPos:X} detected.");
                                }
                                break;
                            }
                            layerVisited.Add(layerPos);

                            byte cmd = data[layerPos++];
                            if (cmd == 0xff)
                            {
                                if (nestedCallDepth > 0)
                                {
                                    reports.Add($"Info: Layer return from call at offset 0x{layerPos-1:X}");
                                    nestedCallDepth--;
                                }
                                else
                                {
                                    reports.Add($"Info: Layer End (0xFF) reached at offset 0x{layerPos-1:X}");
                                    break;
                                }
                            }
                            else if (cmd == 0xfb) // Layer jump
                            {
                                int jumpOffset = (data[layerPos++] << 8) | data[layerPos++];
                                if (jumpOffset < 0 || jumpOffset >= data.Length)
                                {
                                    reports.Add($"CRITICAL: Layer JUMP points to out-of-bounds offset 0x{jumpOffset:X}");
                                    break;
                                }
                                layerPos = jumpOffset;
                            }
                            else if (cmd == 0xfc) // Layer call
                            {
                                int callOffset = (data[layerPos++] << 8) | data[layerPos++];
                                reports.Add($"Info: Layer CALL to 0x{callOffset:X} at offset 0x{layerPos-3:X}");
                                nestedCallDepth++;
                                if (nestedCallDepth > 4)
                                {
                                    reports.Add($"CRITICAL: Layer call stack depth is {nestedCallDepth} (exceeds SM64 audio driver limit of 4 calls). This WILL crash the audio driver stack memory!");
                                }
                                if (callOffset < 0 || callOffset >= data.Length)
                                {
                                    reports.Add($"CRITICAL: Layer call points to out-of-bounds offset 0x{callOffset:X}");
                                    break;
                                }
                                layerPos = callOffset;
                            }
                            else if (cmd == 0xf8) // Loop start
                            {
                                loopStarts++;
                                layerPos++; // Skip remIters
                            }
                            else if (cmd == 0xf7) // Loop end
                            {
                                loopEnds++;
                                if (loopEnds > loopStarts)
                                {
                                    reports.Add($"WARNING: Loop End (0xF7) encountered without matching Loop Start (0xF8) at offset 0x{layerPos-1:X}");
                                }
                            }
                            else if (cmd == 0xc0) // Delay (varint)
                            {
                                int startPos = layerPos;
                                int delay = ReadVarInt(data, ref layerPos);
                                totalDelaysOrNotes++;
                                if (layerPos > data.Length)
                                {
                                    reports.Add($"CRITICAL: Malformed Delay varint at offset 0x{startPos:X} reads out of file bounds!");
                                }
                            }
                            else if (cmd >= 0x00 && cmd <= 0x3f) // Type 0 note (large)
                            {
                                totalDelaysOrNotes++;
                                int noteStartPos = layerPos - 1;

                                // Read duration (varint)
                                int durPos = layerPos;
                                ReadVarInt(data, ref layerPos);
                                if (layerPos > data.Length)
                                {
                                    reports.Add($"CRITICAL: Malformed Type 0 note duration varint at offset 0x{durPos:X} reads out of file bounds!");
                                    break;
                                }

                                // Read velocity (1 byte)
                                layerPos++;

                                // Read gateTime (1 byte)
                                int gatePos = layerPos;
                                if (layerPos >= data.Length)
                                {
                                    reports.Add($"CRITICAL: Malformed Type 0 note gateTime at offset 0x{gatePos:X} reads out of file bounds!");
                                    break;
                                }
                                byte gateVal = data[layerPos++];
                            }
                            else if (cmd >= 0x40 && cmd <= 0x7f) // Type 1 note (medium)
                            {
                                totalDelaysOrNotes++;
                                int durPos = layerPos;
                                ReadVarInt(data, ref layerPos);
                                if (layerPos > data.Length)
                                {
                                    reports.Add($"CRITICAL: Malformed Type 1 note duration varint at offset 0x{durPos:X} reads out of file bounds!");
                                    break;
                                }
                                layerPos++; // Skip velocity
                            }
                            else if (cmd >= 0x80 && cmd <= 0xbf) // Type 2 note (small)
                            {
                                totalDelaysOrNotes++;
                                layerPos += 2; // Skip velocity (1 byte) and duration/gate (1 byte)
                            }
                            else
                            {
                                int argSize = GetLayerCmdSize(cmd, data, ref layerPos);
                                layerPos += argSize;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                reports.Add($"CRITICAL DIAGNOSTIC ERROR: Exception occurred during analysis: {ex.Message}\n{ex.StackTrace}");
            }

            return reports;
        }
}
