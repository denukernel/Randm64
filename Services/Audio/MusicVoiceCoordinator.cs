namespace Sm64DecompLevelViewer.Services.Audio;
internal sealed class MusicVoiceCoordinator
{
    private readonly MidiPlayer _midiPlayer;
    private readonly Func<SampleSynthPlayer?> _getSamplePlayer;
    private SampleSynthPlayer? _samplePlayer => _getSamplePlayer();
    public readonly Dictionary<int, int> MidiOwners = new();
    public readonly HashSet<int> ActiveMidiNotes = new();
    private HashSet<int> _activeMidiNotes => ActiveMidiNotes;
    public readonly Dictionary<M64Note, int> VoiceIds = new();
    private Dictionary<int, int> _midiOwners => MidiOwners;
    private Dictionary<M64Note, int> _previewVoiceIds => VoiceIds;
    public MusicVoiceCoordinator(MidiPlayer midi, Func<SampleSynthPlayer?> samples) { _midiPlayer = midi; _getSamplePlayer = samples; }
        internal void AcquireMidi(byte channel, byte pitch, byte velocity)
        {
            int key = (channel << 8) | pitch;
            _midiOwners.TryGetValue(key, out int owners);
            _midiOwners[key] = owners + 1;
            if (owners == 0) _midiPlayer.NoteOn(channel, pitch, velocity);
            _activeMidiNotes.Add(key);
        }
        internal void ReleaseVoice(ActivePlayingNote voice)
        {
            if (voice.UsesSamples) _samplePlayer?.StopVoice(voice.Channel, voice.VoiceId);
            else
            {
                int key = (voice.Channel << 8) | voice.Pitch;
                if (!_midiOwners.TryGetValue(key, out int owners)) return;
                if (owners > 1) _midiOwners[key] = owners - 1;
                else
                {
                    _midiOwners.Remove(key);
                    _activeMidiNotes.Remove(key);
                    _midiPlayer.NoteOff(voice.Channel, voice.Pitch);
                }
            }
        }
        internal int GetPreviewVoiceId(M64Note note)
        {
            // Live notes may share the default layer until the next save/replan.
            // Give each preview note its own voice so it cannot cut another layer.
            if (!_previewVoiceIds.TryGetValue(note, out int id))
            {
                id = _previewVoiceIds.Count;
                _previewVoiceIds.Add(note, id);
            }
            return id;
        }
        internal static bool SamePlayback(M64Note a, M64Note b) =>
            a.StartTick == b.StartTick && a.DurationTicks == b.DurationTicks && a.Gate == b.Gate &&
            a.CommandType == b.CommandType && a.Pitch == b.Pitch && a.Instrument == b.Instrument &&
            a.Velocity == b.Velocity && a.ChannelPan == b.ChannelPan && a.Reverb == b.Reverb && a.ChannelVolume == b.ChannelVolume;
}
