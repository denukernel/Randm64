using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.IO;
using Sm64DecompLevelViewer.Services.Audio;
using Sm64DecompLevelViewer.Services.Chaos;
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
            public bool IsAvailable => Id != 27;

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

        public Services.Chaos.M64MutationSettings MusicSettings { get; private set; } = new();
        private readonly ChaosControls.MusicEditor _musicEditor;
        private readonly string? _projectRoot;
        private string? _previewPath;
        private M64CorruptionPreview? _preview;

        public M64CorruptionWindow(List<int> initialSelectedIds, Services.Chaos.M64MutationSettings? settings = null,
            string? projectRoot = null, double intensity = 50, int seed = 0)
        {
            InitializeComponent();
            Height = 900;
            MaxHeight = SystemParameters.WorkArea.Height;
            _projectRoot = projectRoot;
            PreviewPanel.Visibility = projectRoot == null ? Visibility.Collapsed : Visibility.Visible;
            PreviewIntensity.Text = intensity.ToString(System.Globalization.CultureInfo.InvariantCulture);
            PreviewSeed.Text = seed.ToString();
            Closed += (_, _) => _preview?.Dispose();
            _musicEditor = new ChaosControls.MusicEditor(((Grid)Content).Children.OfType<StackPanel>().First(), settings ?? new());

            // IDs remain stable for existing preset files.
            var allModes = new List<M64CorruptionModeItem>
            {
                // Original modes
                new M64CorruptionModeItem { Id = 0, Name = "Transpose (Pitch Shift)", Description = "Shifts selected melodic note pitches by a shared semitone offset; encoded pitches stay within 0â€“63.", IsNew = false },
                new M64CorruptionModeItem { Id = 1, Name = "Tempo Shift (BPM Warp)", Description = "Warps the playback speed (BPM) of the sequence, making it run faster, slower, or fluctuating wildly.", IsNew = false },
                new M64CorruptionModeItem { Id = 2, Name = "Instrument Swap (Orchestration)", Description = "Swaps melodic instrument IDs already used in the same sequence bank. Percussion and synthetic instruments stay intact.", IsNew = false },
                new M64CorruptionModeItem { Id = 3, Name = "Duration Glitcher", Description = "Randomly changes the length/sustain of notes, creating short plucks or extremely long ringing notes.", IsNew = false },
                new M64CorruptionModeItem { Id = 4, Name = "Velocity Randomizer", Description = "Scrambles the attack velocity/volume of individual notes, making notes play unpredictably loud or quiet.", IsNew = false },
                new M64CorruptionModeItem { Id = 5, Name = "Melody Scrambler (Shuffle)", Description = "Shuffles note pitches in-place, keeping the timing grids the same but generating brand new scrambled melodies.", IsNew = false },

                // Additional value mutation modes
                new M64CorruptionModeItem { Id = 6, Name = "Octave Jumper", Description = "Shifts selected melodic notes by one octave, clamped to the encoded pitch range.", IsNew = true },
                new M64CorruptionModeItem { Id = 7, Name = "Channel Muter", Description = "Randomly sets selected channel volume parameters to 0, completely muting some instrument tracks to thin out the orchestration.", IsNew = true },
                new M64CorruptionModeItem { Id = 8, Name = "Pan Scrambler", Description = "Scrambles spatial panning offsets, throwing sound channels dynamically between absolute left, center, and right speakers.", IsNew = true },
                new M64CorruptionModeItem { Id = 9, Name = "Reverb Maxer", Description = "Maxes out the reverberation volume parameters, generating a dense, muddy echo chamber/hall acoustic environment.", IsNew = true },
                new M64CorruptionModeItem { Id = 10, Name = "Pitch Bend Chaos", Description = "Randomizes existing channel pitch-bend commands. Sequences without these commands stay unchanged; no new slides or portamento events are inserted.", IsNew = true },
                new M64CorruptionModeItem { Id = 11, Name = "Note Lengthener (Sustain)", Description = "Reduces the silent gate fraction to extend notes within their existing delays. Timing and note count stay intact.", IsNew = true },
                new M64CorruptionModeItem { Id = 12, Name = "Note Shortener (Staccato)", Description = "Raises the silent gate fraction to shorten notes within their existing delays.", IsNew = true },
                new M64CorruptionModeItem { Id = 13, Name = "Major Scale Lock", Description = "Forces/clamps every note pitch onto the nearest Major Scale degree, maintaining harmonious, uplifting, happy intervals.", IsNew = true },
                new M64CorruptionModeItem { Id = 14, Name = "Minor Scale Lock", Description = "Forces/clamps every note pitch onto the nearest Natural Minor Scale degree, giving all melodies a sad, mysterious, or spooky mood.", IsNew = true },
                new M64CorruptionModeItem { Id = 15, Name = "Atonal Glitcher", Description = "Moves pitch offsets completely at random without scale restriction, creating highly atonal, dissonant, modern noise experiments.", IsNew = true },
                new M64CorruptionModeItem { Id = 16, Name = "Velocity Inverter", Description = "Inverts note velocity mappings (loud notes become extremely quiet, and quiet background details pop out at full volume).", IsNew = true },
                new M64CorruptionModeItem { Id = 17, Name = "Volume Variation", Description = "Varies existing volume commands; does not insert additional tremolo events.", IsNew = true },
                new M64CorruptionModeItem { Id = 18, Name = "Transpose Per Channel", Description = "Shifts the pitch of each channel track by a different random transposition value, leading to bizarre, polytonal, shifting chords.", IsNew = true },
                new M64CorruptionModeItem { Id = 19, Name = "Alternate Octave Shift", Description = "Raises every other selected melodic note by one octave. Changes existing notes without adding voices.", IsNew = true },
                new M64CorruptionModeItem { Id = 20, Name = "Percussion Accents", Description = "Randomizes existing percussion velocities between 64 and 127. Preserves drum indices and note timing.", IsNew = true },
                new M64CorruptionModeItem { Id = 21, Name = "Tempo Drifter", Description = "Modulates tempo commands to slowly drift up and down over time, simulating an unstable tape player with mechanical drag.", IsNew = true },
                new M64CorruptionModeItem { Id = 22, Name = "Max Recognized Volumes", Description = "Sets recognized sequence/channel volume commands and note velocities to 127. Preserves gain scales and envelopes.", IsNew = true },
                new M64CorruptionModeItem { Id = 23, Name = "Note Eraser and Lengthener", Description = "Silences some note velocities and reduces gates to extend the remaining notes within their original delays.", IsNew = true },
                new M64CorruptionModeItem { Id = 24, Name = "Melody Reverser", Description = "Reverses the order of pitches in the sequence, playing melodies backward while preserving timing.", IsNew = true },
                new M64CorruptionModeItem { Id = 25, Name = "Alternate Minor Third (+3)", Description = "Raises every other selected melodic note by three semitones. Changes existing notes without duplicating them.", IsNew = true },
                new M64CorruptionModeItem { Id = 26, Name = "Pitch Flatten (Monotone)", Description = "Flattens all note pitches in each channel to a single note, making every instrument play in a monotone, straight-line drone.", IsNew = true },
                new M64CorruptionModeItem { Id = 27, Name = "Sequence Pointer Slider (Disabled)", Description = "Unavailable: swapping pointers across script contexts can crash the SM64 audio thread. Old presets containing this mode must be edited before running.", IsNew = true },
                new M64CorruptionModeItem { Id = 28, Name = "Full Note Gate", Description = "Sets existing gates to zero for full note duration. Preserves sound-bank release settings; does not create infinite sustain.", IsNew = true },
                new M64CorruptionModeItem { Id = 29, Name = "M64 Sparta Remix Mode", Description = "Replaces notes sequentially with the relative semitone offsets 0, 0, 1, 1, -2, -2, 1, 1, 0, 12, 0, 12, 1, 13, 1, 13, -2, 10, -2, 10, 1, 13, 1, 13, looping the melody sequence.", IsNew = true }
            };

            foreach (var item in allModes)
            {
                item.IsEnabled = item.IsAvailable && initialSelectedIds.Contains(item.Id);
                item.PropertyChanged += Item_PropertyChanged;
                Modes.Add(item);
            }

            ModesListBox.ItemsSource = Modes;
            ChaosControls.AddSearch(this, ModesListBox, item => ((M64CorruptionModeItem)item).Name + " " + ((M64CorruptionModeItem)item).Description);
            UpdateStatusText();
        }

        private void ChoosePreview_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                StopPreview_Click(sender, e);
                var items = Directory.EnumerateFiles(Path.Combine(_projectRoot!, "sound", "sequences"), "*.m64", SearchOption.AllDirectories)
                    .OrderBy(p => p).Select(p => new SequenceSelectItem { FilePath = p,
                        DisplayName = $"{Path.GetFileName(p)} ({Path.GetFileName(Path.GetDirectoryName(p))})" }).ToList();
                var dialog = new SequenceSelectionWindow(items, _previewPath) { Owner = this };
                if (dialog.ShowDialog() == true && dialog.SelectedItem != null)
                {
                    _previewPath = dialog.SelectedItem.FilePath;
                    PreviewSequenceText.Text = dialog.SelectedItem.DisplayName;
                }
            }
            catch (Exception ex) { PreviewStatus.Text = ex.Message; }
        }

        private void PreviewOriginal_Click(object sender, RoutedEventArgs e) => PlayPreview(false);
        private void PreviewCorrupted_Click(object sender, RoutedEventArgs e) => PlayPreview(true);
        private void StopPreview_Click(object sender, RoutedEventArgs e)
        {
            _preview?.Stop();
            PreviewStatus.Text = "Preview stopped. No files changed.";
        }

        private void PlayPreview(bool corrupted)
        {
            _preview?.Stop();
            try
            {
                if (_previewPath == null) throw new InvalidOperationException("Choose a preview sequence first.");
                byte[] data = File.ReadAllBytes(File.Exists(_previewPath + ".bak") ? _previewPath + ".bak" : _previewPath);
                string effectSummary = "";
                if (corrupted)
                {
                    if (!double.TryParse(PreviewIntensity.Text, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double intensity) || !double.IsFinite(intensity) || intensity < 0 || intensity > 100)
                        throw new InvalidOperationException("Preview intensity must be 0 to 100.");
                    if (!int.TryParse(PreviewSeed.Text, out int seed)) throw new InvalidOperationException("Preview seed must be an integer.");
                    var result = M64ModePreview.Create(data,
                        Path.GetFileName(Path.GetDirectoryName(_previewPath))!.ToLowerInvariant(),
                        Modes.Where(m => m.IsEnabled).Select(m => m.Id), intensity, seed, _musicEditor.Read());
                    data = result.Data;
                    effectSummary = $"{result.ChangedBytes} bytes changed. ";
                    if (result.ModesWithoutTargets.Length > 0)
                        effectSummary += "No applicable commands: " + string.Join(", ", result.ModesWithoutTargets.Select(id => Modes.Single(m => m.Id == id).Name)) + ". ";
                    if (result.ChangedBytes == 0)
                    {
                        PreviewStatus.Text = effectSummary + "Preview is unchanged. Try another sequence, seed, intensity or mode.";
                        return;
                    }
                }
                if (_preview == null)
                {
                    _preview = new M64CorruptionPreview(_projectRoot!);
                    _preview.Finished += () => PreviewStatus.Text = "Preview finished.";
                    _preview.Failed += message => PreviewStatus.Text = "Preview failed: " + message;
                }
                PreviewStatus.Text = effectSummary + (corrupted ? "Corrupted: " : "Original: ") + _preview.Play(_previewPath, data, PreviewMidi.IsChecked == true);
            }
            catch (Exception ex) { _preview?.Stop(); PreviewStatus.Text = ex.Message; }
        }

        private void Item_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(M64CorruptionModeItem.IsEnabled))
            {
                _preview?.Stop();
                PreviewStatus.Text = "Modes changed. Press Corrupted to preview the checked effects.";
                UpdateStatusText();
            }
        }

        private void UpdateStatusText()
        {
            int count = Modes.Count(m => m.IsEnabled);
            StatusTextBlock.Text = $"{count} selected; {Modes.Count(m => m.IsAvailable)} available, {Modes.Count(m => !m.IsAvailable)} unavailable";
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
            foreach (var mode in Modes) mode.IsEnabled = mode.IsAvailable;
        }

        private void DeselectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var mode in Modes) mode.IsEnabled = false;
        }

        private void SelectOriginal_Click(object sender, RoutedEventArgs e)
        {
            foreach (var mode in Modes)
            {
                mode.IsEnabled = mode.IsAvailable && !mode.IsNew;
            }
        }

        private void SelectNew_Click(object sender, RoutedEventArgs e)
        {
            foreach (var mode in Modes)
            {
                mode.IsEnabled = mode.IsAvailable && mode.IsNew;
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try { MusicSettings = _musicEditor.Read(); }
            catch (InvalidOperationException ex) { MessageBox.Show(this, ex.Message); return; }
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
