using System.Collections.Generic;
using System.Linq;

namespace Sm64DecompLevelViewer.Services;

public static class RomAddressMapping
{
    public sealed record Segment(ulong RamStart, ulong RamEnd, ulong RomStart, ulong RomEnd);
    public static ulong? Translate(ulong address, IEnumerable<Segment> segments)
    {
        // Compressed segments and overlays cannot be translated by adding a RAM offset.
        var matches = segments.Where(s => s.RamEnd > s.RamStart && s.RomEnd > s.RomStart
            && address >= s.RamStart && address < s.RamEnd).Distinct().ToArray();
        if (matches.Length != 1) return null;
        var segment = matches[0];
        if (segment.RomEnd - segment.RomStart < segment.RamEnd - segment.RamStart) return null;
        return segment.RomStart + address - segment.RamStart;
    }
}
