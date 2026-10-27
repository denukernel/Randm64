namespace Sm64DecompLevelViewer.Services;

internal static class M64LayerPlanner
{
    public static List<List<M64Note>> Split(IEnumerable<M64Note> notes, int maximumLayers = 4)
    {
        var layers = new List<List<M64Note>>();
        foreach (var note in notes.OrderBy(n => n.StartTick).ThenBy(n => n.LayerIndex))
        {
            if (note.StartTick < 0 || note.DurationTicks is < 1 or > 32767 || note.Pitch > 127 || note.Velocity > 127 || note.CommandType > 2)
                throw new InvalidOperationException("Notes need nonnegative start ticks, pitches/velocities 0–127, and durations 1–32767 ticks.");
            if ((long)note.StartTick + note.DurationTicks > int.MaxValue)
                throw new InvalidOperationException("A note extends beyond the supported timeline.");
            var layer = layers.FirstOrDefault(l => (long)l[^1].StartTick + l[^1].DurationTicks <= note.StartTick);
            if (layer == null)
            {
                if (layers.Count == maximumLayers)
                    throw new InvalidOperationException($"Saving this channel needs more than four M64 layers at tick {note.StartTick}. Overlapping notes are supported, but each channel has four layer slots. Preview can still play these notes. For a harmony, use Duplicate Channel with an unused channel and transpose that copy. No notes were discarded.");
                layers.Add(layer = new());
            }
            layer.Add(note);
        }
        if (layers.Count == 0) layers.Add(new());
        return layers;
    }
}
