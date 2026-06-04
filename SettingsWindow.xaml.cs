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
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            _settings.CameraMoveSpeed = (float)MoveSpeedSlider.Value;
            _settings.CameraRotationSpeed = (float)RotationSlider.Value;
            
            _settingsService.SaveSettings(_settings);
            
            MessageBox.Show("Settings saved! Changes will apply when you open the Level Editor.", "Settings", MessageBoxButton.OK, MessageBoxImage.Information);
            this.Close();
        }
    }
}
