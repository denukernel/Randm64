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
        private int _initialX, _initialY, _initialZ;
        private List<string> _supportedModels;
        
        public ObjectAddWindow(string projectRoot, List<string> behaviors, List<string> macroPresets, List<string> specialPresets, List<string> supportedModels, int x = 0, int y = 0, int z = 0)
        {
            InitializeComponent();
            _projectRoot = projectRoot;
            _supportedModels = supportedModels;
            _initialX = x;
            _initialY = y;
            _initialZ = z;
            BehaviorListBox.ItemsSource = behaviors;
            MacroListBox.ItemsSource = macroPresets;
            SpecialListBox.ItemsSource = specialPresets;
            
            // Default values
            NormalModelTextBox.Text = "MODEL_NONE";
            ValidateModel(NormalModelTextBox.Text);
        }

        private void NormalModelTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ValidateModel(NormalModelTextBox.Text);
        }

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
            var dialog = new ModelSelectionWindow(_projectRoot, _supportedModels);
            dialog.Owner = this;

            if (dialog.ShowDialog() == true)
            {
                NormalModelTextBox.Text = dialog.SelectedModel ?? "MODEL_NONE";
            }
        }

        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedTab = ((TabItem)MainTabControl.SelectedItem).Header.ToString();
            
            NewObject = new LevelObject
            {
                X = _initialX, Y = _initialY, Z = _initialZ,
                RX = 0, RY = 0, RZ = 0,
                Params = 0,
                IsNew = true
            };

            if (selectedTab == "Normal")
            {
                NewObject.ModelName = NormalModelTextBox.Text;
                NewObject.Behavior = BehaviorListBox.SelectedItem?.ToString() ?? "bhvStaticObject";
                NewObject.SourceType = ObjectSourceType.Normal;
            }
            else if (selectedTab == "Macro")
            {
                NewObject.ModelName = MacroListBox.SelectedItem?.ToString() ?? "macro_goomba_triplet_formation";
                NewObject.Behavior = "(Macro Preset)";
                NewObject.SourceType = ObjectSourceType.Macro;
            }
            else if (selectedTab == "Special")
            {
                NewObject.ModelName = SpecialListBox.SelectedItem?.ToString() ?? "special_empty_room_0";
                NewObject.Behavior = "(Special Object)";
                NewObject.SourceType = ObjectSourceType.Special;
            }

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
