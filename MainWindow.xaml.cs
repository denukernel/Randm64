using Sm64DecompLevelViewer.Services.Levels;
using System.ComponentModel;
using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Data;
using System.Text.RegularExpressions;
using Sm64DecompLevelViewer.Models;
using Sm64DecompLevelViewer.Services;
using Sm64DecompLevelViewer.Rendering;
using OpenTK.Windowing.Desktop;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;

namespace Sm64DecompLevelViewer;

public partial class MainWindow : Window
{
    private List<LevelMetadata> _levels = new();
    private readonly LevelScanner _levelScanner;
    private readonly CollisionParser _collisionParser;
    private LevelMetadata? _selectedLevel;
    private Dictionary<string, string> _collisionFiles = new();
    private int _meshLoadVersion;
    private readonly System.Threading.SemaphoreSlim _meshLoadGate = new(1, 1);
    private readonly MacroObjectParser _macroParser;
    private CollisionMesh? _currentCollisionMesh;
    private VisualMesh? _currentVisualMesh;
    private GeometryRenderer? _renderer;
    private string? _projectRootPath;
    private List<LevelObject> _currentObjects = new();
    private readonly LevelSaver _levelSaver;
    private readonly SettingsService _settingsService;
    private readonly PluginService _pluginService;
    private List<IPlugin> _loadedPlugins = new();

    public MainWindow()
    {
        InitializeComponent();
        
        // Disable GLFW's main thread check to allow creating windows on background threads.
        // This is necessary because we run the 3D viewer on a separate thread to keep the WPF UI responsive.
        GLFWProvider.CheckForMainThread = false;

        _levelScanner = new LevelScanner();
        _collisionParser = new CollisionParser();
        _macroParser = new MacroObjectParser();
        _levelSaver = new LevelSaver();
        _settingsService = new SettingsService();
        var startupSettings = _settingsService.LoadSettings();
        if (!string.IsNullOrWhiteSpace(startupSettings.LastProjectRoot))
            Services.Build.WslBuildSessions.PrewarmProject(startupSettings.LastProjectRoot, startupSettings);
        _pluginService = new PluginService();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "Please select an SM64 Project Folder to start";
        this.Closing += MainWindow_Closing;

        // Auto-load last project root
        var settings = _settingsService.LoadSettings();
        if (!string.IsNullOrEmpty(settings.LastProjectRoot) && Directory.Exists(settings.LastProjectRoot))
        {
            // Keep the saved project available when import is declined, cancelled or fails.
            _projectRootPath = OfferWslImport(settings.LastProjectRoot, settings) ?? settings.LastProjectRoot;
            if (_projectRootPath != settings.LastProjectRoot)
            {
                settings.LastProjectRoot = _projectRootPath;
                _settingsService.SaveSettings(settings);
            }
            Services.Build.WslBuildSessions.PrewarmProject(_projectRootPath, settings);
            LoadLevels();
            LoadPlugins();
        }
        else
        {
            LoadPlugins();
        }
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _meshLoadVersion++;
        try
        {
            if (_renderer != null)
            {
                _renderer.RequestCloseThreadSafe();
                _renderer = null;
            }
        }
        catch { }
    }

    private void Click_LoadLevel(object sender, RoutedEventArgs e)
    {
        LoadLevels();
    }

    private void SelectFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select SM64 Project Root Folder",
            InitialDirectory = _projectRootPath ?? AppDomain.CurrentDomain.BaseDirectory
        };

        if (dialog.ShowDialog() == true)
        {
            var selectedPath = dialog.FolderName;
            
            var dirName = Path.GetFileName(selectedPath);
            if (dirName.Equals("levels", StringComparison.OrdinalIgnoreCase) || 
                dirName.Equals("howtomake", StringComparison.OrdinalIgnoreCase))
            {
                selectedPath = Path.GetDirectoryName(selectedPath) ?? selectedPath;
            }

            var settings = _settingsService.LoadSettings();
            string? projectPath = OfferWslImport(selectedPath, settings);
            if (projectPath == null) return;
            _projectRootPath = projectPath;
            
            // Save to settings
            settings.LastProjectRoot = _projectRootPath;
            Services.Build.WslBuildSessions.PrewarmProject(_projectRootPath, settings);
            _settingsService.SaveSettings(settings);

            LoadLevels();
            LoadPlugins();
        }
    }

    private void OpenProjectFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_projectRootPath) || !Directory.Exists(_projectRootPath))
        {
            MessageBox.Show("Please select a valid SM64 Project Folder first.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        try
        {
            System.Diagnostics.Process.Start("explorer.exe", _projectRootPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to open project folder: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenToolsDashboard_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_projectRootPath) || !Directory.Exists(_projectRootPath))
        {
            MessageBox.Show("Please select a valid SM64 Project Folder first.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var tools = new ToolsWindow(_projectRootPath, _selectedLevel?.LevelPath, () =>
        {
            // If active level is selected, reload details
            if (_selectedLevel != null)
            {
                LevelListBox_SelectionChanged(null, null);
            }
        }, AreaComboBox.SelectedItem as string);
        tools.Owner = this;
        tools.Show();
    }

    private void LevelSearch_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (LevelListBox?.ItemsSource is System.ComponentModel.ICollectionView view)
        {
            string query = LevelSearchText.Text.Trim();
            view.Filter = item => item is LevelMetadata level && (level.FullName.Contains(query, StringComparison.OrdinalIgnoreCase) || level.ShortName.Contains(query, StringComparison.OrdinalIgnoreCase) || level.Category.Contains(query, StringComparison.OrdinalIgnoreCase));
        }
    }

    private void LoadLevels()
    {
        if (string.IsNullOrEmpty(_projectRootPath) || !Directory.Exists(_projectRootPath))
        {
            return;
        }

        try
        {
            string? previousLevel = _selectedLevel?.LevelPath;
            _levels = new List<LevelMetadata>();
            LevelListBox.ItemsSource = null;
            HideLevelDetails();

            _levels = ProjectCatalog.ScanLevels(_projectRootPath);

            if (_levels.Count == 0)
            {
                StatusText.Text = "No levels found";
                MessageBox.Show(
                    "no level metadata or level scripts were found in the selected project",
                    "No Levels Found",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var groupedLevels = CollectionViewSource.GetDefaultView(_levels);
            groupedLevels.GroupDescriptions.Clear();
            groupedLevels.GroupDescriptions.Add(new PropertyGroupDescription("Category"));
            
            LevelListBox.ItemsSource = groupedLevels;
            LevelSearch_TextChanged(null, null);
            LevelListBox.SelectedItem = _levels.FirstOrDefault(l => l.LevelPath == previousLevel) ?? _levels.FirstOrDefault();
            OpenLevelsFolderButton.IsEnabled = true;

            StatusText.Text = $"{_levels.Count} levels loaded";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Error loading levels";
            MessageBox.Show(
                $"An error occurred while loading levels:\n{ex.Message}",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void LevelListBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (LevelListBox.SelectedItem is LevelMetadata level)
        {
            DisplayLevelDetails(level);
        }
        else
        {
            HideLevelDetails();
        }
    }

    private void DisplayLevelDetails(LevelMetadata level)
    {
        _selectedLevel = level;
        ToolContextText.Text = "Selected level: " + level.FullName;
        MeshMainToolButton.IsEnabled = true; WarpMainToolButton.IsEnabled = File.Exists(Path.Combine(level.LevelPath, "script.c")); PaintingMainToolButton.IsEnabled = new PaintingService().FindPaintingFile(level.LevelPath) != null;
        NoSelectionText.Visibility = Visibility.Collapsed;
        LevelDetailsPanel.Visibility = Visibility.Visible;

        FullNameText.Text = level.FullName;
        ShortNameText.Text = level.ShortName;
        CategoryText.Text = level.Category;
        AreaCountText.Text = level.AreaCount.ToString();
        SkyboxText.Text = level.SkyboxBin ?? "(none)";
        TextureBinText.Text = level.TextureBin;
        EffectsText.Text = level.Effects ? "Yes" : "No";
        
        ObjectsText.Text = level.Objects.Count > 0 
            ? string.Join(", ", level.Objects) 
            : "(none)";
        
        ActorBinsText.Text = level.ActorBins.Count > 0 
            ? string.Join(", ", level.ActorBins) 
            : "(none)";
        
        CommonBinText.Text = level.CommonBin.Count > 0 
            ? string.Join(", ", level.CommonBin) 
            : "(none)";
        
        LevelPathText.Text = level.LevelPath;

        LoadCollisionFiles(level);

        StatusText.Text = $"Selected: {level.FullName}";
    }

    private void HideLevelDetails()
    {
        _meshLoadVersion++;
        _selectedLevel = null; _currentCollisionMesh = null; _currentVisualMesh = null;
        if (ToolContextText != null) ToolContextText.Text = "Select a level in the levels tab for level tools";
        if (MeshMainToolButton != null) { MeshMainToolButton.IsEnabled = false; WarpMainToolButton.IsEnabled = false; PaintingMainToolButton.IsEnabled = false; }
        if (View3DButton != null) View3DButton.IsEnabled = false;
        NoSelectionText.Visibility = Visibility.Visible;
        LevelDetailsPanel.Visibility = Visibility.Collapsed;
        StatusText.Text = $"{_levels.Count} levels loaded";
    }

    private void OpenScanDebugger_Click(object sender, RoutedEventArgs e)
    {
        var debuggerWindow = new ScanDebuggerWindow();
        debuggerWindow.Owner = this;
        debuggerWindow.ShowDialog();
    }

    private void LoadCollisionFiles(LevelMetadata level)
    {
        string? previousArea = AreaComboBox.SelectedItem as string;
        _collisionFiles = _collisionParser.FindCollisionFiles(level.LevelPath);
        // The scene loader discovers its own sources. The old model catalog was unused
        // and scanned/read every area before loading the selected one.
        _meshLoadVersion++;
        
        AreaComboBox.Items.Clear();
        CollisionStatsText.Text = "";
        View3DButton.IsEnabled = false;
        _currentCollisionMesh = null;
        _currentVisualMesh = null;

        if (_collisionFiles.Count > 0)
        {
            foreach (var areaName in _collisionFiles.Keys.OrderBy(k => int.TryParse(Regex.Match(k, @"\d+").Value, out int n) ? n : int.MaxValue))
            {
                AreaComboBox.Items.Add(areaName);
            }
            
            AreaComboBox.SelectedItem = previousArea != null && AreaComboBox.Items.Contains(previousArea) ? previousArea : AreaComboBox.Items[0];
            MeshMainToolButton.IsEnabled = true;
        }
        else
        {
            MeshMainToolButton.IsEnabled = false;
            CollisionStatsText.Text = "No collision files found for this level.";
        }
    }

    private async void AreaComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (AreaComboBox.SelectedItem is string areaName && _selectedLevel != null)
        {
            await LoadCollisionMesh(areaName);
        }
    }

    private async Task LoadCollisionMesh(string areaName)
    {
        int version = ++_meshLoadVersion;
        var level = _selectedLevel;
        var projectRoot = _projectRootPath;
        _currentCollisionMesh = null;
        _currentVisualMesh = null;
        View3DButton.IsEnabled = false;
        if (level == null || !_collisionFiles.TryGetValue(areaName, out var collisionFilePath)) return;
        CollisionStatsText.Text = "loading level geometry...";
        await _meshLoadGate.WaitAsync();
        try
        {
            // Serialize disk work and skip queued selections superseded by a newer one.
            if (version != _meshLoadVersion) return;
            var meshes = await Task.Run(() =>
            {
                var collision = new CollisionParser().ParseCollisionFile(collisionFilePath, areaName, level.FullName);
                int sceneArea = int.Parse(Regex.Match(areaName, @"\d+").Value);
                var visual = LevelSceneLoader.Load(projectRoot!, level.LevelPath, sceneArea, level.TextureBin);
                return (Collision: collision, Visual: visual);
            });
            if (version != _meshLoadVersion) return;
            _currentCollisionMesh = meshes.Collision;
            _currentVisualMesh = meshes.Visual;
            if (_currentCollisionMesh != null)
            {
                string statsText = $"✓ Collision: {_currentCollisionMesh.VertexCount} vertices, {_currentCollisionMesh.TriangleCount} triangles";
                if (_currentVisualMesh != null) statsText += $"\n✓ Visual: {_currentVisualMesh.VertexCount} vertices, {_currentVisualMesh.TriangleCount} triangles";
                CollisionStatsText.Text = statsText;
                View3DButton.IsEnabled = true;
            }
            else
            {
                CollisionStatsText.Text = "✗ Failed to parse collision file.";
                View3DButton.IsEnabled = false;
            }
        }
        catch (Exception ex)
        {
            if (version != _meshLoadVersion) return;
            CollisionStatsText.Text = $"✗ Error loading mesh: {ex.Message}";
            View3DButton.IsEnabled = false;
        }
        finally { _meshLoadGate.Release(); }
    }

    private void WorkshopButton_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("The Level Workshop feature is currently under development and will be available in a future update.", "Coming Soon", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OnObjectSelected(int index)
    {
        Dispatcher.Invoke(() =>
        {
            LevelObject? selectedObject = (index >= 0 && index < _currentObjects.Count) ? _currentObjects[index] : null;
            if (selectedObject != null)
            {
                ObjectInfoText.Text = $"Selected: {selectedObject.ModelName} | Pos: ({selectedObject.X:F0}, {selectedObject.Y:F0}, {selectedObject.Z:F0}) | Rot: ({selectedObject.RX}, {selectedObject.RY}, {selectedObject.RZ}) | Behavior: {selectedObject.Behavior}";
            }
            else
            {
                ObjectInfoText.Text = "";
            }
        });
    }

    private void OpenLevelsFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_projectRootPath) || !Directory.Exists(_projectRootPath)) return;

        try
        {
            var levelsDir = Path.Combine(_projectRootPath, "levels");
            if (!Directory.Exists(levelsDir)) levelsDir = Path.Combine(_projectRootPath, "howtomake", "levels");

            if (Directory.Exists(levelsDir))
            {
                Process.Start("explorer.exe", levelsDir);
            }
            else
            {
                MessageBox.Show("Could not find the levels folder in this project.", "Folder Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error opening folder: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void LoadPlugins()
    {
        try
        {
            _loadedPlugins = _pluginService.LoadPlugins(_projectRootPath ?? string.Empty);
            PluginsListBox.ItemsSource = null;
            PluginsListBox.ItemsSource = _loadedPlugins;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading plugins: {ex.Message}");
        }
    }

    private void ReloadPlugins_Click(object sender, RoutedEventArgs e)
    {
        LoadPlugins();
        MessageBox.Show("Plugins reloaded successfully!", "Plugins", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OpenPluginsFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start("explorer.exe", _settingsService.PluginsFolderPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to open plugins folder: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExecutePlugin_Click(object sender, RoutedEventArgs e)
    {
        if (PluginsListBox.SelectedItem is IPlugin plugin)
        {
            try
            {
                plugin.Execute();
                MessageBox.Show($"Plugin '{plugin.Name}' executed successfully!", "Plugin Execution", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error executing plugin: {ex.Message}", "Plugin Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        else
        {
            MessageBox.Show("Please select a plugin to execute first.", "Plugin Execution", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}

