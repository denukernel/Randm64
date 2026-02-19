using System.ComponentModel;
using System.IO;
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
    private readonly ModelParser _modelParser;
    private LevelMetadata? _selectedLevel;
    private Dictionary<string, string> _collisionFiles = new();
    private Dictionary<string, List<string>> _modelFiles = new();
    private readonly MacroObjectParser _macroParser;
    private CollisionMesh? _currentCollisionMesh;
    private VisualMesh? _currentVisualMesh;
    private GeometryRenderer? _renderer;
    private string? _projectRootPath;
    private List<LevelObject> _currentObjects = new();
    private readonly LevelSaver _levelSaver;
    private readonly SettingsService _settingsService;

    private static readonly Regex SpecialObjectPattern = new Regex(
        @"SPECIAL_OBJECT\s*\(\s*(?:/\*.*?\*/\s*)?([^,]+)\s*,\s*(?:/\*.*?\*/\s*)?(-?\d+)\s*,\s*(?:/\*.*?\*/\s*)?(-?\d+)\s*,\s*(?:/\*.*?\*/\s*)?(-?\d+)\s*\)[\s,]*",
        RegexOptions.Compiled
    );

    private static readonly Regex SpecialObjectWithYawPattern = new Regex(
        @"SPECIAL_OBJECT_WITH_YAW\s*\(\s*(?:/\*.*?\*/\s*)?([^,]+)\s*,\s*(?:/\*.*?\*/\s*)?(-?\d+)\s*,\s*(?:/\*.*?\*/\s*)?(-?\d+)\s*,\s*(?:/\*.*?\*/\s*)?(-?\d+)\s*,\s*(?:/\*.*?\*/\s*)?(-?\d+)\s*\)[\s,]*",
        RegexOptions.Compiled
    );

    private static readonly Regex SpecialObjectWithYawAndParamPattern = new Regex(
        @"SPECIAL_OBJECT_WITH_YAW_AND_PARAM\s*\(\s*(?:/\*.*?\*/\s*)?([^,]+)\s*,\s*(?:/\*.*?\*/\s*)?(-?\d+)\s*,\s*(?:/\*.*?\*/\s*)?(-?\d+)\s*,\s*(?:/\*.*?\*/\s*)?(-?\d+)\s*,\s*(?:/\*.*?\*/\s*)?(-?\d+)\s*,\s*(?:/\*.*?\*/\s*)?([^)]+)\)[\s,]*",
        RegexOptions.Compiled
    );

    public MainWindow()
    {
        InitializeComponent();
        
        // Disable GLFW's main thread check to allow creating windows on background threads.
        // This is necessary because we run the 3D viewer on a separate thread to keep the WPF UI responsive.
        GLFWProvider.CheckForMainThread = false;

        _levelScanner = new LevelScanner();
        _collisionParser = new CollisionParser();
        _modelParser = new ModelParser();
        _macroParser = new MacroObjectParser();
        _levelSaver = new LevelSaver();
        _settingsService = new SettingsService();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "Please select an SM64 Project Folder to start";
        this.Closing += MainWindow_Closing;

        // Auto-load last project root
        var settings = _settingsService.LoadSettings();
        if (!string.IsNullOrEmpty(settings.LastProjectRoot) && Directory.Exists(settings.LastProjectRoot))
        {
            _projectRootPath = settings.LastProjectRoot;
            LoadLevels();
        }
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        try
        {
            if (_renderer != null)
            {
                _renderer.Close();
                _renderer.Dispose();
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

            _projectRootPath = selectedPath;
            
            // Save to settings
            var settings = _settingsService.LoadSettings();
            settings.LastProjectRoot = _projectRootPath;
            _settingsService.SaveSettings(settings);

            LoadLevels();
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
            _levels = new List<LevelMetadata>();

            var levelsDir = Path.Combine(_projectRootPath, "levels");
            if (!Directory.Exists(levelsDir)) levelsDir = Path.Combine(_projectRootPath, "howtomake", "levels");

            if (Directory.Exists(levelsDir))
            {
                _levels = _levelScanner.ScanLevels(Path.GetDirectoryName(levelsDir)!);
            }

            if (_levels.Count == 0)
            {
                StatusText.Text = "No levels found";
                MessageBox.Show(
                    "No level.yaml files were found in the howtomake/levels directory.",
                    "No Levels Found",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var groupedLevels = CollectionViewSource.GetDefaultView(_levels);
            groupedLevels.GroupDescriptions.Add(new PropertyGroupDescription("Category"));
            
            LevelListBox.ItemsSource = groupedLevels;

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
        NoSelectionText.Visibility = Visibility.Visible;
        LevelDetailsPanel.Visibility = Visibility.Collapsed;
        StatusText.Text = $"{_levels.Count} levels loaded";
    }

    private void LoadCollisionFiles(LevelMetadata level)
    {
        _collisionFiles = _collisionParser.FindCollisionFiles(level.LevelPath);
        _modelFiles = _modelParser.FindModelFiles(level.LevelPath);
        
        AreaComboBox.Items.Clear();
        CollisionStatsText.Text = "";
        View3DButton.IsEnabled = false;
        _currentCollisionMesh = null;
        _currentVisualMesh = null;

        if (_collisionFiles.Count > 0)
        {
            foreach (var areaName in _collisionFiles.Keys.OrderBy(k => k))
            {
                AreaComboBox.Items.Add(areaName);
            }
            
            AreaComboBox.SelectedIndex = 0;
        }
        else
        {
            CollisionStatsText.Text = "No collision files found for this level.";
        }
    }

    private void AreaComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (AreaComboBox.SelectedItem is string areaName && _selectedLevel != null)
        {
            LoadCollisionMesh(areaName);
        }
    }

    private void LoadCollisionMesh(string areaName)
    {
        try
        {
            if (!_collisionFiles.ContainsKey(areaName) || _selectedLevel == null)
                return;

            var collisionFilePath = _collisionFiles[areaName];
            _currentCollisionMesh = _collisionParser.ParseCollisionFile(
                collisionFilePath, 
                areaName, 
                _selectedLevel.FullName
            );

            if (_modelFiles.ContainsKey(areaName))
            {
                var modelFilePaths = _modelFiles[areaName];
                
                Dictionary<string, OpenTK.Mathematics.Matrix4>? transforms = null;
                var areaPath = Path.GetDirectoryName(modelFilePaths[0]);
                if (areaPath != null)
                {
                    var geoLayoutPath = Path.Combine(areaPath, "..", "geo.inc.c");
                    if (File.Exists(geoLayoutPath))
                    {
                        var geoParser = new GeoLayoutParser();
                        var geoRoot = geoParser.ParseGeoLayout(geoLayoutPath);
                        if (geoRoot != null) transforms = geoParser.ExtractTransformations(geoRoot);
                    }
                }

                if (transforms == null) transforms = new Dictionary<string, OpenTK.Mathematics.Matrix4>();

                var levelRoot = Path.GetDirectoryName(Path.GetDirectoryName(modelFilePaths[0]));
                if (levelRoot != null)
                {
                    try
                    {
                        var specialParser = new SpecialObjectParser();
                        var includePath = ResolveIncludePath("special_presets.inc.c");
                        if (includePath != null)
                        {
                            var presetMapping = specialParser.ParsePresets(includePath);
                            var objectParser = new ObjectParser();
                            var scriptPath = Path.Combine(_selectedLevel.LevelPath, "script.c");
                            var modelToGeo = objectParser.ParseLoadModels(scriptPath);

                            // Load model IDs for resolving aliases
                            var modelIdsPath = ResolveIncludePath("model_ids.h");
                            var modelIdMapping = modelIdsPath != null ? objectParser.ParseModelIds(modelIdsPath) : new Dictionary<string, int>();

                            // Map numeric ID to geo layout name
                            var idToGeo = new Dictionary<int, string>();
                            foreach (var kvp in modelToGeo)
                            {
                                if (modelIdMapping.TryGetValue(kvp.Key, out int id))
                                {
                                    idToGeo[id] = kvp.Value;
                                }
                            }

                            string colContent = File.ReadAllText(collisionFilePath);
                            var geoParser = new GeoLayoutParser();

                            var specialMatches = SpecialObjectPattern.Matches(colContent);
                            var specialWithYawMatches = SpecialObjectWithYawPattern.Matches(colContent);

                            void ProcessSpecial(string preset, float x, float y, float z, float yaw)
                            {
                                if (presetMapping.TryGetValue(preset, out string? modelId))
                                {
                                    string? geoLayoutName = null;
                                    
                                    // Try direct map
                                    if (!modelToGeo.TryGetValue(modelId, out geoLayoutName))
                                    {
                                        // Try via ID alias
                                        if (modelIdMapping.TryGetValue(modelId, out int id))
                                        {
                                            idToGeo.TryGetValue(id, out geoLayoutName);
                                        }
                                    }

                                    if (geoLayoutName != null)
                                    {
                                        string? subGeoPath = FindGeoLayoutForModel(levelRoot, geoLayoutName);
                                        if (subGeoPath != null)
                                        {
                                            string? dlName = geoParser.GetPrimaryDisplayListName(subGeoPath);
                                            if (dlName != null)
                                            {
                                                var mat = OpenTK.Mathematics.Matrix4.CreateRotationY(OpenTK.Mathematics.MathHelper.DegreesToRadians(yaw)) *
                                                          OpenTK.Mathematics.Matrix4.CreateTranslation(x, y, z);
                                                transforms[dlName] = mat;
                                            }
                                        }
                                    }
                                }
                            }

                            foreach (Match m in specialMatches)
                                ProcessSpecial(m.Groups[1].Value, float.Parse(m.Groups[2].Value), float.Parse(m.Groups[3].Value), float.Parse(m.Groups[4].Value), 0);
                            foreach (Match m in specialWithYawMatches)
                                ProcessSpecial(m.Groups[1].Value, float.Parse(m.Groups[2].Value), float.Parse(m.Groups[3].Value), float.Parse(m.Groups[4].Value), float.Parse(m.Groups[5].Value));
                        }
                    }
                    catch (Exception ex) { Console.WriteLine($"Special object parsing error: {ex.Message}"); }
                }
                
                _currentVisualMesh = _modelParser.ParseMultipleModelFiles(modelFilePaths, areaName, _selectedLevel.FullName, transforms);
                if (_currentVisualMesh != null && _projectRootPath != null)
                {
                    _modelParser.ParseTextureMapping(_selectedLevel.LevelPath, _currentVisualMesh, _projectRootPath);
                }
            }
            else
            {
                _currentVisualMesh = null;
            }

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
            CollisionStatsText.Text = $"✗ Error loading mesh: {ex.Message}";
            View3DButton.IsEnabled = false;
        }
    }

    private void View3DButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentCollisionMesh == null)
            return;

        // Capture current state to local variables for thread safety
        var collisionMesh = _currentCollisionMesh;
        var visualMesh = _currentVisualMesh;
        var selectedLevel = _selectedLevel;
        var projectRoot = _projectRootPath;
        var macroParser = _macroParser;
        var areaName = AreaComboBox.SelectedItem as string;
        _collisionFiles.TryGetValue(areaName ?? "", out var collisionFilePath);

        try
        {
            // Close existing renderer if already open
            if (_renderer != null)
            {
                try { _renderer.Close(); } catch { }
                try { _renderer.Dispose(); } catch { }
                _renderer = null;
            }

            List<LevelObject> objects = new();
            int areaIndex = -1;
            if (selectedLevel != null)
            {
                var scriptPath = Path.Combine(selectedLevel.LevelPath, "script.c");
                if (File.Exists(scriptPath))
                {
                    var objectParser = new ObjectParser();
                    if (areaName != null && areaName.StartsWith("Area "))
                    {
                        int.TryParse(areaName.Substring(5), out areaIndex);
                    }

                    objects = objectParser.ParseScriptFile(scriptPath, areaIndex);

                    // Load model IDs for resolving aliases
                    var modelIdsPath = ResolveIncludePath("model_ids.h");
                    var modelIdMapping = modelIdsPath != null ? objectParser.ParseModelIds(modelIdsPath) : new Dictionary<string, int>();

                    // Map numeric ID to geo layout name
                    var modelToGeo = objectParser.ParseLoadModels(scriptPath);
                    var idToGeo = new Dictionary<int, string>();
                    foreach (var kvp in modelToGeo)
                    {
                        if (modelIdMapping.TryGetValue(kvp.Key, out int id))
                        {
                            idToGeo[id] = kvp.Value;
                        }
                    }

                    try
                    {
                        var macroListName = objectParser.ParseMacroListName(scriptPath, areaIndex);
                        if (macroListName != null && !string.IsNullOrEmpty(projectRoot))
                        {
                            Dictionary<string, MacroPreset> presets = new();
                            var selectedPresetsPath = Path.Combine(projectRoot, "include", "macro_presets.inc.c");
                            if (!File.Exists(selectedPresetsPath)) selectedPresetsPath = Path.Combine(projectRoot, "howtomake", "include", "macro_presets.inc.c");

                            if (File.Exists(selectedPresetsPath))
                            {
                                presets = macroParser.ParsePresets(selectedPresetsPath);
                            }

                            var levelPath = selectedLevel.LevelPath;
                            var macroFiles = Directory.GetFiles(levelPath, "macro.inc.c", SearchOption.AllDirectories);
                            foreach (var macroFile in macroFiles)
                            {
                                if (File.ReadAllText(macroFile).Contains(macroListName))
                                {
                                    var macroObjects = macroParser.ParseMacroFile(macroFile, presets);
                                    objects.AddRange(macroObjects);
                                    break;
                                }
                            }
                        }
                    }
                    catch (Exception ex) { Console.WriteLine($"Error loading macro objects: {ex.Message}"); }

                    try
                    {
                        if (areaName != null && collisionFilePath != null)
                        {
                            var specialParser = new SpecialObjectParser();
                            Dictionary<string, string> presetMapping = new();
                            var selectedIncludePath = ResolveIncludePath("special_presets.inc.c");
                            if (selectedIncludePath != null) presetMapping = specialParser.ParsePresets(selectedIncludePath);

                            string colContent = File.ReadAllText(collisionFilePath);
                            var specialMatches = SpecialObjectPattern.Matches(colContent);
                            var specialWithYawMatches = SpecialObjectWithYawPattern.Matches(colContent);
                            var specialWithYawAndParamMatches = SpecialObjectWithYawAndParamPattern.Matches(colContent);

                            void ProcessSpecialObject(string preset, int x, int y, int z, int ry, string sourceFile, int sourceIndex, int sourceLength)
                            {
                                if (presetMapping.TryGetValue(preset, out string? modelId))
                                {
                                    string resolvedModelName = modelId;
                                    
                                    // Handle numeric model IDs (e.g. "0x1D" or "29")
                                    int numericId = -1;
                                    if (modelId.StartsWith("0x"))
                                    {
                                        int.TryParse(modelId.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out numericId);
                                    }
                                    else if (int.TryParse(modelId, out int parsedId))
                                    {
                                        numericId = parsedId;
                                    }

                                    // Try to resolve alias if direct match fails
                                    if (!modelToGeo.ContainsKey(modelId))
                                    {
                                        int id = numericId;
                                        if (id == -1 && modelIdMapping.TryGetValue(modelId, out int mId))
                                        {
                                            id = mId;
                                        }

                                        if (id != -1 && idToGeo.TryGetValue(id, out string? loadedModelName))
                                        {
                                            resolvedModelName = modelToGeo.Keys.FirstOrDefault(k => modelIdMapping.TryGetValue(k, out int lid) && lid == id) ?? modelId;
                                        }
                                    }

                                    objects.Add(new LevelObject 
                                    { 
                                        ModelName = resolvedModelName, 
                                        PresetName = preset,
                                        Behavior = "(Special Object)", 
                                        X = x, 
                                        Y = y, 
                                        Z = z, 
                                        RY = ry, 
                                        SourceFile = sourceFile, 
                                        SourceIndex = sourceIndex, 
                                        SourceLength = sourceLength,
                                        SourceType = ObjectSourceType.Special
                                    });
                                }
                            }

                            foreach (Match m in specialMatches)
                            {
                                ProcessSpecialObject(m.Groups[1].Value.Trim(), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value), int.Parse(m.Groups[4].Value), 0, collisionFilePath, m.Index, m.Length);
                            }
                            foreach (Match m in specialWithYawMatches)
                            {
                                int byteYaw = int.Parse(m.Groups[5].Value);
                                int degreeYaw = (int)(byteYaw * 360.0 / 256.0);
                                ProcessSpecialObject(m.Groups[1].Value.Trim(), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value), int.Parse(m.Groups[4].Value), degreeYaw, collisionFilePath, m.Index, m.Length);
                            }
                            foreach (Match m in specialWithYawAndParamMatches)
                            {
                                int byteYaw = int.Parse(m.Groups[5].Value);
                                int degreeYaw = (int)(byteYaw * 360.0 / 256.0);
                                ProcessSpecialObject(m.Groups[1].Value.Trim(), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value), int.Parse(m.Groups[4].Value), degreeYaw, collisionFilePath, m.Index, m.Length);
                            }
                        }
                    }
                    catch (Exception ex) { Console.WriteLine($"Error loading special objects: {ex.Message}"); }

                    _currentObjects = objects;
                    
                    // Parse supported models for safety validation
                    var supportedModels = objectParser.ParseSupportedModels(selectedLevel.LevelPath, projectRoot ?? "");
                    
                    // Open the new Level Editor window
                    var editor = new LevelEditorWindow(objects, collisionMesh, visualMesh, projectRoot ?? "", areaIndex, supportedModels);
                    editor.Show();
                }
            }
        }
        catch (Exception ex) { MessageBox.Show($"Error launching 3D editor:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
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

    private string? FindGeoLayoutForModel(string areaPath, string geoLayoutName)
{
    try
    {
        var geoFiles = Directory.GetFiles(areaPath, "geo.inc.c", SearchOption.AllDirectories);
        foreach (var file in geoFiles)
        {
            if (File.ReadAllText(file).Contains(geoLayoutName)) return file;
        }
    }
        catch { }
        return null;
    }

    private string? ResolveIncludePath(string fileName)
    {
        if (_selectedLevel == null) return null;

        // 1. Search upwards from level path
        string? current = _selectedLevel.LevelPath;
        while (!string.IsNullOrEmpty(current))
        {
            // Check in include, howtomake/include, leveleditor/include
            var subDirs = new[] { "include", "howtomake/include", "leveleditor/include", "levels/include" };
            foreach (var sub in subDirs)
            {
                var path = Path.Combine(current, sub.Replace('/', Path.DirectorySeparatorChar), fileName);
                if (File.Exists(path)) return path;
            }

            current = Path.GetDirectoryName(current);
            if (current == null || current.Length < 3) break; // Don't go above drive root
        }

        // 2. Try project root directly as fallback
        if (!string.IsNullOrEmpty(_projectRootPath))
        {
            var rootSubDirs = new[] { "include", "leveleditor/include", "howtomake/include" };
            foreach (var sub in rootSubDirs)
            {
                var path = Path.Combine(_projectRootPath, sub.Replace('/', Path.DirectorySeparatorChar), fileName);
                if (File.Exists(path)) return path;
            }
        }

        return null;
    }
}

