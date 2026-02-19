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
    public partial class ModelSelectionWindow : Window
    {
        private List<string> _allModels = new List<string>();
        public string? SelectedModel { get; private set; }

        public ModelSelectionWindow(string projectRoot)
        {
            InitializeComponent();
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
            if (string.IsNullOrWhiteSpace(search))
            {
                ModelListBox.ItemsSource = _allModels;
            }
            else
            {
                ModelListBox.ItemsSource = _allModels
                    .Where(m => m.Contains(search, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            FilterModels(SearchTextBox.Text);
        }

        private void SelectButton_Click(object sender, RoutedEventArgs e)
        {
            if (ModelListBox.SelectedItem != null)
            {
                SelectedModel = ModelListBox.SelectedItem.ToString();
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
