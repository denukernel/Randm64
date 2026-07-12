using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace Sm64DecompLevelViewer
{
    public partial class TextureReplacerWindow : Window
    {
        private readonly string _projectRoot;
        private readonly List<TextureItemInfo> _allTextures = new();
        private string? _customPngPath;
        public List<TextureReplacementRule> ActiveRules { get; set; } = new();

        public class TextureReplacementRule
        {
            public string TargetName { get; set; } = string.Empty;
            public string TargetPath { get; set; } = string.Empty;
            public string ReplacementName { get; set; } = string.Empty;
            public string ReplacementPath { get; set; } = string.Empty;
        }

        private class TextureItemInfo
        {
            public string DisplayName { get; set; } = string.Empty;
            public string FilePath { get; set; } = string.Empty;
            public string Category { get; set; } = string.Empty;
        }

        public TextureReplacerWindow(string projectRoot, List<TextureReplacementRule> existingRules)
        {
            InitializeComponent();
            _projectRoot = projectRoot;
            ActiveRules = new List<TextureReplacementRule>(existingRules);

            LoadTextures();
            RefreshRulesList();
        }

        private void LoadTextures()
        {
            try
            {
                string texDir = Path.Combine(_projectRoot, "textures");
                string actorsDir = Path.Combine(_projectRoot, "actors");

                var fileList = new List<string>();
                if (Directory.Exists(texDir))
                {
                    fileList.AddRange(Directory.GetFiles(texDir, "*.png", SearchOption.AllDirectories));
                }
                if (Directory.Exists(actorsDir))
                {
                    fileList.AddRange(Directory.GetFiles(actorsDir, "*.png", SearchOption.AllDirectories));
                }

                HashSet<string> categories = new HashSet<string>();

                foreach (var file in fileList)
                {
                    string name = Path.GetFileName(file);
                    
                    string category = "";
                    if (file.StartsWith(texDir, StringComparison.OrdinalIgnoreCase))
                    {
                        string relativePath = file.Substring(texDir.Length).TrimStart(Path.DirectorySeparatorChar);
                        string relativeDir = Path.GetDirectoryName(relativePath) ?? string.Empty;
                        category = string.IsNullOrEmpty(relativeDir) ? "textures" : "textures/" + relativeDir.Replace('\\', '/');
                    }
                    else if (file.StartsWith(actorsDir, StringComparison.OrdinalIgnoreCase))
                    {
                        string relativePath = file.Substring(actorsDir.Length).TrimStart(Path.DirectorySeparatorChar);
                        string relativeDir = Path.GetDirectoryName(relativePath) ?? string.Empty;
                        category = string.IsNullOrEmpty(relativeDir) ? "actors" : "actors/" + relativeDir.Replace('\\', '/');
                    }

                    string displayName = $"[{category}] {name}";

                    _allTextures.Add(new TextureItemInfo
                    {
                        DisplayName = displayName,
                        FilePath = file,
                        Category = category
                    });

                    categories.Add(category);
                }

                // Populate categories ComboBoxes
                TargetCategoryComboBox.Items.Clear();
                TargetCategoryComboBox.Items.Add(new ComboBoxItem { Content = "[All Textures]" });
                TargetCategoryComboBox.Items.Add(new ComboBoxItem { Content = "[All actors]" });
                TargetCategoryComboBox.Items.Add(new ComboBoxItem { Content = "[All textures/]" });

                ReplacementCategoryComboBox.Items.Clear();
                ReplacementCategoryComboBox.Items.Add(new ComboBoxItem { Content = "[All Textures]" });
                ReplacementCategoryComboBox.Items.Add(new ComboBoxItem { Content = "[All actors]" });
                ReplacementCategoryComboBox.Items.Add(new ComboBoxItem { Content = "[All textures/]" });

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
                MessageBox.Show($"Error loading texture files: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RefreshRulesList()
        {
            RulesListBox.Items.Clear();
            foreach (var rule in ActiveRules)
            {
                RulesListBox.Items.Add(rule);
            }
            StatusLabel.Text = $"Active rules count: {ActiveRules.Count}";
        }

        private void FilterTargetList()
        {
            if (TargetListBox == null || TargetCategoryComboBox == null) return;

            string selectedCat = (TargetCategoryComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "[All Textures]";
            string targetSearch = TargetSearchTextBox.Text.Trim();

            var selectedTargets = new HashSet<string>(TargetListBox.SelectedItems.Cast<ListBoxItem>().Select(i => i.Tag.ToString() ?? string.Empty));

            TargetListBox.Items.Clear();

            foreach (var item in _allTextures)
            {
                bool matchesCategory = false;
                if (selectedCat == "[All Textures]")
                {
                    matchesCategory = true;
                }
                else if (selectedCat == "[All actors]")
                {
                    matchesCategory = item.Category.StartsWith("actors", StringComparison.OrdinalIgnoreCase);
                }
                else if (selectedCat == "[All textures/]")
                {
                    matchesCategory = item.Category.StartsWith("textures", StringComparison.OrdinalIgnoreCase);
                }
                else
                {
                    matchesCategory = (item.Category == selectedCat);
                }

                if (!matchesCategory) continue;

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

            string selectedCat = (ReplacementCategoryComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "[All Textures]";
            string replacementSearch = ReplacementSearchTextBox.Text.Trim();

            string? selectedReplacement = (ReplacementListBox.SelectedItem as ListBoxItem)?.Tag?.ToString();

            ReplacementListBox.Items.Clear();

            // Re-insert custom PNG if we browsed one and it matches the text filter
            if (_customPngPath != null && File.Exists(_customPngPath))
            {
                string name = Path.GetFileName(_customPngPath);
                string disp = $"[Custom PNG] {name}";
                if (string.IsNullOrEmpty(replacementSearch) || disp.Contains(replacementSearch, StringComparison.OrdinalIgnoreCase))
                {
                    var cItem = new ListBoxItem 
                    { 
                        Content = disp, 
                        Tag = _customPngPath,
                        FontWeight = FontWeights.Bold
                    };
                    if (_customPngPath == selectedReplacement)
                    {
                        cItem.IsSelected = true;
                    }
                    ReplacementListBox.Items.Add(cItem);
                }
            }

            foreach (var item in _allTextures)
            {
                bool matchesCategory = false;
                if (selectedCat == "[All Textures]")
                {
                    matchesCategory = true;
                }
                else if (selectedCat == "[All actors]")
                {
                    matchesCategory = item.Category.StartsWith("actors", StringComparison.OrdinalIgnoreCase);
                }
                else if (selectedCat == "[All textures/]")
                {
                    matchesCategory = item.Category.StartsWith("textures", StringComparison.OrdinalIgnoreCase);
                }
                else
                {
                    matchesCategory = (item.Category == selectedCat);
                }

                if (!matchesCategory) continue;

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

        private void TargetListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                var selected = TargetListBox.SelectedItem as ListBoxItem;
                if (selected != null && selected.Tag != null)
                {
                    string path = selected.Tag.ToString() ?? string.Empty;
                    if (File.Exists(path))
                    {
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.UriSource = new Uri(path);
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.EndInit();
                        TargetPreviewImage.Source = bitmap;
                        TargetPreviewText.Text = $"{Path.GetFileName(path)}\nSize: {bitmap.PixelWidth}x{bitmap.PixelHeight}";
                        return;
                    }
                }
                TargetPreviewImage.Source = null;
                TargetPreviewText.Text = "No Selection";
            }
            catch
            {
                TargetPreviewImage.Source = null;
                TargetPreviewText.Text = "Preview Error";
            }
        }

        private void ReplacementListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                var selected = ReplacementListBox.SelectedItem as ListBoxItem;
                if (selected != null && selected.Tag != null)
                {
                    string path = selected.Tag.ToString() ?? string.Empty;
                    if (File.Exists(path))
                    {
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.UriSource = new Uri(path);
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.EndInit();
                        ReplacementPreviewImage.Source = bitmap;
                        ReplacementPreviewText.Text = $"{Path.GetFileName(path)}\nSize: {bitmap.PixelWidth}x{bitmap.PixelHeight}";
                        return;
                    }
                }
                ReplacementPreviewImage.Source = null;
                ReplacementPreviewText.Text = "No Selection";
            }
            catch
            {
                ReplacementPreviewImage.Source = null;
                ReplacementPreviewText.Text = "Preview Error";
            }
        }

        private void BrowseCustomPng_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "PNG Image (*.png)|*.png|All files (*.*)|*.*",
                Title = "Select Custom Replacement PNG"
            };
            if (openFileDialog.ShowDialog() == true)
            {
                _customPngPath = openFileDialog.FileName;
                string name = Path.GetFileName(_customPngPath);
                
                var item = new ListBoxItem 
                { 
                    Content = $"[Custom PNG] {name}", 
                    Tag = _customPngPath,
                    FontWeight = FontWeights.Bold
                };
                
                ReplacementListBox.Items.Insert(0, item);
                item.IsSelected = true;
                ReplacementListBox.ScrollIntoView(item);
            }
        }

        private void AddRuleButton_Click(object sender, RoutedEventArgs e)
        {
            if (TargetListBox.SelectedItems.Count == 0)
            {
                MessageBox.Show("Please select one or more target textures to replace.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (ReplacementListBox.SelectedItem == null)
            {
                MessageBox.Show("Please select a replacement texture or custom PNG.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var replacementItem = (ListBoxItem)ReplacementListBox.SelectedItem;
            string repPath = replacementItem.Tag.ToString() ?? string.Empty;
            string repName = replacementItem.Content.ToString() ?? string.Empty;

            int addedCount = 0;
            foreach (ListBoxItem targetItem in TargetListBox.SelectedItems)
            {
                string targetPath = targetItem.Tag.ToString() ?? string.Empty;
                string targetName = targetItem.Content.ToString() ?? string.Empty;

                // Remove existing rule for target first to prevent duplicates
                ActiveRules.RemoveAll(r => r.TargetPath.Equals(targetPath, StringComparison.OrdinalIgnoreCase));

                ActiveRules.Add(new TextureReplacementRule
                {
                    TargetName = targetName,
                    TargetPath = targetPath,
                    ReplacementName = repName,
                    ReplacementPath = repPath
                });
                addedCount++;
            }

            RefreshRulesList();
            StatusLabel.Text = $"Successfully added {addedCount} rule(s).";
        }

        private void RemoveRuleButton_Click(object sender, RoutedEventArgs e)
        {
            if (RulesListBox.SelectedItems.Count == 0)
            {
                MessageBox.Show("Please select one or more active rules to remove.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var toRemove = RulesListBox.SelectedItems.Cast<TextureReplacementRule>().ToList();
            foreach (var rule in toRemove)
            {
                ActiveRules.Remove(rule);
            }

            RefreshRulesList();
            StatusLabel.Text = $"Removed {toRemove.Count} rule(s).";
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
