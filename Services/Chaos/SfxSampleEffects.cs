using System.Buffers.Binary;
using System.IO;

namespace Sm64DecompLevelViewer.Services.Chaos;

internal static class SfxSampleEffects
{
    internal static readonly string[] Names = ["High Pitch (Chipmunk)", "Low Pitch (Demonic)", "Chaotic Pitch (Random)",
        "Reverse Playback", "Short Gate (Silence Tail)", "Amplify Volume", "Mute (Silence)", "Invert Phase",
        "Bitcrush (8-bit)", "Sample Hold (Lo-fi)", "Stutter", "Tremolo", "Echo", "Fade In", "Fade Out"];

    internal static byte[] Apply(byte[] original, IEnumerable<int> selected, bool pitchVariation, Random random)
    {
        int[] modes = selected.Distinct().Order().ToArray();
        if (modes.Any(m => m < 0 || m > Names.Length)) throw new InvalidDataException("unknown sound effect mode");
        var range = PcmSampleFile.AudioRange(original);
        var chunks = PcmSampleFile.Chunks(original);
        var comm = chunks.Single(c => c.Id == "COMM");
        int channels = BinaryPrimitives.ReadUInt16BigEndian(original.AsSpan(comm.Start));
        var pcm = new double[range.Frames * channels];
        for (int i = 0; i < pcm.Length; i++) pcm[i] = BinaryPrimitives.ReadInt16BigEndian(original.AsSpan(range.Start + i * 2));
        double pitch = 1;
        foreach (int mode in modes)
            pitch *= mode switch { 1 => 1.3 + random.NextDouble() * .9, 2 => .45 + random.NextDouble() * .3, 3 => .35 + random.NextDouble() * 2.15, _ => 1 };
        if (pitchVariation) pitch *= .7 + random.NextDouble() * .7;
        if (pitch != 1)
        {
            var source = (double[])pcm.Clone();
            for (int f = 0; f < range.Frames; f++)
            {
                double pos = f * pitch;
                int left = (int)pos;
                for (int ch = 0; ch < channels; ch++)
                    pcm[f * channels + ch] = left >= range.Frames ? 0 :
                        source[left * channels + ch] * (1 - (pos - left)) + source[Math.Min(left + 1, range.Frames - 1) * channels + ch] * (pos - left);
            }
        }
        foreach (int mode in modes)
        {
            var source = (double[])pcm.Clone();
            int gate = Math.Max(1, (int)(range.Frames * (.1 + random.NextDouble() * .4)));
            int block = Math.Max(1, (int)(range.Rate * .06));
            int delay = Math.Max(1, (int)(range.Rate * .12));
            for (int f = 0; f < range.Frames; f++)
                for (int ch = 0; ch < channels; ch++)
                {
                    int i = f * channels + ch;
                    pcm[i] = mode switch
                    {
                        4 => source[(range.Frames - 1 - f) * channels + ch],
                        5 => f < gate ? source[i] : 0,
                        6 => Math.Clamp(source[i] * 2, short.MinValue, short.MaxValue),
                        7 => 0,
                        8 => -source[i],
                        9 => Math.Truncate(source[i] / 256) * 256,
                        10 => source[(f / 4 * 4) * channels + ch],
                        11 => source[((f / (block * 2)) * (block * 2) + f % block) * channels + ch],
                        12 => source[i] * (.55 + .45 * Math.Sin(2 * Math.PI * 8 * f / range.Rate)),
                        13 => source[i] + (f >= delay ? pcm[(f - delay) * channels + ch] * .4 : 0),
                        14 => source[i] * f / Math.Max(1, range.Frames - 1),
                        15 => source[i] * (range.Frames - 1 - f) / Math.Max(1, range.Frames - 1),
                        _ => source[i]
                    };
                }
        }
        if (pitch == 1 && !modes.Any(m => m >= 4)) return (byte[])original.Clone();
        byte[] data = (byte[])original.Clone();
        for (int i = 0; i < pcm.Length; i++)
            BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(range.Start + i * 2), (short)Math.Clamp(Math.Round(pcm[i]), short.MinValue, short.MaxValue));
        // Keep duration, channel layout, offsets and loop markers. The native encoder
        // recomputes predictors/history rather than reusing state from the original PCM.
        return PcmSampleFile.WithoutEncodedState(data);
    }

    internal static bool IsSfx(string sampleRoot, string path) =>
        Path.GetRelativePath(sampleRoot, path).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0]
            .StartsWith("sfx", StringComparison.OrdinalIgnoreCase);
}
