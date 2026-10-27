using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Sm64DecompLevelViewer
{
    public partial class AudioDistortionWindow : Window
    {
        public class AudioDistortionPatchItem : INotifyPropertyChanged
        {
            private bool _isEnabled;
            public int Id { get; set; }
            public string Name { get; set; }
            public string Description { get; set; }

            public bool IsAvailable => Id == 0;
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

        public ObservableCollection<AudioDistortionPatchItem> Patches { get; } = new();
        public List<int> SelectedPatchIds { get; private set; } = new();

        public AudioDistortionWindow(List<int> initialSelectedIds)
        {
            InitializeComponent();

            var allPatches = new List<AudioDistortionPatchItem>
            {
                new AudioDistortionPatchItem { Id = 0, Name = "Long Notes (Reduced Gate)", Description = "Reduces the silent tail of each note to one quarter, extending sustained notes within their existing delays. Uses the sequence layer duration field. Samples and ADSR envelopes can still end earlier; a zero gate is unchanged." },
                new AudioDistortionPatchItem { Id = 1, Name = "Wackier Sounds & Music (A031C1BF) [unavailable]", Description = "[unavailable: no source patch implemented] Overwrites stack-frame offsets during audio processing to corrupt sequence registers, producing extremely wacky and unpredictable instrument tones." },
                new AudioDistortionPatchItem { Id = 2, Name = "Wacky Waveform Instruments (A031C1BA) [unavailable]", Description = "[unavailable: no source patch implemented] Corrupts sound bank channel tables on startup, forcing audio sequence instruments to cycle through unstable, glitchy waveforms." },
                new AudioDistortionPatchItem { Id = 3, Name = "ADSR Envelope Scrambler [unavailable]", Description = "[unavailable: no source patch implemented] Randomly overrides ADSR volume envelope registers, causing sounds to sustain infinitely, decay instantly, or pulse rapidly." },
                new AudioDistortionPatchItem { Id = 4, Name = "Vibrato Pitch Bend Chaos [unavailable]", Description = "[unavailable: no source patch implemented] Injects low-frequency pitch bend oscillations directly into active sequence channels, causing continuous vibrato and warbling." },
                new AudioDistortionPatchItem { Id = 5, Name = "DMA Output Buffer Bitcrusher [unavailable]", Description = "[unavailable: no source patch implemented] Periodically inserts low-amplitude random noise or swaps bytes directly inside the audio synthesis output DMA buffer, generating digital static and bitcrushed sound effects." }
            };

            foreach (var item in allPatches)
            {
                item.IsEnabled = item.IsAvailable && initialSelectedIds.Contains(item.Id);
                item.PropertyChanged += Item_PropertyChanged;
                Patches.Add(item);
            }

            ModesListBox.ItemsSource = Patches;
            ChaosControls.AddSearch(this, ModesListBox, item => ((AudioDistortionPatchItem)item).Name + " " + ((AudioDistortionPatchItem)item).Description);
            UpdateStatusText();
        }

        private void Item_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AudioDistortionPatchItem.IsEnabled))
            {
                UpdateStatusText();
            }
        }

        private void UpdateStatusText()
        {
            int count = Patches.Count(p => p.IsEnabled);
            StatusTextBlock.Text = $"{count} selected; {Patches.Count(p => p.IsAvailable)} available, {Patches.Count(p => !p.IsAvailable)} unavailable";
        }

        private void ModesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ModesListBox.SelectedItem is AudioDistortionPatchItem selected)
            {
                DescriptionTextBlock.Text = selected.Description;
            }
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in Patches)
            {
                item.IsEnabled = item.IsAvailable;
            }
        }

        private void DeselectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in Patches)
            {
                item.IsEnabled = false;
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            SelectedPatchIds = Patches.Where(p => p.IsEnabled).Select(p => p.Id).ToList();
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
