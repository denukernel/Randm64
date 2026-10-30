using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using Sm64DecompLevelViewer.Services;
using Sm64DecompLevelViewer.Services.Build;
using System.Text.Json;

namespace Sm64DecompLevelViewer
{
    public partial class BuildOptionsWindow : Window
    {
        private readonly string _projectRoot;
        private readonly SettingsService? _settingsService;
        private readonly AppSettings _settings;

        public bool IsCleanAndClone { get; private set; } = false;
        public string GitUrlToClone { get; private set; } = string.Empty;
        public bool IsRevertSource { get; private set; } = false;
        public AppSettings SelectedSettings { get; private set; }

        private readonly RepositoryBuildProfile _profile;
        private AppSettings _draft;
        private bool _initializing = true;
        private readonly Dictionary<string, AppSettings> _targetChoices = new();
        private readonly bool _prewarm;

        public BuildOptionsWindow(string projectRoot) : this(projectRoot, null, new SettingsService(), prewarm: true) { }

        internal BuildOptionsWindow(string projectRoot, AppSettings? settings, SettingsService? service = null, bool prewarm = false)
        {
            _projectRoot = projectRoot;
            _settingsService = service;
            _prewarm = prewarm;
            _settings = settings ?? service?.LoadSettings() ?? new AppSettings();
            _draft = Clone(_settings);
            if (_prewarm) WslBuildSessions.Prewarm(projectRoot, _draft);
            _profile = RepositoryBuildProfile.Load(projectRoot);
            SelectedSettings = _settings;
            InitializeComponent();
            LoadSettingsIntoUi();
        }

        private static AppSettings Clone(AppSettings settings) => JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        private void EnvComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateVisibility();
            if (!_initializing && _prewarm)
            {
                var options = Clone(_draft);
                options.BuildEnvironment = GetComboBoxValue(EnvComboBox) == "MSYS2 (Native Windows)" ? "MSYS2" : "WSL";
                WslBuildSessions.Prewarm(_projectRoot, options);
            }
        }

        private void BrowseMsysButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                ValidateNames = false,
                CheckFileExists = false,
                CheckPathExists = true,
                FileName = "Folder Selection"
            };
            if (dialog.ShowDialog() == true)
            {
                MsysPathTextBox.Text = Path.GetDirectoryName(dialog.FileName);
            }
        }

        private void UpdateVisibility()
        {
            if (PlatformComboBox == null || EnvLabel == null || EnvComboBox == null || MsysLabel == null || MsysGrid == null) return;

            string platform = GetComboBoxValue(PlatformComboBox);
            if (platform == "PC Port")
            {
                EnvLabel.Visibility = Visibility.Visible;
                EnvComboBox.Visibility = Visibility.Visible;

                string env = GetComboBoxValue(EnvComboBox);
                if (env == "MSYS2 (Native Windows)")
                {
                    MsysLabel.Visibility = Visibility.Visible;
                    MsysGrid.Visibility = Visibility.Visible;
                }
                else
                {
                    MsysLabel.Visibility = Visibility.Collapsed;
                    MsysGrid.Visibility = Visibility.Collapsed;
                }
            }
            else
            {
                EnvLabel.Visibility = Visibility.Collapsed;
                EnvComboBox.Visibility = Visibility.Collapsed;
                MsysLabel.Visibility = Visibility.Collapsed;
                MsysGrid.Visibility = Visibility.Collapsed;
            }
        }

        private void LoadSettingsIntoUi()
        {
            _targetChoices[_draft.BuildTargetPlatform] = Clone(_draft);
            var changes = _profile.Normalize(_draft);
            Fill(PlatformComboBox, (_profile.SupportsN64 ? new[] { "N64 ROM" } : Array.Empty<string>())
                .Concat(_profile.SupportsPc ? new[] { "PC Port" } : Array.Empty<string>()), _draft.BuildTargetPlatform == "PC" ? "PC Port" : "N64 ROM");
            SetComboBoxValue(EnvComboBox, _draft.BuildEnvironment == "MSYS2" ? "MSYS2 (Native Windows)" : "WSL (Ubuntu)");
            MsysPathTextBox.Text = _draft.MsysPath;
            JobsTextBox.Text = BuildCommandFactory.Jobs(_draft).ToString();
            EmulatorPathTextBox.Text = _draft.EmulatorPath;
            AutoRunCheckBox.IsChecked = _draft.AutoRunEmulator;
            GitUrlTextBox.Text = DetectGitUrl(_projectRoot) ?? _draft.GitRepositoryUrl;
            RenderChoices();
            ProfileNotice.Text = _profile.Error ?? _profile.Name + (changes.Count > 0 ? ": " + string.Join("; ", changes) : "");
            BuildButton.IsEnabled = _profile.Error == null;
            _initializing = false;
        }

        private void Fill(ComboBox combo, IEnumerable<string> values, string selected)
        {
            combo.Items.Clear();
            foreach (string value in values) combo.Items.Add(new ComboBoxItem { Content = value });
            SetComboBoxValue(combo, selected);
        }

        private void RenderChoices()
        {
            bool previous = _initializing;
            _initializing = true;
            bool pc = _draft.BuildTargetPlatform == "PC";
            Fill(VersionComboBox, _profile.Versions, _draft.BuildVersion);
            Fill(CompilerComboBox, pc ? new[] { "gcc" } : _profile.Compilers, _draft.BuildCompiler);
            Fill(GrucodeComboBox, pc && _profile.Has("GRUCODE") ? new[] { "f3dex2e" } : _profile.Microcodes, _draft.BuildGrucode);
            CompilerComboBox.IsEnabled = !pc && _profile.Has("COMPILER") && _profile.Compilers.Length > 1;
            GrucodeComboBox.IsEnabled = !pc && _profile.Microcodes.Length > 1;
            CompareCheckBox.IsChecked = _draft.BuildCompare;
            CompareCheckBox.IsEnabled = !pc && _profile.Has("COMPARE");
            CorruptorRomCheckBox.IsChecked = _draft.BuildCorruptorRom;
            CorruptorRomCheckBox.IsEnabled = !pc;
            NonMatchingCheckBox.IsChecked = _draft.BuildNonMatching;
            NonMatchingCheckBox.IsEnabled = !_profile.RequiresNonMatching(_draft) && _profile.Has("NON_MATCHING");
            EmulatorPathTextBox.IsEnabled = BrowseEmulatorButton.IsEnabled = !pc;
            AutoRunCheckBox.Content = pc ? "Auto-launch PC Port game after successful build" : "Auto-launch emulator after successful build";
            UpdateVisibility();
            UpdateRomStatus();
            _initializing = previous;
        }

        private void VersionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_initializing) UpdateRomStatus();
        }

        private void UpdateRomStatus()
        {
            if (RomStatusText == null) return;
            string version = GetComboBoxValue(VersionComboBox);
            string destination = BaseRomSelection.Destination(_projectRoot, version);
            bool present = File.Exists(destination);
            RomStatusText.Text = present ? $"Ready: {Path.GetFileName(destination)}"
                : _settings.BaseRomSources.TryGetValue(version, out string? source) && File.Exists(source)
                    ? $"Missing {Path.GetFileName(destination)}. Will restore from: {source}"
                    : $"Missing {Path.GetFileName(destination)}. Select a ROM to continue.";
            RomStatusText.ToolTip = destination;
        }

        private async void SelectRom_Click(object sender, RoutedEventArgs e)
        {
            await SelectRomAsync();
        }

        private async Task<bool> SelectRomAsync()
        {
            string version = GetComboBoxValue(VersionComboBox);
            string destination = BaseRomSelection.Destination(_projectRoot, version);
            var dialog = new OpenFileDialog
            {
                Title = $"Select ROM ({version})",
                Filter = "Nintendo 64 ROM (*.z64)|*.z64",
                CheckFileExists = true
            };
            if (_settings.BaseRomSources.TryGetValue(version, out string? previous) && File.Exists(previous))
                dialog.InitialDirectory = Path.GetDirectoryName(previous);
            if (dialog.ShowDialog(this) != true) return false;
            bool replace = File.Exists(destination)
                && !Path.GetFullPath(dialog.FileName).Equals(Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase);
            if (replace && MessageBox.Show(this, $"Replace {destination} with the selected ROM?", "Select ROM",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return false;
            IsEnabled = false;
            try
            {
                await Task.Run(() => BaseRomSelection.Import(dialog.FileName, _projectRoot, version, replace));
                _settings.BaseRomSources[version] = dialog.FileName;
                _settingsService?.SaveSettings(_settings);
                UpdateRomStatus();
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Could not select ROM", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
            finally { IsEnabled = true; }
        }

        private async Task<bool> EnsureBaseRomAsync()
        {
            string version = GetComboBoxValue(VersionComboBox);
            if (File.Exists(BaseRomSelection.Destination(_projectRoot, version))) return true;
            if (_settings.BaseRomSources.TryGetValue(version, out string? source) && File.Exists(source))
            {
                IsEnabled = false;
                try
                {
                    await Task.Run(() => BaseRomSelection.Import(source, _projectRoot, version));
                    UpdateRomStatus();
                    return true;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, $"Could not restore the remembered ROM: {ex.Message}\nSelect the ROM again.",
                        "Select ROM", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                finally { IsEnabled = true; }
            }
            return await SelectRomAsync();
        }

        private void CaptureChoices()
        {
            _draft.BuildVersion = GetComboBoxValue(VersionComboBox);
            _draft.BuildCompiler = GetComboBoxValue(CompilerComboBox);
            _draft.BuildGrucode = GrucodeComboBox.SelectedItem == null ? "" : GetComboBoxValue(GrucodeComboBox);
            _draft.BuildCompare = CompareCheckBox.IsChecked == true;
            _draft.BuildCorruptorRom = CorruptorRomCheckBox.IsChecked == true;
            _draft.BuildNonMatching = NonMatchingCheckBox.IsChecked == true;
        }
        private void SetComboBoxValue(ComboBox comboBox, string value)
        {
            foreach (ComboBoxItem item in comboBox.Items)
            {
                if (item.Content.ToString() == value)
                {
                    comboBox.SelectedItem = item;
                    break;
                }
            }
        }

        private string GetComboBoxValue(ComboBox comboBox)
        {
            if (comboBox.SelectedItem is ComboBoxItem item)
            {
                return item.Content.ToString() ?? "us";
            }
            return "us";
        }

        private string? DetectGitUrl(string projectRoot)
        {
            try
            {
                string configPath = Path.Combine(projectRoot, ".git", "config");
                if (File.Exists(configPath))
                {
                    string[] lines = File.ReadAllLines(configPath);
                    foreach (string line in lines)
                    {
                        var match = Regex.Match(line, @"^\s*url\s*=\s*(.+)$");
                        if (match.Success)
                        {
                            return match.Groups[1].Value.Trim();
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        private void PlatformComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_initializing) return;
            CaptureChoices();
            _targetChoices[_draft.BuildTargetPlatform] = Clone(_draft);
            string target = GetComboBoxValue(PlatformComboBox) == "PC Port" ? "PC" : "N64";
            _draft = Clone(_targetChoices.GetValueOrDefault(target, _settings));
            _draft.BuildTargetPlatform = target;
            var changes = _profile.Normalize(_draft);
            RenderChoices();
            ProfileNotice.Text = _profile.Name + (changes.Count > 0 ? ": " + string.Join("; ", changes) : "");
            if (_prewarm) WslBuildSessions.Prewarm(_projectRoot, _draft);
        }

        private void CompilerComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_initializing) return;
            CaptureChoices();
            _profile.Normalize(_draft);
            RenderChoices();
        }
        private void NumberValidationTextBox(object sender, TextCompositionEventArgs e)
        {
            Regex regex = new Regex("[^0-9]+");
            e.Handled = regex.IsMatch(e.Text);
        }

        private void BrowseEmulator_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Executable Files (*.exe)|*.exe|All Files (*.*)|*.*",
                Title = "Select N64 Emulator"
            };

            if (dialog.ShowDialog() == true)
            {
                EmulatorPathTextBox.Text = dialog.FileName;
            }
        }

        private void CleanAndClone_Click(object sender, RoutedEventArgs e)
        {
            string gitUrl = GitUrlTextBox.Text.Trim();
            if (string.IsNullOrEmpty(gitUrl))
            {
                MessageBox.Show("Please enter a valid Git Repository URL.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var result = MessageBox.Show(
                "Are you sure you want to clean the project?\n\n" +
                "WARNING: This will delete all files and directories in your project folder EXCEPT files starting with 'baserom.', and perform a fresh clone. Any uncommitted changes will be lost.",
                "Clean and Re-clone Project",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                IsCleanAndClone = true;
                GitUrlToClone = gitUrl;
                
                // Save Git URL to settings for convenience
                _settings.GitRepositoryUrl = gitUrl;
                _settingsService?.SaveSettings(_settings);

                DialogResult = true;
                Close();
            }
        }

        private void RevertSource_Click(object sender, RoutedEventArgs e)
        {
            IsRevertSource = true;
            DialogResult = true;
            Close();
        }

        private async void Build_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(JobsTextBox.Text, out int jobs) || jobs <= 0)
            {
                MessageBox.Show("Enter a positive job count.", "Invalid build options", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            CaptureChoices();
            _draft.BuildJobs = jobs;
            _draft.BuildEnvironment = GetComboBoxValue(EnvComboBox) == "MSYS2 (Native Windows)" ? "MSYS2" : "WSL";
            _draft.MsysPath = MsysPathTextBox.Text.Trim();
            _profile.Normalize(_draft);
            string? error = _profile.Validate(_draft);
            try { if (error == null) _profile.ArtifactPath(_draft); }
            catch (Exception ex) { error = ex.Message; }
            if (error != null)
            {
                MessageBox.Show(error, "Invalid build options", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _draft.EmulatorPath = EmulatorPathTextBox.Text.Trim();
            _draft.AutoRunEmulator = AutoRunCheckBox.IsChecked == true;
            _draft.GitRepositoryUrl = GitUrlTextBox.Text.Trim();
            if (!await EnsureBaseRomAsync()) return;
            _draft.BaseRomSources = new Dictionary<string, string>(_settings.BaseRomSources);
            _settingsService?.SaveSettings(_draft);
            SelectedSettings = _draft;
            IsCleanAndClone = false;
            DialogResult = true;
            Close();
        }
        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

    }
}

