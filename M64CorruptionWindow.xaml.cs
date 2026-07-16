using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Sm64DecompLevelViewer
{
    public partial class M64CorruptionWindow : Window
    {
        public class M64CorruptionModeItem : INotifyPropertyChanged
        {
            private bool _isEnabled;
            public int Id { get; set; }
            public string Name { get; set; }
            public string Description { get; set; }
            public bool IsNew { get; set; }

            public bool IsEnabled
            {
                get => _isEnabled;
                set
                {
                    if (_isEnabled != value)
                    {
                        _isEnabled = value;
                        OnPropertyChanged(nameof(IsEnabled));
                    }
                }
            }

            public event PropertyChangedEventHandler? PropertyChanged;
            protected void OnPropertyChanged(string propertyName)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        public ObservableCollection<M64CorruptionModeItem> Modes { get; } = new();
        public List<int> SelectedModeIds { get; private set; } = new();

        public M64CorruptionWindow(List<int> initialSelectedIds)
        {
            InitializeComponent();

            // Populate all 22 modes
            var allModes = new List<M64CorruptionModeItem>
            {
                // Original modes
                new M64CorruptionModeItem { Id = 0, Name = "Transpose (Pitch Shift)", Description = "Shifts the musical pitch of all notes in the sequence up or down by a constant number of semitones, preserving relative melodies.", IsNew = false },
                new M64CorruptionModeItem { Id = 1, Name = "Tempo Shift (BPM Warp)", Description = "Warps the playback speed (BPM) of the sequence, making it run faster, slower, or fluctuating wildly.", IsNew = false },
                new M64CorruptionModeItem { Id = 2, Name = "Instrument Swap (Orchestration)", Description = "Randomizes channel instrument assignments, forcing instruments to play with entirely different sound bank patches.", IsNew = false },
                new M64CorruptionModeItem { Id = 3, Name = "Duration Glitcher", Description = "Randomly changes the length/sustain of notes, creating short plucks or extremely long ringing notes.", IsNew = false },
                new M64CorruptionModeItem { Id = 4, Name = "Velocity Randomizer", Description = "Scrambles the attack velocity/volume of individual notes, making notes play unpredictably loud or quiet.", IsNew = false },
                new M64CorruptionModeItem { Id = 5, Name = "Melody Scrambler (Shuffle)", Description = "Shuffles note pitches in-place, keeping the timing grids the same but generating brand new scrambled melodies.", IsNew = false },

                // 16 New Crash-Proof Modes
                new M64CorruptionModeItem { Id = 6, Name = "Octave Jumper", Description = "Transposes random notes up or down by exactly 12 or 24 semitones, jumping notes into extreme octaves without breaking harmonies.", IsNew = true },
                new M64CorruptionModeItem { Id = 7, Name = "Channel Muter", Description = "Randomly sets selected channel volume parameters to 0, completely muting some instrument tracks to thin out the orchestration.", IsNew = true },
                new M64CorruptionModeItem { Id = 8, Name = "Pan Scrambler", Description = "Scrambles spatial panning offsets, throwing sound channels dynamically between absolute left, center, and right speakers.", IsNew = true },
                new M64CorruptionModeItem { Id = 9, Name = "Reverb Maxer", Description = "Maxes out the reverberation volume parameters, generating a dense, muddy echo chamber/hall acoustic environment.", IsNew = true },
                new M64CorruptionModeItem { Id = 10, Name = "Pitch Bend Chaos", Description = "Modulates pitch bend registers with unstable random parameters, creating detuned slides, warbles, and synth portamento effects.", IsNew = true },
                new M64CorruptionModeItem { Id = 11, Name = "Note Lengthener (Sustain)", Description = "Drastically multiplies note duration/sustain values, overlapping notes to blend chords and sound font release envelopes.", IsNew = true },
                new M64CorruptionModeItem { Id = 12, Name = "Note Shortener (Staccato)", Description = "Shrinks all note durations to absolute minimum values, turning the tracks into rapid, short, staccato mechanical sound effects.", IsNew = true },
                new M64CorruptionModeItem { Id = 13, Name = "Major Scale Lock", Description = "Forces/clamps every note pitch onto the nearest Major Scale degree, maintaining harmonious, uplifting, happy intervals.", IsNew = true },
                new M64CorruptionModeItem { Id = 14, Name = "Minor Scale Lock", Description = "Forces/clamps every note pitch onto the nearest Natural Minor Scale degree, giving all melodies a sad, mysterious, or spooky mood.", IsNew = true },
                new M64CorruptionModeItem { Id = 15, Name = "Atonal Glitcher", Description = "Moves pitch offsets completely at random without scale restriction, creating highly atonal, dissonant, modern noise experiments.", IsNew = true },
                new M64CorruptionModeItem { Id = 16, Name = "Velocity Inverter", Description = "Inverts note velocity mappings (loud notes become extremely quiet, and quiet background details pop out at full volume).", IsNew = true },
                new M64CorruptionModeItem { Id = 17, Name = "Dynamic Tremolo", Description = "Inserts rapid tremolo amplitude modulation onto channel volumes, causing the sound of instruments to pulsate rapidly.", IsNew = true },
                new M64CorruptionModeItem { Id = 18, Name = "Transpose Per Channel", Description = "Shifts the pitch of each channel track by a different random transposition value, leading to bizarre, polytonal, shifting chords.", IsNew = true },
                new M64CorruptionModeItem { Id = 19, Name = "Harmonizer (Octave Duplicator)", Description = "Harmonizes notes by copying and layering duplicates at octave intervals, giving tracks a rich, layered, multi-voice chorus sound.", IsNew = true },
                new M64CorruptionModeItem { Id = 20, Name = "Drum Roll Glitcher", Description = "Identifies percussion/drum layers and rapidly replicates note commands in quick succession to generate automated drum rolls.", IsNew = true },
                new M64CorruptionModeItem { Id = 21, Name = "Tempo Drifter", Description = "Modulates tempo commands to slowly drift up and down over time, simulating an unstable tape player with mechanical drag.", IsNew = true },
                new M64CorruptionModeItem { Id = 22, Name = "Max all sequence volumes", Description = "Forces all volume control parameters to maximum limits (127), making the tracks significantly louder.", IsNew = true },
                new M64CorruptionModeItem { Id = 23, Name = "Note Eraser and Lengthener", Description = "Mutes 50% of note velocities entirely and multiplies the duration of some remaining notes, creating fragmented, sustaining sounds.", IsNew = true },
                new M64CorruptionModeItem { Id = 24, Name = "Melody Reverser", Description = "Reverses the order of pitches in the sequence, playing melodies backward while preserving timing.", IsNew = true },
                new M64CorruptionModeItem { Id = 25, Name = "Double Note (+3 Pitch)", Description = "Turns notes into double notes with a +3 pitch offset (minor third interval) mimicking chord voicings like G major 7.", IsNew = true },
                new M64CorruptionModeItem { Id = 26, Name = "Pitch Flatten (Monotone)", Description = "Flattens all note pitches in each channel to a single note, making every instrument play in a monotone, straight-line drone.", IsNew = true }
            };

            foreach (var item in allModes)
            {
                item.IsEnabled = initialSelectedIds.Contains(item.Id);
                item.PropertyChanged += Item_PropertyChanged;
                Modes.Add(item);
            }

            ModesListBox.ItemsSource = Modes;
            UpdateStatusText();
        }

        private void Item_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(M64CorruptionModeItem.IsEnabled))
            {
                UpdateStatusText();
            }
        }

        private void UpdateStatusText()
        {
            int count = Modes.Count(m => m.IsEnabled);
            StatusTextBlock.Text = $"{count} of {Modes.Count} modes enabled";
        }

        private void ModesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selectedItem = ModesListBox.SelectedItem as M64CorruptionModeItem;
            if (selectedItem != null)
            {
                DescriptionTextBlock.Text = selectedItem.Description;
            }
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var mode in Modes) mode.IsEnabled = true;
        }

        private void DeselectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var mode in Modes) mode.IsEnabled = false;
        }

        private void SelectOriginal_Click(object sender, RoutedEventArgs e)
        {
            foreach (var mode in Modes)
            {
                mode.IsEnabled = !mode.IsNew;
            }
        }

        private void SelectNew_Click(object sender, RoutedEventArgs e)
        {
            foreach (var mode in Modes)
            {
                mode.IsEnabled = mode.IsNew;
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            SelectedModeIds = Modes.Where(m => m.IsEnabled).Select(m => m.Id).ToList();

            if (SelectedModeIds.Count == 0)
            {
                MessageBox.Show("Please select at least one active corruption mode.", "Selection Empty", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
