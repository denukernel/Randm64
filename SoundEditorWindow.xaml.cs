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
        private MemoryStream? _activeStream;

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
                                     .OrderBy(f => f);

                foreach (var file in files)
                {
                    string filename = Path.GetFileName(file);
                    string displayName = Randm64.Services.SoundDetailsHelper.FormatDisplayName(category, filename);
                    SampleListBox.Items.Add(new AudioSampleItem
                    {
                        Name = displayName,
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

                string backupPath = item.FilePath + ".orig";
                if (File.Exists(backupPath))
                {
                    RevertButton.IsEnabled = true;
                    RevertButton.ToolTip = "Restore the original audio sample from the local backup.";
                }
                else
                {
                    RevertButton.IsEnabled = false;
                    RevertButton.ToolTip = "No local backup found. Revert is only available for samples replaced using version 1.4+ of this tool.";
                }
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

                byte[] aiffBytes = AiffWavTranscoder.SafeReadAllBytes(filePath);
                byte[] wavBytes = AiffWavTranscoder.ConvertAiffToWav(aiffBytes);

                if (wavBytes.Length == 0)
                {
                    StatusTextBlock.Text = "Error: Failed to transcode AIFF to WAV format.";
                    return;
                }

                _activeStream = new MemoryStream(wavBytes);
                _activePlayer = new System.Media.SoundPlayer(_activeStream);
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
                _activeStream?.Dispose();
                _activeStream = null;
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

                        // Create backup before writing if not already backed up
                        string backupPath = item.FilePath + ".orig";
                        if (!File.Exists(backupPath))
                        {
                            File.Copy(item.FilePath, backupPath, true);
                        }

                        // Overwrite original file
                        File.WriteAllBytes(item.FilePath, aiffBytes);
                        
                        RevertButton.IsEnabled = true;
                        RevertButton.ToolTip = "Restore the original audio sample from the local backup.";
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

        private void RevertButton_Click(object sender, RoutedEventArgs e)
        {
            if (SampleListBox == null || SampleListBox.SelectedItem == null) return;

            if (SampleListBox.SelectedItem is AudioSampleItem item)
            {
                string backupPath = item.FilePath + ".orig";
                if (!File.Exists(backupPath))
                {
                    MessageBox.Show("No backup file found for this sample. It might already be original.", "Revert Unavailable", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                try
                {
                    StatusTextBlock.Text = "Restoring original sound sample...";
                    
                    // Overwrite customized file with backup
                    File.Copy(backupPath, item.FilePath, true);
                    
                    // Delete backup
                    File.Delete(backupPath);
                    
                    RevertButton.IsEnabled = false;
                    RevertButton.ToolTip = "No local backup found. Revert is only available for samples replaced using version 1.4+ of this tool.";
                    MessageBox.Show($"Successfully reverted {item.Name} to the original sound sample!", "Revert Successful", MessageBoxButton.OK, MessageBoxImage.Information);
                    StatusTextBlock.Text = $"Reverted: {item.Name}";
                    
                    // Play restored sample
                    PlaySample(item.FilePath);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error reverting sample: {ex.Message}", "File Write Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    StatusTextBlock.Text = $"Revert error: {ex.Message}";
                }
            }
        }
    }
}
