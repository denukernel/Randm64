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

    // A looping sequence jumps at its script boundary, even if a parsed or
    // edited layer contains notes after that boundary. One-shots may be edited
    // past their original ending, so retain the longer editable extent.
    public static int GetTransportEndTick(IEnumerable<M64Track> tracks, int sequenceEndTick, bool hasSequenceLoop) =>
        hasSequenceLoop && sequenceEndTick > 0 ? sequenceEndTick : Math.Max(sequenceEndTick, GetEndTick(tracks));
}
