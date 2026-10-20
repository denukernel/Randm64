using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Sm64DecompLevelViewer.Services.Chaos;

namespace Sm64DecompLevelViewer
{
    public partial class DisplayListChaosWindow : Window
    {
        private readonly string _projectRoot;
        private bool _busy;

        public DisplayListChaosWindow(string projectRoot)
        {
            InitializeComponent();
            _projectRoot = projectRoot;
            Closing += (_, e) => { if (_busy) e.Cancel = true; };
            UpdateDescription();
        }

        private void LevelSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (LevelValueText != null && DescriptionText != null) UpdateDescription();
        }

        private void UpdateDescription()
        {
            int level = (int)LevelSlider.Value;
            LevelValueText.Text = level == 0 ? "Disabled" : $"{level * 10}% of target hashes";
            DescriptionText.Text = level == 0
                ? "Removes this mode's exclusions and recognized legacy hooks. Other source edits are preserved."
                : "Selected nested display-list calls in actors/ and levels/ emit no-ops. Calls to the same target use the same decision. Excluded branch tails return instead of falling through. The seed chooses another pattern; actual coverage varies with the available targets.";
        }

        private async void Apply_Click(object sender, RoutedEventArgs e)
        {
            if (!uint.TryParse(SeedTextBox.Text, NumberStyles.None, CultureInfo.InvariantCulture, out uint seed))
            {
                MessageBox.Show("Enter an integer seed from 0 to 4294967295.", "Invalid Seed", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            await ApplyAsync((int)LevelSlider.Value, seed);
        }

        private async void Restore_Click(object sender, RoutedEventArgs e) => await ApplyAsync(0, 0);

        private async Task ApplyAsync(int level, uint seed)
        {
            if (_busy) return;
            bool includeBranches = IncludeBranchesCheck.IsChecked == true;
            _busy = true;
            if (Content is UIElement content) content.IsEnabled = false;
            DescriptionText.Text = "Scanning and staging display-list exclusions...";
            try
            {
                if (!Directory.Exists(_projectRoot)) throw new DirectoryNotFoundException("Load a valid SM64 project first.");
                var result = await Task.Run(() => DisplayListExclusionService.Apply(_projectRoot, level, seed, includeBranches));
                string message = level == 0
                    ? $"Removed managed exclusions; {result.Files} source files changed."
                    : $"Excluded {result.Excluded} of {result.Calls} static call/branch sites; {result.Files} source files changed.";
                if (level > 0 && result.Excluded == 0) message += "\nNo calls matched this density and seed. Try a higher density or another seed.";
                MessageBox.Show(message + "\n\nRebuild the ROM to see the result. These are source-level call exclusions; no runtime movement trigger is installed. HUD and generated skybox code are outside this mode's scope.",
                    "Display-List Exclusion", MessageBoxButton.OK, MessageBoxImage.Information);
                if (level == 0) LevelSlider.Value = 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Exclusions were not applied successfully:\n{ex.GetBaseException().Message}", "Exclusion Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _busy = false;
                if (Content is UIElement current) current.IsEnabled = true;
                UpdateDescription();
            }
        }
    }
}
