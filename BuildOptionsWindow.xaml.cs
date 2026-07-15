using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using Sm64DecompLevelViewer.Services;

namespace Sm64DecompLevelViewer
{
    public partial class BuildOptionsWindow : Window
    {
        private readonly string _projectRoot;
        private readonly SettingsService _settingsService;
        private readonly AppSettings _settings;

        public bool IsCleanAndClone { get; private set; } = false;
        public string GitUrlToClone { get; private set; } = string.Empty;
        public bool IsRevertSource { get; private set; } = false;
        public AppSettings SelectedSettings { get; private set; }

        public BuildOptionsWindow(string projectRoot)
        {
            InitializeComponent();
            _projectRoot = projectRoot;
            _settingsService = new SettingsService();
            _settings = _settingsService.LoadSettings();
            SelectedSettings = _settings;

            LoadSettingsIntoUi();
        }

        private void EnvComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateVisibility();
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
            // Set fields from settings
            SetComboBoxValue(PlatformComboBox, _settings.BuildTargetPlatform == "PC" ? "PC Port" : "N64 ROM");
            SetComboBoxValue(EnvComboBox, _settings.BuildEnvironment == "MSYS2" ? "MSYS2 (Native Windows)" : "WSL (Ubuntu)");
            MsysPathTextBox.Text = _settings.MsysPath;
            PopulateVersions();
            PopulateCompilers();
            PopulateGrucodes();
            JobsTextBox.Text = _settings.BuildJobs.ToString();
            CompareCheckBox.IsChecked = _settings.BuildCompare;
            NonMatchingCheckBox.IsChecked = _settings.BuildNonMatching;
            ExtendRomCheckBox.IsChecked = false;
            EmulatorPathTextBox.Text = _settings.EmulatorPath;
            AutoRunCheckBox.IsChecked = _settings.AutoRunEmulator;
            
            UpdateVisibility();

            // Detect Git URL
            string? detectedUrl = DetectGitUrl(_projectRoot);
            if (!string.IsNullOrEmpty(detectedUrl))
            {
                GitUrlTextBox.Text = detectedUrl;
            }
            else if (!string.IsNullOrEmpty(_settings.GitRepositoryUrl))
            {
                GitUrlTextBox.Text = _settings.GitRepositoryUrl;
            }
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
            if (PlatformComboBox == null || CompilerComboBox == null || CompareCheckBox == null || 
                ExtendRomCheckBox == null || EmulatorPathTextBox == null || BrowseEmulatorButton == null || AutoRunCheckBox == null) return;

            string platform = GetComboBoxValue(PlatformComboBox);
            if (platform == "PC Port")
            {
                // Force and disable Compiler
                SetComboBoxValue(CompilerComboBox, "gcc");
                CompilerComboBox.IsEnabled = false;

                // Disable checkboxes
                CompareCheckBox.IsChecked = false;
                CompareCheckBox.IsEnabled = false;
                ExtendRomCheckBox.IsChecked = false;
                ExtendRomCheckBox.IsEnabled = false;

                // Disable emulator path controls
                EmulatorPathTextBox.IsEnabled = false;
                BrowseEmulatorButton.IsEnabled = false;

                // Rename autorun checkbox
                AutoRunCheckBox.Content = "Auto-launch PC Port game after successful build";
            }
            else
            {
                // Enable Compiler
                CompilerComboBox.IsEnabled = true;
                if (_settings != null)
                {
                    SetComboBoxValue(CompilerComboBox, _settings.BuildCompiler);
                }

                // Enable checkboxes
                CompareCheckBox.IsEnabled = true;
                if (_settings != null)
                {
                    CompareCheckBox.IsChecked = _settings.BuildCompare;
                }
                ExtendRomCheckBox.IsEnabled = true;

                // Enable emulator path controls
                EmulatorPathTextBox.IsEnabled = true;
                BrowseEmulatorButton.IsEnabled = true;

                // Rename autorun checkbox
                AutoRunCheckBox.Content = "Auto-launch emulator after successful build";
            }

            UpdateVisibility();
        }

        private void CompilerComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CompilerComboBox == null || NonMatchingCheckBox == null) return;

            string compiler = GetComboBoxValue(CompilerComboBox);
            if (compiler == "gcc")
            {
                // gcc requires non-matching
                NonMatchingCheckBox.IsChecked = true;
                NonMatchingCheckBox.IsEnabled = false;
            }
            else
            {
                NonMatchingCheckBox.IsEnabled = true;
                NonMatchingCheckBox.IsChecked = _settings.BuildNonMatching;
            }
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
                _settingsService.SaveSettings(_settings);

                DialogResult = true;
                Close();
            }
        }

        private void RevertSource_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Are you sure you want to revert all source code changes?\n\n" +
                "This will discard modifications you made to levels and C source code files, but will keep all your built ROMs, tools, and baseroms intact.",
                "Revert Source Changes",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                IsRevertSource = true;
                DialogResult = true;
                Close();
            }
        }

        private void Build_Click(object sender, RoutedEventArgs e)
        {
            // Validate jobs input
            if (!int.TryParse(JobsTextBox.Text, out int jobs) || jobs <= 0)
            {
                jobs = 8;
            }

            // Save settings
            _settings.BuildTargetPlatform = GetComboBoxValue(PlatformComboBox) == "PC Port" ? "PC" : "N64";
            _settings.BuildEnvironment = GetComboBoxValue(EnvComboBox) == "MSYS2 (Native Windows)" ? "MSYS2" : "WSL";
            _settings.MsysPath = MsysPathTextBox.Text;
            _settings.BuildVersion = GetComboBoxValue(VersionComboBox);
            _settings.BuildCompiler = GetComboBoxValue(CompilerComboBox);
            _settings.BuildGrucode = GetComboBoxValue(GrucodeComboBox);
            _settings.BuildJobs = jobs;
            _settings.BuildCompare = CompareCheckBox.IsChecked ?? false;
            _settings.BuildNonMatching = NonMatchingCheckBox.IsChecked ?? false;
            _settings.EmulatorPath = EmulatorPathTextBox.Text;
            _settings.AutoRunEmulator = AutoRunCheckBox.IsChecked ?? true;
            _settings.GitRepositoryUrl = GitUrlTextBox.Text;

            _settingsService.SaveSettings(_settings);
            SelectedSettings = _settings;

            IsCleanAndClone = false;
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void HelpButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string infoKey)
            {
                string title = "";
                string message = "";

                switch (infoKey)
                {
                    case "PC_MSYS2":
                        title = "PC Port: MSYS2 MinGW64";
                        message = "MSYS2 MinGW64 is highly stable for compiling and running the native PC Port. It produces native Windows executables directly linked with native DirectX and SDL2 drivers, avoiding any virtualization layers.";
                        break;
                    case "PC_WSL":
                        title = "PC Port: WSL (Ubuntu)";
                        message = "WSL requires translation layers (like X11 forwarding or PulseAudio servers) to run graphical and audio outputs. This can lead to timing mismatches, audio stuttering, or sync control frame drops.";
                        break;
                    case "PC_GCC":
                        title = "PC Port: GCC Compiler";
                        message = "GCC is the standard and recommended compiler for PC Port builds. It handles modern C/C++ language features and performance optimizations required for native execution.";
                        break;
                    case "PC_IDO":
                        title = "PC Port: IDO Compiler";
                        message = "IDO is a legacy compiler from the 1990s used specifically for building the original N64 hardware ROMs. It is not supported and cannot compile native PC executables.";
                        break;
                    case "N64_IDO":
                        title = "SM64 N64 Decomp: IDO Compiler";
                        message = "IDO is the original compiler used by Nintendo to compile Super Mario 64. Using IDO is highly recommended because it guarantees a byte-matching, accurate ROM output.";
                        break;
                    case "N64_GCC":
                        title = "SM64 N64 Decomp: GCC Compiler";
                        message = "GCC compiling N64 targets is technically functional but is considered kinda broken and unstable. It does not produce a byte-matching ROM and can introduce game physics bugs.";
                        break;
                }

                MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void PopulateCompilers()
        {
            if (CompilerComboBox == null) return;

            CompilerComboBox.Items.Clear();

            string makefilePath = Path.Combine(_projectRoot, "Makefile");
            bool supportsIdo = true;
            bool supportsClang = false;

            if (File.Exists(makefilePath))
            {
                try
                {
                    string content = File.ReadAllText(makefilePath);
                    if (content.Contains("must be one of the following: gcc clang") || 
                        content.Contains("must be one of: gcc clang") ||
                        (content.Contains("COMPILER") && !content.Contains("ido")))
                    {
                        supportsIdo = false;
                    }
                    if (content.Contains("clang"))
                    {
                        supportsClang = true;
                    }
                }
                catch { }
            }

            if (supportsIdo)
            {
                CompilerComboBox.Items.Add(new ComboBoxItem { Content = "ido" });
            }
            CompilerComboBox.Items.Add(new ComboBoxItem { Content = "gcc" });
            if (supportsClang)
            {
                CompilerComboBox.Items.Add(new ComboBoxItem { Content = "clang" });
            }

            // Set default / loaded selection
            string savedCompiler = _settings?.BuildCompiler ?? "gcc";
            bool selected = false;
            foreach (ComboBoxItem item in CompilerComboBox.Items)
            {
                if (item.Content.ToString() == savedCompiler)
                {
                    CompilerComboBox.SelectedItem = item;
                    selected = true;
                    break;
                }
            }

            if (!selected && CompilerComboBox.Items.Count > 0)
            {
                CompilerComboBox.SelectedIndex = 0;
            }
        }

        private void PopulateGrucodes()
        {
            if (GrucodeComboBox == null) return;

            GrucodeComboBox.Items.Clear();

            string makefilePath = Path.Combine(_projectRoot, "Makefile");
            List<string> grucodes = new List<string>();

            if (File.Exists(makefilePath))
            {
                try
                {
                    string content = File.ReadAllText(makefilePath);
                    
                    // Match 1: validate-option,GRUCODE,opt1 opt2 opt3
                    var matchValidate = Regex.Match(content, @"validate-option,GRUCODE,\s*([^)]+)");
                    if (matchValidate.Success)
                    {
                        var opts = matchValidate.Groups[1].Value.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        grucodes.AddRange(opts);
                    }
                    else
                    {
                        // Match 2: Value of GRUCODE must be one of the following: opt1 opt2...
                        var matchError = Regex.Match(content, @"Value of GRUCODE must be one of (?:the following:)?\s*([^.\r\n]+)");
                        if (matchError.Success)
                        {
                            var opts = matchError.Groups[1].Value.Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries);
                            grucodes.AddRange(opts);
                        }
                    }
                }
                catch { }
            }

            // Fallback to standard ones if none found
            if (grucodes.Count == 0)
            {
                grucodes.AddRange(new[] { "f3d_old", "f3d_new", "f3dex", "f3dex2", "f3dzex" });
            }

            foreach (var code in grucodes)
            {
                GrucodeComboBox.Items.Add(new ComboBoxItem { Content = code });
            }

            // Set default / loaded selection
            string savedGrucode = _settings?.BuildGrucode ?? "f3d_old";
            bool selected = false;
            foreach (ComboBoxItem item in GrucodeComboBox.Items)
            {
                if (item.Content.ToString() == savedGrucode)
                {
                    GrucodeComboBox.SelectedItem = item;
                    selected = true;
                    break;
                }
            }

            if (!selected && GrucodeComboBox.Items.Count > 0)
            {
                // Try default value from Makefile: "GRUCODE ?= [value]"
                string defaultVal = "f3d_old";
                try
                {
                    if (File.Exists(makefilePath))
                    {
                        string content = File.ReadAllText(makefilePath);
                        var matchDefault = Regex.Match(content, @"\bGRUCODE\s*\?=\s*(\S+)");
                        if (matchDefault.Success)
                        {
                            defaultVal = matchDefault.Groups[1].Value;
                        }
                    }
                }
                catch { }

                bool selectDefault = false;
                foreach (ComboBoxItem item in GrucodeComboBox.Items)
                {
                    if (item.Content.ToString() == defaultVal)
                    {
                        GrucodeComboBox.SelectedItem = item;
                        selectDefault = true;
                        break;
                    }
                }

                if (!selectDefault)
                {
                    GrucodeComboBox.SelectedIndex = 0;
                }
            }
        }

        private void PopulateVersions()
        {
            if (VersionComboBox == null) return;

            VersionComboBox.Items.Clear();

            // 1. Scan for present baserom files in project root
            var presentVersions = new List<string>();
            string[] possibleVersions = { "us", "jp", "eu", "sh", "cn" };
            
            try
            {
                string[] files = Directory.GetFiles(_projectRoot, "baserom.*");
                foreach (string file in files)
                {
                    string filename = Path.GetFileName(file).ToLower();
                    // Extract version (e.g. baserom.us.z64 -> us)
                    string[] parts = filename.Split('.');
                    if (parts.Length >= 2)
                    {
                        string ver = parts[1];
                        if (Array.Exists(possibleVersions, v => v == ver) && !presentVersions.Contains(ver))
                        {
                            presentVersions.Add(ver);
                        }
                    }
                }
            }
            catch { }

            // 2. If no baseroms found, check Makefile allowed versions
            if (presentVersions.Count == 0)
            {
                string makefilePath = Path.Combine(_projectRoot, "Makefile");
                if (File.Exists(makefilePath))
                {
                    try
                    {
                        string content = File.ReadAllText(makefilePath);
                        var matchValidate = Regex.Match(content, @"validate-option,VERSION,\s*([^)]+)");
                        if (matchValidate.Success)
                        {
                            var opts = matchValidate.Groups[1].Value.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                            presentVersions.AddRange(opts);
                        }
                        else
                        {
                            var matchError = Regex.Match(content, @"Value of VERSION must be one of (?:the following:)?\s*([^.\r\n]+)");
                            if (matchError.Success)
                            {
                                var opts = matchError.Groups[1].Value.Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries);
                                presentVersions.AddRange(opts);
                            }
                        }
                    }
                    catch { }
                }
            }

            // Fallback to all standard versions if still empty
            if (presentVersions.Count == 0)
            {
                presentVersions.AddRange(possibleVersions);
            }

            foreach (var ver in presentVersions)
            {
                VersionComboBox.Items.Add(new ComboBoxItem { Content = ver });
            }

            // Set default / loaded selection
            string savedVersion = _settings?.BuildVersion ?? "us";
            bool selected = false;
            foreach (ComboBoxItem item in VersionComboBox.Items)
            {
                if (item.Content.ToString() == savedVersion)
                {
                    VersionComboBox.SelectedItem = item;
                    selected = true;
                    break;
                }
            }

            if (!selected && VersionComboBox.Items.Count > 0)
            {
                // Try to find default from Makefile: "VERSION ?= [value]"
                string defaultVal = "us";
                try
                {
                    string makefilePath = Path.Combine(_projectRoot, "Makefile");
                    if (File.Exists(makefilePath))
                    {
                        string content = File.ReadAllText(makefilePath);
                        var matchDefault = Regex.Match(content, @"\bVERSION\s*\?=\s*(\S+)");
                        if (matchDefault.Success)
                        {
                            defaultVal = matchDefault.Groups[1].Value;
                        }
                    }
                }
                catch { }

                bool selectDefault = false;
                foreach (ComboBoxItem item in VersionComboBox.Items)
                {
                    if (item.Content.ToString() == defaultVal)
                    {
                        VersionComboBox.SelectedItem = item;
                        selectDefault = true;
                        break;
                    }
                }

                if (!selectDefault)
                {
                    VersionComboBox.SelectedIndex = 0;
                }
            }
        }
    }
}
