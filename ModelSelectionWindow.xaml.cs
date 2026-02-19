using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Sm64DecompLevelViewer
{
    public class ModelItem
    {
        public string Name { get; set; }
        public bool IsSupported { get; set; }
        public string Color => IsSupported ? "#4EC9B0" : "#F44747";
    }

    public partial class ModelSelectionWindow : Window
    {
        private List<string> _allModels = new List<string>();
        private List<string> _supportedModels;
        public string? SelectedModel { get; private set; }

        public ModelSelectionWindow(string projectRoot, List<string> supportedModels)
        {
            InitializeComponent();
            _supportedModels = supportedModels;
            LoadModels(projectRoot);
            FilterModels("");
        }

        private void LoadModels(string projectRoot)
        {
            try
            {
                // Common locations for model_ids.h
                var paths = new[] 
                { 
                    Path.Combine(projectRoot, "include", "model_ids.h"),
                    Path.Combine(projectRoot, "howtomake", "include", "model_ids.h"),
                    Path.Combine(projectRoot, "leveleditor", "include", "model_ids.h")
                };

                foreach (var path in paths)
                {
                    if (File.Exists(path))
                    {
                        var content = File.ReadAllText(path);
                        var matches = Regex.Matches(content, @"#define\s+(MODEL_[A-Z0-9_]+)");
                        foreach (Match m in matches)
                        {
                            string model = m.Groups[1].Value;
                            if (!_allModels.Contains(model))
                                _allModels.Add(model);
                        }
                    }
                }

                if (_allModels.Count == 0)
                {
                    // Fallback defaults if no file found
                    _allModels.Add("MODEL_NONE");
                    _allModels.Add("MODEL_MARIO");
                    _allModels.Add("MODEL_GOOMBA");
                    _allModels.Add("MODEL_BOBOMB");
                }
                
                _allModels.Sort();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading model IDs: {ex.Message}");
            }
        }

        private void FilterModels(string search)
        {
            var filtered = string.IsNullOrWhiteSpace(search)
                ? _allModels
                : _allModels.Where(m => m.Contains(search, StringComparison.OrdinalIgnoreCase));

            ModelListBox.ItemsSource = filtered.Select(m => new ModelItem
            {
                Name = m,
                IsSupported = _supportedModels.Contains(m, StringComparer.OrdinalIgnoreCase)
            }).ToList();
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            FilterModels(SearchTextBox.Text);
        }

        private void SelectButton_Click(object sender, RoutedEventArgs e)
        {
            if (ModelListBox.SelectedItem is ModelItem item)
            {
                SelectedModel = item.Name;
                DialogResult = true;
                Close();
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void ModelListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            SelectButton_Click(sender, e);
        }
    }
}
