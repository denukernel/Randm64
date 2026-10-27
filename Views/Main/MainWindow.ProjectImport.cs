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
                "this folder already has a wsl copy:\n\n" + previous
                + "\n\nopen that copy, including any edits made there?\n"
                + "yes: open the wsl copy\nno: choose whether to import a fresh copy\ncancel: keep the current project",
                "open wsl project", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (reuse == MessageBoxResult.Yes) return previous;
            if (reuse != MessageBoxResult.No) return null;
        }
        var choice = MessageBox.Show(this,
            "building from a windows folder can be much slower.\n\n"
            + "import this project into the default wsl distribution for faster builds? "
            + "randm64 will edit and build the wsl copy. the windows folder will stay as a backup. "
            + "the two copies will not sync automatically.\n\n"
            + "yes: import into wsl\nno: use the windows folder\ncancel: keep the current project",
            "faster builds in wsl", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (choice == MessageBoxResult.No) return selectedPath;
        if (choice != MessageBoxResult.Yes) return null;

        string? imported = null;
        bool busy = true;
        var progress = new Window
        {
            Owner = this, Title = "importing project into wsl", Width = 460, Height = 170,
            ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock
        {
            Text = "copying project files into wsl...\nlarge repositories may take a few minutes. the windows folder will stay as a backup.",
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
                MessageBox.Show(progress, ex.Message + "\n\nselect the folder again to retry or use the windows folder.",
                    "wsl import failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally { busy = false; progress.Close(); }
        };
        progress.ShowDialog();
        return imported;
    }
}
