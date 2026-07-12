using System.Windows;
using Sm64DecompLevelViewer.Services;

namespace Sm64DecompLevelViewer
{
    public partial class SettingsWindow : Window
    {
        private readonly SettingsService _settingsService;
        private readonly AppSettings _settings;
        private readonly bool _isInitialized = false;

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

            // Initialize theme combo box
            string currentTheme = AppearanceService.CurrentConfig.ThemeName;
            switch (currentTheme)
            {
                case "Dark": ThemeComboBox.SelectedIndex = 0; break;
                case "Light": ThemeComboBox.SelectedIndex = 1; break;
                case "Cyberpunk": ThemeComboBox.SelectedIndex = 2; break;
                case "Forest Green": ThemeComboBox.SelectedIndex = 3; break;
                case "Royal Purple": ThemeComboBox.SelectedIndex = 4; break;
                default: ThemeComboBox.SelectedIndex = 0; break;
            }

            _isInitialized = true;
        }

        private void ThemeComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (!_isInitialized || ThemeComboBox == null) return;

            var newConfig = GetConfigFromSelection();
            AppearanceService.ApplyColors(newConfig);
        }

        private AppearanceConfig GetConfigFromSelection()
        {
            var config = new AppearanceConfig();
            int index = ThemeComboBox.SelectedIndex;
            switch (index)
            {
                case 0: // Dark
                    config.ThemeName = "Dark";
                    config.BackgroundColor = "#1E1E1E";
                    config.PanelColor = "#252526";
                    config.BorderColor = "#3F3F46";
                    config.TextColor = "#CCCCCC";
                    config.AccentColor = "#00A2FF";
                    config.HeaderColor = "#569CD6";
                    config.HoverColor = "#2A2D2E";
                    break;
                case 1: // Light
                    config.ThemeName = "Light";
                    config.BackgroundColor = "#F3F3F3";
                    config.PanelColor = "#FFFFFF";
                    config.BorderColor = "#CCCCCC";
                    config.TextColor = "#333333";
                    config.AccentColor = "#007ACC";
                    config.HeaderColor = "#004B87";
                    config.HoverColor = "#E0E0E0";
                    break;
                case 2: // Cyberpunk
                    config.ThemeName = "Cyberpunk";
                    config.BackgroundColor = "#0F0F1A";
                    config.PanelColor = "#1A1A2E";
                    config.BorderColor = "#FF007F";
                    config.TextColor = "#E0E0FF";
                    config.AccentColor = "#00F0FF";
                    config.HeaderColor = "#FF007F";
                    config.HoverColor = "#2E1A47";
                    break;
                case 3: // Forest Green
                    config.ThemeName = "Forest Green";
                    config.BackgroundColor = "#1B241C";
                    config.PanelColor = "#253226";
                    config.BorderColor = "#3B4E3C";
                    config.TextColor = "#D0E0D0";
                    config.AccentColor = "#4CAF50";
                    config.HeaderColor = "#81C784";
                    config.HoverColor = "#2D3E2E";
                    break;
                case 4: // Royal Purple
                    config.ThemeName = "Royal Purple";
                    config.BackgroundColor = "#1A1525";
                    config.PanelColor = "#241E34";
                    config.BorderColor = "#42345E";
                    config.TextColor = "#E0D5F5";
                    config.AccentColor = "#9C27B0";
                    config.HeaderColor = "#BA68C8";
                    config.HoverColor = "#2F2644";
                    break;
            }
            return config;
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

            // Save and permanently apply appearance
            var appearanceConfig = GetConfigFromSelection();
            AppearanceService.SaveAndApplyAppearance(appearanceConfig);
            
            MessageBox.Show("Settings and theme saved!", "Settings", MessageBoxButton.OK, MessageBoxImage.Information);
            this.Close();
        }

        protected override void OnClosed(System.EventArgs e)
        {
            // Re-apply original saved colors in case they selected a preview and didn't save
            AppearanceService.ApplyColors(AppearanceService.CurrentConfig);
            base.OnClosed(e);
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
