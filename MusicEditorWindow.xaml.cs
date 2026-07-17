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
        private readonly M64Service _m64Service = new();
        private List<M64Track> _sequenceTracks = new();
        private M64Track? _currentTrack;
        private string _activeM64Path = "";

        private class ActivePlayingNote
        {
            public byte Channel { get; set; }
            public byte Pitch { get; set; }
            public double EndTick { get; set; }
        }
        private readonly List<ActivePlayingNote> _activePlayingNotesList = new();
        private readonly Dictionary<M64Track, int> _trackPlayIndices = new();
        
        private readonly System.Windows.Threading.DispatcherTimer _playbackTimer = new();
        private double _playheadTick = 0;
        private DateTime _lastTickTime;
        private double _tempoBpm = 150;
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
        private readonly HashSet<int> _activeMidiNotes = new();

        // Selection & Effects state
        private readonly HashSet<M64Note> _selectedNotes = new();
        private Dictionary<M64Note, Point> _draggedNotesStartCoords = new();
        private bool _isBoxSelecting = false;
        private Point _boxSelectStartPoint;
        private bool _isDraggingPlayhead = false;
        private string _lastSavedDetails = "";
        private static readonly List<M64Note> _copiedNotes = new();
        private static int _copiedNotesMinTick = 0;
        private readonly Stack<List<M64Note>> _undoStack = new();
        private int _maxSongTick = 0;
        private int _loopStartTick = 0;
        private HashSet<byte> _audibleSelectedChannels = new() { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 };

        public MusicEditorWindow(string projectRoot)
        {
            InitializeComponent();
            _projectRoot = projectRoot;

            _playbackTimer.Interval = TimeSpan.FromMilliseconds(20);
            _playbackTimer.Tick += PlaybackTimer_Tick;

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
                string name = GetInstrumentName(_activeM64Path, (byte)i);
                InstrumentSelector.Items.Add(new ComboBoxItem { Content = name, Tag = (byte)i });
            }
        }

        private string GetInstrumentName(string m64Path, byte sm64Inst)
        {
            if (string.IsNullOrEmpty(m64Path)) return sm64Inst.ToString();
            
            string fileName = System.IO.Path.GetFileName(m64Path).ToLower();
            
            if (fileName.Contains("bob_omb") || fileName.Contains("field") || fileName.Contains("grass") || fileName.Contains("lakitu") || fileName.Contains("victory"))
            {
                switch (sm64Inst)
                {
                    case 0: return $"{sm64Inst} (Trumpet / Brass)";
                    case 1: return $"{sm64Inst} (Slap Bass)";
                    case 2: return $"{sm64Inst} (Steel Drums)";
                    case 3: return $"{sm64Inst} (Nylon Guitar)";
                    case 4: return $"{sm64Inst} (Flute / Camera Buzz)";
                    case 5: return $"{sm64Inst} (Marimba / Camera Shutter)";
                }
            }
            else if (fileName.Contains("slider") || fileName.Contains("slide") || fileName.Contains("snow") || fileName.Contains("mountain"))
            {
                switch (sm64Inst)
                {
                    case 0: return $"{sm64Inst} (Marimba / Xylophone)";
                    case 1: return $"{sm64Inst} (Acoustic Guitar)";
                    case 2: return $"{sm64Inst} (Slap Bass)";
                    case 3: return $"{sm64Inst} (Trumpet / Brass)";
                    case 4: return $"{sm64Inst} (Flute / Whistle)";
                    case 5: return $"{sm64Inst} (Steel Drums)";
                }
            }
            else if (fileName.Contains("castle") || fileName.Contains("peach") || fileName.Contains("inside"))
            {
                switch (sm64Inst)
                {
                    case 0:
                    case 1:
                    case 2: return $"{sm64Inst} (Strings Ensemble)";
                    case 3: return $"{sm64Inst} (Pizzicato Strings)";
                    case 4: return $"{sm64Inst} (Trombone)";
                    case 5:
                    case 6: return $"{sm64Inst} (Rhodes / E-Piano)";
                }
            }
            else if (fileName.Contains("water") || fileName.Contains("ocean") || fileName.Contains("docks") || fileName.Contains("bay"))
            {
                switch (sm64Inst)
                {
                    case 0:
                    case 1: return $"{sm64Inst} (Strings Ensemble)";
                    case 2:
                    case 6: return $"{sm64Inst} (Synth Bass)";
                    case 14:
                    case 15: return $"{sm64Inst} (Rhodes / E-Piano)";
                }
            }
            else if (fileName.Contains("boss") || fileName.Contains("bowser_level") || fileName.Contains("road") || fileName.Contains("koopa"))
            {
                switch (sm64Inst)
                {
                    case 0: return $"{sm64Inst} (Distorted Guitar)";
                    case 1:
                    case 2: return $"{sm64Inst} (Synth Bass)";
                    case 3: return $"{sm64Inst} (Orchestra Hit)";
                    case 4: return $"{sm64Inst} (Alto Flute)";
                    case 6: return $"{sm64Inst} (Strings Ensemble)";
                }
            }
            else if (fileName.Contains("bowser_battle") || fileName.Contains("organ") || fileName.Contains("final"))
            {
                switch (sm64Inst)
                {
                    case 0:
                    case 1:
                    case 2:
                    case 3:
                    case 4: return $"{sm64Inst} (Pipe Organ)";
                    case 5: return $"{sm64Inst} (Choir Aahs)";
                }
            }
            
            return sm64Inst.ToString();
        }

        private Brush GetNoteBrush(byte instrument, bool isSelected)
        {
            if (isSelected)
            {
                return new LinearGradientBrush(
                    Color.FromRgb(255, 165, 0),
                    Color.FromRgb(255, 99, 71),
                    new Point(0, 0), new Point(1, 1));
            }

            Color startColor;
            Color endColor;
            
            int colorIndex = instrument % 8;
            switch (colorIndex)
            {
                case 0:
                    startColor = Color.FromRgb(0, 191, 255);
                    endColor = Color.FromRgb(0, 102, 204);
                    break;
                case 1:
                    startColor = Color.FromRgb(50, 205, 50);
                    endColor = Color.FromRgb(34, 139, 34);
                    break;
                case 2:
                    startColor = Color.FromRgb(255, 20, 147);
                    endColor = Color.FromRgb(199, 21, 133);
                    break;
                case 3:
                    startColor = Color.FromRgb(255, 215, 0);
                    endColor = Color.FromRgb(218, 165, 32);
                    break;
                case 4:
                    startColor = Color.FromRgb(153, 50, 204);
                    endColor = Color.FromRgb(138, 43, 226);
                    break;
                case 5:
                    startColor = Color.FromRgb(255, 69, 0);
                    endColor = Color.FromRgb(205, 92, 92);
                    break;
                case 6:
                    startColor = Color.FromRgb(0, 255, 255);
                    endColor = Color.FromRgb(0, 139, 139);
                    break;
                default:
                    startColor = Color.FromRgb(255, 105, 180);
                    endColor = Color.FromRgb(186, 85, 211);
                    break;
            }

            return new LinearGradientBrush(startColor, endColor, new Point(0, 0), new Point(0, 1));
        }

        private void OnSequenceLoaded()
        {
            StopPlayback();

            _tempoBpm = _m64Service.Tempo * 0.5;
            string fileName = System.IO.Path.GetFileName(_activeM64Path).ToLower();
            if (_samplePlayer != null)
            {
                _samplePlayer.EnableReleaseFade = !fileName.Contains("1e");
            }

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
                crashReasons.Add("infinite loop/recursion in sequence scripts");
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

        private void ScanSoundSequences()
        {
            _scannedSequences.Clear();
            string soundDir = System.IO.Path.Combine(_projectRoot, "sound");

            if (!Directory.Exists(soundDir))
            {
                return;
            }

            try
            {
                var files = Directory.GetFiles(soundDir, "*.m64", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    string relativePath = file.Substring(soundDir.Length).TrimStart('\\', '/');
                    string fileName = System.IO.Path.GetFileName(file);
                    string parentFolder = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(file) ?? "");

                    _scannedSequences.Add(new SequenceSelectItem
                    {
                        DisplayName = $"{fileName} ({parentFolder})",
                        FilePath = file
                    });
                }

                if (_scannedSequences.Count > 0)
                {
                    var settingsService = new Sm64DecompLevelViewer.Services.SettingsService();
                    var settings = settingsService.LoadSettings();
                    
                    SequenceSelectItem defaultSeq = null;
                    if (!string.IsNullOrEmpty(settings.LastSelectedM64Path))
                    {
                        defaultSeq = _scannedSequences.FirstOrDefault(s => s.FilePath == settings.LastSelectedM64Path);
                    }
                    
                    if (defaultSeq == null)
                    {
                        defaultSeq = _scannedSequences.FirstOrDefault(s => s.DisplayName.Contains("22_cutscene_lakitu")) ?? _scannedSequences[0];
                    }
                    
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

        private byte MapSm64InstrumentToGm(string m64Path, byte channel, byte sm64Inst)
        {
            string fileName = System.IO.Path.GetFileName(m64Path).ToLower();
            
            // Grass theme / Bob-omb Battlefield (Bank 22 / 1B)
            if (fileName.Contains("bob_omb") || fileName.Contains("field") || fileName.Contains("grass") || fileName.Contains("lakitu") || fileName.Contains("victory"))
            {
                switch (sm64Inst)
                {
                    case 0: return 56;  // Trumpet / Brass
                    case 1: return 36;  // Slap Bass 1
                    case 2: return 114; // Steel Drums
                    case 3: return 24;  // Nylon Guitar
                    case 4: return 73;  // Flute (camera buzz)
                    case 5: return 12;  // Marimba (camera shutter)
                    default: return 114; // Steel Drums / Marimba
                }
            }
            
            // Slider / Snow theme / Cool Cool Mountain / Slide (Bank 0D / 0B)
            if (fileName.Contains("slider") || fileName.Contains("slide") || fileName.Contains("snow") || fileName.Contains("mountain"))
            {
                switch (sm64Inst)
                {
                    case 0: return 12;  // Marimba / Xylophone
                    case 1: return 24;  // Acoustic Guitar
                    case 2: return 36;  // Slap Bass 1
                    case 3: return 61;  // Brass Section / Trumpet
                    case 4: return 73;  // Flute / Whistle
                    case 5: return 114; // Steel Drums
                    default: return 12;
                }
            }

            // Castle Inside / Peach Message (Bank 0E / 1E)
            if (fileName.Contains("castle") || fileName.Contains("peach") || fileName.Contains("inside"))
            {
                switch (sm64Inst)
                {
                    case 0:
                    case 1:
                    case 2: return 48;  // String Ensemble 1
                    case 3: return 45;  // Pizzicato Strings
                    case 4: return 57;  // Trombone / Synth Brass
                    case 5:
                    case 6: return 4;   // Rhodes / Electric Piano
                    default: return 48;
                }
            }

            // Water theme / Dire Dire Docks / Jolly Roger Bay (Bank 13)
            if (fileName.Contains("water") || fileName.Contains("ocean") || fileName.Contains("docks") || fileName.Contains("bay"))
            {
                switch (sm64Inst)
                {
                    case 0:
                    case 1: return 48;  // String Ensemble 1
                    case 2:
                    case 6: return 38;  // Sine Bass / Synth Bass
                    case 14:
                    case 15: return 4;   // Crystal Rhodes / Electric Piano
                    default: return 4;
                }
            }

            // Boss theme / Bowser Levels / Koopa Road (Bank 12 / 19 / 1B)
            if (fileName.Contains("boss") || fileName.Contains("bowser_level") || fileName.Contains("road") || fileName.Contains("koopa"))
            {
                switch (sm64Inst)
                {
                    case 0: return 30;  // Distortion Guitar
                    case 1:
                    case 2: return 38;  // Synth Bass
                    case 3: return 55;  // Orchestra Hit
                    case 4: return 73;  // Alto Flute
                    case 6: return 48;  // String Ensemble 1
                    default: return 30;
                }
            }

            // Bowser Battle / Organ theme (Bank 1D_bowser_organ)
            if (fileName.Contains("bowser_battle") || fileName.Contains("organ") || fileName.Contains("final"))
            {
                switch (sm64Inst)
                {
                    case 0:
                    case 1:
                    case 2:
                    case 3:
                    case 4: return 19;  // Pipe Organ
                    case 5: return 52;  // Choir Aahs
                    default: return 19;
                }
            }

            // Piranha Plant Lullaby (Bank 14_piranha_music_box)
            if (fileName.Contains("piranha") || fileName.Contains("lullaby"))
            {
                switch (sm64Inst)
                {
                    case 0: return 10;  // Music Box
                    case 1: return 8;   // Celesta
                    default: return 10;
                }
            }

            // Hazy Maze Cave / Underground (Bank 15)
            if (fileName.Contains("cave") || fileName.Contains("hazy") || fileName.Contains("underground"))
            {
                switch (sm64Inst)
                {
                    case 0: return 32;  // Acoustic Bass
                    case 1: return 11;  // Vibraphone
                    case 2: return 4;   // Electric Piano
                    default: return 11;
                }
            }

            // Merry-Go-Round / Spooky Carousel (Bank 21)
            if (fileName.Contains("carousel") || fileName.Contains("merry") || fileName.Contains("spooky"))
            {
                switch (sm64Inst)
                {
                    case 0: return 19;  // Pipe Organ
                    case 1: return 8;   // Celesta
                    case 2: return 24;  // Nylon Guitar
                    default: return 19;
                }
            }

            // Fallback GM General Map (Standard N64 instrument list mappings)
            switch (sm64Inst)
            {
                case 0: return 12;  // Marimba
                case 1: return 48;  // Strings
                case 2: return 61;  // Brass Section
                case 3: return 73;  // Flute
                case 4: return 36;  // Slap Bass 1
                case 5: return 24;  // Nylon Guitar
                case 6: return 19;  // Pipe Organ
                case 7: return 114; // Steel Drums
                case 8: return 11;  // Vibraphone
                case 9: return 56;  // Trumpet
                case 10: return 68; // Oboe
                case 11: return 33; // Acoustic Bass
                case 12: return 55; // Orchestra Hit
                case 13: return 30; // Distortion Guitar
                case 14: return 80; // Square Lead
                case 15: return 8;  // Celesta
                default: return sm64Inst;
            }
        }

        private void SetupPianoKeyboard()
        {
            PianoKeyboardContainer.Children.Clear();
            PianoKeyboardContainer.Height = GRID_HEIGHT;

            for (int pitch = MAX_NOTE_PITCH; pitch >= MIN_NOTE_PITCH; pitch--)
            {
                bool isBlackKey = IsMidiPitchBlackKey(pitch);

                var keyBorder = new Border
                {
                    Height = KEY_HEIGHT,
                    BorderBrush = Brushes.DimGray,
                    BorderThickness = new Thickness(0, 0, 1, 1),
                    Background = isBlackKey ? Brushes.Black : Brushes.White,
                    Tag = (byte)pitch
                };

                var keyText = new TextBlock
                {
                    Text = GetNoteName(pitch),
                    Foreground = isBlackKey ? Brushes.White : Brushes.Black,
                    FontSize = 9,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0, 0, 5, 0)
                };

                keyBorder.Child = keyText;
                
                // Audio previews
                keyBorder.MouseDown += (s, e) => {
                    byte p = (byte)((Border)s).Tag;
                    byte ch = _currentTrack?.ChannelIndex ?? 0;
                    byte patch = _currentTrack?.Instrument ?? 0;
                    
                    bool forceMidi = UseMidiSynthCheckBox != null && UseMidiSynthCheckBox.IsChecked == true;
                    double tuning = 0.0;
                    string samplePath = forceMidi ? null : GetSamplePathForInstrument(_activeM64Path, patch, p, out tuning);
                    if (!string.IsNullOrEmpty(samplePath))
                    {
                        _samplePlayer?.PlayNote(ch, p, samplePath, 100, _currentTrack?.Volume ?? 127, 64, tuning);
                    }
                    else
                    {
                        patch = MapSm64InstrumentToGm(_activeM64Path, ch, patch);
                        _midiPlayer.ProgramChange(ch, patch);
                        _midiPlayer.NoteOn(ch, p, 100);
                    }
                };
                keyBorder.MouseUp += (s, e) => {
                    byte p = (byte)((Border)s).Tag;
                    byte ch = _currentTrack?.ChannelIndex ?? 0;
                    _midiPlayer.NoteOff(ch, p);
                    _samplePlayer?.StopNote(ch, p);
                };

                PianoKeyboardContainer.Children.Add(keyBorder);
            }
        }

        private void SetupPianoGridBackground()
        {
            PianoRollCanvas.Height = GRID_HEIGHT;
            TimelineCursor.Y2 = GRID_HEIGHT;

            // Draw horizontal row separators
            for (int i = 0; i <= KEY_COUNT; i++)
            {
                var line = new Line
                {
                    X1 = 0,
                    Y1 = i * KEY_HEIGHT,
                    X2 = PianoRollCanvas.Width,
                    Y2 = i * KEY_HEIGHT,
                    Stroke = new SolidColorBrush(Color.FromRgb(45, 45, 48)),
                    StrokeThickness = 1
                };
                PianoRollCanvas.Children.Add(line);
            }

            // Draw vertical beat separators (columns)
            int totalBeats = (int)(PianoRollCanvas.Width / BEAT_WIDTH);
            for (int beat = 0; beat <= totalBeats; beat++)
            {
                bool isMeasureLine = (beat % 4 == 0);
                var line = new Line
                {
                    X1 = beat * BEAT_WIDTH,
                    Y1 = 0,
                    X2 = beat * BEAT_WIDTH,
                    Y2 = GRID_HEIGHT,
                    Stroke = new SolidColorBrush(isMeasureLine ? Color.FromRgb(80, 80, 80) : Color.FromRgb(45, 45, 48)),
                    StrokeThickness = isMeasureLine ? 1.5 : 1
                };
                PianoRollCanvas.Children.Add(line);
            }
        }

        private void SequenceSelectButton_Click(object sender, RoutedEventArgs e)
        {
            var selectWindow = new SequenceSelectionWindow(_scannedSequences, _activeM64Path) { Owner = this };
            if (selectWindow.ShowDialog() == true && selectWindow.SelectedItem != null)
            {
                var selected = selectWindow.SelectedItem;
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
                        Instrument = _currentTrack.Instrument,
                        LayerIndex = note.LayerIndex,
                        Gate = note.Gate,
                        CommandType = note.CommandType
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

        private void RenderNotes()
        {
            if (PianoRollCanvas == null) return;

            // Clear previous notes and grid lines instantly to avoid WPF layout/rendering loops
            PianoRollCanvas.Children.Clear();
            if (SelectionBox != null)
            {
                PianoRollCanvas.Children.Add(SelectionBox);
                SelectionBox.Visibility = Visibility.Collapsed;
            }

            if (_currentTrack == null) return;

            // Dynamically adjust canvas width based on maximum note tick
            double maxTick = 2000;
            if (_sequenceTracks != null && _sequenceTracks.Count > 0)
            {
                var allNotes = _sequenceTracks.SelectMany(t => t.Notes).ToList();
                if (allNotes.Count > 0)
                {
                    // Clamp maxTick to 60000 ticks to avoid rendering OOM/hang on corrupted files
                    double realMax = allNotes.Max(n => n.StartTick + n.DurationTicks);
                    maxTick = Math.Min(60000, realMax);
                }
            }

            double canvasWidth = Math.Max(2000, maxTick * (BEAT_WIDTH / _ticksPerBeat) + 1000);
            PianoRollCanvas.Width = canvasWidth;
            if (TimelineCanvas != null) TimelineCanvas.Width = canvasWidth;

            // Re-draw background grid lines matching current canvas width
            SetupPianoGridBackground();

            foreach (var note in _currentTrack.Notes)
            {
                DrawNoteRect(note);
            }
        }

        private void DrawNoteRect(M64Note note)
        {
            if (note.Pitch > MAX_NOTE_PITCH || note.Pitch < MIN_NOTE_PITCH) return;
            if (note.StartTick > 60000) return; // Ignore corrupted note rendering beyond limit

            double left = note.StartTick * (BEAT_WIDTH / _ticksPerBeat);
            double top = (MAX_NOTE_PITCH - note.Pitch) * KEY_HEIGHT;
            double width = note.DurationTicks * (BEAT_WIDTH / _ticksPerBeat);

            bool isSelected = _selectedNotes.Contains(note);

            var rect = new Rectangle
            {
                Width = Math.Max(width, 5),
                Height = KEY_HEIGHT - 2,
                Fill = GetNoteBrush(note.Instrument, isSelected),
                Stroke = isSelected ? Brushes.White : new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
                StrokeThickness = isSelected ? 2 : 1,
                RadiusX = 2.5,
                RadiusY = 2.5,
                Tag = note
            };

            Canvas.SetLeft(rect, left);
            Canvas.SetTop(rect, top + 1);

            PianoRollCanvas.Children.Add(rect);
        }

        private void RefreshPlaybackIndices()
        {
            _activePlayingNotesList.Clear();
            
            foreach (var noteKey in _activeMidiNotes.ToList())
            {
                byte ch = (byte)(noteKey >> 8);
                byte pitch = (byte)(noteKey & 0xFF);
                _midiPlayer.NoteOff(ch, pitch);
            }
            _activeMidiNotes.Clear();
            _samplePlayer?.StopAll();

            if (_sequenceTracks != null)
            {
                foreach (var track in _sequenceTracks)
                {
                    int idx = 0;
                    while (idx < track.Notes.Count && track.Notes[idx].StartTick < _playheadTick)
                    {
                        idx++;
                    }
                    _trackPlayIndices[track] = idx;
                }
            }
        }

        private void PianoRollCanvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            Point mousePos = e.GetPosition(PianoRollCanvas);

            if (_isNoteEditingDisabled)
            {
                // Only allow playhead dragging, discard all edits
                if (e.ChangedButton == MouseButton.Left && mousePos.Y < 24)
                {
                    _isDraggingPlayhead = true;
                    _playheadTick = mousePos.X / (BEAT_WIDTH / _ticksPerBeat);
                    Canvas.SetLeft(TimelineCursor, mousePos.X);
                    TimelineCursor.Visibility = Visibility.Visible;
                    RefreshPlaybackIndices();
                    PianoRollCanvas.CaptureMouse();
                    e.Handled = true;
                    return;
                }
                else if (e.ChangedButton == MouseButton.Right && !(e.OriginalSource is Rectangle))
                {
                    _isDraggingPlayhead = true;
                    _playheadTick = mousePos.X / (BEAT_WIDTH / _ticksPerBeat);
                    Canvas.SetLeft(TimelineCursor, mousePos.X);
                    TimelineCursor.Visibility = Visibility.Visible;
                    RefreshPlaybackIndices();
                    PianoRollCanvas.CaptureMouse();
                    e.Handled = true;
                    return;
                }
                e.Handled = true;
                return;
            }

            if (e.ChangedButton == MouseButton.Right)
            {
                if (e.OriginalSource is Rectangle rect && rect.Tag is M64Note note)
                {
                    // Right click on note: Delete note
                    PushUndoState();
                    _currentTrack?.Notes.Remove(note);
                    _selectedNotes.Remove(note);
                    PianoRollCanvas.Children.Remove(rect);
                    RenderNotes();
                }
                else
                {
                    // Right click on background: Set and drag playhead
                    _isDraggingPlayhead = true;
                    _playheadTick = mousePos.X / (BEAT_WIDTH / _ticksPerBeat);
                    Canvas.SetLeft(TimelineCursor, mousePos.X);
                    TimelineCursor.Visibility = Visibility.Visible;
                    RefreshPlaybackIndices();
                    PianoRollCanvas.CaptureMouse();
                }
                e.Handled = true;
                return;
            }

            if (e.ChangedButton == MouseButton.Left)
            {
                // Top area click: drag playback cursor (Vegas Pro style)
                if (mousePos.Y < 24)
                {
                    _isDraggingPlayhead = true;
                    _playheadTick = mousePos.X / (BEAT_WIDTH / _ticksPerBeat);
                    Canvas.SetLeft(TimelineCursor, mousePos.X);
                    TimelineCursor.Visibility = Visibility.Visible;
                    RefreshPlaybackIndices();
                    PianoRollCanvas.CaptureMouse();
                    e.Handled = true;
                    return;
                }

                if (e.OriginalSource is Rectangle noteRect && noteRect.Tag is M64Note noteData)
                {
                    // Selection handling
                    if (Keyboard.Modifiers == ModifierKeys.Control)
                    {
                        if (_selectedNotes.Contains(noteData))
                            _selectedNotes.Remove(noteData);
                        else
                            _selectedNotes.Add(noteData);
                    }
                    else
                    {
                        if (!_selectedNotes.Contains(noteData))
                        {
                            _selectedNotes.Clear();
                            _selectedNotes.Add(noteData);
                        }
                    }

                    RenderNotes();

                    // Start drag/resize
                    _selectedNoteRect = noteRect;
                    _selectedNote = noteData;
                    _dragStartPoint = e.GetPosition(PianoRollCanvas);
                    _dragStartLeft = Canvas.GetLeft(noteRect);
                    _dragStartWidth = noteRect.Width;

                    PushUndoState(); // Push state before modification!

                    if (mousePos.X >= _dragStartLeft + _dragStartWidth - 8)
                    {
                        _isResizingNote = true;
                    }
                    else
                    {
                        _isDraggingNote = true;
                        _draggedNotesStartCoords = _selectedNotes.ToDictionary(n => n, n => new Point(n.StartTick, n.Pitch));

                        byte ch = _currentTrack?.ChannelIndex ?? 0;
                        byte patch = _currentTrack?.Instrument ?? 0;
                        bool forceMidi = UseMidiSynthCheckBox != null && UseMidiSynthCheckBox.IsChecked == true;
                        double tuning = 0.0;
                        string samplePath = forceMidi ? null : GetSamplePathForInstrument(_activeM64Path, patch, noteData.Pitch, out tuning);
                        if (!string.IsNullOrEmpty(samplePath))
                        {
                            _samplePlayer?.PlayNote(ch, noteData.Pitch, samplePath, 100, _currentTrack?.Volume ?? 127, 64, tuning);
                        }
                        else
                        {
                            patch = MapSm64InstrumentToGm(_activeM64Path, ch, patch);
                            _midiPlayer.ProgramChange(ch, patch);
                            _midiPlayer.NoteOn(ch, noteData.Pitch, 100);
                        }
                    }

                    PianoRollCanvas.CaptureMouse();
                    e.Handled = true;
                }
                else
                {
                    // Empty grid click: Start Box Selection
                    _isBoxSelecting = true;
                    _boxSelectStartPoint = mousePos;
                    Canvas.SetLeft(SelectionBox, mousePos.X);
                    Canvas.SetTop(SelectionBox, mousePos.Y);
                    SelectionBox.Width = 0;
                    SelectionBox.Height = 0;
                    SelectionBox.Visibility = Visibility.Visible;
                    PianoRollCanvas.CaptureMouse();
                }
                e.Handled = true;
            }
        }

        private void PianoRollCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            Point mousePos = e.GetPosition(PianoRollCanvas);

            if (_isDraggingPlayhead)
            {
                double newX = Math.Max(0, Math.Min(mousePos.X, PianoRollCanvas.Width));
                _playheadTick = newX / (BEAT_WIDTH / _ticksPerBeat);
                Canvas.SetLeft(TimelineCursor, newX);
                TimelineCursor.Visibility = Visibility.Visible;
                RefreshPlaybackIndices();
                return;
            }

            if (_isBoxSelecting)
            {
                double left = Math.Min(_boxSelectStartPoint.X, mousePos.X);
                double top = Math.Min(_boxSelectStartPoint.Y, mousePos.Y);
                double width = Math.Abs(_boxSelectStartPoint.X - mousePos.X);
                double height = Math.Abs(_boxSelectStartPoint.Y - mousePos.Y);

                Canvas.SetLeft(SelectionBox, left);
                Canvas.SetTop(SelectionBox, top);
                SelectionBox.Width = width;
                SelectionBox.Height = height;
                return;
            }

            if (_selectedNoteRect == null || _selectedNote == null) return;

            double deltaX = mousePos.X - _dragStartPoint.X;

            if (_isResizingNote)
            {
                double newWidth = Math.Max(_dragStartWidth + deltaX, BEAT_WIDTH / 4);
                _selectedNoteRect.Width = newWidth;

                double snapWidth = BEAT_WIDTH / 4;
                int snappedDuration = (int)(Math.Round(newWidth / snapWidth) * snapWidth * (_ticksPerBeat / BEAT_WIDTH));
                _selectedNote.DurationTicks = Math.Max(snappedDuration, 12);
            }
            else if (_isDraggingNote)
            {
                double snapWidth = BEAT_WIDTH / 4;
                int deltaTicks = (int)(Math.Round(deltaX / snapWidth) * snapWidth * (_ticksPerBeat / BEAT_WIDTH));

                int curPitch = (int)(MAX_NOTE_PITCH - (int)(mousePos.Y / KEY_HEIGHT));
                int startPitch = (int)(MAX_NOTE_PITCH - (int)(_dragStartPoint.Y / KEY_HEIGHT));
                int deltaPitch = curPitch - startPitch;

                foreach (var kvp in _draggedNotesStartCoords)
                {
                    var note = kvp.Key;
                    var startCoords = kvp.Value;

                    int newTick = (int)(startCoords.X + deltaTicks);
                    int newPitch = (int)(startCoords.Y + deltaPitch);

                    note.StartTick = Math.Max(0, newTick);
                    note.Pitch = (byte)Math.Clamp(newPitch, MIN_NOTE_PITCH, MAX_NOTE_PITCH);
                }

                RenderNotes();
            }
        }

        private void PianoRollCanvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            PianoRollCanvas.ReleaseMouseCapture();

            if (_isDraggingPlayhead)
            {
                _isDraggingPlayhead = false;
                RefreshPlaybackIndices();
                return;
            }

            if (_isBoxSelecting)
            {
                SelectionBox.Visibility = Visibility.Collapsed;
                _isBoxSelecting = false;

                double boxLeft = Canvas.GetLeft(SelectionBox);
                double boxTop = Canvas.GetTop(SelectionBox);
                double width = SelectionBox.Width;
                double height = SelectionBox.Height;

                if (width < 5 && height < 5)
                {
                    if (_selectedNotes.Count > 0 && Keyboard.Modifiers != ModifierKeys.Control)
                    {
                        _selectedNotes.Clear();
                        RenderNotes();
                    }
                    else
                    {
                        // Tiny drag: Create new note
                        double gridSnapWidth = BEAT_WIDTH / 4;
                        int startTick = (int)(Math.Round(_boxSelectStartPoint.X / gridSnapWidth) * gridSnapWidth * (_ticksPerBeat / BEAT_WIDTH));
                        byte pitch = (byte)(MAX_NOTE_PITCH - (int)(_boxSelectStartPoint.Y / KEY_HEIGHT));

                        PushUndoState();

                        var newNote = new M64Note
                        {
                            StartTick = startTick,
                            DurationTicks = (int)(_ticksPerBeat / 2),
                            Pitch = pitch,
                            Velocity = 100,
                            Instrument = _currentTrack?.Instrument ?? 0
                        };

                        _currentTrack?.Notes.Add(newNote);

                        if (Keyboard.Modifiers != ModifierKeys.Control)
                        {
                            _selectedNotes.Clear();
                        }
                        _selectedNotes.Add(newNote);
                        RenderNotes();

                        // Play preview
                        byte ch = _currentTrack?.ChannelIndex ?? 0;
                        byte patch = _currentTrack?.Instrument ?? 0;
                        bool forceMidi = UseMidiSynthCheckBox != null && UseMidiSynthCheckBox.IsChecked == true;
                        double tuning = 0.0;
                        string samplePath = forceMidi ? null : GetSamplePathForInstrument(_activeM64Path, patch, pitch, out tuning);
                        if (!string.IsNullOrEmpty(samplePath))
                        {
                            _samplePlayer?.PlayNote(ch, pitch, samplePath, 100, _currentTrack?.Volume ?? 127, 64, tuning);
                        }
                        else
                        {
                            patch = MapSm64InstrumentToGm(_activeM64Path, ch, patch);
                            _midiPlayer.ProgramChange(ch, patch);
                            _midiPlayer.NoteOn(ch, pitch, 100);
                            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
                            timer.Tick += (s, ev) => {
                                _midiPlayer.NoteOff(ch, pitch);
                                timer.Stop();
                            };
                            timer.Start();
                        }
                    }
                }
                else
                {
                    // Select intersecting notes
                    if (Keyboard.Modifiers != ModifierKeys.Control)
                    {
                        _selectedNotes.Clear();
                    }

                    var boxRect = new Rect(boxLeft, boxTop, width, height);

                    foreach (var child in PianoRollCanvas.Children)
                    {
                        if (child is Rectangle rect && rect.Tag is M64Note note)
                        {
                            double noteLeft = Canvas.GetLeft(rect);
                            double noteTop = Canvas.GetTop(rect);
                            var noteRect = new Rect(noteLeft, noteTop, rect.Width, rect.Height);

                            if (boxRect.IntersectsWith(noteRect))
                            {
                                _selectedNotes.Add(note);
                            }
                        }
                    }
                    RenderNotes();
                }
            }

            if (_selectedNote != null)
            {
                byte ch = _currentTrack?.ChannelIndex ?? 0;
                _midiPlayer.NoteOff(ch, _selectedNote.Pitch);
            }

            if (_isDraggingNote || _isResizingNote)
            {
                _isNotesModified = true;
            }

            _selectedNoteRect = null;
            _selectedNote = null;
            _isDraggingNote = false;
            _isResizingNote = false;
            _draggedNotesStartCoords.Clear();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (_isNoteEditingDisabled)
            {
                if (e.Key == Key.Delete || (Keyboard.Modifiers == ModifierKeys.Control && (e.Key == Key.V || e.Key == Key.Z)))
                {
                    e.Handled = true;
                    return;
                }
            }

            if (e.Key == Key.Delete)
            {
                if (_selectedNotes.Count > 0)
                {
                    PushUndoState();
                    foreach (var note in _selectedNotes)
                    {
                        _currentTrack?.Notes.Remove(note);
                    }
                    _selectedNotes.Clear();
                    RenderNotes();
                }
            }
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.A)
            {
                // Select All
                if (_currentTrack != null)
                {
                    _selectedNotes.Clear();
                    foreach (var note in _currentTrack.Notes)
                    {
                        _selectedNotes.Add(note);
                    }
                    RenderNotes();
                    e.Handled = true;
                }
            }
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.C)
            {
                // Copy
                if (_selectedNotes.Count > 0)
                {
                    _copiedNotes.Clear();
                    foreach (var note in _selectedNotes)
                    {
                        _copiedNotes.Add(note);
                    }
                    _copiedNotesMinTick = _copiedNotes.Min(n => n.StartTick);
                    e.Handled = true;
                }
            }
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.V)
            {
                // Paste
                if (_copiedNotes.Count > 0 && _currentTrack != null)
                {
                    PushUndoState();
                    _selectedNotes.Clear();
                    
                    int snapTicks = 12; // 48 / 4
                    int pasteTickStart = (int)(Math.Round(_playheadTick / snapTicks) * snapTicks);

                    foreach (var note in _copiedNotes)
                    {
                        int offset = note.StartTick - _copiedNotesMinTick;
                        var newNote = new M64Note
                        {
                            StartTick = pasteTickStart + offset,
                            DurationTicks = note.DurationTicks,
                            Pitch = note.Pitch,
                            Velocity = note.Velocity,
                            Instrument = _currentTrack.Instrument,
                            LayerIndex = note.LayerIndex,
                            Gate = note.Gate,
                            CommandType = note.CommandType
                        };
                        _currentTrack.Notes.Add(newNote);
                        _selectedNotes.Add(newNote);
                    }
                    RenderNotes();
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.D)
            {
                // Duplicate selection in-place (either D or Ctrl+D)
                if (_selectedNotes.Count > 0 && _currentTrack != null)
                {
                    PushUndoState();
                    var duplicatedList = new List<M64Note>();
                    foreach (var note in _selectedNotes)
                    {
                        var newNote = new M64Note
                        {
                            StartTick = note.StartTick,
                            DurationTicks = note.DurationTicks,
                            Pitch = note.Pitch,
                            Velocity = note.Velocity,
                            Instrument = note.Instrument,
                            LayerIndex = note.LayerIndex,
                            Gate = note.Gate,
                            CommandType = note.CommandType
                        };
                        _currentTrack.Notes.Add(newNote);
                        duplicatedList.Add(newNote);
                    }
                    _selectedNotes.Clear();
                    foreach (var note in duplicatedList)
                    {
                        _selectedNotes.Add(note);
                    }
                    RenderNotes();
                    e.Handled = true;
                }
            }
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Z)
            {
                // Undo
                Undo();
                e.Handled = true;
            }
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

            _isPlaying = true;
            _playheadTick = 0;
            _lastTickTime = DateTime.UtcNow;
            _tempoBpm = _m64Service.Tempo * 0.5;

            _trackPlayIndices.Clear();
            _activePlayingNotesList.Clear();
            
            _maxSongTick = 0;
            if (_sequenceTracks != null)
            {
                foreach (var track in _sequenceTracks)
                {
                    track.Notes = track.Notes.OrderBy(n => n.StartTick).ToList();
                    _trackPlayIndices[track] = 0;
                    foreach (var note in track.Notes)
                    {
                        int end = note.StartTick + note.DurationTicks;
                        if (end > _maxSongTick) _maxSongTick = end;
                    }
                }
            }
            if (_maxSongTick <= 0) _maxSongTick = 480;

            TimelineCursor.Visibility = Visibility.Visible;
            _playbackTimer.Start();
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            StopPlayback();
        }

        private void StopPlayback()
        {
            _isPlaying = false;
            _playbackTimer.Stop();
            TimelineCursor.Visibility = Visibility.Hidden;
            _playheadTick = 0;

            // Clear all active MIDI notes
            foreach (var noteKey in _activeMidiNotes.ToList())
            {
                byte ch = (byte)(noteKey >> 8);
                byte pitch = (byte)(noteKey & 0xFF);
                _midiPlayer.NoteOff(ch, pitch);
            }
            _activeMidiNotes.Clear();
            _activePlayingNotesList.Clear();
            _trackPlayIndices.Clear();
            _samplePlayer?.StopAll();
        }

        private void PlaybackTimer_Tick(object? sender, EventArgs e)
        {
            if (!_isPlaying || _sequenceTracks == null || _isDraggingPlayhead) return;

            DateTime now = DateTime.UtcNow;
            double dt = (now - _lastTickTime).TotalSeconds;
            _lastTickTime = now;

            // Clamp dt to a reasonable range to prevent jumps if the app freezes/lags
            if (dt < 0.0) dt = 0.02;
            if (dt > 0.3) dt = 0.3;
            double beatsPassed = dt * (_tempoBpm / 60.0);
            double ticksPassed = beatsPassed * _ticksPerBeat;

            double nextTick = _playheadTick + ticksPassed;

            // 1. Process active notes for note-offs
            for (int i = _activePlayingNotesList.Count - 1; i >= 0; i--)
            {
                var note = _activePlayingNotesList[i];
                if (note.EndTick < nextTick)
                {
                    _midiPlayer.NoteOff(note.Channel, note.Pitch);
                    _samplePlayer?.StopNote(note.Channel, note.Pitch);
                    _activeMidiNotes.Remove((note.Channel << 8) | note.Pitch);
                    _activePlayingNotesList.RemoveAt(i);
                }
            }

            // 2. Process note-ons for each track using sorted play index
            foreach (var track in _sequenceTracks)
            {
                if (!_trackPlayIndices.TryGetValue(track, out int idx))
                {
                    idx = 0;
                    _trackPlayIndices[track] = 0;
                }

                byte targetChannel = track.ChannelIndex;

                bool shouldPlay = true;
                int playMode = PlaybackModeComboBox != null ? PlaybackModeComboBox.SelectedIndex : 0;
                if (playMode == 1) // Play Active Channel Only
                {
                    if (ChannelSelector != null && ChannelSelector.SelectedItem != null)
                    {
                        string chStr = ((ComboBoxItem)ChannelSelector.SelectedItem).Content.ToString()!;
                        if (chStr.Contains(" "))
                        {
                            chStr = chStr.Split(' ')[0];
                        }
                        if (byte.TryParse(chStr, out byte activeChannel))
                        {
                            if (targetChannel != activeChannel) shouldPlay = false;
                        }
                    }
                }
                else if (playMode == 2) // Play Selected Channels Only
                {
                    if (!_audibleSelectedChannels.Contains(targetChannel)) shouldPlay = false;
                }

                while (idx < track.Notes.Count)
                {
                    var note = track.Notes[idx];
                    
                    if (note.StartTick >= nextTick) break;

                    if (note.StartTick >= _playheadTick)
                    {
                        if (shouldPlay)
                        {
                            bool forceMidi = UseMidiSynthCheckBox != null && UseMidiSynthCheckBox.IsChecked == true;
                            double tuning = 0.0;
                            string samplePath = forceMidi ? null : GetSamplePathForInstrument(_activeM64Path, note.Instrument, note.Pitch, out tuning);
                            if (!string.IsNullOrEmpty(samplePath))
                            {
                                _samplePlayer?.PlayNote(targetChannel, note.Pitch, samplePath, note.Velocity, note.ChannelVolume, note.ChannelPan, tuning);
                            }
                            else
                            {
                                byte gmPatch = MapSm64InstrumentToGm(_activeM64Path, targetChannel, note.Instrument);
                                _midiPlayer.ProgramChange(targetChannel, gmPatch);
                                _midiPlayer.ControlChange(targetChannel, 7, note.ChannelVolume);
                                _midiPlayer.ControlChange(targetChannel, 10, note.ChannelPan);
                                _midiPlayer.ControlChange(targetChannel, 91, note.Reverb);
                                _midiPlayer.NoteOn(targetChannel, note.Pitch, note.Velocity);
                            }
                            
                            _activeMidiNotes.Add((targetChannel << 8) | note.Pitch);
                            
                            double gateFactor = note.Gate / 256.0;
                            if (gateFactor <= 0.0 || gateFactor > 1.0) gateFactor = 0.8;
                            int playTicks = (int)Math.Max(1, Math.Round(note.DurationTicks * gateFactor));

                            _activePlayingNotesList.Add(new ActivePlayingNote
                            {
                                Channel = targetChannel,
                                Pitch = note.Pitch,
                                EndTick = note.StartTick + playTicks
                            });
                        }
                    }

                    idx++;
                }

                _trackPlayIndices[track] = idx;
            }

            _playheadTick = nextTick;
            Canvas.SetLeft(TimelineCursor, _playheadTick * (BEAT_WIDTH / _ticksPerBeat));

            // Auto loop back at the end of the music
            if (_playheadTick >= _maxSongTick)
            {
                _playheadTick = _loopStartTick;
                RefreshPlaybackIndices();
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_activeM64Path)) return;

            var result = _m64Service.SaveM64(_activeM64Path, _sequenceTracks, _isNotesModified);

            _lastSavedDetails = $"M64 SAVED DETAILS:\n\n" +
                                $"File Path: {_activeM64Path}\n" +
                                $"Original Size: {result.OriginalSize} bytes\n" +
                                $"Saved Size: {result.FileSize} bytes\n\n" +
                                $"Saved Channels:\n" +
                                (result.SavedChannels.Count > 0 ? string.Join("\n", result.SavedChannels) : "None (empty sequence)") + "\n\n" +
                                $"Warnings / Optimization Hints:\n" +
                                (result.Warnings.Count > 0 ? string.Join("\n", result.Warnings) : "None (Perfect compile!)");

            StatusBarText.Text = $"Saved successfully ({result.FileSize} bytes)";
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
            var state = _currentTrack.Notes.Select(n => new M64Note
            {
                StartTick = n.StartTick,
                DurationTicks = n.DurationTicks,
                Pitch = n.Pitch,
                Velocity = n.Velocity,
                Instrument = n.Instrument,
                LayerIndex = n.LayerIndex,
                Gate = n.Gate,
                CommandType = n.CommandType
            }).ToList();
            _undoStack.Push(state);
        }

        private void Undo()
        {
            if (_undoStack.Count == 0 || _currentTrack == null) return;
            
            var previousState = _undoStack.Pop();
            _currentTrack.Notes.Clear();
            foreach (var note in previousState)
            {
                _currentTrack.Notes.Add(note);
            }
            _selectedNotes.Clear();
            RenderNotes();
        }



        private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUpdatingUi || _currentTrack == null) return;
            byte vol = (byte)Math.Clamp(VolumeSlider.Value, 0.0, 127.0);
            _currentTrack.Volume = vol;
            if (VolumeLabel != null)
            {
                VolumeLabel.Text = vol.ToString();
            }
        }

        // Keep scroll positions aligned between keyboard sidebar and note grid
        private void GridScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            PianoScrollViewer.ScrollToVerticalOffset(e.VerticalOffset);
        }

        private void PianoScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            GridScrollViewer.ScrollToVerticalOffset(e.VerticalOffset);
        }

        private bool IsMidiPitchBlackKey(int pitch)
        {
            int note = pitch % 12;
            return note == 1 || note == 3 || note == 6 || note == 8 || note == 10;
        }

        private string GetNoteName(int pitch)
        {
            string[] names = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
            int octave = (pitch / 12) - 1;
            return $"{names[pitch % 12]}{octave}";
        }

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

        private string GetBankIdForSequence(string m64Path)
        {
            string fileName = System.IO.Path.GetFileNameWithoutExtension(m64Path).ToLower();
            string sequencesJsonPath = System.IO.Path.Combine(_projectRoot, "sound", "sequences.json");

            if (System.IO.File.Exists(sequencesJsonPath))
            {
                try
                {
                    string jsonText = System.IO.File.ReadAllText(sequencesJsonPath);
                    using (var doc = System.Text.Json.JsonDocument.Parse(jsonText))
                    {
                        var root = doc.RootElement;
                        foreach (var prop in root.EnumerateObject())
                        {
                            string key = prop.Name.ToLower();
                            if (fileName.Contains(key) || key.Contains(fileName))
                            {
                                var val = prop.Value;
                                if (val.ValueKind == System.Text.Json.JsonValueKind.Array && val.GetArrayLength() > 0)
                                {
                                    return val[0].GetString() ?? "22";
                                }
                                else if (val.ValueKind == System.Text.Json.JsonValueKind.Object)
                                {
                                    if (val.TryGetProperty("banks", out var banksProp) && banksProp.ValueKind == System.Text.Json.JsonValueKind.Array && banksProp.GetArrayLength() > 0)
                                    {
                                        return banksProp[0].GetString() ?? "22";
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error reading sequences.json: {ex.Message}");
                }
            }

            // Expanded fallback heuristics if sequences.json is unavailable
            if (fileName.Contains("grass") || fileName.Contains("bob_omb") || fileName.Contains("field")) return "22";
            if (fileName.Contains("castle") || fileName.Contains("inside")) return "0E";
            if (fileName.Contains("water") || fileName.Contains("ocean") || fileName.Contains("docks")) return "13";
            if (fileName.Contains("slide") || fileName.Contains("slider") || fileName.Contains("snow")) return "0D";
            if (fileName.Contains("boss") || fileName.Contains("road")) return "12";
            if (fileName.Contains("organ") || fileName.Contains("final") || fileName.Contains("battle")) return "1D_bowser_organ";
            if (fileName.Contains("lakitu") || fileName.Contains("cutscene")) return "1B";
            if (fileName.Contains("piranha") || fileName.Contains("lullaby")) return "14_piranha_music_box";
            if (fileName.Contains("stairs")) return "1C_endless_stairs";
            if (fileName.Contains("race")) return "1A";
            if (fileName.Contains("powerup")) return "17";
            if (fileName.Contains("metal")) return "18";
            if (fileName.Contains("credits")) return "25";
            if (fileName.Contains("ending")) return "23";
            if (fileName.Contains("file")) return "24";
            if (fileName.Contains("spooky")) return "10";

            return "22";
        }

        private string ParseSoundProperty(System.Text.Json.JsonElement prop, out double tuning)
        {
            tuning = 0.0;
            if (prop.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                return prop.GetString() ?? "";
            }
            else if (prop.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                string sample = "";
                if (prop.TryGetProperty("sample", out var sampleProp))
                {
                    sample = sampleProp.GetString() ?? "";
                }
                if (prop.TryGetProperty("tuning", out var tuningProp))
                {
                    if (tuningProp.ValueKind == System.Text.Json.JsonValueKind.Number)
                    {
                        tuning = tuningProp.GetDouble();
                    }
                }
                return sample;
            }
            return "";
        }

        private string GetSamplePathForInstrument(string m64Path, byte sm64Inst, byte pitch, out double tuning)
        {
            tuning = 0.0;
            string bankId = GetBankIdForSequence(m64Path);
            string bankPath = System.IO.Path.Combine(_projectRoot, "sound", "sound_banks", bankId + ".json");

            if (!System.IO.File.Exists(bankPath)) return string.Empty;

            try
            {
                string jsonText = System.IO.File.ReadAllText(bankPath);
                using (var doc = System.Text.Json.JsonDocument.Parse(jsonText))
                {
                    var root = doc.RootElement;
                    string sampleBank = "instruments";
                    if (root.TryGetProperty("sample_bank", out var sbProp))
                    {
                        if (sbProp.ValueKind == System.Text.Json.JsonValueKind.String)
                        {
                            sampleBank = sbProp.GetString() ?? "instruments";
                        }
                        else if (sbProp.ValueKind == System.Text.Json.JsonValueKind.Object)
                        {
                            if (sbProp.TryGetProperty("else", out var elseProp))
                            {
                                sampleBank = elseProp.GetString() ?? "instruments";
                            }
                        }
                    }

                    if (sm64Inst == 127) // Percussion/Drums
                    {
                        if (root.TryGetProperty("percussion", out var percProp) && percProp.ValueKind == System.Text.Json.JsonValueKind.Array)
                        {
                            int index = pitch - 21;
                            if (index >= 0 && index < percProp.GetArrayLength())
                            {
                                var percElement = percProp[index];
                                if (percElement.TryGetProperty("sound", out var soundProp))
                                {
                                    string soundSample = ParseSoundProperty(soundProp, out tuning);

                                    if (!string.IsNullOrEmpty(soundSample))
                                    {
                                        string sampleFile = soundSample + ".aiff";
                                        string samplePath = System.IO.Path.Combine(_projectRoot, "sound", "samples", sampleBank, sampleFile);
                                        if (System.IO.File.Exists(samplePath)) return samplePath;

                                        samplePath = System.IO.Path.Combine(_projectRoot, "sound", "samples", "instruments", sampleFile);
                                        if (System.IO.File.Exists(samplePath)) return samplePath;
                                    }
                                }
                            }
                        }
                    }
                    else // Normal instrument
                    {
                        if (root.TryGetProperty("instrument_list", out var listProp) && 
                            root.TryGetProperty("instruments", out var instsProp))
                        {
                            if (sm64Inst < listProp.GetArrayLength())
                            {
                                var instNameElement = listProp[sm64Inst];
                                if (instNameElement.ValueKind == System.Text.Json.JsonValueKind.String)
                                {
                                    string instName = instNameElement.GetString() ?? "";
                                    if (instsProp.TryGetProperty(instName, out var instProp))
                                    {
                                        string soundSample = "";
                                        
                                        int rangeLo = 0;
                                        int rangeHi = 127;
                                        if (instProp.TryGetProperty("normal_range_lo", out var loProp))
                                        {
                                            rangeLo = loProp.GetInt32();
                                        }
                                        if (instProp.TryGetProperty("normal_range_hi", out var hiProp))
                                        {
                                            rangeHi = hiProp.GetInt32();
                                        }

                                        if (pitch < rangeLo && instProp.TryGetProperty("sound_lo", out var soundLoProp))
                                        {
                                            soundSample = ParseSoundProperty(soundLoProp, out tuning);
                                        }
                                        else if (pitch > rangeHi && instProp.TryGetProperty("sound_hi", out var soundHiProp))
                                        {
                                            soundSample = ParseSoundProperty(soundHiProp, out tuning);
                                        }
                                        else if (instProp.TryGetProperty("sound", out var soundProp))
                                        {
                                            soundSample = ParseSoundProperty(soundProp, out tuning);
                                        }
                                        else if (instProp.TryGetProperty("sound_hi", out var fallbackHiProp))
                                        {
                                            soundSample = ParseSoundProperty(fallbackHiProp, out tuning);
                                        }
                                        else if (instProp.TryGetProperty("sound_lo", out var fallbackLoProp))
                                        {
                                            soundSample = ParseSoundProperty(fallbackLoProp, out tuning);
                                        }

                                        if (!string.IsNullOrEmpty(soundSample))
                                        {
                                            string sampleFile = soundSample + ".aiff";
                                            string samplePath = System.IO.Path.Combine(_projectRoot, "sound", "samples", sampleBank, sampleFile);
                                            if (System.IO.File.Exists(samplePath)) return samplePath;

                                            samplePath = System.IO.Path.Combine(_projectRoot, "sound", "samples", "instruments", sampleFile);
                                            if (System.IO.File.Exists(samplePath)) return samplePath;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing soundbank JSON: {ex.Message}");
            }

            return string.Empty;
        }
    }



    public class ChannelSelectionWindow : Window
    {
        public HashSet<byte> SelectedChannels { get; } = new();

        public ChannelSelectionWindow(HashSet<byte> initiallySelected)
        {
            Title = "Select Channels";
            Width = 280;
            Height = 400;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromRgb(45, 45, 48));
            Foreground = Brushes.White;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;

            var grid = new Grid { Margin = new Thickness(15) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var title = new TextBlock 
            { 
                Text = "Select Audible Channels:", 
                FontWeight = FontWeights.Bold, 
                Margin = new Thickness(0, 0, 0, 10),
                Foreground = Brushes.White
            };
            grid.Children.Add(title);

            var list = new ListBox 
            { 
                Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)), 
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromRgb(63, 63, 70)),
                Margin = new Thickness(0, 0, 0, 10)
            };
            Grid.SetRow(list, 1);
            grid.Children.Add(list);

            var checkBoxes = new List<CheckBox>();
            for (byte i = 0; i < 16; i++)
            {
                var cb = new CheckBox 
                { 
                    Content = $"Channel {i}", 
                    Foreground = Brushes.White,
                    Margin = new Thickness(6, 4, 6, 4),
                    IsChecked = initiallySelected.Contains(i)
                };
                checkBoxes.Add(cb);
                list.Items.Add(cb);
            }

            var buttons = new StackPanel 
            { 
                Orientation = Orientation.Horizontal, 
                HorizontalAlignment = HorizontalAlignment.Right 
            };
            Grid.SetRow(buttons, 2);
            grid.Children.Add(buttons);

            var okBtn = new Button 
            { 
                Content = "OK", 
                Width = 75, 
                Height = 26, 
                Margin = new Thickness(0, 0, 8, 0), 
                Background = new SolidColorBrush(Color.FromRgb(0, 122, 204)), 
                Foreground = Brushes.White, 
                BorderThickness = new Thickness(0) 
            };
            okBtn.Click += (s, e) =>
            {
                for (byte i = 0; i < 16; i++)
                {
                    if (checkBoxes[i].IsChecked == true)
                    {
                        SelectedChannels.Add(i);
                    }
                }
                DialogResult = true;
                Close();
            };
            buttons.Children.Add(okBtn);

            var cancelBtn = new Button 
            { 
                Content = "Cancel", 
                Width = 75, 
                Height = 26, 
                Background = new SolidColorBrush(Color.FromRgb(62, 62, 66)), 
                Foreground = Brushes.White, 
                BorderThickness = new Thickness(0) 
            };
            cancelBtn.Click += (s, e) => { DialogResult = false; Close(); };
            buttons.Children.Add(cancelBtn);

            Content = grid;
        }
    }

    public class DuplicateChannelDialog : Window
    {
        public byte SelectedChannel { get; private set; }
        private ComboBox _comboBox;
        
        public DuplicateChannelDialog(List<byte> activeChannels)
        {
            Title = "Duplicate Channel";
            Width = 300;
            Height = 150;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromRgb(45, 45, 48));
            Foreground = Brushes.White;
            ResizeMode = ResizeMode.NoResize;
            
            var stackPanel = new StackPanel { Margin = new Thickness(15) };
            
            var label = new TextBlock 
            { 
                Text = "Select target channel to copy notes to:", 
                Margin = new Thickness(0, 0, 0, 10),
                Foreground = Brushes.LightGray
            };
            stackPanel.Children.Add(label);
            
            _comboBox = new ComboBox 
            { 
                Height = 25, 
                Margin = new Thickness(0, 0, 0, 15),
                Background = new SolidColorBrush(Color.FromRgb(51, 51, 55)),
                Foreground = Brushes.Black
            };
            
            for (byte ch = 0; ch < 16; ch++)
            {
                string suffix = activeChannels.Contains(ch) ? " (Active)" : "";
                _comboBox.Items.Add(new ComboBoxItem { Content = $"{ch}{suffix}", Tag = ch });
            }
            _comboBox.SelectedIndex = 0;
            stackPanel.Children.Add(_comboBox);
            
            var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            
            var okButton = new Button 
            { 
                Content = "Duplicate", 
                Width = 80, 
                Height = 25, 
                IsDefault = true,
                Background = new SolidColorBrush(Color.FromRgb(0, 122, 204)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Margin = new Thickness(0, 0, 10, 0)
            };
            okButton.Click += (s, e) => 
            {
                if (_comboBox.SelectedItem is ComboBoxItem item && item.Tag is byte ch)
                {
                    SelectedChannel = ch;
                    DialogResult = true;
                }
            };
            buttonPanel.Children.Add(okButton);
            
            var cancelButton = new Button 
            { 
                Content = "Cancel", 
                Width = 80, 
                Height = 25, 
                IsCancel = true,
                Background = new SolidColorBrush(Color.FromRgb(107, 107, 107)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0)
            };
            buttonPanel.Children.Add(cancelButton);
            stackPanel.Children.Add(buttonPanel);
            
            Content = stackPanel;
        }
    }
}
