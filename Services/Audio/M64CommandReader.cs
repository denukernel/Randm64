using System;
using System.Collections.Generic;
using System.IO;

namespace Sm64DecompLevelViewer.Services.Audio;
internal static class M64CommandReader
{
        internal static int GetSeqCmdSize(byte cmd, byte[] data, ref int pos)
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
        internal static int GetChanCmdSize(byte cmd, byte[] data, ref int pos)
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
                cmd == 0xf5 || cmd == 0xc2 || cmd == 0xda || cmd == 0xde || cmd == 0xe7)
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
        internal static int GetLayerCmdSize(byte cmd, byte[] data, ref int pos)
        {
            // 0 parameter bytes
            if (cmd == 0xff || cmd == 0xf7 || cmd == 0xc4 || cmd == 0xc5 || 
                cmd == 0xc8 || cmd == 0xcc)
            {
                return 0;
            }

            // Rest command (0xc0) takes a variable-length integer delay parameter
            // Set short note default play percentage (0xc3) takes a variable-length integer parameter
            if (cmd == 0xc0 || cmd == 0xc3)
            {
                ReadVarInt(data, ref pos);
                return 0;
            }

            // 1 parameter byte
            if (cmd == 0xf8 || cmd == 0xf4 || cmd == 0xc1 || cmd == 0xca || 
                cmd == 0xc2 || cmd == 0xc9 || cmd == 0xc6)
            {
                return 1;
            }

            // 2 parameter bytes
            if (cmd == 0xfc || cmd == 0xfb)
            {
                return 2;
            }

            // 3 parameter bytes (e.g. 0xcb reads s16 + u8 = 3 bytes)
            if (cmd == 0xcb)
            {
                return 3;
            }

            if (cmd == 0xc7)
            {
                if (pos < data.Length)
                {
                    byte mode = data[pos++];
                    pos++; // targetNote
                    if ((mode & 0x80) != 0)
                    {
                        pos++;
                    }
                    else
                    {
                        ReadVarInt(data, ref pos);
                    }
                }
                return 0;
            }

            // Default for short notes: d0..df select velocity from table (0 bytes parameter)
            if (cmd >= 0xd0 && cmd <= 0xef)
            {
                return 0;
            }

            return 0;
        }
        internal static int ReadVarInt(byte[] data, ref int pos)
        {
            if (pos < 0 || pos >= data.Length) return 0;
            byte b1 = data[pos++];
            if (b1 >= 0x80)
            {
                if (pos < 0 || pos >= data.Length) return b1 & 0x7F;
                byte b2 = data[pos++];
                return ((b1 & 0x7F) << 8) | b2;
            }
            return b1;
        }
        internal static void WriteVarInt(BinaryWriter bw, int value)
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
}
