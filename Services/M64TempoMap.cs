namespace Sm64DecompLevelViewer.Services;

public readonly record struct M64TempoChange(int Tick, double Bpm);

// Sequence delays are musical ticks. Convert through the whole tempo timeline,
// so a timer update or a note duration can cross any number of tempo changes.
public sealed class M64TempoMap(double initialBpm = 120)
{
    private readonly List<M64TempoChange> _changes = [new(0, initialBpm)];
    public IReadOnlyList<M64TempoChange> Changes => _changes.AsReadOnly();
    private const double TicksPerBeat = 48;

    internal void Reset(double bpm)
    {
        _changes.Clear();
        _changes.Add(new(0, bpm));
    }

    internal void SetTempo(int tick, double bpm)
    {
        if (tick < 0 || !double.IsFinite(bpm) || bpm <= 0) throw new ArgumentOutOfRangeException();
        int index = _changes.FindIndex(change => change.Tick >= tick);
        var change = new M64TempoChange(tick, bpm);
        if (index < 0) _changes.Add(change);
        else if (_changes[index].Tick == tick) _changes[index] = change;
        else _changes.Insert(index, change);
    }

    public double GetTempoAt(double tick)
    {
        for (int i = _changes.Count - 1; i >= 0; i--)
            if (_changes[i].Tick <= tick) return _changes[i].Bpm;
        return _changes[0].Bpm;
    }

    public double TickToSeconds(double tick)
    {
        tick = Math.Max(0, tick);
        double seconds = 0;
        for (int i = 0; i < _changes.Count; i++)
        {
            var change = _changes[i];
            double end = i + 1 < _changes.Count ? Math.Min(tick, _changes[i + 1].Tick) : tick;
            seconds += (end - change.Tick) * 60 / (change.Bpm * TicksPerBeat);
            if (end >= tick) return seconds;
        }
        return seconds;
    }

    public double SecondsToTick(double seconds)
    {
        seconds = Math.Max(0, seconds);
        for (int i = 0; i < _changes.Count; i++)
        {
            var change = _changes[i];
            double ticksPerSecond = change.Bpm * TicksPerBeat / 60;
            if (i + 1 == _changes.Count) return change.Tick + seconds * ticksPerSecond;
            double segmentSeconds = (_changes[i + 1].Tick - change.Tick) / ticksPerSecond;
            if (seconds <= segmentSeconds) return change.Tick + seconds * ticksPerSecond;
            seconds -= segmentSeconds;
        }
        return 0;
    }

    public double Advance(double startTick, double elapsedSeconds) =>
        SecondsToTick(TickToSeconds(startTick) + Math.Max(0, elapsedSeconds));

    public double GetDurationSeconds(double startTick, double durationTicks) =>
        TickToSeconds(startTick + Math.Max(0, durationTicks)) - TickToSeconds(startTick);
}
