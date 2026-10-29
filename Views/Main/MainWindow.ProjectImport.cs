using System.IO;
using System.Windows;
using System.Windows.Controls;
using Sm64DecompLevelViewer.Services;
using Sm64DecompLevelViewer.Services.Build;

namespace Sm64DecompLevelViewer;

public partial class MainWindow
{
    private string? OfferWslImport(string selectedPath, AppSettings settings)
    {
        if (!WslProjectImporter.ShouldOffer(selectedPath, settings)) return selectedPath;
        string key = BuildConfigurationState.RepositoryKey(selectedPath);
        if (settings.WslProjectImports.TryGetValue(key, out string? previous) && Directory.Exists(previous))
        {
            var reuse = MessageBox.Show(this,
                "This folder already has a WSL copy:\n\n" + previous
                + "\n\nOpen that copy, including any edits made there?\n"
                + "Yes: Open the WSL copy\nNo: Choose whether to import a fresh copy\nCancel: Keep the current project",
                "Open WSL Project", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (reuse == MessageBoxResult.Yes) return previous;
            if (reuse != MessageBoxResult.No) return null;
        }
        var choice = MessageBox.Show(this,
            "Building from a Windows folder can be much slower.\n\n"
            + "Import this project into the default WSL distribution for faster builds? "
            + "Randm64 will edit and build the WSL copy. The Windows folder will stay as a backup. "
            + "The two copies will not sync automatically.\n\n"
            + "Yes: Import into WSL\nNo: Use the Windows folder\nCancel: Keep the current project",
            "Faster Builds in WSL", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (choice == MessageBoxResult.No) return selectedPath;
        if (choice != MessageBoxResult.Yes) return null;

        string? imported = null;
        bool busy = true;
        var progress = new Window
        {
            Owner = this, Title = "Importing Project Into WSL", Width = 460, Height = 170,
            ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock
        {
            Text = "Copying project files into WSL...\nLarge repositories may take a few minutes. The Windows folder will stay as a backup.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16),
        });
        panel.Children.Add(new ProgressBar { IsIndeterminate = true, Height = 12 });
        progress.Content = panel;
        progress.Closing += (_, e) => e.Cancel = busy;
        progress.Loaded += async (_, _) =>
        {
            try
            {
                imported = await WslProjectImporter.ImportAsync(selectedPath);
                settings.WslProjectImports[key] = imported;
            }
            catch (Exception ex)
            {
                MessageBox.Show(progress, ex.Message + "\n\nSelect the folder again to retry or use the Windows folder.",
                    "WSL Import Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally { busy = false; progress.Close(); }
        };
        progress.ShowDialog();
        return imported;
    }
}
