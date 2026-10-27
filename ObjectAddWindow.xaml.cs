using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Sm64DecompLevelViewer.Models;

namespace Sm64DecompLevelViewer
{
    public partial class ObjectAddWindow : Window
    {
        public LevelObject? NewObject { get; private set; }
        private string _projectRoot;
        private List<string> _supportedModels = new();
        private Services.BehaviorCompatibility? _behaviorCompatibility;
        private CollisionMesh? _placementCollision;
        
        public ObjectAddWindow(string projectRoot, List<string> behaviors, List<string> macroPresets, List<string> specialPresets, List<string> supportedModels, int x = 0, int y = 0, int z = 0, CollisionMesh? placementCollision = null, string? levelDirectory = null)
        {
            _supportedModels = supportedModels;
            InitializeComponent();
            _projectRoot = projectRoot;
            _placementCollision = placementCollision;
            _behaviorCompatibility = new Services.BehaviorCompatibility(projectRoot, supportedModels, levelDirectory);
            BehaviorListBox.ItemsSource = behaviors.OrderBy(v => v).ToList();
            MacroListBox.ItemsSource = macroPresets.OrderBy(v => v).ToList();
            SpecialListBox.ItemsSource = specialPresets.OrderBy(v => v).ToList();
            
            PositionX.Text = Math.Clamp(x, short.MinValue, short.MaxValue).ToString();
            PositionY.Text = Math.Clamp(y, short.MinValue, short.MaxValue).ToString();
            PositionZ.Text = Math.Clamp(z, short.MinValue, short.MaxValue).ToString();
            // Default values
            NormalModelTextBox.Text = "MODEL_NONE";
            ValidateModel(NormalModelTextBox.Text);
            UpdateBehaviorSafeguard();
        }

        private void NormalModelTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ValidateModel(NormalModelTextBox.Text);
            UpdateBehaviorSafeguard();
        }

        private void BehaviorSelection_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_behaviorCompatibility != null && BehaviorListBox?.SelectedItem is string behavior &&
                _behaviorCompatibility.Rejection(behavior, NormalModelTextBox.Text.Trim()) != null)
            {
                string? suggested = _behaviorCompatibility.SuggestedModel(behavior);
                if (suggested != null) NormalModelTextBox.Text = suggested;
            }
            UpdateBehaviorSafeguard();
        }

        private void UpdateBehaviorSafeguard()
        {
            if (AddObjectButton == null || BehaviorStatus == null || _behaviorCompatibility == null) return;
            string? problem = MainTabControl.SelectedIndex <= 0
                ? BehaviorListBox.SelectedItem is string behavior
                    ? _behaviorCompatibility.Rejection(behavior, NormalModelTextBox.Text.Trim()) : "select a behavior"
                : null;
            string? warning = MainTabControl.SelectedIndex <= 0 && BehaviorListBox.SelectedItem is string selected
                ? _behaviorCompatibility.ModelWarning(selected, NormalModelTextBox.Text.Trim()) : null;
            BehaviorStatus.Text = problem ?? warning ?? "no known model or behavior conflicts";
            AddObjectButton.IsEnabled = problem == null;
        }

        private void ObjectTab_Changed(object sender, SelectionChangedEventArgs e) => UpdateBehaviorSafeguard();

        private void ValidateModel(string modelName)
        {
            if (UnsupportedModelWarning == null || BuildRequiredWarning == null) return;

            // Handle missing actors folder state
            if (_supportedModels.Contains("MISSING_ACTORS_FOLDER"))
            {
                BuildRequiredWarning.Visibility = Visibility.Visible;
                UnsupportedModelWarning.Visibility = Visibility.Collapsed;
                return;
            }

            BuildRequiredWarning.Visibility = Visibility.Collapsed;

            if (string.IsNullOrWhiteSpace(modelName) || modelName == "MODEL_NONE")
            {
                UnsupportedModelWarning.Visibility = Visibility.Collapsed;
                return;
            }

            bool isSupported = _supportedModels.Contains(modelName, StringComparer.OrdinalIgnoreCase);
            UnsupportedModelWarning.Visibility = isSupported ? Visibility.Collapsed : Visibility.Visible;
        }

        private void SelectModelButton_Click(object sender, RoutedEventArgs e)
        {
            string? behavior = BehaviorListBox.SelectedItem as string;
            var dialog = new ModelSelectionWindow(_projectRoot, _supportedModels,
                model => behavior == null || _behaviorCompatibility?.Rejection(behavior, model) == null);
            dialog.Owner = this;

            if (dialog.ShowDialog() == true)
            {
                NormalModelTextBox.Text = dialog.SelectedModel ?? "MODEL_NONE";
            }
        }

        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            if (!short.TryParse(PositionX.Text, out short x) || !short.TryParse(PositionY.Text, out short y) || !short.TryParse(PositionZ.Text, out short z))
            {
                MessageBox.Show("Coordinates must be between -32768 and 32767", "Invalid position");
                return;
            }
            var list = MainTabControl.SelectedIndex == 0 ? BehaviorListBox : MainTabControl.SelectedIndex == 1 ? MacroListBox : SpecialListBox;
            if (list.SelectedItem is not string selected)
            {
                MessageBox.Show("Select a behavior or preset from the list", "Select an object");
                return;
            }
            var obj = new LevelObject { X = x, Y = y, Z = z, IsNew = true, SourceIndex = -1 };
            if (PlaceOnTerrain.IsChecked == true)
            {
                try { obj.Y = Services.ObjectSpawnPlacement.GroundY(x, y, z, _placementCollision); }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Cannot place object on terrain", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }
            if (MainTabControl.SelectedIndex == 0)
            {
                string model = NormalModelTextBox.Text.Trim();
                string? problem = _behaviorCompatibility?.Rejection(selected, model);
                if (problem != null)
                {
                    MessageBox.Show(problem, "Unsupported behavior or model", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (!System.Text.RegularExpressions.Regex.IsMatch(model, @"^MODEL_\w+$"))
                {
                    MessageBox.Show("Enter a model identifier such as MODEL_NONE", "Invalid model");
                    return;
                }
                obj.ModelName = model;
                obj.Behavior = selected;
                obj.SourceType = ObjectSourceType.Normal;
            }
            else
            {
                obj.PresetName = selected;
                obj.ModelName = "MODEL_NONE";
                obj.SourceType = MainTabControl.SelectedIndex == 1 ? ObjectSourceType.Macro : ObjectSourceType.Special;
                string name = obj.SourceType == ObjectSourceType.Macro ? "macro_presets.inc.c" : "special_presets.inc.c";
                string path = Path.Combine(_projectRoot, "include", name);
                if (!File.Exists(path)) path = Path.Combine(_projectRoot, "howtomake", "include", name);
                if (obj.SourceType == ObjectSourceType.Macro)
                {
                    var presets = new Services.MacroObjectParser().ParsePresets(path);
                    if (presets.TryGetValue(selected, out var preset))
                    {
                        obj.ModelName = preset.Model;
                        obj.Behavior = preset.Behavior;
                        obj.Params = (uint)preset.Param;
                    }
                }
                else
                {
                    var presets = new Services.SpecialObjectParser().ParsePresets(path);
                    if (presets.TryGetValue(selected, out var model)) obj.ModelName = model;
                    obj.Behavior = "(Special Object)";
                }
            }
            NewObject = obj;

            DialogResult = true;
            Close();
        }

        private void Search_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (BehaviorListBox == null || MacroListBox == null || SpecialListBox == null) return;
            string query = SearchBox.Text.Trim();
            foreach (var list in new[] { BehaviorListBox, MacroListBox, SpecialListBox })
            {
                if (list.ItemsSource == null) continue;
                var view = System.Windows.Data.CollectionViewSource.GetDefaultView(list.ItemsSource);
                view.Filter = item => item is string name && name.Contains(query, StringComparison.OrdinalIgnoreCase);
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
