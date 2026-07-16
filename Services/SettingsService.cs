using System;
using System.IO;
using System.Text.Json;

namespace Sm64DecompLevelViewer.Services
{
    public class AppSettings
    {
        public string LastProjectRoot { get; set; } = string.Empty;
        public string EmulatorPath { get; set; } = string.Empty;
        public float CameraMoveSpeed { get; set; } = 800f;
        public float CameraRotationSpeed { get; set; } = 0.02f;
        public bool RenderGrid { get; set; } = true;
        public float RenderDistance { get; set; } = 30000f;
        public float FieldOfView { get; set; } = 60f;
        public bool EnableFlatShading { get; set; } = false;
        
        // Build options settings
        public string BuildTargetPlatform { get; set; } = "N64";
        public string BuildEnvironment { get; set; } = "WSL";
        public string LastBuildEnvironment { get; set; } = string.Empty;
        public string LastBuildPlatform { get; set; } = string.Empty;
        public string MsysPath { get; set; } = @"C:\msys64";
        public string BuildVersion { get; set; } = "us";
        public string BuildCompiler { get; set; } = "ido";
        public string BuildGrucode { get; set; } = "f3d_old";
        public bool BuildCompare { get; set; } = false;
        public bool BuildNonMatching { get; set; } = false;
        public int BuildJobs { get; set; } = 8;
        public string GitRepositoryUrl { get; set; } = "https://github.com/n64decomp/sm64.git";
        public bool AutoRunEmulator { get; set; } = true;
        public string LastSelectedM64Path { get; set; } = string.Empty;
    }

    public class SettingsService
    {
        private readonly string _settingsFilePath;
        private readonly string _rootPath;

        public string RootPath => _rootPath;
        public string SettingsFolderPath => Path.Combine(_rootPath, "Settings");
        public string DataFolderPath => Path.Combine(_rootPath, "Data");
        public string PluginsFolderPath => Path.Combine(_rootPath, "Data", "Plugins");

        public SettingsService()
        {
            _rootPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Randm64");
            
            // Create root and subfolders
            Directory.CreateDirectory(_rootPath);
            Directory.CreateDirectory(SettingsFolderPath);
            
            string dataDir = DataFolderPath;
            Directory.CreateDirectory(dataDir);
            Directory.CreateDirectory(Path.Combine(dataDir, "Patchs"));
            Directory.CreateDirectory(PluginsFolderPath);
            Directory.CreateDirectory(Path.Combine(dataDir, "Custom Objects"));

            _settingsFilePath = Path.Combine(SettingsFolderPath, "settings.json");

            // Write instruction readme
            try
            {
                string readmeFile = Path.Combine(PluginsFolderPath, "readme.txt");
                if (!File.Exists(readmeFile))
                {
                    File.WriteAllText(readmeFile, 
                        "How to write a Randm64 Plugin:\n\n" +
                        "1. Create a C# Class Library (.NET 8.0) project in Visual Studio.\n" +
                        "2. Add a reference to Randm64.exe.\n" +
                        "3. Create a class that implements Sm64DecompLevelViewer.Services.IPlugin.\n" +
                        "   Example:\n\n" +
                        "   using System;\n" +
                        "   using System.Windows;\n" +
                        "   using Sm64DecompLevelViewer.Services;\n\n" +
                        "   namespace MyRandm64Plugin\n" +
                        "   {\n" +
                        "       public class SayHelloPlugin : IPlugin\n" +
                        "       {\n" +
                        "           public string Name => \"Say Hello Plugin\";\n" +
                        "           public string Description => \"Displays a greetings message box.\";\n" +
                        "           \n" +
                        "           private string _projectRoot;\n\n" +
                        "           public void Initialize(string projectRoot)\n" +
                        "           {\n" +
                        "               _projectRoot = projectRoot;\n" +
                        "           }\n\n" +
                        "           public void Execute()\n" +
                        "           {\n" +
                        "               MessageBox.Show(\"Hello World from Randm64 Plugin!\", \"Hello\", MessageBoxButton.OK, MessageBoxImage.Information);\n" +
                        "           }\n" +
                        "       }\n" +
                        "   }\n\n" +
                        "4. Build the project and drop your output DLL inside this Plugins folder.\n" +
                        "5. In Randm64, click 'Reload' inside the Plugins tab to see your plugin list!\n"
                    );
                }
            }
            catch { }
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
