using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Collections.Generic;

namespace Sm64DecompLevelViewer
{
    public partial class ToolsWindow : Window
    {
        private readonly string _projectRoot;
        private string? _activeLevelPath;
        private readonly string? _initialArea;
        private bool _loadingLevels;
        private readonly Action? _onReloadRequest;

        public ToolsWindow(string projectRoot, string? activeLevelPath = null, Action? onReloadRequest = null, string? initialArea = null)
        {
            InitializeComponent();
            _projectRoot = projectRoot;
            _activeLevelPath = activeLevelPath;
            _onReloadRequest = onReloadRequest;
            _initialArea = initialArea;
            Loaded += (_, _) => LoadLevelChoices();

            if (!string.IsNullOrEmpty(_activeLevelPath))
            {
                ActiveLevelText.Text = $"Active Level Path: {Path.GetFileName(_activeLevelPath)}";
            }
            else
            {
                ActiveLevelText.Text = "Active Level: None (Select a level to enable level tools)";
            }
        }

        private void LoadLevelChoices()
        {
            try
            {
                _loadingLevels = true;
                var scanner = new Services.LevelScanner();
                string scanRoot = Directory.Exists(Path.Combine(_projectRoot, "levels")) ? _projectRoot : Path.Combine(_projectRoot, "howtomake");
                var levels = scanner.ScanLevels(scanRoot);
                LevelPicker.ItemsSource = levels;
                LevelPicker.SelectedItem = levels.FirstOrDefault(l => string.Equals(l.LevelPath, _activeLevelPath, StringComparison.OrdinalIgnoreCase));
                _loadingLevels = false;
                UpdateLevelContext();
            }
            catch (Exception ex) { _loadingLevels = false; ActiveLevelText.Text = "Level scan failed: " + ex.Message; }
        }
        private void LevelPicker_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e) { if (!_loadingLevels && AreaPicker != null) UpdateLevelContext(); }
        private void UpdateLevelContext()
        {
            string? previousArea = AreaPicker.SelectedItem as string ?? _initialArea;
            var level = LevelPicker.SelectedItem as Models.LevelMetadata;
            _activeLevelPath = level?.LevelPath;
            ActiveLevelText.Text = level == null ? "choose a level for level-specific tools" : level.FullName + "  |  " + level.LevelPath;
            var areas = level == null ? new List<string>() : new Services.CollisionParser().FindCollisionFiles(level.LevelPath).Keys.OrderBy(k => int.TryParse(System.Text.RegularExpressions.Regex.Match(k, @"\d+").Value, out int n) ? n : int.MaxValue).ToList();
            AreaPicker.ItemsSource = areas;
            AreaPicker.SelectedItem = previousArea != null && areas.Contains(previousArea) ? previousArea : areas.FirstOrDefault();
            MeshToolButton.IsEnabled = areas.Count > 0;
            PaintingToolButton.IsEnabled = level != null && new Services.PaintingService().FindPaintingFile(level.LevelPath) != null;
        }
        private void TextEditor_Click(object sender, RoutedEventArgs e) => new TextEditorWindow(_projectRoot) { Owner = this }.Show();
        private void PatchTools_Click(object sender, RoutedEventArgs e) => new PatchWindow(_projectRoot) { Owner = this }.Show();
        private void RomTools_Click(object sender, RoutedEventArgs e) => new RomAddressWindow(_projectRoot) { Owner = this }.Show();

        private void LevelMeshEditor_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_activeLevelPath))
            {
                MessageBox.Show("This tool requires a level to be selected. Please select a level or open it in the level editor first.", "Level Specific Tool", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var meshEditor = new LevelMeshEditorWindow(_activeLevelPath, AreaPicker.SelectedItem as string ?? "Area 1", _projectRoot, _onReloadRequest);
            meshEditor.Owner = this;
            meshEditor.Show();
        }

        private void PaintingEditor_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_activeLevelPath))
            {
                MessageBox.Show("This tool requires a level to be selected. Please select a level or open it in the level editor first.", "Level Specific Tool", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var paintingService = new Sm64DecompLevelViewer.Services.PaintingService();
            string? paintingPath = paintingService.FindPaintingFile(_activeLevelPath);
            if (string.IsNullOrEmpty(paintingPath) || !File.Exists(paintingPath))
            {
                MessageBox.Show("No painting.inc.c file found in this level's folders.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var paintingEditor = new PaintingEditorWindow(_activeLevelPath, _onReloadRequest);
            paintingEditor.Owner = this;
            paintingEditor.Show();
        }

        private void ActorEditor_Click(object sender, RoutedEventArgs e)
        {
            var actorEditor = new ActorEditorWindow(_projectRoot);
            actorEditor.Owner = this;
            actorEditor.ShowDialog();
        }

        private void MusicEditor_Click(object sender, RoutedEventArgs e)
        {
            var musicEditor = new MusicEditorWindow(_projectRoot);
            musicEditor.Owner = this;
            musicEditor.Show();
        }

        private void SoundEditor_Click(object sender, RoutedEventArgs e)
        {
            var soundEditor = new SoundEditorWindow(_projectRoot);
            soundEditor.Owner = this;
            soundEditor.Show();
        }

        private void BuildRom_Click(object sender, RoutedEventArgs e)
        {
            var optionsWindow = new BuildOptionsWindow(_projectRoot);
            optionsWindow.Owner = this;
            if (optionsWindow.ShowDialog() == true)
            {
                if (optionsWindow.IsCleanAndClone)
                {
                    var cleanWindow = new BuildOutputWindow(_projectRoot, optionsWindow.GitUrlToClone);
                    cleanWindow.Owner = this;
                    cleanWindow.ShowDialog();

                    if (cleanWindow.IsSuccessful)
                    {
                        _onReloadRequest?.Invoke();
                    }
                }
                else if (optionsWindow.IsRevertSource)
                {
                    var revertSelectWindow = new RevertChangesWindow(_projectRoot) { Owner = this };
                    if (revertSelectWindow.ShowDialog() == true)
                    {
                        var revertWindow = new BuildOutputWindow(_projectRoot, revertSelectWindow.SelectedBackupFiles, revertSelectWindow.RunGitRevert);
                        revertWindow.Owner = this;
                        revertWindow.ShowDialog();

                        if (revertWindow.IsSuccessful)
                        {
                            _onReloadRequest?.Invoke();
                        }
                    }
                }
                else
                {
                    var buildWindow = new BuildOutputWindow(_projectRoot, optionsWindow.SelectedSettings);
                    buildWindow.Owner = this;
                    buildWindow.ShowDialog();
                }
            }
        }

        private void ChaosEngine_Click(object sender, RoutedEventArgs e)
        {
            var chaos = new ChaosEngineWindow(_projectRoot);
            chaos.Owner = this;
            chaos.Show();
        }

        private void BehaviorBuilder_Click(object sender, RoutedEventArgs e)
        {
            var builder = new CustomBehaviorBuilderWindow(_projectRoot);
            builder.Owner = this;
            builder.ShowDialog();
        }

        private void OpenProjectFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start("explorer.exe", _projectRoot);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to open project folder: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
