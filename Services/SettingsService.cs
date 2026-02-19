using System;
using System.IO;
using System.Text.Json;

namespace Sm64DecompLevelViewer.Services
{
    public class AppSettings
    {
        public string LastProjectRoot { get; set; } = string.Empty;
    }

    public class SettingsService
    {
        private readonly string _settingsFilePath;

        public SettingsService()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string appDir = Path.Combine(appData, "Sm64DecompLevelViewer");
            if (!Directory.Exists(appDir)) Directory.CreateDirectory(appDir);
            
            _settingsFilePath = Path.Combine(appDir, "settings.json");
        }

        public AppSettings LoadSettings()
        {
            if (!File.Exists(_settingsFilePath))
            {
                return new AppSettings();
            }

            try
            {
                string json = File.ReadAllText(_settingsFilePath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading settings: {ex.Message}");
                return new AppSettings();
            }
        }

        public void SaveSettings(AppSettings settings)
        {
            try
            {
                string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_settingsFilePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving settings: {ex.Message}");
            }
        }
    }
}
