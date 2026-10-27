using System.Buffers.Binary;

namespace Sm64DecompLevelViewer.Services;

public static class SoundSampleInfo
{
    public static string Describe(byte[] aiff)
    {
        var range=PcmSampleFile.AudioRange(aiff);
        var comm=PcmSampleFile.Chunks(aiff).Single(chunk=>chunk.Id=="COMM");
        int channels=BinaryPrimitives.ReadUInt16BigEndian(aiff.AsSpan(comm.Start));
        var loop=AiffLoopReader.Read(aiff,range.Frames);
        string looping=loop.HasValue ? $"declared loop: frames {loop.Value.Start} to {loop.Value.End} (end exclusive), count {loop.Value.Count}" : "no valid sustain loop declared";
        return $"16-bit PCM · {channels} channel(s) · {range.Rate:0.##} Hz\n{range.Frames:N0} frames · {range.Frames/range.Rate:0.000} seconds\n{looping}\n\nraw sample preview uses the file rate. game pitch, envelopes, pan and effects depend on the sound bank and sequence; a sample index does not identify a gameplay sound.";
    }
}
