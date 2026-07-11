using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Sm64DecompLevelViewer.Services;

namespace Sm64DecompLevelViewer
{
    public partial class SoundEditorWindow : Window
    {
        private readonly string _projectRoot;
        private string _samplesDir = string.Empty;
        private System.Media.SoundPlayer? _activePlayer;

        public class AudioSampleItem
        {
            public string Name { get; set; } = string.Empty;
            public string FilePath { get; set; } = string.Empty;
        }

        public SoundEditorWindow(string projectRoot)
        {
            InitializeComponent();
            _projectRoot = projectRoot;
            _samplesDir = Path.Combine(_projectRoot, "sound", "samples");

            this.Loaded += SoundEditorWindow_Loaded;
            this.Closed += SoundEditorWindow_Closed;
        }

        private void SoundEditorWindow_Loaded(object sender, RoutedEventArgs e)
        {
            LoadCategories();
        }

        private void SoundEditorWindow_Closed(object? sender, EventArgs e)
        {
            StopPlayback();
        }

        private void LoadCategories()
        {
            if (CategoryListBox == null) return;
            CategoryListBox.Items.Clear();

            if (!Directory.Exists(_samplesDir))
            {
                StatusTextBlock.Text = $"Error: Samples folder not found at {_samplesDir}";
                return;
            }

            try
            {
                var dirs = Directory.GetDirectories(_samplesDir);
                foreach (var dir in dirs)
                {
                    string folderName = Path.GetFileName(dir);
                    CategoryListBox.Items.Add(folderName);
                }

                if (CategoryListBox.Items.Count > 0)
                {
                    CategoryListBox.SelectedIndex = 0;
                }
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"Error loading categories: {ex.Message}";
            }
        }

        private void CategoryListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CategoryListBox == null || CategoryListBox.SelectedItem == null) return;

            string category = CategoryListBox.SelectedItem.ToString()!;
            LoadSamples(category);
        }

        private void LoadSamples(string category)
        {
            if (SampleListBox == null) return;
            SampleListBox.Items.Clear();

            string categoryPath = Path.Combine(_samplesDir, category);
            if (!Directory.Exists(categoryPath)) return;

            try
            {
                var files = Directory.GetFiles(categoryPath, "*.*", SearchOption.TopDirectoryOnly)
                                     .Where(f => f.EndsWith(".aiff", StringComparison.OrdinalIgnoreCase) || 
                                                 f.EndsWith(".aif", StringComparison.OrdinalIgnoreCase))
                                     .OrderBy(f => f)
                                     .ToList();

                foreach (var file in files)
                {
                    SampleListBox.Items.Add(new AudioSampleItem
                    {
                        Name = Path.GetFileName(file),
                        FilePath = file
                    });
                }

                if (SampleListBox.Items.Count > 0)
                {
                    SampleListBox.SelectedIndex = 0;
                }
                else
                {
                    SelectedSampleTextBlock.Text = "No samples found";
                    FilePathTextBlock.Text = "-";
                }
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"Error loading samples: {ex.Message}";
            }
        }

        private void SampleListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SampleListBox == null || SampleListBox.SelectedItem == null) return;

            if (SampleListBox.SelectedItem is AudioSampleItem item)
            {
                SelectedSampleTextBlock.Text = item.Name;
                FilePathTextBlock.Text = item.FilePath.Substring(_projectRoot.Length).TrimStart('\\', '/');
                StatusTextBlock.Text = $"Selected {item.Name}";
            }
        }

        private void PlayButton_Click(object sender, RoutedEventArgs e)
        {
            if (SampleListBox == null || SampleListBox.SelectedItem == null) return;

            if (SampleListBox.SelectedItem is AudioSampleItem item)
            {
                PlaySample(item.FilePath);
            }
        }

        private void PlaySample(string filePath)
        {
            try
            {
                StopPlayback();

                if (!File.Exists(filePath))
                {
                    StatusTextBlock.Text = $"Error: File not found: {filePath}";
                    return;
                }

                StatusTextBlock.Text = $"Transcoding and playing {Path.GetFileName(filePath)}...";

                byte[] aiffBytes = File.ReadAllBytes(filePath);
                byte[] wavBytes = AiffWavTranscoder.ConvertAiffToWav(aiffBytes);

                if (wavBytes.Length == 0)
                {
                    StatusTextBlock.Text = "Error: Failed to transcode AIFF to WAV format.";
                    return;
                }

                var ms = new MemoryStream(wavBytes);
                _activePlayer = new System.Media.SoundPlayer(ms);
                _activePlayer.Play();
                StatusTextBlock.Text = $"Playing: {Path.GetFileName(filePath)}";
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"Playback error: {ex.Message}";
            }
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            StopPlayback();
        }

        private void StopPlayback()
        {
            try
            {
                _activePlayer?.Stop();
                _activePlayer?.Dispose();
                _activePlayer = null;
                StatusTextBlock.Text = "Playback stopped";
            }
            catch { }
        }

        private void ReplaceButton_Click(object sender, RoutedEventArgs e)
        {
            if (SampleListBox == null || SampleListBox.SelectedItem == null) return;

            if (SampleListBox.SelectedItem is AudioSampleItem item)
            {
                var ofd = new OpenFileDialog
                {
                    Filter = "WAV Audio Files (*.wav)|*.wav",
                    Title = $"Select Replacement Audio for {item.Name}"
                };

                if (ofd.ShowDialog() == true)
                {
                    try
                    {
                        StatusTextBlock.Text = "Transcoding WAV to SM64 AIFF format...";
                        
                        byte[] wavBytes = File.ReadAllBytes(ofd.FileName);
                        byte[] aiffBytes = AiffWavTranscoder.ConvertWavToAiff(wavBytes);

                        if (aiffBytes.Length == 0)
                        {
                            MessageBox.Show("Failed to transcode replacement WAV audio. Make sure it is a standard uncompressed PCM WAV file.", "Transcoding Error", MessageBoxButton.OK, MessageBoxImage.Error);
                            StatusTextBlock.Text = "Replacement failed: Transcoding error.";
                            return;
                        }

                        // Overwrite original file
                        File.WriteAllBytes(item.FilePath, aiffBytes);
                        
                        MessageBox.Show($"Successfully replaced sample {item.Name} with custom audio!", "Replacement Successful", MessageBoxButton.OK, MessageBoxImage.Information);
                        StatusTextBlock.Text = $"Replaced: {item.Name}";
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Error replacing sample: {ex.Message}", "File Write Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        StatusTextBlock.Text = $"Replacement error: {ex.Message}";
                    }
                }
            }
        }
    }
}
