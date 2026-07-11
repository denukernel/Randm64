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
        
        private readonly System.Windows.Threading.DispatcherTimer _playbackTimer = new();
        private double _playheadTick = 0;
        private readonly double _tempoBpm = 150;
        private readonly double _ticksPerBeat = 48; // Quarter note division
        private bool _isPlaying = false;
        private bool _isUpdatingUi = false;

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
                InstrumentSelector.Items.Add(new ComboBoxItem { Content = i.ToString() });
            }
        }

        private void ScanSoundSequences()
        {
            if (SequenceSelector == null) return;
            
            SequenceSelector.Items.Clear();
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

                    var item = new ComboBoxItem
                    {
                        Content = $"{fileName} ({parentFolder})",
                        Tag = file
                    };
                    SequenceSelector.Items.Add(item);
                }

                if (SequenceSelector.Items.Count > 0)
                {
                    bool foundDefault = false;
                    for (int i = 0; i < SequenceSelector.Items.Count; i++)
                    {
                        var item = (ComboBoxItem)SequenceSelector.Items[i];
                        if (item.Content.ToString()!.Contains("22_cutscene_lakitu"))
                        {
                            SequenceSelector.SelectedIndex = i;
                            foundDefault = true;
                            break;
                        }
                    }

                    if (!foundDefault)
                    {
                        SequenceSelector.SelectedIndex = 0;
                    }
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
            _samplePlayer?.StopAll();
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
                    
                    string samplePath = GetSamplePathForInstrument(_activeM64Path, patch);
                    if (!string.IsNullOrEmpty(samplePath))
                    {
                        _samplePlayer?.PlayNote(samplePath, p, 100, _currentTrack?.Volume ?? 127);
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

        private void SequenceSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SequenceSelector == null) return;
            if (SequenceSelector.SelectedItem is ComboBoxItem item)
            {
                _activeM64Path = item.Tag as string ?? "";
                _sequenceTracks = _m64Service.LoadM64(_activeM64Path);

                if (ChannelSelector != null)
                {
                    _isUpdatingUi = true;
                    ChannelSelector.Items.Clear();

                    var sortedChannels = _sequenceTracks.Select(t => t.ChannelIndex).Distinct().OrderBy(c => c).ToList();
                    foreach (byte ch in sortedChannels)
                    {
                        ChannelSelector.Items.Add(new ComboBoxItem { Content = ch.ToString() });
                    }

                    if (ChannelSelector.Items.Count > 0)
                    {
                        ChannelSelector.SelectedIndex = 0;
                    }
                    _isUpdatingUi = false;
                }

                LoadSelectedChannel();
            }
        }

        private void ChannelSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            LoadSelectedChannel();
        }

        private void LoadSelectedChannel()
        {
            if (ChannelSelector == null || ChannelSelector.SelectedItem == null) return;

            string chStr = ((ComboBoxItem)ChannelSelector.SelectedItem).Content.ToString()!;
            byte channelIndex = byte.Parse(chStr);

            _currentTrack = _sequenceTracks.FirstOrDefault(t => t.ChannelIndex == channelIndex);
            if (_currentTrack == null)
            {
                _currentTrack = new M64Track { ChannelIndex = channelIndex };
                _sequenceTracks.Add(_currentTrack);
            }

            _isUpdatingUi = true;
            if (InstrumentSelector != null)
            {
                InstrumentSelector.SelectedIndex = -1;
                foreach (ComboBoxItem item in InstrumentSelector.Items)
                {
                    if (item.Content.ToString() == _currentTrack.Instrument.ToString())
                    {
                        InstrumentSelector.SelectedItem = item;
                        break;
                    }
                }
            }
            if (VolumeTextBox != null)
                VolumeTextBox.Text = _currentTrack.Volume.ToString();
            _isUpdatingUi = false;

            _selectedNotes.Clear();
            RenderNotes();
        }

        private void RenderNotes()
        {
            if (PianoRollCanvas == null) return;

            // Clear previous notes
            var toRemove = PianoRollCanvas.Children.OfType<Rectangle>().ToList();
            foreach (var rect in toRemove)
            {
                if (rect != SelectionBox)
                {
                    PianoRollCanvas.Children.Remove(rect);
                }
            }

            if (_currentTrack == null) return;

            // Dynamically adjust canvas width based on maximum note tick
            double maxTick = 2000;
            if (_sequenceTracks != null && _sequenceTracks.Count > 0)
            {
                var allNotes = _sequenceTracks.SelectMany(t => t.Notes).ToList();
                if (allNotes.Count > 0)
                {
                    maxTick = allNotes.Max(n => n.StartTick + n.DurationTicks);
                }
            }

            double canvasWidth = Math.Max(2000, maxTick * (BEAT_WIDTH / _ticksPerBeat) + 1000);
            PianoRollCanvas.Width = canvasWidth;
            if (TimelineCanvas != null) TimelineCanvas.Width = canvasWidth;

            foreach (var note in _currentTrack.Notes)
            {
                DrawNoteRect(note);
            }
        }

        private void DrawNoteRect(M64Note note)
        {
            if (note.Pitch > MAX_NOTE_PITCH || note.Pitch < MIN_NOTE_PITCH) return;

            double left = note.StartTick * (BEAT_WIDTH / _ticksPerBeat);
            double top = (MAX_NOTE_PITCH - note.Pitch) * KEY_HEIGHT;
            double width = note.DurationTicks * (BEAT_WIDTH / _ticksPerBeat);

            bool isSelected = _selectedNotes.Contains(note);

            var rect = new Rectangle
            {
                Width = Math.Max(width, 5),
                Height = KEY_HEIGHT - 2,
                Fill = isSelected ? new SolidColorBrush(Color.FromRgb(255, 140, 0)) : new SolidColorBrush(Color.FromRgb(0, 122, 204)),
                Stroke = isSelected ? Brushes.OrangeRed : Brushes.White,
                StrokeThickness = isSelected ? 2 : 1,
                RadiusX = 2,
                RadiusY = 2,
                Tag = note
            };

            Canvas.SetLeft(rect, left);
            Canvas.SetTop(rect, top + 1);

            PianoRollCanvas.Children.Add(rect);
        }

        private void PianoRollCanvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            Point mousePos = e.GetPosition(PianoRollCanvas);

            if (e.ChangedButton == MouseButton.Right)
            {
                // Right click: Delete note
                if (e.OriginalSource is Rectangle rect && rect.Tag is M64Note note)
                {
                    PushUndoState();
                    _currentTrack?.Notes.Remove(note);
                    _selectedNotes.Remove(note);
                    PianoRollCanvas.Children.Remove(rect);
                    RenderNotes();
                }
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
                        string samplePath = GetSamplePathForInstrument(_activeM64Path, patch);
                        if (!string.IsNullOrEmpty(samplePath))
                        {
                            _samplePlayer?.PlayNote(samplePath, noteData.Pitch, 100, _currentTrack?.Volume ?? 127);
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
                        string samplePath = GetSamplePathForInstrument(_activeM64Path, patch);
                        if (!string.IsNullOrEmpty(samplePath))
                        {
                            _samplePlayer?.PlayNote(samplePath, pitch, 100, _currentTrack?.Volume ?? 127);
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

            _selectedNoteRect = null;
            _selectedNote = null;
            _isDraggingNote = false;
            _isResizingNote = false;
            _draggedNotesStartCoords.Clear();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
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

            string instStr = ((ComboBoxItem)InstrumentSelector.SelectedItem).Content.ToString()!;
            if (byte.TryParse(instStr, out byte instr))
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
            _samplePlayer?.StopAll();
        }

        private void PlaybackTimer_Tick(object? sender, EventArgs e)
        {
            if (!_isPlaying || _sequenceTracks == null) return;

            // Delta time: timer ticks every 20ms = 0.02s
            double dt = 0.02;
            double beatsPassed = dt * (_tempoBpm / 60.0);
            double ticksPassed = beatsPassed * _ticksPerBeat;

            double nextTick = _playheadTick + ticksPassed;

            // Handle Note ON and Note OFF events in real time across all tracks
            foreach (var track in _sequenceTracks)
            {
                byte targetChannel = track.ChannelIndex;

                foreach (var note in track.Notes)
                {
                    // Play note
                    if (note.StartTick >= _playheadTick && note.StartTick < nextTick)
                    {
                        string samplePath = GetSamplePathForInstrument(_activeM64Path, note.Instrument);
                        if (!string.IsNullOrEmpty(samplePath))
                        {
                            _samplePlayer?.PlayNote(samplePath, note.Pitch, note.Velocity, track.Volume);
                        }
                        else
                        {
                            byte gmPatch = MapSm64InstrumentToGm(_activeM64Path, targetChannel, note.Instrument);
                            _midiPlayer.ProgramChange(targetChannel, gmPatch);
                            _midiPlayer.NoteOn(targetChannel, note.Pitch, note.Velocity);
                        }
                        _activeMidiNotes.Add((targetChannel << 8) | note.Pitch);
                    }
                    // Stop note
                    int endTick = note.StartTick + note.DurationTicks;
                    if (endTick >= _playheadTick && endTick < nextTick)
                    {
                        _midiPlayer.NoteOff(targetChannel, note.Pitch);
                        _activeMidiNotes.Remove((targetChannel << 8) | note.Pitch);
                    }
                }
            }

            _playheadTick = nextTick;
            Canvas.SetLeft(TimelineCursor, _playheadTick * (BEAT_WIDTH / _ticksPerBeat));

            // Auto loop back at the end of the canvas
            if (_playheadTick * (BEAT_WIDTH / _ticksPerBeat) >= PianoRollCanvas.Width)
            {
                StopPlayback();
                PlayButton_Click(this, new RoutedEventArgs());
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_activeM64Path)) return;

            var result = _m64Service.SaveM64(_activeM64Path, _sequenceTracks);

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



        private void VolumeTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingUi || _currentTrack == null) return;
            if (byte.TryParse(VolumeTextBox.Text, out byte vol))
            {
                _currentTrack.Volume = vol;
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
                    if (ChannelSelector != null)
                    {
                        _isUpdatingUi = true;
                        ChannelSelector.Items.Clear();
                        var sortedChannels = _sequenceTracks.Select(t => t.ChannelIndex).Distinct().OrderBy(c => c).ToList();
                        foreach (byte ch in sortedChannels)
                        {
                            ChannelSelector.Items.Add(new ComboBoxItem { Content = ch.ToString() });
                        }
                        if (ChannelSelector.Items.Count > 0)
                        {
                            ChannelSelector.SelectedIndex = 0;
                        }
                        _isUpdatingUi = false;
                    }

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

        private static string GetBankIdForSequence(string m64Path)
        {
            string fileName = System.IO.Path.GetFileName(m64Path).ToLower();
            if (fileName.Contains("grass") || fileName.Contains("bob_omb") || fileName.Contains("field")) return "22";
            if (fileName.Contains("castle") || fileName.Contains("inside")) return "0E";
            if (fileName.Contains("water") || fileName.Contains("ocean") || fileName.Contains("docks")) return "13";
            if (fileName.Contains("slide") || fileName.Contains("slider") || fileName.Contains("snow")) return "0D";
            if (fileName.Contains("boss") || fileName.Contains("road")) return "12";
            if (fileName.Contains("organ") || fileName.Contains("final") || fileName.Contains("battle")) return "1D_bowser_organ";
            if (fileName.Contains("lakitu") || fileName.Contains("cutscene")) return "1B";
            if (fileName.Contains("piranha") || fileName.Contains("lullaby")) return "14_piranha_music_box";
            if (fileName.Contains("stairs")) return "1C_endless_stairs";
            
            return "22";
        }

        private string GetSamplePathForInstrument(string m64Path, byte sm64Inst)
        {
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
                                    if (instProp.TryGetProperty("sound", out var soundProp))
                                    {
                                        if (soundProp.ValueKind == System.Text.Json.JsonValueKind.String)
                                        {
                                            soundSample = soundProp.GetString() ?? "";
                                        }
                                        else if (soundProp.ValueKind == System.Text.Json.JsonValueKind.Object)
                                        {
                                            if (soundProp.TryGetProperty("sample", out var sampleProp))
                                            {
                                                soundSample = sampleProp.GetString() ?? "";
                                            }
                                        }
                                    }
                                    else if (instProp.TryGetProperty("sound_hi", out var soundHiProp))
                                    {
                                        soundSample = soundHiProp.GetString() ?? "";
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
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing soundbank JSON: {ex.Message}");
            }

            return string.Empty;
        }

        private static int GetMidiPitchFromNoteName(string name)
        {
            name = name.ToUpper();
            int idx = name.IndexOf('_');
            if (idx >= 0) name = name.Substring(idx + 1);
            
            int octaveIndex = -1;
            for (int i = 0; i < name.Length; i++)
            {
                if (char.IsDigit(name[i]) || name[i] == '-')
                {
                    octaveIndex = i;
                    break;
                }
            }

            if (octaveIndex == -1) return 60;

            string notePart = name.Substring(0, octaveIndex);
            string octavePart = name.Substring(octaveIndex);
            
            if (!int.TryParse(octavePart, out int octave)) octave = 4;

            int noteOffset = 0;
            switch (notePart[0])
            {
                case 'C': noteOffset = 0; break;
                case 'D': noteOffset = 2; break;
                case 'E': noteOffset = 4; break;
                case 'F': noteOffset = 5; break;
                case 'G': noteOffset = 7; break;
                case 'A': noteOffset = 9; break;
                case 'B': noteOffset = 11; break;
            }

            if (notePart.Contains("#")) noteOffset += 1;
            else if (notePart.Contains("B") && notePart.Length > 1 && notePart[1] == 'B') noteOffset -= 1;
            else if (notePart.Contains("E") && notePart.Length > 1 && notePart[1] == 'B') noteOffset -= 1;
            else if (notePart.Contains("A") && notePart.Length > 1 && notePart[1] == 'B') noteOffset -= 1;
            else if (notePart.Contains("D") && notePart.Length > 1 && notePart[1] == 'B') noteOffset -= 1;
            else if (notePart.Contains("G") && notePart.Length > 1 && notePart[1] == 'B') noteOffset -= 1;
            
            return (octave + 1) * 12 + noteOffset;
        }
    }

    public class SampleSynthPlayer
    {
        private readonly string _scratchDir;
        private readonly List<System.Windows.Media.MediaPlayer> _activePlayers = new();
        private int _fileCounter = 0;

        public SampleSynthPlayer(string conversationId)
        {
            _scratchDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), 
                ".gemini", "antigravity-ide", "brain", conversationId, "scratch", "audio");
            
            try
            {
                if (System.IO.Directory.Exists(_scratchDir))
                {
                    System.IO.Directory.Delete(_scratchDir, true);
                }
                System.IO.Directory.CreateDirectory(_scratchDir);
            }
            catch { }
        }

        public void PlayNote(string samplePath, byte pitch, byte velocity, byte channelVolume)
        {
            if (string.IsNullOrEmpty(samplePath) || !System.IO.File.Exists(samplePath)) return;

            try
            {
                string fileName = System.IO.Path.GetFileNameWithoutExtension(samplePath);
                int basePitch = GetMidiPitchFromNoteName(fileName);

                int delta = pitch - basePitch;
                double ratio = Math.Pow(2.0, delta / 12.0);

                byte[] aiffBytes = System.IO.File.ReadAllBytes(samplePath);
                byte[] wavBytes = AiffWavTranscoder.ConvertAiffToWav(aiffBytes);
                if (wavBytes.Length == 0) return;

                int baseRate = BitConverter.ToInt32(wavBytes, 24);
                int targetRate = (int)(baseRate * ratio);
                if (targetRate <= 0) targetRate = 16000;

                byte[] rateBytes = BitConverter.GetBytes(targetRate);
                Array.Copy(rateBytes, 0, wavBytes, 24, 4);

                short blockAlign = BitConverter.ToInt16(wavBytes, 32);
                int targetByteRate = targetRate * blockAlign;
                byte[] byteRateBytes = BitConverter.GetBytes(targetByteRate);
                Array.Copy(byteRateBytes, 0, wavBytes, 28, 4);

                string tempFile = System.IO.Path.Combine(_scratchDir, $"note_{_fileCounter++}_{Guid.NewGuid()}.wav");
                System.IO.File.WriteAllBytes(tempFile, wavBytes);

                Application.Current.Dispatcher.Invoke(() =>
                {
                    var player = new System.Windows.Media.MediaPlayer();
                    player.Open(new Uri(tempFile));
                    
                    double vol = (velocity / 127.0) * (channelVolume / 127.0);
                    player.Volume = Math.Clamp(vol, 0.0, 1.0);
                    
                    player.Play();

                    _activePlayers.Add(player);

                    player.MediaEnded += (s, e) =>
                    {
                        player.Close();
                        _activePlayers.Remove(player);
                        try { System.IO.File.Delete(tempFile); } catch { }
                    };
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error playing sample note: {ex.Message}");
            }
        }

        private static int GetMidiPitchFromNoteName(string name)
        {
            name = name.ToUpper();
            int idx = name.IndexOf('_');
            if (idx >= 0) name = name.Substring(idx + 1);
            
            int octaveIndex = -1;
            for (int i = 0; i < name.Length; i++)
            {
                if (char.IsDigit(name[i]) || name[i] == '-')
                {
                    octaveIndex = i;
                    break;
                }
            }

            if (octaveIndex == -1) return 60;

            string notePart = name.Substring(0, octaveIndex);
            string octavePart = name.Substring(octaveIndex);
            
            if (!int.TryParse(octavePart, out int octave)) octave = 4;

            int noteOffset = 0;
            switch (notePart[0])
            {
                case 'C': noteOffset = 0; break;
                case 'D': noteOffset = 2; break;
                case 'E': noteOffset = 4; break;
                case 'F': noteOffset = 5; break;
                case 'G': noteOffset = 7; break;
                case 'A': noteOffset = 9; break;
                case 'B': noteOffset = 11; break;
            }

            if (notePart.Contains("#")) noteOffset += 1;
            else if (notePart.Contains("B") && notePart.Length > 1 && notePart[1] == 'B') noteOffset -= 1;
            else if (notePart.Contains("E") && notePart.Length > 1 && notePart[1] == 'B') noteOffset -= 1;
            else if (notePart.Contains("A") && notePart.Length > 1 && notePart[1] == 'B') noteOffset -= 1;
            else if (notePart.Contains("D") && notePart.Length > 1 && notePart[1] == 'B') noteOffset -= 1;
            else if (notePart.Contains("G") && notePart.Length > 1 && notePart[1] == 'B') noteOffset -= 1;
            
            return (octave + 1) * 12 + noteOffset;
        }

        public void StopAll()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                foreach (var player in _activePlayers.ToList())
                {
                    player.Stop();
                    player.Close();
                }
                _activePlayers.Clear();
            });

            try
            {
                if (System.IO.Directory.Exists(_scratchDir))
                {
                    foreach (var file in System.IO.Directory.GetFiles(_scratchDir))
                    {
                        System.IO.File.Delete(file);
                    }
                }
            }
            catch { }
        }
    }
}
