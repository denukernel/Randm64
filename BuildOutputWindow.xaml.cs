using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace Sm64DecompLevelViewer
{
    public partial class BuildOutputWindow : Window
    {
        private readonly string _projectRoot;
        private readonly string _emulatorPath;
        private bool _isBuildSuccessful = false;

        public BuildOutputWindow(string projectRoot, string emulatorPath)
        {
            InitializeComponent();
            _projectRoot = projectRoot;
            _emulatorPath = emulatorPath;
            
            Loaded += async (s, e) => await StartBuildAsync();
        }

        private async Task StartBuildAsync()
        {
            StatusText.Text = "Building...";
            LogTextBox.AppendText($"> Building project in: {_projectRoot}\n");

            try
            {
                // Run wsl make -j8 COMPARE=0
                var startInfo = new ProcessStartInfo
                {
                    FileName = "wsl",
                    Arguments = $"make -j8 COMPARE=0",
                    WorkingDirectory = _projectRoot,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var process = new Process { StartInfo = startInfo })
                {
                    process.OutputDataReceived += (s, e) => AppendLog(e.Data);
                    process.ErrorDataReceived += (s, e) => AppendLog(e.Data);

                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    await Task.Run(() => process.WaitForExit());

                    _isBuildSuccessful = process.ExitCode == 0;
                    
                    if (_isBuildSuccessful)
                    {
                        // Check if ROM exists even if make succeeded
                        if (RomExists())
                        {
                            MainStatusBar.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(40, 167, 69)); // Green
                            StatusText.Text = "Build Succeeded!";
                            LogTextBox.AppendText("\n> BUILD SUCCESSFUL!\n");

                            if (!string.IsNullOrEmpty(_emulatorPath) && File.Exists(_emulatorPath))
                            {
                                LogTextBox.AppendText($"> Launching emulator: {Path.GetFileName(_emulatorPath)}\n");
                                LaunchEmulator();
                            }
                            else
                            {
                                LogTextBox.AppendText("> No emulator path configured or emulator not found. Build only.\n");
                            }
                        }
                        else
                        {
                            _isBuildSuccessful = false;
                            MainStatusBar.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 53, 69)); // Red
                            StatusText.Text = "Build Error: ROM Not Found";
                            LogTextBox.AppendText("\n> ERROR: make reported success, but no ROM file was found in build/.\n");
                        }
                    }
                    else
                    {
                        MainStatusBar.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 53, 69)); // Red
                        StatusText.Text = "Build Failed";
                        LogTextBox.AppendText($"\n> BUILD FAILED with exit code {process.ExitCode}\n");
                    }
                }
            }
            catch (Exception ex)
            {
                MainStatusBar.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 53, 69)); // Red
                LogTextBox.AppendText($"\n> FATAL ERROR: {ex.Message}\n");
                StatusText.Text = "Execution Error";
            }
        }

        private bool RomExists()
        {
            string[] potentialRoms = {
                Path.Combine(_projectRoot, "build", "us", "sm64.us.z64"),
                Path.Combine(_projectRoot, "build", "jp", "sm64.jp.z64"),
                Path.Combine(_projectRoot, "build", "eu", "sm64.eu.z64")
            };

            foreach (var p in potentialRoms)
            {
                if (File.Exists(p)) return true;
            }
            return false;
        }

        private void AppendLog(string? data)
        {
            if (data == null) return;
            Dispatcher.Invoke(() =>
            {
                LogTextBox.AppendText(data + "\n");
                LogScrollViewer.ScrollToEnd();
            });
        }

        private void LaunchEmulator()
        {
            try
            {
                // Find the built ROM. Usually build/us/sm64.us.z64
                // We should check common locations
                string[] potentialRoms = {
                    Path.Combine(_projectRoot, "build", "us", "sm64.us.z64"),
                    Path.Combine(_projectRoot, "build", "jp", "sm64.jp.z64"),
                    Path.Combine(_projectRoot, "build", "eu", "sm64.eu.z64")
                };

                string? romPath = null;
                foreach (var p in potentialRoms)
                {
                    if (File.Exists(p))
                    {
                        romPath = p;
                        break;
                    }
                }

                if (romPath != null)
                {
                    Process.Start(_emulatorPath, $"\"{romPath}\"");
                }
                else
                {
                    Dispatcher.Invoke(() => LogTextBox.AppendText("> ERROR: Built ROM not found in build/ subfolders.\n"));
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => LogTextBox.AppendText($"> ERROR launching emulator: {ex.Message}\n"));
            }
        }
    }
}
