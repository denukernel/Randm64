using System;
using System.IO;
using System.Linq;
using System.Windows;
using Microsoft.Win32;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Collections.Generic;
using Sm64DecompLevelViewer.Services;

namespace Sm64DecompLevelViewer
{
    public partial class PatchWindow : Window
    {
        public class PatchFileItem
        {
            public string Name { get; set; } = string.Empty;
            public string FullPath { get; set; } = string.Empty;

            public override string ToString() => Name;
        }

        private readonly string _projectRoot;
        private bool _running;


        public PatchWindow(string projectRoot)
        {
            InitializeComponent();
            _projectRoot = projectRoot;
            Closing += (_, e) => { if (_running) e.Cancel = true; };
            LogOutputText.AppendText("selected project: " + _projectRoot + "\n\n");

            ScanEnhancementsFolder();
        }

        private void ScanEnhancementsFolder()
        {
            AvailablePatchesList.Items.Clear();
            string enhancementsPath = Path.Combine(_projectRoot, "enhancements");
            
            if (Directory.Exists(enhancementsPath))
            {
                try
                {
                    var files = Directory.GetFiles(enhancementsPath, "*.*")
                        .Where(file => file.EndsWith(".patch", StringComparison.OrdinalIgnoreCase) || 
                                       file.EndsWith(".diff", StringComparison.OrdinalIgnoreCase))
                        .OrderBy(f => Path.GetFileName(f));

                    foreach (var file in files)
                    {
                        AvailablePatchesList.Items.Add(new PatchFileItem
                        {
                            Name = Path.GetFileName(file),
                            FullPath = file
                        });
                    }

                    LogOutputText.AppendText($"Scanned enhancements folder: found {AvailablePatchesList.Items.Count} patches.\n\n");
                }
                catch (Exception ex)
                {
                    LogOutputText.AppendText($"Error scanning enhancements: {ex.Message}\n\n");
                }
            }
            else
            {
                LogOutputText.AppendText($"Enhancements folder not found at: {enhancementsPath}\n\n");
            }
        }

        private void RescanButton_Click(object sender, RoutedEventArgs e)
        {
            ScanEnhancementsFolder();
        }

        private void AvailablePatchesList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (AvailablePatchesList.SelectedItem is PatchFileItem selected)
            {
                PatchPathText.Text = selected.FullPath;
            }
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "Patch Files (*.patch;*.diff;*.txt)|*.patch;*.diff;*.txt|All Files (*.*)|*.*",
                Title = "Select Decomp Patch File"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                PatchPathText.Text = openFileDialog.FileName;
                AvailablePatchesList.SelectedItem = null; // Clear list selection since a custom file was chosen
            }
        }

        private async void ApplyGit_Click(object sender, RoutedEventArgs e) => await ApplyExternalAsync(true);
        private async void ApplyPatchCmd_Click(object sender, RoutedEventArgs e) => await ApplyExternalAsync(false);

        private async Task ApplyExternalAsync(bool git)
        {
            if (_running) return;
            string patch = PatchPathText.Text.Trim();
            if (!File.Exists(patch)) { MessageBox.Show("Select a valid patch file", "Patch", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            _running = true;
            if (Content is UIElement panel) panel.IsEnabled = false;
            LogOutputText.Clear();
            try
            {
                LogOutputText.AppendText("Target: " + _projectRoot + "\nChecking patch applicability...\n");
                if (await RunAsync(WslPatchCommand.Create(_projectRoot, patch, git, true)) != 0)
                    throw new InvalidOperationException("Patch preflight failed; nothing was applied");
                if (await RunAsync(WslPatchCommand.Create(_projectRoot, patch, git, false)) != 0)
                    throw new InvalidOperationException("Patch command failed; inspect the output before retrying");
                LogOutputText.AppendText("Patch applied successfully\n");
            }
            catch (Exception ex) { LogOutputText.AppendText("Error: " + ex.Message + "\n"); }
            finally { _running = false; if (Content is UIElement current) current.IsEnabled = true; }
        }

        private async Task<int> RunAsync(ProcessStartInfo info)
        {
            using var process = Process.Start(info) ?? throw new IOException("Could not start WSL");
            Task<string> output = process.StandardOutput.ReadToEndAsync(), error = process.StandardError.ReadToEndAsync();
            using var timeout = new System.Threading.CancellationTokenSource(TimeSpan.FromMinutes(2));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw new IOException("Patch command timed out; inspect the project before retrying"); }
            LogOutputText.AppendText(await output + await error + "\n");
            LogOutputText.ScrollToEnd();
            return process.ExitCode;
        }

        private SourceEditPlan? _builtInPlan;
        private string _planSelection = "";
        private string[] SelectedBuiltIns() => new[]
        {
            SkipGoddardCheck.IsChecked == true ? "skip-head" : "",
            SkipTitleCheck.IsChecked == true ? "skip-title" : "",
            Start99LivesCheck.IsChecked == true ? "lives" : "",
            StageSelectCheck.IsChecked == true ? "stage-select" : ""
        }.Where(id => id.Length > 0).ToArray();
        private void PreviewCPatches_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var selected = SelectedBuiltIns();
                if (selected.Length == 0) throw new InvalidOperationException("Select a supported patch first");
                _builtInPlan = BuiltInPatchCatalog.Prepare(_projectRoot, selected);
                _planSelection = string.Join("|", selected);
                LogOutputText.Text = _builtInPlan.Writes.Count == 0 ? "Selected patches are already applied; no changes" : _builtInPlan.Preview(_projectRoot);
            }
            catch (Exception ex) { _builtInPlan = null; LogOutputText.Text = "Patch preview failed: " + ex.Message; }
        }
        private void ApplyCPatches_Click(object sender, RoutedEventArgs e)
        {
            if (_running) return;
            try
            {
                var selected = SelectedBuiltIns();
                if (selected.Length == 0) throw new InvalidOperationException("Select a supported patch first");
                var plan = _builtInPlan != null && _planSelection == string.Join("|", selected)
                    ? _builtInPlan : BuiltInPatchCatalog.Prepare(_projectRoot, selected);
                plan.Commit();
                LogOutputText.Text = $"{plan.Writes.Count} source files updated in one transaction; first backups retained. build the ROM to validate the result.";
                _builtInPlan = null;
            }
            catch (Exception ex) { LogOutputText.Text = "Patches were not applied: " + ex.Message; _builtInPlan = null; }
        }
        private async void CheckPatch_Click(object sender, RoutedEventArgs e)
        {
            if (_running) return;
            _running = true;
            try
            {
                string patch = PatchPathText.Text.Trim();
                if (!File.Exists(patch)) throw new FileNotFoundException("Select a patch file");
                LogOutputText.Text = "Checking applicability without changing source...\n";
                int result = await RunAsync(WslPatchCommand.Create(_projectRoot, patch, true, true));
                LogOutputText.AppendText(result == 0 ? "Applicable; no source files changed\n" : "Not applicable; no source files changed\n");
            }
            catch (Exception ex) { LogOutputText.AppendText(ex.Message); }
            finally { _running = false; }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
