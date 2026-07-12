using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace Sm64DecompLevelViewer
{
    public partial class TextureSelectionWindow : Window
    {
        public class TextureSelectItem
        {
            public string RelativePath { get; set; } = string.Empty;
            public string AbsolutePath { get; set; } = string.Empty;
            public string FileName { get; set; } = string.Empty;
            public string SizeString { get; set; } = string.Empty;
            public bool IsChecked { get; set; }
        }

        private readonly string _projectRoot;
        private readonly List<TextureSelectItem> _allItems = new();
        private List<TextureSelectItem> _filteredItems = new();
        
        public List<string> SelectedRelativePaths { get; private set; } = new();

        public TextureSelectionWindow(string projectRoot, List<string> previouslySelected)
        {
            InitializeComponent();
            _projectRoot = projectRoot;

            var previousSet = new HashSet<string>(previouslySelected ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            ScanTextures(previousSet);
            ApplyFilter();
        }

        private void ScanTextures(HashSet<string> previousSet)
        {
            string texDir = Path.Combine(_projectRoot, "textures");
            string actorsDir = Path.Combine(_projectRoot, "actors");

            try
            {
                var filesList = new List<string>();
                if (Directory.Exists(texDir))
                {
                    filesList.AddRange(Directory.GetFiles(texDir, "*.png", SearchOption.AllDirectories));
                }
                if (Directory.Exists(actorsDir))
                {
                    filesList.AddRange(Directory.GetFiles(actorsDir, "*.png", SearchOption.AllDirectories));
                }

                var sortedFiles = filesList.OrderBy(f => Path.GetFileName(f)).ToList();

                foreach (var file in sortedFiles)
                {
                    string relPath = Path.GetRelativePath(_projectRoot, file).Replace("\\", "/");
                    string fileName = Path.GetFileName(file);
                    
                    bool isChecked = previousSet.Contains(relPath);

                    _allItems.Add(new TextureSelectItem
                    {
                        RelativePath = relPath,
                        AbsolutePath = file,
                        FileName = fileName,
                        IsChecked = isChecked
                    });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error scanning textures: {ex.Message}", "Scan Failure", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ApplyFilter()
        {
            string query = SearchTextBox.Text.Trim().ToLower();
            if (string.IsNullOrEmpty(query))
            {
                _filteredItems = _allItems.ToList();
            }
            else
            {
                _filteredItems = _allItems
                    .Where(item => item.FileName.ToLower().Contains(query) || item.RelativePath.ToLower().Contains(query))
                    .ToList();
            }

            TexturesListBox.ItemsSource = null;
            TexturesListBox.ItemsSource = _filteredItems;

            UpdateCount();
        }

        private void UpdateCount()
        {
            int checkedCount = _allItems.Count(i => i.IsChecked);
            SelectedCountTextBlock.Text = $"Selected: {checkedCount} / {_allItems.Count} textures";
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilter();
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _filteredItems)
            {
                item.IsChecked = true;
            }
            TexturesListBox.ItemsSource = null;
            TexturesListBox.ItemsSource = _filteredItems;
            UpdateCount();
        }

        private void DeselectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _filteredItems)
            {
                item.IsChecked = false;
            }
            TexturesListBox.ItemsSource = null;
            TexturesListBox.ItemsSource = _filteredItems;
            UpdateCount();
        }

        private bool IsValidPng(string path)
        {
            try
            {
                if (!File.Exists(path)) return false;

                var fileInfo = new FileInfo(path);
                if (fileInfo.Length < 8) return false;

                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    byte[] header = new byte[8];
                    int bytesRead = stream.Read(header, 0, 8);
                    if (bytesRead < 8) return false;

                    return header[0] == 0x89 &&
                           header[1] == 0x50 && // P
                           header[2] == 0x4E && // N
                           header[3] == 0x47 && // G
                           header[4] == 0x0D && // \r
                           header[5] == 0x0A && // \n
                           header[6] == 0x1A &&
                           header[7] == 0x0A;   // \n
                }
            }
            catch
            {
                return false;
            }
        }

        private void TexturesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TexturesListBox.SelectedItem is TextureSelectItem selected)
            {
                try
                {
                    if (File.Exists(selected.AbsolutePath) && IsValidPng(selected.AbsolutePath))
                    {
                        var bitmap = new BitmapImage();
                        using (var stream = new FileStream(selected.AbsolutePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                        {
                            bitmap.BeginInit();
                            bitmap.CacheOption = BitmapCacheOption.OnLoad;
                            bitmap.StreamSource = stream;
                            bitmap.EndInit();
                        }
                        bitmap.Freeze();

                        PreviewImage.Source = bitmap;
                        NoPreviewText.Visibility = Visibility.Collapsed;
                        PreviewImage.Visibility = Visibility.Visible;

                        MetadataFileName.Text = selected.FileName;
                        MetadataDimensions.Text = $"{bitmap.PixelWidth} x {bitmap.PixelHeight} px";
                        
                        string subfolder = Path.GetDirectoryName(selected.RelativePath) ?? string.Empty;
                        MetadataSubfolder.Text = subfolder.Replace("\\", "/");
                    }
                    else
                    {
                        ClearPreview();
                        MetadataFileName.Text = selected.FileName;
                        MetadataDimensions.Text = "Not a valid PNG (Symlink or placeholder)";
                        string subfolder = Path.GetDirectoryName(selected.RelativePath) ?? string.Empty;
                        MetadataSubfolder.Text = subfolder.Replace("\\", "/");
                    }
                }
                catch
                {
                    ClearPreview();
                }
            }
            else
            {
                ClearPreview();
            }
        }

        private void ClearPreview()
        {
            PreviewImage.Source = null;
            PreviewImage.Visibility = Visibility.Collapsed;
            NoPreviewText.Visibility = Visibility.Visible;
            MetadataFileName.Text = "-";
            MetadataDimensions.Text = "-";
            MetadataSubfolder.Text = "-";
        }

        private void CheckBox_Changed(object sender, RoutedEventArgs e)
        {
            UpdateCount();
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            SelectedRelativePaths = _allItems
                .Where(item => item.IsChecked)
                .Select(item => item.RelativePath)
                .ToList();

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
