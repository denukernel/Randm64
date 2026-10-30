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
            if (LevelSlider == null || AlternativeComboBox == null || LevelValueText == null || DescriptionText == null) return;
            int alternative = AlternativeComboBox.SelectedIndex;
            LevelSlider.IsEnabled = alternative == 0;
            if (IncludeBranchesCheck != null) IncludeBranchesCheck.IsEnabled = alternative == 0;
            if (alternative > 0)
            {
                LevelValueText.Text = "Severity " + alternative;
                DescriptionText.Text = AlternativeExclusionService.Descriptions[alternative];
                return;
            }
            int level = (int)LevelSlider.Value;
            LevelValueText.Text = level == 0 ? "Disabled" : $"{level * 10}% of target hashes";
            DescriptionText.Text = level == 0
                ? "Removes this mode's exclusions and recognized legacy hooks. Other source edits are preserved."
                : "Selected nested display-list calls in actors/ and levels/ emit no-ops. Calls to the same target use the same decision. Excluded branch tails return instead of falling through. The seed chooses another pattern; actual coverage varies with the available targets.";
        }

        private void Alternative_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => UpdateDescription();

        private async void Apply_Click(object sender, RoutedEventArgs e)
        {
            if (!uint.TryParse(SeedTextBox.Text, NumberStyles.None, CultureInfo.InvariantCulture, out uint seed))
            {
                MessageBox.Show("Enter an integer seed from 0 to 4294967295.", "Invalid Seed", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            await ApplyAsync((int)LevelSlider.Value, seed, AlternativeComboBox.SelectedIndex);
        }

        private async void Restore_Click(object sender, RoutedEventArgs e) => await ApplyAsync(0, 0);

        private async Task ApplyAsync(int level, uint seed, int alternative = 0)
        {
            if (_busy) return;
            bool includeBranches = IncludeBranchesCheck.IsChecked == true;
            _busy = true;
            if (Content is UIElement content) content.IsEnabled = false;
            DescriptionText.Text = "Scanning and staging display-list exclusions...";
            using var cancellation = new CancellationTokenSource();
            var busy = ChaosControls.Busy(this, cancellation);
            try
            {
                if (!Directory.Exists(_projectRoot)) throw new DirectoryNotFoundException("Load a valid SM64 project first.");
                var prepared = await Task.Run(() => DisplayListExclusionService.Prepare(_projectRoot, level, seed, includeBranches, cancellation.Token, alternative));
                using var plan = prepared.Plan;
                busy.Hide();
                cancellation.Token.ThrowIfCancellationRequested();
                if (!ChaosControls.Preview(this, plan)) throw new OperationCanceledException();
                busy.Show();
                var committed = await Task.Run(() => plan.Commit(cancellation.Token));
                if (committed.Status == ChaosRunStatus.Cancelled) throw new OperationCanceledException();
                if (committed.Status != ChaosRunStatus.Completed) throw new IOException(committed.Status + ": " + committed.Message + "\n" + committed.RecoveryPath);
                var result = prepared.Result;
                string report = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Randm64", "Settings", "ChaosPresets", "runs", Guid.NewGuid().ToString("N") + ".json");
                try { plan.SaveReport(report); }
                catch (Exception ex) { MessageBox.Show(this, "Changes completed; could not save report: " + ex.Message); }
                string message = alternative > 0 ? $"Applied alternative severity {alternative}; {result.Files} source files changed.\n{AlternativeExclusionService.Descriptions[alternative]}" : level == 0
                    ? $"Removed managed exclusions; {result.Files} source files changed."
                    : $"Excluded {result.Excluded} of {result.Calls} static call/branch sites; {result.Files} source files changed.";
                if (alternative == 0 && level > 0 && result.Excluded == 0) message += "\nNo calls matched this density and seed. Try a higher density or another seed.";
                MessageBox.Show(message + "\n\nRebuild the ROM to see the result.",
                    "Display-List Exclusion", MessageBoxButton.OK, MessageBoxImage.Information);
                if (level == 0 && alternative == 0) { LevelSlider.Value = 0; AlternativeComboBox.SelectedIndex = 0; }
            }
            catch (OperationCanceledException) { DescriptionText.Text = "Cancelled; pre-run files preserved"; }
            catch (Exception ex)
            {
                MessageBox.Show($"Exclusions were not applied successfully:\n{ex.GetBaseException().Message}", "Exclusion Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                busy.Tag = "finished"; busy.Close();
                _busy = false;
                if (Content is UIElement current) current.IsEnabled = true;
                UpdateDescription();
            }
        }
    }
}
