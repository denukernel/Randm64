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

namespace Sm64DecompLevelViewer;
public partial class MusicEditorWindow
{
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
                
                keyBorder.MouseDown += (s, e) => {
                    if (e.ChangedButton != MouseButton.Left) return;
                    var key = (Border)s;
                    key.CaptureMouse();
                    StartAudition((byte)key.Tag);
                    e.Handled = true;
                };
                keyBorder.MouseUp += (s, e) => {
                    StopAudition();
                    ((Border)s).ReleaseMouseCapture();
                };
                keyBorder.LostMouseCapture += (_, _) => StopAudition();

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
                ToolTip = $"Pitch {note.Pitch} | Tick {note.StartTick} | Length {note.DurationTicks} | Velocity {note.Velocity}",
                Tag = note
            };

            Canvas.SetLeft(rect, left);
            Canvas.SetTop(rect, top + 1);

            PianoRollCanvas.Children.Add(rect);
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
                    ResetPreviewVoices();
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
                    ResetPreviewVoices();
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
                    ResetPreviewVoices();
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
                    ResetPreviewVoices();
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
                    _selectedNoteRect = PianoRollCanvas.Children.OfType<Rectangle>().First(r => ReferenceEquals(r.Tag, noteData));
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

                        StartAudition(noteData.Pitch);
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
                ResetPreviewVoices();
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
                double newWidth = Math.Max(_dragStartWidth + deltaX, BEAT_WIDTH * SnapTicks / _ticksPerBeat);
                _selectedNoteRect.Width = newWidth;

                double snapWidth = BEAT_WIDTH * SnapTicks / _ticksPerBeat;
                int snappedDuration = (int)(Math.Round(newWidth / snapWidth) * snapWidth * (_ticksPerBeat / BEAT_WIDTH));
                _selectedNote.DurationTicks = Math.Max(snappedDuration, SnapTicks);
                RenderNotes();
            }
            else if (_isDraggingNote)
            {
                double snapWidth = BEAT_WIDTH * SnapTicks / _ticksPerBeat;
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
            StopAudition();

            if (_isDraggingPlayhead)
            {
                _isDraggingPlayhead = false;
                ResetPreviewVoices();
                if (_isPlaying) PrimePreviewAtPlayhead();
                PianoRollCanvas.ReleaseMouseCapture();
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
                    if (DrawTool.IsChecked != true || Keyboard.Modifiers == ModifierKeys.Control)
                    {
                        _selectedNotes.Clear();
                        RenderNotes();
                    }
                    else
                    {
                        // Tiny drag: Create new note
                        double gridSnapWidth = BEAT_WIDTH * SnapTicks / _ticksPerBeat;
                        int startTick = (int)(Math.Round(_boxSelectStartPoint.X / gridSnapWidth) * gridSnapWidth * (_ticksPerBeat / BEAT_WIDTH));
                        byte pitch = (byte)(MAX_NOTE_PITCH - (int)(_boxSelectStartPoint.Y / KEY_HEIGHT));

                        PushUndoState();

                        var newNote = new M64Note
                        {
                            StartTick = startTick,
                            DurationTicks = NewNoteTicks,
                            Pitch = pitch,
                            Velocity = 100,
                            Instrument = _currentTrack?.Instrument ?? 0,
                            ChannelVolume = _currentTrack?.Volume ?? 127,
                            ChannelPan = _currentTrack?.Notes.FirstOrDefault()?.ChannelPan ?? 64,
                            Reverb = _currentTrack?.Notes.FirstOrDefault()?.Reverb ?? 0,
                            Gate = 0
                        };

                        _currentTrack?.Notes.Add(newNote);

                        if (Keyboard.Modifiers != ModifierKeys.Control)
                        {
                            _selectedNotes.Clear();
                        }
                        _selectedNotes.Add(newNote);
                        RenderNotes();

                        StartAudition(pitch, timed: true);
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


            if (_isDraggingNote || _isResizingNote)
            {
                _isNotesModified = true;
            }

            _selectedNoteRect = null;
            _selectedNote = null;
            _isDraggingNote = false;
            _isResizingNote = false;
            _draggedNotesStartCoords.Clear();
            PianoRollCanvas.ReleaseMouseCapture();
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
                        _copiedNotes.Add(note.Clone());
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
                    
                    int snapTicks = SnapTicks;
                    int pasteTickStart = (int)(Math.Round(_playheadTick / snapTicks) * snapTicks);

                    foreach (var note in _copiedNotes)
                    {
                        int offset = note.StartTick - _copiedNotesMinTick;
                        var newNote = note.Clone();
                        newNote.StartTick = pasteTickStart + offset;
                        newNote.Instrument = _currentTrack.Instrument;
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
                        var newNote = note.Clone();
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
}
