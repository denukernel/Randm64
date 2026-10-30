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
using Sm64DecompLevelViewer.Services.Levels;
namespace Sm64DecompLevelViewer;
public partial class MainWindow
{
    private void SourceEditorMain_Click(object sender,RoutedEventArgs e)
    {
        if(string.IsNullOrWhiteSpace(_projectRootPath) || !Directory.Exists(_projectRootPath)) { StatusText.Text="Select a project folder first"; return; }
        new SourceEditorWindow(_projectRootPath) { Owner=this }.Show();
    }
    private void SoundReplacerButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_projectRootPath) || !Directory.Exists(_projectRootPath))
        {
            MessageBox.Show("Please select a valid SM64 Project Folder first.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var sfx = new SoundReplacerWindow(_projectRootPath, new List<SoundReplacerWindow.SoundReplacementRule>());
        sfx.Owner = this;
        sfx.ShowDialog();
    }
    private void MusicEditorMain_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_projectRootPath) || !Directory.Exists(_projectRootPath))
        {
            MessageBox.Show("Please select a valid SM64 Project Folder first.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var musicEditor = new MusicEditorWindow(_projectRootPath);
        musicEditor.Owner = this;
        musicEditor.Show();
    }
    private void SoundEditorMain_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_projectRootPath) || !Directory.Exists(_projectRootPath))
        {
            MessageBox.Show("Please select a valid SM64 Project Folder first.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var soundEditor = new SoundEditorWindow(_projectRootPath);
        soundEditor.Owner = this;
        soundEditor.Show();
    }
    private void LevelMeshEditorMain_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_projectRootPath) || !Directory.Exists(_projectRootPath))
        {
            MessageBox.Show("Please select a valid SM64 Project Folder first.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_selectedLevel == null)
        {
            MessageBox.Show("This tool requires a level to be selected. Please select a level in the Levels tab first.", "Level Specific Tool", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var meshEditor = new LevelMeshEditorWindow(_selectedLevel.LevelPath, AreaComboBox.SelectedItem as string ?? "Area 1", _projectRootPath, () =>
        {
            LevelListBox_SelectionChanged(null, null);
        });
        meshEditor.Owner = this;
        meshEditor.Show();
    }
    private void WarpEditorMain_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_projectRootPath) || !Directory.Exists(_projectRootPath))
        {
            MessageBox.Show("Please select a valid SM64 Project Folder first.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_selectedLevel == null)
        {
            MessageBox.Show("This tool requires a level to be selected. Please select a level in the Levels tab first.", "Level Specific Tool", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var warpEditor = new WarpEditorWindow(_selectedLevel.LevelPath, () =>
        {
            LevelListBox_SelectionChanged(null, null);
        }, AreaComboBox.SelectedItem as string);
        warpEditor.Owner = this;
        warpEditor.Show();
    }
    private void PaintingEditorMain_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_projectRootPath) || !Directory.Exists(_projectRootPath))
        {
            MessageBox.Show("Please select a valid SM64 Project Folder first.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_selectedLevel == null)
        {
            MessageBox.Show("This tool requires a level to be selected. Please select a level in the Levels tab first.", "Level Specific Tool", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var paintingService = new Sm64DecompLevelViewer.Services.PaintingService();
        string? paintingPath = paintingService.FindPaintingFile(_selectedLevel.LevelPath);
        if (string.IsNullOrEmpty(paintingPath) || !File.Exists(paintingPath))
        {
            MessageBox.Show("No painting.inc.c file found in this level's folders.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var paintingEditor = new PaintingEditorWindow(_selectedLevel.LevelPath, () =>
        {
            LevelListBox_SelectionChanged(null, null);
        });
        paintingEditor.Owner = this;
        paintingEditor.Show();
    }
    private void BehaviorBuilderButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_projectRootPath) || !Directory.Exists(_projectRootPath))
        {
            MessageBox.Show("Please select a valid SM64 Project Folder first.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var builder = new CustomBehaviorBuilderWindow(_projectRootPath);
        builder.Owner = this;
        builder.ShowDialog();
    }
    private void ActorEditorMain_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_projectRootPath) || !Directory.Exists(_projectRootPath))
        {
            MessageBox.Show("Please select a valid SM64 Project Folder first.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var actorEditor = new ActorEditorWindow(_projectRootPath);
        actorEditor.Owner = this;
        actorEditor.ShowDialog();
    }
    private void BuildRomMain_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_projectRootPath) || !Directory.Exists(_projectRootPath))
        {
            MessageBox.Show("Please select a valid SM64 Project Folder first.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var optionsWindow = new BuildOptionsWindow(_projectRootPath);
        optionsWindow.Owner = this;
        if (optionsWindow.ShowDialog() == true)
        {
            if (optionsWindow.IsCleanAndClone)
            {
                var cleanWindow = new BuildOutputWindow(_projectRootPath, optionsWindow.GitUrlToClone);
                cleanWindow.Owner = this;
                cleanWindow.ShowDialog();

                if (cleanWindow.IsSuccessful)
                {
                    LoadLevels();
                }
            }
            else if (optionsWindow.IsRevertSource)
            {
                var revertSelectWindow = new RevertChangesWindow(_projectRootPath) { Owner = this };
                if (revertSelectWindow.ShowDialog() == true)
                {
                    var revertWindow = new BuildOutputWindow(_projectRootPath, revertSelectWindow.SelectedBackupFiles, revertSelectWindow.RunGitRevert);
                    revertWindow.Owner = this;
                    revertWindow.ShowDialog();

                    if (revertWindow.IsSuccessful)
                    {
                        // Editors hold object anchors, material caches and mesh snapshots from before recovery.
                        foreach(var editor in Application.Current.Windows.OfType<LevelEditorWindow>().ToArray()) editor.Close();
                        foreach(var editor in Application.Current.Windows.OfType<LevelMeshEditorWindow>().ToArray()) editor.Close();
                        _renderer?.RequestCloseThreadSafe();
                        _renderer=null;
                        _currentObjects.Clear();
                        _currentVisualMesh=null;
                        _currentCollisionMesh=null;
                        LoadLevels();
                    }
                }
            }
            else
            {
                var buildWindow = new BuildOutputWindow(_projectRootPath, optionsWindow.SelectedSettings);
                buildWindow.Owner = this;
                buildWindow.ShowDialog();
            }
        }
    }
    private void PatchButtonMain_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_projectRootPath) || !Directory.Exists(_projectRootPath))
        {
            MessageBox.Show("Please select a valid SM64 Project Folder first.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var patchWindow = new PatchWindow(_projectRootPath);
        patchWindow.Owner = this;
        patchWindow.ShowDialog();
    }
    private void RomAddressFinderMain_Click(object sender, RoutedEventArgs e)
    {
        var addressWindow = new RomAddressWindow(_projectRootPath);
        addressWindow.Owner = this;
        addressWindow.Show();
    }
    private void OptionsButton_Click(object sender, RoutedEventArgs e)
    {
        var settingsWindow = new SettingsWindow();
        settingsWindow.Owner = this;
        settingsWindow.ShowDialog();
    }
    private void ChaosEngineButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_projectRootPath) || !Directory.Exists(_projectRootPath))
        {
            MessageBox.Show("Please select a valid SM64 Project Folder first.", "Chaos Engine Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var chaosWindow = new ChaosEngineWindow(_projectRootPath)
        {
            Owner = this
        };
        chaosWindow.ShowDialog();
    }
    private void TextEditorMain_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_projectRootPath) || !Directory.Exists(_projectRootPath)) { StatusText.Text = "Select a project folder first"; return; }
        new TextEditorWindow(_projectRootPath) { Owner = this }.Show();
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
                try { _renderer.RequestCloseThreadSafe(); } catch { }
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

                    objects = LevelEditorLaunchData.LoadObjects(selectedLevel, projectRoot, areaName, collisionFilePath, areaIndex, macroParser);

                    _currentObjects = objects;
                    
                    // Parse supported models for safety validation
                    var supportedModels = objectParser.ParseSupportedModels(selectedLevel.LevelPath, projectRoot ?? "");
                    
                    // Open the new Level Editor window
                    var settings = _settingsService.LoadSettings();
                    var editor = new LevelEditorWindow(objects, collisionMesh, visualMesh, projectRoot ?? "", areaIndex, supportedModels, settings, collisionFilePath ?? "");
                    editor.GeometryChanged += async (s, ev) =>
                    {
                        if (_selectedLevel?.LevelPath == selectedLevel.LevelPath &&
                            AreaComboBox.SelectedItem is string selectedArea && selectedArea == areaName)
                            await LoadCollisionMesh(selectedArea);
                    };
                    
                    editor.RequestReload += (s, ev) =>
                    {
                        Dispatcher.BeginInvoke(new Action(async () =>
                        {
                            editor.Close();
                            // Re-parse the meshes from the newly cloned files
                            if (_selectedLevel?.LevelPath != selectedLevel.LevelPath ||
                                !Equals(AreaComboBox.SelectedItem, areaName)) return;
                            await LoadCollisionMesh(areaName!);
                            if (_selectedLevel?.LevelPath != selectedLevel.LevelPath ||
                                !Equals(AreaComboBox.SelectedItem, areaName)) return;
                            // Re-parse objects and open LevelEditorWindow again
                            View3DButton_Click(this, new RoutedEventArgs());
                        }));
                    };

                    editor.Show();
                }
            }
        }
        catch (Exception ex) { MessageBox.Show($"Error launching 3D editor:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
    }
}
