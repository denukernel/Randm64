using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Sm64DecompLevelViewer.Services;

public static class PcmSampleFile
{
    public sealed record Chunk(string Id, int Start, int Length);
    public static List<Chunk> Chunks(byte[] data, bool wav = false)
    {
        if (data.Length < 12 || Encoding.ASCII.GetString(data, 0, 4) != (wav ? "RIFF" : "FORM") || Encoding.ASCII.GetString(data, 8, 4) != (wav ? "WAVE" : "AIFF"))
            throw new InvalidDataException("expected uncompressed wav or aiff audio");
        uint size = wav ? BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4)) : BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(4));
        if ((long)size + 8 != data.Length) throw new InvalidDataException("audio container length is invalid");
        var result = new List<Chunk>();
        for (int pos = 12; pos < data.Length;)
        {
            if (pos + 8 > data.Length) throw new InvalidDataException("truncated audio chunk");
            uint length = wav ? BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(pos + 4)) : BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(pos + 4));
            long next = pos + 8L + length + (length & 1);
            if (next > data.Length) throw new InvalidDataException("audio chunk exceeds file bounds");
            result.Add(new(Encoding.ASCII.GetString(data, pos, 4), pos + 8, checked((int)length)));
            pos = (int)next;
        }
        return result;
    }
    public static (int Start, int Length, int Frames, double Rate) AudioRange(byte[] aiff)
    {
        var chunks = Chunks(aiff);
        var comm = chunks.Single(c => c.Id == "COMM"); var sound = chunks.Single(c => c.Id == "SSND");
        if (comm.Length < 18 || sound.Length < 8) throw new InvalidDataException("truncated aiff audio header");
        int channels = BinaryPrimitives.ReadUInt16BigEndian(aiff.AsSpan(comm.Start));
        int bits = BinaryPrimitives.ReadUInt16BigEndian(aiff.AsSpan(comm.Start + 6));
        uint frames = BinaryPrimitives.ReadUInt32BigEndian(aiff.AsSpan(comm.Start + 2));
        uint offset = BinaryPrimitives.ReadUInt32BigEndian(aiff.AsSpan(sound.Start));
        long length = (long)frames * channels * 2;
        if (channels is < 1 or > 2 || bits != 16 || frames == 0 || offset > sound.Length - 8 || length > sound.Length - 8L - offset)
            throw new InvalidDataException("expected valid 16-bit pcm aiff samples");
        int exponent = BinaryPrimitives.ReadUInt16BigEndian(aiff.AsSpan(comm.Start + 8));
        ulong mantissa = BinaryPrimitives.ReadUInt64BigEndian(aiff.AsSpan(comm.Start + 10));
        double rate = mantissa * Math.Pow(2, (exponent & 32767) - 16383 - 63);
        // Older chaos runs changed this metadata above normal device rates. PCM
        // transforms can still edit those files without changing their bank tuning.
        if ((exponent & 32768) != 0 || !double.IsFinite(rate) || rate < 1000 || rate > 1000000) throw new InvalidDataException("unsupported sample rate");
        return (checked(sound.Start + 8 + (int)offset), checked((int)length), checked((int)frames), rate);
    }
    public static byte[] ReplaceFromWav(byte[] original, byte[] wav)
    {
        var range = AudioRange(original); var originalChunks = Chunks(original); var chunks = Chunks(wav, true);
        var fmt = chunks.Single(c => c.Id == "fmt "); var pcm = chunks.Single(c => c.Id == "data");
        if (fmt.Length < 16) throw new InvalidDataException("truncated wav format");
        int format = BinaryPrimitives.ReadUInt16LittleEndian(wav.AsSpan(fmt.Start));
        int channels = BinaryPrimitives.ReadUInt16LittleEndian(wav.AsSpan(fmt.Start + 2));
        int rate = BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(fmt.Start + 4));
        int align = BinaryPrimitives.ReadUInt16LittleEndian(wav.AsSpan(fmt.Start + 12));
        int bits = BinaryPrimitives.ReadUInt16LittleEndian(wav.AsSpan(fmt.Start + 14));
        if (format != 1 || channels is < 1 or > 2 || bits != 16 || align != channels * 2 || pcm.Length == 0 || pcm.Length % align != 0 || rate < 1000 || rate > 192000)
            throw new InvalidDataException("use a mono or stereo 16-bit pcm wav file");
        int frames = pcm.Length / align;
        int outputFrames = checked((int)Math.Round(frames * range.Rate / rate));
        if (outputFrames < 1 || outputFrames > 8 * 1024 * 1024) throw new InvalidDataException("replacement sample length is unsupported");
        bool instrumentLoop = originalChunks.Any(c => c.Id == "INST" && c.Length >= 20
            && (BinaryPrimitives.ReadUInt16BigEndian(original.AsSpan(c.Start + 8)) != 0 || BinaryPrimitives.ReadUInt16BigEndian(original.AsSpan(c.Start + 14)) != 0));
        bool hasEncodedLoops = originalChunks.Any(c => c.Id == "APPL" && c.Length >= 20
            && Encoding.ASCII.GetString(original, c.Start, Math.Min(c.Length, 16)).Contains("VADPCMLOOPS")
            && BinaryPrimitives.ReadUInt16BigEndian(original.AsSpan(c.Start + 18)) != 0);
        bool loopMetadata = instrumentLoop || hasEncodedLoops;
        if (originalChunks.Any(c => c.Id == "INST" && c.Length >= 20
            && (BinaryPrimitives.ReadUInt16BigEndian(original.AsSpan(c.Start + 8)) > 1 || BinaryPrimitives.ReadUInt16BigEndian(original.AsSpan(c.Start + 14)) > 1)))
            throw new InvalidDataException("the target encoder cannot preserve bidirectional instrument loops");
        foreach (var chunk in originalChunks.Where(c => c.Id == "APPL" && c.Length >= 20 && Encoding.ASCII.GetString(original, c.Start, 16).Contains("VADPCMLOOPS")))
        {
            int loops = BinaryPrimitives.ReadUInt16BigEndian(original.AsSpan(chunk.Start + 18));
            if (loops > (chunk.Length - 20) / 44) throw new InvalidDataException("truncated encoded loop metadata");
            for (int i = 0; i < loops; i++)
                if (BinaryPrimitives.ReadInt32BigEndian(original.AsSpan(chunk.Start + 28 + i * 44)) != -1)
                    throw new InvalidDataException("the target encoder cannot preserve a finite encoded loop count");
        }
        if (loopMetadata && outputFrames != range.Frames)
            throw new InvalidDataException($"this sample has instrument/loop metadata; preserve its {range.Frames} frames after conversion to {range.Rate:0.##} hz");
        if (hasEncodedLoops && (!originalChunks.Any(c => c.Id == "MARK") || !instrumentLoop))
            throw new InvalidDataException("this sample has encoded-only loop metadata; author marker/instrument loops before replacing its pcm");
        short Sample(int frame)
        {
            int pos = pcm.Start + Math.Clamp(frame, 0, frames - 1) * align;
            int value = BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(pos));
            if (channels == 2) value = (value + BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(pos + 2))) / 2;
            return (short)value;
        }
        byte[] sound = new byte[8 + outputFrames * 2];
        for (int i = 0; i < outputFrames; i++)
        {
            double position = i * (double)rate / range.Rate;
            int left = Math.Min(frames - 1, (int)position); double blend = position - left;
            short value = (short)Math.Clamp(Math.Round(Sample(left) * (1 - blend) + Sample(left + 1) * blend), short.MinValue, short.MaxValue);
            BinaryPrimitives.WriteInt16BigEndian(sound.AsSpan(8 + i * 2), value);
        }
        using var output = new MemoryStream();
        output.Write(Encoding.ASCII.GetBytes("FORM\0\0\0\0AIFF"));
        foreach (var chunk in originalChunks)
        {
            // The decomp Makefile regenerates codebooks with tabledesign and loop
            // history with vadpcm_enc. Never feed old PCM predictor/history state back in.
            if (chunk.Id == "APPL")
            {
                string name = Encoding.ASCII.GetString(original, chunk.Start, chunk.Length);
                if (name.Contains("VADPCMCODES") || name.Contains("VADPCMLOOPS")) continue;
            }
            byte[] bytes = original.AsSpan(chunk.Start, chunk.Length).ToArray();
            if (chunk.Id == "SSND") bytes = sound;
            if (chunk.Id == "COMM")
            {
                BinaryPrimitives.WriteUInt16BigEndian(bytes, 1);
                BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(2), (uint)outputFrames);
                BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(6), 16);
            }
            output.Write(Encoding.ASCII.GetBytes(chunk.Id));
            byte[] size = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(size, bytes.Length); output.Write(size); output.Write(bytes);
            if ((bytes.Length & 1) != 0) output.WriteByte(0);
        }
        byte[] result = output.ToArray(); BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(4), result.Length - 8);
        AudioRange(result); return result;
    }
    public static byte[] Corrupt(byte[] original, double intensity, int mode, Random random)
    {
        if (!double.IsFinite(intensity) || intensity < 0 || intensity > 500 || mode is < 0 or > 5) throw new ArgumentOutOfRangeException(nameof(intensity));
        var range = AudioRange(original);
        byte[] bytes = (byte[])original.Clone();
        int count = Math.Clamp((int)(range.Length * intensity / 100 * 0.05), 0, range.Length);
        for (int i = 0; i < count; i++)
        {
            int pos = random.Next(range.Start, range.Start + range.Length);
            switch (mode)
            {
                case 0: if (pos + 1 < range.Start + range.Length) (bytes[pos], bytes[pos + 1]) = (bytes[pos + 1], bytes[pos]); break;
                case 1: bytes[pos] = (byte)random.Next(256); break;
                case 2: bytes[pos] ^= (byte)random.Next(256); break;
                case 3: bytes[pos] = (byte)(bytes[pos] << 1 | bytes[pos] >> 7); break;
                case 4: bytes[pos] = 0; break;
                case 5: bytes[pos] = unchecked((byte)(bytes[pos] + random.Next(-16, 17))); break;
            }
        }
        return bytes.SequenceEqual(original) ? bytes : WithoutEncodedState(bytes);
    }

    public static byte[] WithoutEncodedState(byte[] data)
    {
        var chunks = Chunks(data);
        // Encoded-only loops cannot be dropped: keep their bounds/count. The game
        // encoder generates new ADPCM loop state when encoding the changed PCM.
        bool markerLoops = chunks.Any(c => c.Id == "MARK") && chunks.Any(c => c.Id == "INST");
        using var output = new MemoryStream();
        output.Write(data, 0, 12);
        foreach (var chunk in chunks)
        {
            if (chunk.Id == "APPL")
            {
                string name = Encoding.ASCII.GetString(data, chunk.Start, Math.Min(chunk.Length, 16));
                if (name.Contains("VADPCMCODES") || (markerLoops && name.Contains("VADPCMLOOPS"))) continue;
            }
            output.Write(data, chunk.Start - 8, 8 + chunk.Length + (chunk.Length & 1));
        }
        byte[] result = output.ToArray();
        BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(4), result.Length - 8);
        AudioRange(result);
        return result;
    }
}
