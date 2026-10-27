using System.Diagnostics;
using System.Windows.Threading;

namespace Sm64DecompLevelViewer.Services.Audio;

// Uses the editor playback path and the original sequence identity for bank/region lookup.
// Corrupted bytes are parsed in memory; no sequence, backup or project file is written.
internal sealed class M64CorruptionPreview : IDisposable
{
    private readonly MidiPlayer _midi = new();
    private readonly SampleSynthPlayer _samples = new("6c6c45b1-9c10-4cec-bab0-0f4595828f14");
    private readonly MusicVoiceCoordinator _voices;
    private readonly MusicPlaybackCoordinator _playback;
    private readonly MusicInstrumentResolver _resolver;
    private readonly M64Service _sequence = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(20) };
    private readonly Stopwatch _clock = new();
    private List<M64Track> _tracks = new();
    private string _path = "";
    private double _tick;
    private int _end;
    internal event Action? Finished;
    internal event Action<string>? Failed;

    internal M64CorruptionPreview(string root)
    {
        _voices = new(_midi, () => _samples);
        _resolver = new(root, new Sm64SoundBank(root), new());
        _playback = new(_midi, _voices, _resolver, () => _samples);
        _timer.Tick += Tick;
    }

    internal string Play(string path, byte[] data, bool midi)
    {
        Stop();
        _path = path;
        _tracks = _sequence.LoadPreview(path, data);
        _end = M64PreviewSchedule.GetEndTick(_tracks);
        if (_tracks.Sum(t => t.Notes.Count) == 0) throw new InvalidOperationException("No playable notes in this sequence.");
        int fallback = 0;
        foreach (var key in _tracks.SelectMany(t => t.Notes).Select(n => (n.Instrument, n.Pitch)).Distinct())
        {
            var instrument = midi ? null : _resolver.ResolveInstrument(path, key.Instrument, key.Pitch);
            if (instrument != null) SampleSynthPlayer.Preload(instrument.SamplePath);
            else fallback++;
        }
        _forceMidi = midi;
        Advance(0);
        _clock.Restart();
        _timer.Start();
        return (midi ? "MIDI preview" : fallback == 0 ? "Game samples" : $"Game samples; {fallback} instrument/pitch pairs use MIDI fallback") +
            (_sequence.LoadWarnings.Count == 0 ? "" : $"; {_sequence.LoadWarnings.Count} parser warnings: {_sequence.LoadWarnings[0]}");
    }

    private bool _forceMidi;
    private void Advance(double next)
    {
        _playback.Advance(_tracks, _sequence.TempoMap, _path, _tick, next, _forceMidi, _ => true, (_, volume) => volume);
        _tick = next;
    }

    private void Tick(object? sender, EventArgs e)
    {
        try
        {
            double elapsed = Math.Clamp(_clock.Elapsed.TotalSeconds, 0, 0.05);
            _clock.Restart();
            Advance(_sequence.TempoMap.Advance(_tick, elapsed));
            if (_sequence.TempoMap.TickToSeconds(_tick) > _sequence.TempoMap.TickToSeconds(_end) + 2)
            {
                Stop();
                Finished?.Invoke();
            }
        }
        catch (Exception ex)
        {
            Stop();
            Failed?.Invoke(ex.Message);
        }
    }

    internal void Stop()
    {
        _timer.Stop();
        _clock.Reset();
        foreach (var voice in _playback.ActiveNotes) _voices.ReleaseVoice(voice);
        _playback.ActiveNotes.Clear();
        _samples.StopAll();
        _voices.MidiOwners.Clear();
        _voices.ActiveMidiNotes.Clear();
        _voices.VoiceIds.Clear();
        _tick = 0;
    }

    public void Dispose()
    {
        Stop();
        _timer.Tick -= Tick;
        _midi.Dispose();
        _samples.Dispose();
    }
}
