namespace Sm64DecompLevelViewer.Services.Audio;
internal sealed class MusicPlaybackCoordinator
{
    private readonly MidiPlayer _midiPlayer;
    private readonly MusicVoiceCoordinator _voices;
    private readonly MusicInstrumentResolver _instrumentResolver;
    private readonly Func<SampleSynthPlayer?> _getSamples;
    private SampleSynthPlayer? _samplePlayer => _getSamples();
    public List<ActivePlayingNote> ActiveNotes { get; } = new();
    private List<ActivePlayingNote> _activePlayingNotesList => ActiveNotes;
    private HashSet<int> _activeMidiNotes => _voices.ActiveMidiNotes;
    public MusicPlaybackCoordinator(MidiPlayer midi, MusicVoiceCoordinator voices, MusicInstrumentResolver resolver, Func<SampleSynthPlayer?> samples)
    { _midiPlayer = midi; _voices = voices; _instrumentResolver = resolver; _getSamples = samples; }
    public void Advance(List<M64Track> tracks, M64TempoMap tempoMap, string sequencePath, double currentTick, double nextTick, bool forceMidi, Func<byte, bool> isAudible, Func<byte, byte, byte> channelVolume)
    {
            // 1. Process active notes for note-offs
            for (int i = _activePlayingNotesList.Count - 1; i >= 0; i--)
            {
                var note = _activePlayingNotesList[i];
                var source = note.Source!;
                bool exists = tracks.Any(t => t.ChannelIndex == note.Channel && t.Notes.Contains(source));
                bool samples = !forceMidi &&
                    !string.IsNullOrEmpty(_instrumentResolver.GetSamplePathForInstrument(sequencePath, source.Instrument, source.Pitch, out _));
                bool invalidated = !exists || !isAudible(note.Channel) ||
                    samples != note.UsesSamples || !MusicVoiceCoordinator.SamePlayback(source, note.Snapshot!);
                if (note.EndTick <= currentTick || invalidated)
                {
                    _activePlayingNotesList.RemoveAt(i);
                    // Sample notes already release on the mixer frame clock. A UI callback
                    // may pass a short note's end before its first buffer is rendered; a
                    // second, wall-clock release would silence its attack completely.
                    // Edits, deletes, mutes and preview-mode changes still release early.
                    if (!note.UsesSamples || invalidated) _voices.ReleaseVoice(note);
                }
            }

            // 2. Read the current model so live inserts, deletes and moves take effect.
            foreach (var track in tracks)
            {
                byte targetChannel = track.ChannelIndex;

                bool shouldPlay = isAudible(targetChannel);

                foreach (var note in M64PreviewSchedule.GetNotes(track.Notes, currentTick, nextTick, includeSustained: true, includeAtCursor: nextTick == currentTick))
                {
                    if (shouldPlay && note.Velocity > 0 && !_activePlayingNotesList.Any(v => ReferenceEquals(v.Source, note) && v.Channel == targetChannel))
                    {

                        double tuning = 0.0;
                        string samplePath = forceMidi ? null : _instrumentResolver.GetSamplePathForInstrument(sequencePath, note.Instrument, note.Pitch, out tuning);
                        byte previewVolume = channelVolume(targetChannel, note.ChannelVolume);
                        if (!string.IsNullOrEmpty(samplePath))
                        {
                            _samplePlayer?.PlayNote(targetChannel, note.Pitch, samplePath, note.Velocity, previewVolume, note.ChannelPan, tuning,
                                _instrumentResolver.ResolveInstrument(sequencePath, note.Instrument, note.Pitch), _voices.GetPreviewVoiceId(note),
                                tempoMap.GetDurationSeconds(Math.Max(note.StartTick, currentTick),
                                    note.StartTick + Sm64NoteTiming.GetDurationTicks(note.DurationTicks, note.Gate, note.CommandType)
                                        - Math.Max(note.StartTick, currentTick)), note.Reverb,
                                track.GetPitchFrames(Math.Max(note.StartTick, currentTick),
                                    note.StartTick + Sm64NoteTiming.GetDurationTicks(note.DurationTicks, note.Gate, note.CommandType),
                                    tempoMap, 44100));
                        }
                        else
                        {
                            byte gmPatch = _instrumentResolver.MapSm64InstrumentToGm(sequencePath, targetChannel, note.Instrument);
                            _midiPlayer.ProgramChange(targetChannel, gmPatch);
                            _midiPlayer.ControlChange(targetChannel, 7, previewVolume);
                            _midiPlayer.ControlChange(targetChannel, 10, note.ChannelPan);
                            _midiPlayer.ControlChange(targetChannel, 91, note.Reverb);
                            _midiPlayer.SetPitchScale(targetChannel, track.GetPitchScale(Math.Max(note.StartTick, currentTick)));
                            _voices.AcquireMidi(targetChannel, note.Pitch, note.Velocity);
                        }
                        
                        if (string.IsNullOrEmpty(samplePath))
                            _activeMidiNotes.Add((targetChannel << 8) | note.Pitch);
                        
                        int playTicks = Sm64NoteTiming.GetDurationTicks(note.DurationTicks, note.Gate, note.CommandType);

                        _activePlayingNotesList.Add(new ActivePlayingNote
                        {
                            Source = note, Snapshot = note.Clone(), VoiceId = _voices.GetPreviewVoiceId(note),
                            Channel = targetChannel,
                            Pitch = note.Pitch,
                            EndTick = note.StartTick + playTicks,
                            UsesSamples = !string.IsNullOrEmpty(samplePath)
                        });
                    }
                }
            }

            for (int i = _activePlayingNotesList.Count - 1; i >= 0; i--)
                if (!_activePlayingNotesList[i].UsesSamples && _activePlayingNotesList[i].EndTick <= nextTick)
                {
                    var ended = _activePlayingNotesList[i];
                    _activePlayingNotesList.RemoveAt(i);
                    _voices.ReleaseVoice(ended);
                }

            foreach (var track in tracks)
                if (_activePlayingNotesList.Any(note => note.Channel == track.ChannelIndex && !note.UsesSamples))
                    _midiPlayer.SetPitchScale(track.ChannelIndex, track.GetPitchScale(nextTick));

    }
}
