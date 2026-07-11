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
        private readonly string? _activeLevelPath;
        private readonly Action? _onReloadRequest;

        public ToolsWindow(string projectRoot, string? activeLevelPath = null, Action? onReloadRequest = null)
        {
            InitializeComponent();
            _projectRoot = projectRoot;
            _activeLevelPath = activeLevelPath;
            _onReloadRequest = onReloadRequest;

            if (!string.IsNullOrEmpty(_activeLevelPath))
            {
                ActiveLevelText.Text = $"Active Level Path: {Path.GetFileName(_activeLevelPath)}";
            }
            else
            {
                ActiveLevelText.Text = "Active Level: None (Select a level to enable level tools)";
            }
        }

        private void LevelMeshEditor_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_activeLevelPath))
            {
                MessageBox.Show("This tool requires a level to be selected. Please select a level or open it in the level editor first.", "Level Specific Tool", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var meshEditor = new LevelMeshEditorWindow(_activeLevelPath, "Area 1", _projectRoot, _onReloadRequest);
            meshEditor.Owner = this;
            meshEditor.Show();
        }

        private void WarpEditor_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_activeLevelPath))
            {
                MessageBox.Show("This tool requires a level to be selected. Please select a level or open it in the level editor first.", "Level Specific Tool", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var warpEditor = new WarpEditorWindow(_activeLevelPath, _onReloadRequest);
            warpEditor.Owner = this;
            warpEditor.Show();
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
                    var revertWindow = new BuildOutputWindow(_projectRoot, isRevertMode: true);
                    revertWindow.Owner = this;
                    revertWindow.ShowDialog();

                    if (revertWindow.IsSuccessful)
                    {
                        _onReloadRequest?.Invoke();
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
