namespace Sm64DecompLevelViewer.Services;

// Read the current edit model instead of retaining indices into mutable note lists.
internal static class M64PreviewSchedule
{
    public static IEnumerable<M64Note> GetNotes(IEnumerable<M64Note> notes, double fromTick, double toTick,
        bool includeSustained = false, bool includeAtCursor = false)
    {
        return notes.Where(note => (note.StartTick < toTick ||
                (includeAtCursor && note.StartTick == fromTick && fromTick == toTick)) &&
                (note.StartTick >= fromTick || (includeSustained &&
                    note.StartTick + Sm64NoteTiming.GetDurationTicks(note.DurationTicks, note.Gate, note.CommandType) > fromTick)))
            .OrderBy(note => note.StartTick);
    }

    public static int GetEndTick(IEnumerable<M64Track> tracks) =>
        Math.Max(1, tracks.SelectMany(track => track.Notes)
            .Select(note => note.StartTick + note.DurationTicks).DefaultIfEmpty(480).Max());
}
