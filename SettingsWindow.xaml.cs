using System.Windows;
using Sm64DecompLevelViewer.Services;

namespace Sm64DecompLevelViewer
{
    public partial class SettingsWindow : Window
    {
        private readonly SettingsService _settingsService;
        private readonly AppSettings _settings;

        public SettingsWindow()
        {
            InitializeComponent();
            _settingsService = new SettingsService();
            _settings = _settingsService.LoadSettings();

            // Initialize sliders
            MoveSpeedSlider.Value = _settings.CameraMoveSpeed;
            RotationSlider.Value = _settings.CameraRotationSpeed;
            FovSlider.Value = _settings.FieldOfView;
            DistanceSlider.Value = _settings.RenderDistance;

            // Initialize checkboxes
            RenderGridCheckBox.IsChecked = _settings.RenderGrid;
            FlatShadingCheckBox.IsChecked = _settings.EnableFlatShading;
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            _settings.CameraMoveSpeed = (float)MoveSpeedSlider.Value;
            _settings.CameraRotationSpeed = (float)RotationSlider.Value;
            _settings.FieldOfView = (float)FovSlider.Value;
            _settings.RenderDistance = (float)DistanceSlider.Value;
            
            _settings.RenderGrid = RenderGridCheckBox.IsChecked == true;
            _settings.EnableFlatShading = FlatShadingCheckBox.IsChecked == true;
            
            _settingsService.SaveSettings(_settings);
            
            MessageBox.Show("Settings saved! Changes will apply when you open the Level Editor.", "Settings", MessageBoxButton.OK, MessageBoxImage.Information);
            this.Close();
        }

        private void OpenSettingsFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start("explorer.exe", _settingsService.SettingsFolderPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to open settings folder: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
