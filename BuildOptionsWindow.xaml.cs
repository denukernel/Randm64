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

        private void LoadSettingsIntoUi()
        {
            // Set fields from settings
            SetComboBoxValue(VersionComboBox, _settings.BuildVersion);
            SetComboBoxValue(CompilerComboBox, _settings.BuildCompiler);
            SetComboBoxValue(GrucodeComboBox, _settings.BuildGrucode);
            JobsTextBox.Text = _settings.BuildJobs.ToString();
            CompareCheckBox.IsChecked = _settings.BuildCompare;
            NonMatchingCheckBox.IsChecked = _settings.BuildNonMatching;
            ExtendRomCheckBox.IsChecked = false;
            EmulatorPathTextBox.Text = _settings.EmulatorPath;
            AutoRunCheckBox.IsChecked = _settings.AutoRunEmulator;

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
    }
}
