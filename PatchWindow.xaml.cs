using System;
using System.IO;
using System.Linq;
using System.Windows;
using Microsoft.Win32;

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

        public PatchWindow(string projectRoot)
        {
            InitializeComponent();
            _projectRoot = projectRoot;

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

        private string ConvertToWslPath(string windowsPath)
        {
            if (string.IsNullOrEmpty(windowsPath)) return string.Empty;

            string path = windowsPath.Replace("\\", "/");

            // Handle UNC paths pointing to WSL, e.g. //wsl.localhost/Ubuntu-20.04/root/sm64/... -> /root/sm64/...
            // or //wsl$/Ubuntu-20.04/root/sm64/... -> /root/sm64/...
            if (path.StartsWith("//wsl.localhost/", StringComparison.OrdinalIgnoreCase))
            {
                int nextSlash = path.IndexOf('/', 16); // Find slash after wsl.localhost/distro_name
                if (nextSlash != -1)
                {
                    return path.Substring(nextSlash);
                }
            }
            else if (path.StartsWith("//wsl$/", StringComparison.OrdinalIgnoreCase))
            {
                int nextSlash = path.IndexOf('/', 7); // Find slash after wsl$/distro_name
                if (nextSlash != -1)
                {
                    return path.Substring(nextSlash);
                }
            }

            // Standard Windows drive paths (e.g. C:/...)
            if (path.Length >= 2 && path[1] == ':')
            {
                char drive = char.ToLower(path[0]);
                path = $"/mnt/{drive}{path.Substring(2)}";
            }
            return path;
        }

        private void ApplyGit_Click(object sender, RoutedEventArgs e)
        {
            string patchFile = PatchPathText.Text.Trim();
            if (string.IsNullOrEmpty(patchFile) || !File.Exists(patchFile))
            {
                MessageBox.Show("Please select a valid patch file first.", "Invalid File", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string wslPath = ConvertToWslPath(patchFile);
            string command = $"git -C /root/sm64 apply --ignore-whitespace --verbose \"{wslPath}\"";
            RunWslCommand(command);
        }

        private void ApplyPatchCmd_Click(object sender, RoutedEventArgs e)
        {
            string patchFile = PatchPathText.Text.Trim();
            if (string.IsNullOrEmpty(patchFile) || !File.Exists(patchFile))
            {
                MessageBox.Show("Please select a valid patch file first.", "Invalid File", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string wslPath = ConvertToWslPath(patchFile);
            string command = $"patch -p1 -d /root/sm64 -i \"{wslPath}\"";
            RunWslCommand(command);
        }

        private void RunWslCommand(string command)
        {
            LogOutputText.Clear();
            LogOutputText.AppendText($"> Executing: {command}\n\n");

            try
            {
                var processInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "wsl",
                    Arguments = $"sh -c \"{command.Replace("\"", "\\\"")}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var process = System.Diagnostics.Process.Start(processInfo))
                {
                    if (process == null)
                    {
                        LogOutputText.AppendText("Error: Failed to start WSL process.\n");
                        return;
                    }

                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();
                    process.WaitForExit();

                    if (!string.IsNullOrEmpty(output))
                    {
                        LogOutputText.AppendText(output + "\n");
                    }
                    if (!string.IsNullOrEmpty(error))
                    {
                        LogOutputText.AppendText("Stderr:\n" + error + "\n");
                    }

                    if (process.ExitCode == 0)
                    {
                        LogOutputText.AppendText("\n🎉 Success! The patch was applied successfully.\n");
                        MessageBox.Show("Patch applied successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        LogOutputText.AppendText($"\n❌ Failed with exit code {process.ExitCode}.\n");
                        MessageBox.Show("Failed to apply patch. Check console output logs for errors.", "Patch Failure", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                LogOutputText.AppendText($"Exception occurred: {ex.Message}\n");
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
