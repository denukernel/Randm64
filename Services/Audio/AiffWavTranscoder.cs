using System;
using System.Collections.Generic;
using System.IO;

namespace Sm64DecompLevelViewer.Services;
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
