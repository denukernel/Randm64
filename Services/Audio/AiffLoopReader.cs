using System.Buffers.Binary;
using System.Text;

namespace Sm64DecompLevelViewer.Services;

internal readonly record struct AiffLoop(int Start, int End, int Count, bool PingPong = false);

internal static class AiffLoopReader
{
    // Loop ends are exclusive. MARK/INST is used by the extracted PCM AIFFs;
    // Nintendo's APPL/stoc/VADPCMLOOPS is also used by tool-generated samples.
    public static AiffLoop? Read(byte[] data, int frames)
    {
        if (data.Length < 12 || Encoding.ASCII.GetString(data, 0, 4) != "FORM") return null;
        var markers = new Dictionary<int, int>();
        int mode = 0, begin = 0, end = 0;
        AiffLoop? vadpcm = null;
        for (int pos = 12; pos <= data.Length - 8;)
        {
            string id = Encoding.ASCII.GetString(data, pos, 4);
            uint size = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(pos + 4, 4));
            pos += 8;
            if (size > data.Length - pos) break;
            var chunk = data.AsSpan(pos, (int)size);
            if (id == "MARK" && chunk.Length >= 2)
            {
                int count = BinaryPrimitives.ReadUInt16BigEndian(chunk);
                int cursor = 2;
                for (int i = 0; i < count && cursor <= chunk.Length - 7; i++)
                {
                    int markerId = BinaryPrimitives.ReadUInt16BigEndian(chunk.Slice(cursor, 2));
                    uint frame = BinaryPrimitives.ReadUInt32BigEndian(chunk.Slice(cursor + 2, 4));
                    if (frame <= int.MaxValue) markers[markerId] = (int)frame;
                    int stringBytes = 1 + chunk[cursor + 6];
                    cursor += 6 + ((stringBytes + 1) & ~1);
                }
            }
            else if (id == "INST" && chunk.Length >= 14)
            {
                mode = BinaryPrimitives.ReadUInt16BigEndian(chunk.Slice(8, 2));
                begin = BinaryPrimitives.ReadUInt16BigEndian(chunk.Slice(10, 2));
                end = BinaryPrimitives.ReadUInt16BigEndian(chunk.Slice(12, 2));
            }
            else if (id == "APPL" && chunk.Length >= 5 && Encoding.ASCII.GetString(chunk[..4]) == "stoc")
            {
                int nameLength = chunk[4];
                int offset = (5 + nameLength + 1) & ~1;
                if (offset <= chunk.Length - 16 && nameLength == 11 &&
                    Encoding.ASCII.GetString(chunk.Slice(5, nameLength)) == "VADPCMLOOPS" &&
                    BinaryPrimitives.ReadUInt16BigEndian(chunk.Slice(offset, 2)) == 1 &&
                    BinaryPrimitives.ReadUInt16BigEndian(chunk.Slice(offset + 2, 2)) > 0)
                {
                    int startFrame = BinaryPrimitives.ReadInt32BigEndian(chunk.Slice(offset + 4, 4));
                    int endFrame = BinaryPrimitives.ReadInt32BigEndian(chunk.Slice(offset + 8, 4));
                    int count = BinaryPrimitives.ReadInt32BigEndian(chunk.Slice(offset + 12, 4));
                    if (startFrame >= 0 && endFrame > startFrame && endFrame <= frames && count != 0)
                        vadpcm = new(startFrame, endFrame, count);
                }
            }
            long next = (long)pos + size + (size & 1);
            if (next > data.Length) break;
            pos = (int)next;
        }
        if (vadpcm.HasValue) return vadpcm;
        if ((mode == 1 || mode == 2) && markers.TryGetValue(begin, out int start) && markers.TryGetValue(end, out int finish) &&
            start >= 0 && finish > start && finish <= frames)
            return new(start, finish, -1, mode == 2);
        return null;
    }
}
