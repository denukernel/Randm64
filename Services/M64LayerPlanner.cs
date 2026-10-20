namespace Sm64DecompLevelViewer.Services;

internal static class M64LayerPlanner
{
    public static List<List<M64Note>> Split(IEnumerable<M64Note> notes)
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
                if (layers.Count == 4)
                    throw new InvalidOperationException($"More than four notes overlap in one channel at tick {note.StartTick}. Move an extra note to another channel. No notes were discarded.");
                layers.Add(layer = new());
            }
            layer.Add(note);
        }
        if (layers.Count == 0) layers.Add(new());
        return layers;
    }
}
