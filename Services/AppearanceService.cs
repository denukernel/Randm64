using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace Sm64DecompLevelViewer.Services
{
    public class AppearanceConfig
    {
        public string ThemeName { get; set; } = "Dark";
        public string BackgroundColor { get; set; } = "#1E1E1E";
        public string PanelColor { get; set; } = "#252526";
        public string BorderColor { get; set; } = "#3F3F46";
        public string TextColor { get; set; } = "#CCCCCC";
        public string AccentColor { get; set; } = "#00A2FF";
        public string HeaderColor { get; set; } = "#569CD6";
        public string HoverColor { get; set; } = "#2A2D2E";
    }

    public static class AppearanceService
    {
        private static readonly string AppearanceDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Randm64",
            "Appearance"
        );
        private static readonly string ConfigPath = Path.Combine(AppearanceDir, "appearance.json");

        public static AppearanceConfig CurrentConfig { get; private set; } = new();

        public static void LoadAndApplyAppearance()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    string json = File.ReadAllText(ConfigPath);
                    var config = JsonSerializer.Deserialize<AppearanceConfig>(json);
                    if (config != null)
                    {
                        CurrentConfig = config;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading appearance settings: {ex.Message}");
            }

            ApplyColors(CurrentConfig);
        }

        public static void SaveAndApplyAppearance(AppearanceConfig config)
        {
            try
            {
                if (!Directory.Exists(AppearanceDir))
                {
                    Directory.CreateDirectory(AppearanceDir);
                }

                string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigPath, json);
                CurrentConfig = config;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving appearance settings: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            ApplyColors(CurrentConfig);
        }

        public static void ApplyColors(AppearanceConfig config)
        {
            SetColorResource("BackgroundBrush", config.BackgroundColor);
            SetColorResource("PanelBrush", config.PanelColor);
            SetColorResource("BorderBrush", config.BorderColor);
            SetColorResource("TextBrush", config.TextColor);
            SetColorResource("AccentBrush", config.AccentColor);
            SetColorResource("HeaderBrush", config.HeaderColor);
            SetColorResource("HoverBrush", config.HoverColor);
        }

        private static void SetColorResource(string key, string hexColor)
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(hexColor);
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                
                // Set globally
                Application.Current.Resources[key] = brush;

                // Dynamically update resources in all open windows
                foreach (Window window in Application.Current.Windows)
                {
                    if (window.Resources.Contains(key))
                    {
                        window.Resources[key] = brush;
                    }
                }
            }
            catch
            {
                // Fallback to avoid crashes on corrupt hex values
            }
        }
    }
}
