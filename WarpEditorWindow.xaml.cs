using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using Sm64DecompLevelViewer.Services;

namespace Sm64DecompLevelViewer
{
    public partial class WarpEditorWindow : Window
    {
        public class WarpNodeViewModel
        {
            public string MacroType { get; set; } = "WARP_NODE";
            public string Id { get; set; } = "WARP_NODE_00";
            public string DestLevel { get; set; } = "LEVEL_CASTLE";
            public string DestArea { get; set; } = "1";
            public string DestNode { get; set; } = "WARP_NODE_00";
            public string Flags { get; set; } = "WARP_NO_CHECKPOINT";
            public int LineIndex { get; set; } = -1;
        }

        private static readonly List<string> MacroTypes = new() { "WARP_NODE", "PAINTING_WARP_NODE" };

        private static readonly List<string> SM64Levels = new()
        {
            "LEVEL_NONE", "LEVEL_BBH", "LEVEL_CCM", "LEVEL_CASTLE", "LEVEL_HMC",
            "LEVEL_SSL", "LEVEL_BOB", "LEVEL_SL", "LEVEL_WDW", "LEVEL_JRB",
            "LEVEL_THI", "LEVEL_TTC", "LEVEL_RR", "LEVEL_CASTLE_GROUNDS",
            "LEVEL_BITDW", "LEVEL_VCUTM", "LEVEL_BITFS", "LEVEL_SA",
            "LEVEL_BITS", "LEVEL_LLL", "LEVEL_DDD", "LEVEL_WF",
            "LEVEL_BOWSER_1", "LEVEL_BOWSER_2", "LEVEL_BOWSER_3", "LEVEL_COTMC",
            "LEVEL_MC", "LEVEL_WC", "LEVEL_PSS", "LEVEL_TOTWC", "LEVEL_WMETR",
            "LEVEL_CASTLE_COURTYARD"
        };

        private static readonly List<string> WarpFlags = new() { "WARP_NO_CHECKPOINT", "WARP_CHECKPOINT" };

        private readonly string _scriptPath;
        private readonly WarpService _warpService = new();
        private readonly Action _reloadCallback;

        public ObservableCollection<WarpNodeViewModel> Warps { get; } = new();

        public WarpEditorWindow(string levelPath, Action reloadCallback)
        {
            InitializeComponent();

            _scriptPath = Path.Combine(levelPath, "script.c");
            _reloadCallback = reloadCallback;

            // Bind column sources
            MacroTypeColumn.ItemsSource = MacroTypes;
            DestLevelColumn.ItemsSource = SM64Levels;
            FlagsColumn.ItemsSource = WarpFlags;

            WarpGrid.ItemsSource = Warps;

            LoadWarps();
        }

        private void LoadWarps()
        {
            if (!File.Exists(_scriptPath))
            {
                MessageBox.Show($"Level script file not found at: {_scriptPath}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
                return;
            }

            var parsedNodes = _warpService.LoadWarps(_scriptPath);
            Warps.Clear();
            foreach (var node in parsedNodes)
            {
                Warps.Add(new WarpNodeViewModel
                {
                    MacroType = node.MacroType,
                    Id = node.Id,
                    DestLevel = node.DestLevel,
                    DestArea = node.DestArea,
                    DestNode = node.DestNode,
                    Flags = node.Flags,
                    LineIndex = node.LineIndex
                });
            }
            StatusBarText.Text = $"Loaded {Warps.Count} warp nodes from level script.";
        }

        private void AddWarp_Click(object sender, RoutedEventArgs e)
        {
            // Guess a next ID name (e.g. WARP_NODE_0B)
            string nextId = "WARP_NODE_0A";
            if (Warps.Count > 0)
            {
                var lastId = Warps.Last().Id;
                if (lastId.StartsWith("WARP_NODE_") && lastId.Length == 12)
                {
                    string hexStr = lastId.Substring(10);
                    if (int.TryParse(hexStr, System.Globalization.NumberStyles.HexNumber, null, out int currentVal))
                    {
                        nextId = $"WARP_NODE_{currentVal + 1:X2}";
                    }
                }
            }

            var newWarp = new WarpNodeViewModel
            {
                MacroType = "WARP_NODE",
                Id = nextId,
                DestLevel = "LEVEL_CASTLE",
                DestArea = "1",
                DestNode = nextId,
                Flags = "WARP_NO_CHECKPOINT",
                LineIndex = -1 // Indicates new node
            };

            Warps.Add(newWarp);
            WarpGrid.SelectedItem = newWarp;
            WarpGrid.ScrollIntoView(newWarp);
            StatusBarText.Text = $"Added new warp node {newWarp.Id}.";
        }

        private void DeleteWarp_Click(object sender, RoutedEventArgs e)
        {
            if (WarpGrid.SelectedItem is WarpNodeViewModel selected)
            {
                // Note: if it's an existing node, we will replace its line in script with an empty string or comment
                // Let's implement deletion in WarpService
                Warps.Remove(selected);
                StatusBarText.Text = $"Deleted warp node {selected.Id}.";
            }
            else
            {
                MessageBox.Show("Please select a warp node to delete.", "Delete Warp", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            var nodes = Warps.Select(w => new WarpNode
            {
                MacroType = w.MacroType,
                Id = w.Id,
                DestLevel = w.DestLevel,
                DestArea = w.DestArea,
                DestNode = w.DestNode,
                Flags = w.Flags,
                LineIndex = w.LineIndex
            }).ToList();

            // Support deletion by tracking missing line indexes
            if (File.Exists(_scriptPath))
            {
                try
                {
                    var lines = new List<string>(File.ReadAllLines(_scriptPath));
                    var parsedNodes = _warpService.LoadWarps(_scriptPath);

                    // Find any line index that was in parsedNodes but is no longer in nodes list (indicating deletion)
                    var deletedLineIndices = parsedNodes.Select(p => p.LineIndex).Except(nodes.Where(n => n.LineIndex >= 0).Select(n => n.LineIndex)).ToList();

                    // Comment out deleted warp node lines
                    foreach (int index in deletedLineIndices)
                    {
                        if (index >= 0 && index < lines.Count)
                        {
                            lines[index] = $"    // {lines[index].Trim()} [Deleted]";
                        }
                    }

                    // Save the remaining modifications and new additions
                    var existingNodes = nodes.Where(n => n.LineIndex >= 0 && n.LineIndex < lines.Count).OrderBy(n => n.LineIndex).ToList();
                    var newNodes = nodes.Where(n => n.LineIndex < 0).ToList();

                    // Insert new nodes after the last existing warp node
                    int insertIndex = -1;
                    if (existingNodes.Count > 0)
                    {
                        insertIndex = existingNodes.Max(n => n.LineIndex) + 1;
                    }
                    else
                    {
                        insertIndex = lines.FindIndex(l => l.Contains("WARP_NODE") || l.Contains("OBJECT("));
                        if (insertIndex == -1) insertIndex = lines.Count - 1;
                    }

                    // Update existing lines
                    foreach (var node in existingNodes)
                    {
                        string spaces = lines[node.LineIndex].Substring(0, lines[node.LineIndex].Length - lines[node.LineIndex].TrimStart().Length);
                        if (string.IsNullOrEmpty(spaces)) spaces = "    ";
                        lines[node.LineIndex] = $"{spaces}{node.MacroType}({node.Id}, {node.DestLevel}, {node.DestArea}, {node.DestNode}, {node.Flags}),";
                    }

                    // Insert new nodes
                    int addedOffset = 0;
                    foreach (var node in newNodes)
                    {
                        string newLine = $"    {node.MacroType}({node.Id}, {node.DestLevel}, {node.DestArea}, {node.DestNode}, {node.Flags}),";
                        lines.Insert(insertIndex + addedOffset, newLine);
                        addedOffset++;
                    }

                    // Write with Unix line endings
                    string outputContent = string.Join("\n", lines) + "\n";
                    File.WriteAllText(_scriptPath, outputContent);

                    MessageBox.Show("Warp script changes saved successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    LoadWarps(); // Reload view models with new line indexes
                    _reloadCallback?.Invoke();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to save changes: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
