using Sm64DecompLevelViewer.Services.Rom;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using Sm64DecompLevelViewer.Services;

namespace Sm64DecompLevelViewer
{
    public partial class RomAddressWindow : Window
    {

        private readonly List<RomMapEntry> _defaultEntries;
        private List<RomMapEntry> _displayEntries = new();
        private List<RomMapEntry> _filteredEntries = new();

        private readonly RomMapDocument _map = new();
        private Dictionary<string, RomMapDocument.SegmentInfo> _segments => _map.Segments;
        private Dictionary<string, ulong> _symbols => _map.Symbols;
        private string? _projectRoot;

        public RomAddressWindow(string? projectRootPath)
        {
            InitializeComponent();
            _projectRoot = projectRootPath;

            _defaultEntries = RomReferenceCatalog.Create<RomMapEntry>();

            LoadDefaultMap();
            AutoLoadProjectMap();
        }

        private void LoadDefaultMap()
        {
            _displayEntries = new List<RomMapEntry>(_defaultEntries);
            ApplyFilter();
            ActiveMapLabel.Text = "Reference only: original US ROM; load a compiled map for this build";
        }

        private void AutoLoadProjectMap()
        {
            if (!string.IsNullOrEmpty(_projectRoot) && Directory.Exists(_projectRoot))
            {
                // Look for maps in build folder
                string buildDir = Path.Combine(_projectRoot, "build");
                if (Directory.Exists(buildDir))
                {
                    try
                    {
                        var mapFiles = Directory.GetFiles(buildDir, "*.map", SearchOption.AllDirectories);
                        if (mapFiles.Length > 0)
                        {
                            // Load first found map file
                            string mapPath = mapFiles.OrderByDescending(File.GetLastWriteTimeUtc).ThenBy(p => p, StringComparer.OrdinalIgnoreCase).First();
                            LoadMapFile(mapPath);
                        }
                    }
                    catch (Exception ex) { ActiveMapLabel.Text = "Map load failed: " + ex.Message; }
                }
            }
        }

        private void LoadMapFile(string mapPath)
        {
            try
            {
                _map.ParseMapFile(mapPath);
                if (_symbols.Count == 0 && _segments.Count == 0) throw new InvalidDataException("no recognized linker symbols or segments");
                RebuildDisplayEntries();
                ActiveMapLabel.Text = $"Compiled map: {mapPath}; unresolved and compressed symbol offsets are omitted";
            }
            catch (Exception ex) { _segments.Clear(); _symbols.Clear(); LoadDefaultMap(); ActiveMapLabel.Text = "Map rejected: " + ex.Message + "; showing original us reference only"; }
        }

        private void ApplyFilter()
        {
            string filter = SearchBox.Text.Trim().ToLower();

            if (string.IsNullOrEmpty(filter))
            {
                _filteredEntries = new List<RomMapEntry>(_displayEntries);
            }
            else
            {
                _filteredEntries = _displayEntries.Where(entry =>
                    entry.Start.ToLower().Contains(filter) ||
                    entry.End.ToLower().Contains(filter) ||
                    entry.Type.ToLower().Contains(filter) ||
                    entry.Description.ToLower().Contains(filter)
                ).ToList();
            }

            AddressGrid.ItemsSource = _filteredEntries;
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilter();
        }

        private void ClearSearch_Click(object sender, RoutedEventArgs e)
        {
            SearchBox.Text = string.Empty;
        }

        private void ShowAllSymbolsCheck_Changed(object sender, RoutedEventArgs e)
        {
            if (_symbols.Count > 0)
            {
                RebuildDisplayEntries();
            }
            else
            {
                MessageBox.Show("Please load a compiled .map file first to view all custom symbols.", "Map File Required", MessageBoxButton.OK, MessageBoxImage.Information);
                ShowAllSymbolsCheck.IsChecked = false;
            }
        }

        private void LoadMap_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select Compiled Linker Map File (*.map)",
                Filter = "Linker Map Files (*.map)|*.map|All Files (*.*)|*.*",
                InitialDirectory = _projectRoot ?? AppDomain.CurrentDomain.BaseDirectory
            };

            if (dialog.ShowDialog() == true)
            {
                LoadMapFile(dialog.FileName);
            }
        }

        private void AddressGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (AddressGrid.SelectedItem is RomMapEntry entry)
            {
                CopyTextToClipboard(entry.Start, "Start Address");
            }
        }

        private void CopyStart_Click(object sender, RoutedEventArgs e)
        {
            if (AddressGrid.SelectedItem is RomMapEntry entry)
            {
                CopyTextToClipboard(entry.Start, "Start Address");
            }
            else
            {
                MessageBox.Show("Please select an entry from the list first.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void CopyEnd_Click(object sender, RoutedEventArgs e)
        {
            if (AddressGrid.SelectedItem is RomMapEntry entry)
            {
                CopyTextToClipboard(entry.End, "End Address");
            }
            else
            {
                MessageBox.Show("Please select an entry from the list first.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void CopyFull_Click(object sender, RoutedEventArgs e)
        {
            if (AddressGrid.SelectedItem is RomMapEntry entry)
            {
                string text = $"{entry.Start}\t{entry.End}\t{entry.Type}\t{entry.Description}";
                CopyTextToClipboard(text, "Full Entry");
            }
            else
            {
                MessageBox.Show("Please select an entry from the list first.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void CopyTextToClipboard(string text, string label)
        {
            try
            {
                Clipboard.SetText(text);
                // Mini feedback on status bar, or simple silent copy to avoid annoying popup, or messagebox
                // Let's use a non-blocking dialog or messagebox
                MessageBox.Show($"{label} copied to clipboard: {text}", "Copied", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to copy to clipboard: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
        public class RomMapEntry : Models.Rom.RomMapEntry { }
        private void RebuildDisplayEntries()
        {
            _displayEntries = _map.BuildEntries(_defaultEntries, ShowAllSymbolsCheck.IsChecked == true);
            ApplyFilter();
        }
        private ulong? TranslateRamToRom(ulong ramAddr) => _map.TranslateRamToRom(ramAddr);
    }
}
