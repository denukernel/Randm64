using Sm64DecompLevelViewer.Services.Audio;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Sm64DecompLevelViewer.Services;

namespace Sm64DecompLevelViewer
{
    public partial class MusicEditorWindow : Window
    {
        private readonly string _projectRoot;
        private readonly MidiPlayer _midiPlayer = new();
        private SampleSynthPlayer? _samplePlayer;
        private readonly Dictionary<byte, byte> _previewChannelVolumes = new();
        private readonly Sm64SoundBank _soundBank;
        private readonly Dictionary<string, string> _sequenceBankCache = new();
        private readonly MusicInstrumentResolver _instrumentResolver;
        private readonly M64Service _m64Service = new();
        private List<M64Track> _sequenceTracks = new();
        private M64Track? _currentTrack;
        private string _activeM64Path = "";


        private readonly MusicPlaybackCoordinator _playback;
        private List<ActivePlayingNote> _activePlayingNotesList => _playback.ActiveNotes;
        private ActivePlayingNote? _audition;
        private int _nextAuditionId = int.MaxValue;
        private readonly System.Windows.Threading.DispatcherTimer _auditionTimer = new();
        private readonly MusicVoiceCoordinator _voices;
        private Dictionary<int, int> _midiOwners => _voices.MidiOwners;

        private void StopAudition()
        {
            _auditionTimer.Stop();
            if (_audition == null) return;
            var voice = _audition;
            _audition = null;
            _voices.ReleaseVoice(voice);
        }

        private void StartAudition(byte pitch, bool timed = false)
        {
            StopAudition();
            byte ch = _currentTrack?.ChannelIndex ?? 0;
            byte patch = _currentTrack?.Instrument ?? 0;
            double tuning = 0;
            string? path = UseSm64InstrumentsCheckBox?.IsChecked != true ? null : GetSamplePathForInstrument(_activeM64Path, patch, pitch, out tuning);
            var voice = new ActivePlayingNote { Channel = ch, Pitch = pitch, UsesSamples = !string.IsNullOrEmpty(path), VoiceId = _nextAuditionId-- };
            _audition = voice;
            if (voice.UsesSamples)
                _samplePlayer?.PlayNote(ch, pitch, path!, 100, _currentTrack?.Volume ?? 127, 64, tuning,
                    _instrumentResolver.ResolveInstrument(_activeM64Path, patch, pitch), voice.VoiceId, timed ? 0.15 : null);
            else
            {
                _midiPlayer.ProgramChange(ch, _instrumentResolver.MapSm64InstrumentToGm(_activeM64Path, ch, patch));
                _midiPlayer.ControlChange(ch, 7, GetPreviewChannelVolume(ch, _currentTrack?.Volume ?? 127));
                _voices.AcquireMidi(ch, pitch, 100);
            }
            if (timed) { _auditionTimer.Interval = TimeSpan.FromMilliseconds(150); _auditionTimer.Start(); }
        }

        private bool IsChannelAudible(byte channel)
        {
            int mode = PlaybackModeComboBox?.SelectedIndex ?? 0;
            return mode == 1 ? channel == _currentTrack?.ChannelIndex : mode != 2 || _audibleSelectedChannels.Contains(channel);
        }

        private Dictionary<M64Note, int> _previewVoiceIds => _voices.VoiceIds;

        private readonly System.Windows.Threading.DispatcherTimer _playbackTimer = new();
        private double _playheadTick = 0;
        private readonly System.Diagnostics.Stopwatch _playbackClock = new();
        private readonly double _ticksPerBeat = 48; // Quarter note division
        private bool _isPlaying = false;
        private bool _isUpdatingUi = false;
        private bool _isNotesModified = false;
        private bool _isNoteEditingDisabled = false;

        // UI Grid Settings
        private const double KEY_HEIGHT = 22;
        private const double BEAT_WIDTH = 48;
        private const int MAX_NOTE_PITCH = 127;
        private const int MIN_NOTE_PITCH = 0;
        private const int KEY_COUNT = MAX_NOTE_PITCH - MIN_NOTE_PITCH + 1;
        private const double GRID_HEIGHT = KEY_COUNT * KEY_HEIGHT;

        // Interaction state
        private Rectangle? _selectedNoteRect;
        private M64Note? _selectedNote;
        private bool _isDraggingNote = false;
        private bool _isResizingNote = false;
        private Point _dragStartPoint;
        private double _dragStartLeft;
        private double _dragStartWidth;
        private HashSet<int> _activeMidiNotes => _voices.ActiveMidiNotes;

        // Selection & Effects state
        private readonly MusicEditingState _editing = new();
        private HashSet<M64Note> _selectedNotes => _editing.SelectedNotes;
        private Dictionary<M64Note, Point> _draggedNotesStartCoords = new();
        private bool _isBoxSelecting = false;
        private Point _boxSelectStartPoint;
        private bool _isDraggingPlayhead = false;
        private string _lastSavedDetails = "";
        private static readonly List<M64Note> _copiedNotes = new();
        private static int _copiedNotesMinTick = 0;
        private Stack<(List<M64Track> Tracks, byte Channel, Dictionary<byte, byte> Volumes)> _undoStack => _editing.UndoStack;
        private int _maxSongTick = 0;
        private int _loopStartTick = 0;
        private HashSet<byte> _audibleSelectedChannels = new() { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 };

        private readonly string? _initialM64Path;

        public MusicEditorWindow(string projectRoot, string? initialSequencePath = null)
        {
            InitializeComponent();
            _projectRoot = projectRoot;
            _initialM64Path = initialSequencePath;
            _soundBank = new Sm64SoundBank(projectRoot);
            _voices = new MusicVoiceCoordinator(_midiPlayer, () => _samplePlayer);
            _instrumentResolver = new MusicInstrumentResolver(_projectRoot, _soundBank, _sequenceBankCache);
            _playback = new MusicPlaybackCoordinator(_midiPlayer, _voices, _instrumentResolver, () => _samplePlayer);

            _playbackTimer.Interval = TimeSpan.FromMilliseconds(5);
            _playbackTimer.Tick += PlaybackTimer_Tick;

            _auditionTimer.Tick += (_, _) => StopAudition();
            UseSm64InstrumentsCheckBox.Checked += (_, _) => StopAudition();
            UseSm64InstrumentsCheckBox.Unchecked += (_, _) => StopAudition();
            Deactivated += (_, _) => {
                StopAudition();
                if (Mouse.Captured is UIElement captured && IsAncestorOf(captured)) captured.ReleaseMouseCapture();
            };
            PianoRollCanvas.LostMouseCapture += (_, _) => { if (_isDraggingNote || _isResizingNote || _isBoxSelecting || _isDraggingPlayhead) { StopAudition(); _isDraggingNote = false; _isResizingNote = false; _isBoxSelecting = false; _isDraggingPlayhead = false; SelectionBox.Visibility = Visibility.Collapsed; } };
            this.Loaded += MusicEditorWindow_Loaded;
            this.Closed += MusicEditorWindow_Closed;
        }

        private void MusicEditorWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _samplePlayer = new SampleSynthPlayer("6c6c45b1-9c10-4cec-bab0-0f4595828f14");
            SetupPianoKeyboard();
            SetupPianoGridBackground();
            SetupInstrumentSelector();
            ScanSoundSequences();
        }

        private void SetupInstrumentSelector()
        {
            if (InstrumentSelector == null) return;
            InstrumentSelector.Items.Clear();
            for (int i = 0; i < 128; i++)
            {
                string name = _instrumentResolver.GetInstrumentName(_activeM64Path, (byte)i);
                InstrumentSelector.Items.Add(new ComboBoxItem { Content = name, Tag = (byte)i });
            }
        }

        private void OnSequenceLoaded()
        {
            StopPlayback();
            _undoStack.Clear();
            _previewChannelVolumes.Clear();
            _samplePlayer?.ClearChannelVolumeOverrides();

            _maxSongTick = 0;
            if (_sequenceTracks != null)
            {
                foreach (var track in _sequenceTracks)
                {
                    foreach (var note in track.Notes)
                    {
                        int end = note.StartTick + note.DurationTicks;
                        if (end > _maxSongTick) _maxSongTick = end;
                    }
                }
            }
            if (_maxSongTick <= 0) _maxSongTick = 480;

            _isNoteEditingDisabled = false;
            if (WarningBanner != null) WarningBanner.Visibility = Visibility.Collapsed;

            bool hasCrashWarnings = false;
            var crashReasons = new List<string>();

            if (_m64Service.LoadWarnings.Any(w => w.Contains("infinite") || w.Contains("loop")))
            {
                hasCrashWarnings = true;
                crashReasons.Add("Infinite loop/recursion in sequence scripts");
            }

            if (hasCrashWarnings)
            {
                _isNoteEditingDisabled = true;
                if (WarningBanner != null)
                {
                    WarningBannerText.Text = $"This M64 sequence contains structural anomalies ({string.Join(", ", crashReasons)}) that WILL FREEZE/CRASH the SM64 audio driver. Note editing has been disabled.";
                    WarningBanner.Visibility = Visibility.Visible;
                }
            }
            else
            {
                // Minor non-blocking channel warnings in the status bar
                if (_sequenceTracks.Count > 12)
                {
                    StatusBarText.Text = $"Warning: Sequence has {_sequenceTracks.Count} active channels. Playback may clip/starve notes.";
                }
                else
                {
                    StatusBarText.Text = "Ready";
                }
            }

            // Enable or disable edit buttons based on read-only state
            if (ImportMidiButton != null) ImportMidiButton.IsEnabled = !_isNoteEditingDisabled;
            if (ReverseButton != null) ReverseButton.IsEnabled = !_isNoteEditingDisabled;
            if (TransposeUpButton != null) TransposeUpButton.IsEnabled = !_isNoteEditingDisabled;
            if (TransposeDownButton != null) TransposeDownButton.IsEnabled = !_isNoteEditingDisabled;
            if (OctaveUpButton != null) OctaveUpButton.IsEnabled = !_isNoteEditingDisabled;
            if (OctaveDownButton != null) OctaveDownButton.IsEnabled = !_isNoteEditingDisabled;
            if (DoubleSpeedButton != null) DoubleSpeedButton.IsEnabled = !_isNoteEditingDisabled;
            if (HalfSpeedButton != null) HalfSpeedButton.IsEnabled = !_isNoteEditingDisabled;
            
            _loopStartTick = _m64Service.LoopStartTick;
        }

        private readonly List<SequenceSelectItem> _scannedSequences = new();

        private bool RefreshSequenceList()
        {
            _scannedSequences.Clear();
            string sequenceDir = System.IO.Path.Combine(_projectRoot, "sound", "sequences");
            if (!Directory.Exists(sequenceDir))
            {
                StatusBarText.Text = $"Sequence folder not found: {sequenceDir}";
                return false;
            }
            try
            {
                foreach (string file in Directory.GetFiles(sequenceDir, "*.m64", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                {
                    string parent = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(file) ?? "");
                    _scannedSequences.Add(new SequenceSelectItem { DisplayName = $"{System.IO.Path.GetFileName(file)} ({parent})", FilePath = file });
                }
                if (_scannedSequences.Count == 0) StatusBarText.Text = $"No .m64 sequences found in {sequenceDir}";
                return true;
            }
            catch (Exception ex)
            {
                StatusBarText.Text = $"Could not scan {sequenceDir}: {ex.Message}";
                return false;
            }
        }

        private void ScanSoundSequences()
        {
            if (!RefreshSequenceList()) return;

            try
            {
                if (_scannedSequences.Count > 0)
                {
                    var settingsService = new Sm64DecompLevelViewer.Services.SettingsService();
                    var settings = settingsService.LoadSettings();
                    
                    SequenceSelectItem defaultSeq = _scannedSequences.FirstOrDefault(s => string.Equals(s.FilePath, _initialM64Path, StringComparison.OrdinalIgnoreCase));
                    if (defaultSeq == null && !string.IsNullOrEmpty(settings.LastSelectedM64Path))
                    {
                        defaultSeq = _scannedSequences.FirstOrDefault(s => s.FilePath == settings.LastSelectedM64Path);
                    }
                    
                    if (defaultSeq == null)
                    {
                        defaultSeq = _scannedSequences.FirstOrDefault(s => s.DisplayName.Contains("22_cutscene_lakitu")) ?? _scannedSequences[0];
                    }
                    
                    _sequenceBankCache.Clear();
                    _activeM64Path = defaultSeq.FilePath;
                    if (SequenceSelectButton != null)
                    {
                        SequenceSelectButton.Content = defaultSeq.DisplayName;
                    }
                    
                    _sequenceTracks = _m64Service.LoadM64(_activeM64Path);
                    _isNotesModified = false;
                    OnSequenceLoaded();
                    SetupInstrumentSelector();

                    PopulateChannelSelector();

                    LoadSelectedChannel();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error scanning sound sequences: {ex.Message}");
            }
        }

        private void MusicEditorWindow_Closed(object? sender, EventArgs e)
        {
            StopPlayback();
            _midiPlayer.Dispose();
            _samplePlayer?.Dispose();
        }

        private void SequenceSelectButton_Click(object sender, RoutedEventArgs e)
        {
            if (!RefreshSequenceList()) return;
            var selectWindow = new SequenceSelectionWindow(_scannedSequences, _activeM64Path) { Owner = this };
            if (selectWindow.ShowDialog() == true && selectWindow.SelectedItem != null)
            {
                var selected = selectWindow.SelectedItem;
                _sequenceBankCache.Clear();
                _activeM64Path = selected.FilePath;
                
                if (SequenceSelectButton != null)
                {
                    SequenceSelectButton.Content = selected.DisplayName;
                }

                // Save selected sequence path to settings
                try
                {
                    var settingsService = new Sm64DecompLevelViewer.Services.SettingsService();
                    var settings = settingsService.LoadSettings();
                    settings.LastSelectedM64Path = _activeM64Path;
                    settingsService.SaveSettings(settings);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error saving last selected M64 path: {ex.Message}");
                }

                _sequenceTracks = _m64Service.LoadM64(_activeM64Path);
                _isNotesModified = false;
                OnSequenceLoaded();
                SetupInstrumentSelector();

                PopulateChannelSelector();

                LoadSelectedChannel();
            }
        }

        private void ChannelSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            StopAudition();
            LoadSelectedChannel();
        }

        private void PlaybackModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PlaybackModeComboBox == null || _isUpdatingUi) return;

            if (PlaybackModeComboBox.SelectedIndex == 2) // Play Selected Channels...
            {
                var dialog = new ChannelSelectionWindow(_audibleSelectedChannels) { Owner = this };
                if (dialog.ShowDialog() == true)
                {
                    _audibleSelectedChannels = dialog.SelectedChannels;
                }
            }
        }

        private void PopulateChannelSelector(byte? selectChannel = null)
        {
            if (ChannelSelector == null) return;

            _isUpdatingUi = true;
            ChannelSelector.Items.Clear();

            var activeChannels = _sequenceTracks.Select(t => t.ChannelIndex).Distinct().ToHashSet();
            for (byte ch = 0; ch < 16; ch++)
            {
                string label = activeChannels.Contains(ch) ? $"{ch} (Active)" : $"{ch}";
                ChannelSelector.Items.Add(new ComboBoxItem { Content = label });
            }

            if (selectChannel.HasValue)
            {
                for (int i = 0; i < ChannelSelector.Items.Count; i++)
                {
                    var item = (ComboBoxItem)ChannelSelector.Items[i];
                    string itemText = item.Content.ToString()!;
                    if (itemText == selectChannel.Value.ToString() || itemText.StartsWith(selectChannel.Value.ToString() + " "))
                    {
                        ChannelSelector.SelectedIndex = i;
                        break;
                    }
                }
            }
            else if (ChannelSelector.Items.Count > 0)
            {
                ChannelSelector.SelectedIndex = 0;
            }

            _isUpdatingUi = false;
        }

        private void DuplicateChannelButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentTrack == null || _currentTrack.Notes.Count == 0)
            {
                MessageBox.Show("The active channel has no notes to duplicate.", "Duplicate Channel", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var activeChannels = _sequenceTracks.Select(t => t.ChannelIndex).Distinct().ToList();
            var dialog = new DuplicateChannelDialog(activeChannels) { Owner = this };
            if (dialog.ShowDialog() == true)
            {
                byte targetChannel = dialog.SelectedChannel;
                if (targetChannel == _currentTrack.ChannelIndex)
                {
                    MessageBox.Show("Cannot duplicate a channel to itself.", "Duplicate Channel", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var targetTrack = _sequenceTracks.FirstOrDefault(t => t.ChannelIndex == targetChannel);
                bool overwrite = false;
                if (targetTrack != null && targetTrack.Notes.Count > 0)
                {
                    var mboxResult = MessageBox.Show(
                        $"Channel {targetChannel} already contains {targetTrack.Notes.Count} notes.\n\n" +
                        "Do you want to OVERWRITE the existing notes (Yes), or MERGE the notes together (No)?",
                        "Duplicate Channel",
                        MessageBoxButton.YesNoCancel,
                        MessageBoxImage.Question);

                    if (mboxResult == MessageBoxResult.Cancel) return;
                    if (mboxResult == MessageBoxResult.Yes) overwrite = true;
                }

                PushUndoState();

                if (targetTrack == null)
                {
                    targetTrack = new M64Track { ChannelIndex = targetChannel, Instrument = _currentTrack.Instrument, Volume = _currentTrack.Volume, Bank = _currentTrack.Bank };
                    _sequenceTracks.Add(targetTrack);
                }
                else if (overwrite)
                {
                    targetTrack.Notes.Clear();
                }

                // Copy all notes
                foreach (var note in _currentTrack.Notes)
                {
                    targetTrack.Notes.Add(new M64Note
                    {
                        StartTick = note.StartTick,
                        DurationTicks = note.DurationTicks,
                        Pitch = note.Pitch,
                        Velocity = note.Velocity,
                        Instrument = note.Instrument,
                        LayerIndex = note.LayerIndex,
                        Gate = note.Gate,
                        CommandType = note.CommandType,
                        ChannelVolume = note.ChannelVolume,
                        ChannelPan = note.ChannelPan,
                        Reverb = note.Reverb
                    });
                }

                // Update UI selector to highlight the target channel
                PopulateChannelSelector(targetChannel);
                MessageBox.Show($"Successfully duplicated channel {_currentTrack.ChannelIndex} to channel {targetChannel}!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void LoadSelectedChannel()
        {
            if (ChannelSelector == null || ChannelSelector.SelectedItem == null) return;

            string chStr = ((ComboBoxItem)ChannelSelector.SelectedItem).Content.ToString()!;
            if (chStr.Contains(" "))
            {
                chStr = chStr.Split(' ')[0];
            }
            byte channelIndex = byte.Parse(chStr);

            _currentTrack = _sequenceTracks.FirstOrDefault(t => t.ChannelIndex == channelIndex);
            if (_currentTrack == null)
            {
                _currentTrack = new M64Track { ChannelIndex = channelIndex };
                _sequenceTracks.Add(_currentTrack);

                PopulateChannelSelector(channelIndex);
                return;
            }

            _isUpdatingUi = true;
            if (InstrumentSelector != null)
            {
                InstrumentSelector.SelectedIndex = -1;
                foreach (ComboBoxItem item in InstrumentSelector.Items)
                {
                    if (item.Tag is byte b && b == _currentTrack.Instrument)
                    {
                        InstrumentSelector.SelectedItem = item;
                        break;
                    }
                }
            }
            if (VolumeSlider != null)
                VolumeSlider.Value = _currentTrack.Volume;
            if (VolumeLabel != null)
                VolumeLabel.Text = _currentTrack.Volume.ToString();
            _isUpdatingUi = false;

            _selectedNotes.Clear();
            RenderNotes();
        }

        private int SnapTicks => int.Parse(((ComboBoxItem)SnapSelector.SelectedItem).Tag.ToString()!);
        private int NewNoteTicks => int.Parse(((ComboBoxItem)NoteLengthSelector.SelectedItem).Tag.ToString()!);
        private void UndoButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isNoteEditingDisabled) Undo();
        }
        private void ShowNotesButton_Click(object sender, RoutedEventArgs e)
        {
            var notes = _currentTrack?.Notes;
            if (notes == null || notes.Count == 0) return;
            double center = (MAX_NOTE_PITCH - (notes.Min(n => n.Pitch) + notes.Max(n => n.Pitch)) / 2.0) * KEY_HEIGHT;
            GridScrollViewer.ScrollToVerticalOffset(Math.Max(0, center - GridScrollViewer.ViewportHeight / 2));
            GridScrollViewer.ScrollToHorizontalOffset(Math.Max(0, notes.Min(n => n.StartTick) * BEAT_WIDTH / _ticksPerBeat - 48));
        }

        private void ResetPreviewVoices()
        {
            StopAudition();
            _midiOwners.Clear();
            _activePlayingNotesList.Clear();
            
            foreach (var noteKey in _activeMidiNotes.ToList())
            {
                byte ch = (byte)(noteKey >> 8);
                byte pitch = (byte)(noteKey & 0xFF);
                _midiPlayer.NoteOff(ch, pitch);
            }
            _activeMidiNotes.Clear();
            _samplePlayer?.StopAll();
            _previewVoiceIds.Clear();

            _playbackClock.Restart();
        }

        private void InstrumentSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingUi || _currentTrack == null || InstrumentSelector == null || InstrumentSelector.SelectedItem == null) return;

            if (InstrumentSelector.SelectedItem is ComboBoxItem selectedItem && selectedItem.Tag is byte instr)
            {
                _currentTrack.Instrument = instr;
                foreach (var note in _currentTrack.Notes)
                {
                    note.Instrument = instr;
                }
            }
        }

        private void ReverseButton_Click(object sender, RoutedEventArgs e)
        {
            var notesToProcess = _selectedNotes.Count > 0 ? _selectedNotes.ToList() : _currentTrack?.Notes;
            if (notesToProcess == null || notesToProcess.Count == 0) return;

            PushUndoState();

            int minTick = notesToProcess.Min(n => n.StartTick);
            int maxTick = notesToProcess.Max(n => n.StartTick + n.DurationTicks);

            foreach (var note in notesToProcess)
            {
                int originalStart = note.StartTick;
                note.StartTick = minTick + (maxTick - (originalStart + note.DurationTicks));
            }

            RenderNotes();
        }

        private void TransposeUpButton_Click(object sender, RoutedEventArgs e)
        {
            var notesToProcess = _selectedNotes.Count > 0 ? _selectedNotes.ToList() : _currentTrack?.Notes;
            if (notesToProcess == null || notesToProcess.Count == 0) return;

            PushUndoState();

            foreach (var note in notesToProcess)
            {
                note.Pitch = (byte)Math.Clamp(note.Pitch + 1, MIN_NOTE_PITCH, MAX_NOTE_PITCH);
            }

            RenderNotes();
        }

        private void TransposeDownButton_Click(object sender, RoutedEventArgs e)
        {
            var notesToProcess = _selectedNotes.Count > 0 ? _selectedNotes.ToList() : _currentTrack?.Notes;
            if (notesToProcess == null || notesToProcess.Count == 0) return;

            PushUndoState();

            foreach (var note in notesToProcess)
            {
                note.Pitch = (byte)Math.Clamp(note.Pitch - 1, MIN_NOTE_PITCH, MAX_NOTE_PITCH);
            }

            RenderNotes();
        }

        private void OctaveUpButton_Click(object sender, RoutedEventArgs e)
        {
            var notesToProcess = _selectedNotes.Count > 0 ? _selectedNotes.ToList() : _currentTrack?.Notes;
            if (notesToProcess == null || notesToProcess.Count == 0) return;

            PushUndoState();

            foreach (var note in notesToProcess)
            {
                note.Pitch = (byte)Math.Clamp(note.Pitch + 12, MIN_NOTE_PITCH, MAX_NOTE_PITCH);
            }

            RenderNotes();
        }

        private void OctaveDownButton_Click(object sender, RoutedEventArgs e)
        {
            var notesToProcess = _selectedNotes.Count > 0 ? _selectedNotes.ToList() : _currentTrack?.Notes;
            if (notesToProcess == null || notesToProcess.Count == 0) return;

            PushUndoState();

            foreach (var note in notesToProcess)
            {
                note.Pitch = (byte)Math.Clamp(note.Pitch - 12, MIN_NOTE_PITCH, MAX_NOTE_PITCH);
            }

            RenderNotes();
        }

        private void DoubleSpeedButton_Click(object sender, RoutedEventArgs e)
        {
            var notesToProcess = _selectedNotes.Count > 0 ? _selectedNotes.ToList() : _currentTrack?.Notes;
            if (notesToProcess == null || notesToProcess.Count == 0) return;

            PushUndoState();

            int pivotTick = notesToProcess.Min(n => n.StartTick);

            foreach (var note in notesToProcess)
            {
                int relStart = note.StartTick - pivotTick;
                note.StartTick = pivotTick + (int)Math.Round(relStart * 0.5);
                note.DurationTicks = Math.Max(12, (int)Math.Round(note.DurationTicks * 0.5));
            }

            RenderNotes();
        }

        private void HalfSpeedButton_Click(object sender, RoutedEventArgs e)
        {
            var notesToProcess = _selectedNotes.Count > 0 ? _selectedNotes.ToList() : _currentTrack?.Notes;
            if (notesToProcess == null || notesToProcess.Count == 0) return;

            PushUndoState();

            int pivotTick = notesToProcess.Min(n => n.StartTick);

            foreach (var note in notesToProcess)
            {
                int relStart = note.StartTick - pivotTick;
                note.StartTick = pivotTick + (int)Math.Round(relStart * 2.0);
                note.DurationTicks = (int)Math.Round(note.DurationTicks * 2.0);
            }

            RenderNotes();
        }

        private void PlayButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isPlaying) return;
            StopAudition();

            // Preview voices have independent identities and do not need N64 layer slots.
            // Enforce the four-layer encoding limit only when saving an M64.

            // Resolve banks and decode samples before starting the playback clock.
            if (UseSm64InstrumentsCheckBox?.IsChecked == true)
                foreach (var track in _sequenceTracks)
                    foreach (var key in track.Notes.Select(n => (n.Instrument, n.Pitch)).Distinct())
                    {
                        var instrument = _instrumentResolver.ResolveInstrument(_activeM64Path, key.Instrument, key.Pitch);
                        if (instrument != null) SampleSynthPlayer.Preload(instrument.SamplePath);
                    }
            ResetPreviewVoices();
            _isPlaying = true;
            _maxSongTick = M64PreviewSchedule.GetTransportEndTick(_sequenceTracks, _m64Service.SequenceEndTick, _m64Service.HasSequenceLoop);
            if (_playheadTick >= _maxSongTick) _playheadTick = 0;
            PrimePreviewAtPlayhead();
            _playbackClock.Restart();
            PlayPauseButton.Content = "⏸ Pause";

            TimelineCursor.Visibility = Visibility.Visible;
            _playbackTimer.Start();
        }

        private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isPlaying)
            {
                PlayButton_Click(sender, e);
                return;
            }

            _isPlaying = false;
            _playbackTimer.Stop();
            ResetPreviewVoices();
            _playbackClock.Stop();
            PlayPauseButton.Content = "▶ Play";
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Space || Keyboard.Modifiers != ModifierKeys.None ||
                Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase ||
                Keyboard.FocusedElement is PasswordBox)
                return;

            e.Handled = true;
            if (!e.IsRepeat) PlayPauseButton_Click(sender, e);
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            StopPlayback();
        }

        private void StopPlayback()
        {
            StopAudition();
            _midiOwners.Clear();
            _isPlaying = false;
            _playbackTimer.Stop();
            TimelineCursor.Visibility = Visibility.Hidden;
            _playheadTick = 0;
            Canvas.SetLeft(TimelineCursor, 0);
            PlayPauseButton.Content = "▶ Play";
            _playbackClock.Reset();

            // Clear all active MIDI notes
            foreach (var noteKey in _activeMidiNotes.ToList())
            {
                byte ch = (byte)(noteKey >> 8);
                byte pitch = (byte)(noteKey & 0xFF);
                _midiPlayer.NoteOff(ch, pitch);
            }
            _activeMidiNotes.Clear();
            _activePlayingNotesList.Clear();
            _samplePlayer?.StopAll();
            _previewVoiceIds.Clear();
        }

        private void PlaybackTimer_Tick(object? sender, EventArgs e)
        {
            if (!_isPlaying || _sequenceTracks == null || _isDraggingPlayhead) return;

            double dt = _playbackClock.Elapsed.TotalSeconds;
            _playbackClock.Restart();
            // Resume after UI stalls without fast-forwarding a whole phrase.
            dt = Math.Clamp(dt, 0, 0.05);
            _maxSongTick = M64PreviewSchedule.GetTransportEndTick(_sequenceTracks, _m64Service.SequenceEndTick, _m64Service.HasSequenceLoop);
            double nextTick = Math.Min(_maxSongTick, _m64Service.TempoMap.Advance(_playheadTick, dt));

            UpdatePreview(nextTick);
        }

        private void PrimePreviewAtPlayhead()
        {
            // Queue the cursor interval synchronously, even when no time has elapsed.
            // A normal half-open timer interval must still exclude its right boundary.
            _playback.BeginSampleTimeline(_playheadTick, _m64Service.TempoMap);
            UpdatePreview(_playheadTick);
        }

        private void UpdatePreview(double nextTick)
        {
            _maxSongTick = M64PreviewSchedule.GetTransportEndTick(_sequenceTracks, _m64Service.SequenceEndTick, _m64Service.HasSequenceLoop);
            _playback.Advance(_sequenceTracks, _m64Service.TempoMap, _activeM64Path,
                _playheadTick, nextTick, UseSm64InstrumentsCheckBox?.IsChecked != true, IsChannelAudible, GetPreviewChannelVolume, _maxSongTick);
            _playheadTick = nextTick;
            Canvas.SetLeft(TimelineCursor, _playheadTick * (BEAT_WIDTH / _ticksPerBeat));

            // Only loop sequences whose script actually loops. Leave sample voices
            // and reverb running at a one-shot ending so their tails can decay.
            if (_playheadTick >= _maxSongTick)
            {
                if (!_m64Service.HasSequenceLoop)
                {
                    _isPlaying = false;
                    _playbackTimer.Stop();
                    _playbackClock.Stop();
                    foreach (var note in _activePlayingNotesList.Where(n => !n.UsesSamples).ToList())
                        _voices.ReleaseVoice(note);
                    _activePlayingNotesList.Clear();
                    PlayPauseButton.Content = "▶ Play";
                    return;
                }
                _playheadTick = _loopStartTick < _maxSongTick ? _loopStartTick : 0;
                ResetPreviewVoices();
                PrimePreviewAtPlayhead();
                Canvas.SetLeft(TimelineCursor, _playheadTick * (BEAT_WIDTH / _ticksPerBeat));
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_activeM64Path)) return;

            var result = _m64Service.SaveM64(_activeM64Path, _sequenceTracks, _isNotesModified);

            if (!result.Success)
            {
                StatusBarText.Text = "M64 save failed; the existing file was preserved.";
                MessageBox.Show(result.Error ?? "The sequence could not be saved safely.", "M64 Save Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (result.ChannelMoves.Count > 0)
            {
                byte selectedChannel = _currentTrack?.ChannelIndex ?? 0;
                ResetPreviewVoices();
                foreach (var channel in result.ChannelSources)
                    if (_previewChannelVolumes.TryGetValue(channel.Value, out byte volume))
                    {
                        _previewChannelVolumes[channel.Key] = volume;
                        _samplePlayer?.SetChannelVolume(channel.Key, volume);
                    }
                PopulateChannelSelector(selectedChannel);
            }

            _lastSavedDetails = $"M64 SAVED DETAILS:\n\n" +
                                $"File Path: {_activeM64Path}\n" +
                                $"Original Size: {result.OriginalSize} bytes\n" +
                                $"Saved Size: {result.FileSize} bytes\n\n" +
                                $"Saved Channels:\n" +
                                (result.SavedChannels.Count > 0 ? string.Join("\n", result.SavedChannels) : "None (empty sequence)") + "\n\n" +
                                (result.ChannelMoves.Count > 0 ? "automatic harmony channels:\n" + string.Join("\n", result.ChannelMoves) + "\n\n" : "") +
                                $"Warnings / Optimization Hints:\n" +
                                (result.Warnings.Count > 0 ? string.Join("\n", result.Warnings) : "None");

            StatusBarText.Text = $"Saved successfully ({result.FileSize} bytes)"
                + (result.ChannelMoves.Count > 0 ? $"; harmony routed to {result.ChannelMoves.Count} unused channels" : "");
            SavedDetailsButton.Visibility = Visibility.Visible;

            MessageBox.Show($"M64 Music Sequence saved successfully!\nSaved Size: {result.FileSize} bytes (Original: {result.OriginalSize} bytes)\n\nClick 'View Saved Details' at the bottom status bar to view all saved notes and channels.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ShowLastSavedDetails_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_lastSavedDetails))
            {
                MessageBox.Show(_lastSavedDetails, "M64 Saved Details & Diagnostics", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void DisassembleButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_activeM64Path)) return;

            var diagWindow = new M64DisassemblyWindow(_activeM64Path, _projectRoot)
            {
                Owner = this
            };
            diagWindow.ShowDialog();
        }

        private void DiagnoseM64Button_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_activeM64Path)) return;

            var reportLines = _m64Service.DiagnoseM64(_activeM64Path);
            var reportWindow = new M64DiagnosticsWindow(System.IO.Path.GetFileName(_activeM64Path), reportLines)
            {
                Owner = this
            };
            reportWindow.ShowDialog();
        }

        private void HelpButton_Click(object sender, RoutedEventArgs e)
        {
            var helpWindow = new MusicEditorHelpWindow
            {
                Owner = this
            };
            helpWindow.ShowDialog();
        }

        private void PushUndoState()
        {
            _isNotesModified = true;
            if (_currentTrack == null) return;
            _editing.Capture(_sequenceTracks, _currentTrack.ChannelIndex, _previewChannelVolumes);
        }

        private void Undo()
        {
            if (_undoStack.Count == 0 || _currentTrack == null) return;
            
            var previousState = _undoStack.Pop();
            StopPlayback();
            _sequenceTracks = previousState.Tracks;
            _previewChannelVolumes.Clear();
            _samplePlayer?.ClearChannelVolumeOverrides();
            foreach (var volume in previousState.Volumes)
            {
                _previewChannelVolumes[volume.Key] = volume.Value;
                _samplePlayer?.SetChannelVolume(volume.Key, volume.Value);
            }
            _isNotesModified = true;
            PopulateChannelSelector(previousState.Channel);
            LoadSelectedChannel();
        }

        private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUpdatingUi || _currentTrack == null) return;
            byte vol = (byte)Math.Clamp(VolumeSlider.Value, 0.0, 255.0);
            _currentTrack.Volume = vol;
            byte channel = _currentTrack.ChannelIndex;
            _previewChannelVolumes[channel] = vol;
            _samplePlayer?.SetChannelVolume(channel, vol);
            _midiPlayer.ControlChange(channel, 7, vol);
            if (VolumeLabel != null)
            {
                VolumeLabel.Text = vol.ToString();
            }
        }

        private byte GetPreviewChannelVolume(byte channel, byte originalVolume) =>
            _previewChannelVolumes.TryGetValue(channel, out byte editedVolume) ? editedVolume : originalVolume;

        // Keep scroll positions aligned between keyboard sidebar and note grid

        private void ImportMidiButton_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new Microsoft.Win32.OpenFileDialog()
            {
                Filter = "MIDI Files (*.mid)|*.mid",
                Title = "Select MIDI File to Import"
            };

            if (ofd.ShowDialog() == true)
            {
                try
                {
                    var importedTracks = _m64Service.ImportMidi(ofd.FileName);
                    if (importedTracks == null || importedTracks.Count == 0)
                    {
                        MessageBox.Show("Failed to parse MIDI file or no notes found.", "MIDI Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    PushUndoState();

                    // Replace sequence tracks
                    _sequenceTracks = importedTracks;

                    // Update UI selectors
                    PopulateChannelSelector();

                    LoadSelectedChannel();
                    MessageBox.Show($"Successfully imported {importedTracks.Count} MIDI tracks/channels into the sequencer!", "MIDI Import Successful", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error importing MIDI file: {ex.Message}", "Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void SoundEditorButton_Click(object sender, RoutedEventArgs e)
        {
            var soundEditor = new SoundEditorWindow(_projectRoot)
            {
                Owner = this
            };
            soundEditor.ShowDialog();
        }



        private string GetSamplePathForInstrument(string path, byte instrument, byte pitch, out double tuning) =>
            _instrumentResolver.GetSamplePathForInstrument(path, instrument, pitch, out tuning);
    }

}
