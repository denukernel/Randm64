using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Sm64DecompLevelViewer.Services;

namespace Sm64DecompLevelViewer
{
    public partial class SoundReplacerWindow : Window
    {
        private readonly string _projectRoot;
        private readonly List<SfxItemInfo> _allSounds = new();
        private System.Media.SoundPlayer? _activePlayer;
        private MemoryStream? _activeStream;
        public List<SoundReplacementRule> ActiveRules { get; set; } = new();

        public class SoundReplacementRule
        {
            public string TargetName { get; set; }
            public string TargetPath { get; set; }
            public string ReplacementName { get; set; }
            public string ReplacementPath { get; set; }
        }

        private class SfxItemInfo
        {
            public string DisplayName { get; set; }
            public string FilePath { get; set; }
            public string Category { get; set; }
        }

        public SoundReplacerWindow(string projectRoot, List<SoundReplacementRule> existingRules)
        {
            InitializeComponent();
            _projectRoot = projectRoot;
            ActiveRules = new List<SoundReplacementRule>(existingRules);
            
            this.Closing += (s, e) => StopPlayback();

            LoadSounds();
            RefreshRulesList();
        }

        private void LoadSounds()
        {
            try
            {
                string sampleDir = Path.Combine(_projectRoot, "sound", "samples");
                if (!Directory.Exists(sampleDir))
                {
                    MessageBox.Show($"Sound samples directory not found at: {sampleDir}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var files = Directory.GetFiles(sampleDir, "*.aiff", SearchOption.AllDirectories);
                HashSet<string> categories = new HashSet<string>();

                foreach (var file in files)
                {
                    string name = Path.GetFileName(file);
                    
                    // Extract relative category name
                    string relativePath = file.Substring(sampleDir.Length).TrimStart(Path.DirectorySeparatorChar);
                    string relativeDir = Path.GetDirectoryName(relativePath);
                    string category = string.IsNullOrEmpty(relativeDir) ? "samples" : relativeDir.Replace('\\', '/');

                    string formattedName = Randm64.Services.SoundDetailsHelper.FormatDisplayName(category, name);
                    string displayName = $"[{category}] {formattedName}";

                    _allSounds.Add(new SfxItemInfo
                    {
                        DisplayName = displayName,
                        FilePath = file,
                        Category = category
                    });

                    categories.Add(category);
                }

                // Populate category target ComboBox
                TargetCategoryComboBox.Items.Clear();
                TargetCategoryComboBox.Items.Add(new ComboBoxItem { Content = "[All Sounds]" });
                TargetCategoryComboBox.Items.Add(new ComboBoxItem { Content = "[All SFX]" });
                TargetCategoryComboBox.Items.Add(new ComboBoxItem { Content = "[All Instruments]" });

                // Populate category replacement ComboBox
                ReplacementCategoryComboBox.Items.Clear();
                ReplacementCategoryComboBox.Items.Add(new ComboBoxItem { Content = "[All Sounds]" });
                ReplacementCategoryComboBox.Items.Add(new ComboBoxItem { Content = "[All SFX]" });
                ReplacementCategoryComboBox.Items.Add(new ComboBoxItem { Content = "[All Instruments]" });

                foreach (var cat in categories.OrderBy(c => c))
                {
                    TargetCategoryComboBox.Items.Add(new ComboBoxItem { Content = cat });
                    ReplacementCategoryComboBox.Items.Add(new ComboBoxItem { Content = cat });
                }

                TargetCategoryComboBox.SelectedIndex = 0;
                ReplacementCategoryComboBox.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading sound files: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RefreshRulesList()
        {
            RulesListBox.Items.Clear();
            foreach (var rule in ActiveRules)
            {
                RulesListBox.Items.Add(rule);
            }
        }

        private void FilterTargetList()
        {
            if (TargetListBox == null || TargetCategoryComboBox == null) return;

            string selectedCat = (TargetCategoryComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "[All Sounds]";
            string targetSearch = TargetSearchTextBox.Text.Trim();

            // Store current selected targets so they don't clear unexpectedly
            var selectedTargets = new HashSet<string>(TargetListBox.SelectedItems.Cast<ListBoxItem>().Select(i => i.Tag.ToString()));

            TargetListBox.Items.Clear();

            foreach (var item in _allSounds)
            {
                bool matchesCategory = false;
                if (selectedCat == "[All Sounds]")
                {
                    matchesCategory = true;
                }
                else if (selectedCat == "[All SFX]")
                {
                    matchesCategory = item.Category.StartsWith("sfx", StringComparison.OrdinalIgnoreCase);
                }
                else if (selectedCat == "[All Instruments]")
                {
                    matchesCategory = !item.Category.StartsWith("sfx", StringComparison.OrdinalIgnoreCase);
                }
                else
                {
                    matchesCategory = (item.Category == selectedCat);
                }

                if (!matchesCategory) continue;

                // Apply text searches
                bool matchesTargetSearch = string.IsNullOrEmpty(targetSearch) || item.DisplayName.Contains(targetSearch, StringComparison.OrdinalIgnoreCase);

                if (matchesTargetSearch)
                {
                    var lItem = new ListBoxItem { Content = item.DisplayName, Tag = item.FilePath };
                    if (selectedTargets.Contains(item.FilePath))
                    {
                        lItem.IsSelected = true;
                    }
                    TargetListBox.Items.Add(lItem);
                }
            }
        }

        private void FilterReplacementList()
        {
            if (ReplacementListBox == null || ReplacementCategoryComboBox == null) return;

            string selectedCat = (ReplacementCategoryComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "[All Sounds]";
            string replacementSearch = ReplacementSearchTextBox.Text.Trim();

            string selectedReplacement = (ReplacementListBox.SelectedItem as ListBoxItem)?.Tag?.ToString();

            ReplacementListBox.Items.Clear();

            foreach (var item in _allSounds)
            {
                bool matchesCategory = false;
                if (selectedCat == "[All Sounds]")
                {
                    matchesCategory = true;
                }
                else if (selectedCat == "[All SFX]")
                {
                    matchesCategory = item.Category.StartsWith("sfx", StringComparison.OrdinalIgnoreCase);
                }
                else if (selectedCat == "[All Instruments]")
                {
                    matchesCategory = !item.Category.StartsWith("sfx", StringComparison.OrdinalIgnoreCase);
                }
                else
                {
                    matchesCategory = (item.Category == selectedCat);
                }

                if (!matchesCategory) continue;

                // Apply text searches
                bool matchesReplacementSearch = string.IsNullOrEmpty(replacementSearch) || item.DisplayName.Contains(replacementSearch, StringComparison.OrdinalIgnoreCase);

                if (matchesReplacementSearch)
                {
                    var rItem = new ListBoxItem { Content = item.DisplayName, Tag = item.FilePath };
                    if (item.FilePath == selectedReplacement)
                    {
                        rItem.IsSelected = true;
                    }
                    ReplacementListBox.Items.Add(rItem);
                }
            }
        }

        private void TargetCategoryComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            FilterTargetList();
        }

        private void ReplacementCategoryComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            FilterReplacementList();
        }

        private void TargetSearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            FilterTargetList();
        }

        private void ReplacementSearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            FilterReplacementList();
        }

        private void TargetListBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.A && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                TargetListBox.SelectAll();
                e.Handled = true;
            }
        }

        private void PlayTarget_Click(object sender, RoutedEventArgs e)
        {
            if (TargetListBox.SelectedItems.Count != 1)
            {
                MessageBox.Show("Please select exactly one target sound to play.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var selectedItem = TargetListBox.SelectedItem as ListBoxItem;
            if (selectedItem != null && selectedItem.Tag is string path)
            {
                PlaySample(path);
            }
            else
            {
                MessageBox.Show("Please select a target sound to play first.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void StopTarget_Click(object sender, RoutedEventArgs e)
        {
            StopPlayback();
        }

        private void PlayReplacement_Click(object sender, RoutedEventArgs e)
        {
            if (ReplacementListBox.SelectedItems.Count != 1)
            {
                MessageBox.Show("Please select exactly one replacement sound to play.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var selectedItem = ReplacementListBox.SelectedItem as ListBoxItem;
            if (selectedItem != null && selectedItem.Tag is string path)
            {
                PlaySample(path);
            }
            else
            {
                MessageBox.Show("Please select a replacement sound to play first.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void StopReplacement_Click(object sender, RoutedEventArgs e)
        {
            StopPlayback();
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

        private void AddRuleButton_Click(object sender, RoutedEventArgs e)
        {
            if (TargetListBox.SelectedItems.Count == 0)
            {
                MessageBox.Show("Please select at least one target sound to replace.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var replacementItem = ReplacementListBox.SelectedItem as ListBoxItem;
            if (replacementItem == null)
            {
                MessageBox.Show("Please select a single replacement sound (X) from the right column.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string replacementName = replacementItem.Content.ToString();
            string replacementPath = replacementItem.Tag.ToString();

            int addedCount = 0;
            foreach (ListBoxItem targetItem in TargetListBox.SelectedItems)
            {
                string targetName = targetItem.Content.ToString();
                string targetPath = targetItem.Tag.ToString();

                // Remove rule if it already exists for this target
                ActiveRules.RemoveAll(r => r.TargetPath == targetPath);

                // Do not allow replacing a sound with itself
                if (targetPath == replacementPath) continue;

                ActiveRules.Add(new SoundReplacementRule
                {
                    TargetName = targetName,
                    TargetPath = targetPath,
                    ReplacementName = replacementName,
                    ReplacementPath = replacementPath
                });
                addedCount++;
            }

            RefreshRulesList();
            MessageBox.Show($"Added {addedCount} replacement rules successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void RemoveRuleButton_Click(object sender, RoutedEventArgs e)
        {
            if (RulesListBox.SelectedItems.Count == 0)
            {
                MessageBox.Show("Please select active rules to remove.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var selectedRules = RulesListBox.SelectedItems.Cast<SoundReplacementRule>().ToList();
            foreach (var rule in selectedRules)
            {
                ActiveRules.Remove(rule);
            }

            RefreshRulesList();
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
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
