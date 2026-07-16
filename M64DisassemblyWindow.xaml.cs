using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace Sm64DecompLevelViewer
{
    public partial class M64DisassemblyWindow : Window
    {
        private readonly string _filePath;
        private readonly string _projectRoot;

        public M64DisassemblyWindow(string filePath, string projectRoot)
        {
            InitializeComponent();
            _filePath = filePath;
            _projectRoot = projectRoot;
            PathLabel.Text = Path.GetFileName(filePath);
            
            Loaded += async (s, e) => await LoadDisassemblyAsync();
        }

        private async Task LoadDisassemblyAsync()
        {
            PathLabel.Text = $"Disassembling {Path.GetFileName(_filePath)} via WSL...";
            DisassemblyTextBox.Text = "Please wait, running seq_decoder.py in WSL...";

            string wslPath = ConvertToWslPath(_filePath);
            string decoderPath = "/root/sm64/tools/seq_decoder.py";

            try
            {
                string result = await Task.Run(() => {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = "wsl",
                        Arguments = $"python3 {decoderPath} \"{wslPath}\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using (var process = Process.Start(startInfo))
                    {
                        if (process == null) return "Failed to start WSL process.";

                        var outputBuilder = new StringBuilder();
                        var errorBuilder = new StringBuilder();

                        process.OutputDataReceived += (sender, args) => { if (args.Data != null) outputBuilder.AppendLine(args.Data); };
                        process.ErrorDataReceived += (sender, args) => { if (args.Data != null) errorBuilder.AppendLine(args.Data); };

                        process.BeginOutputReadLine();
                        process.BeginErrorReadLine();

                        // 5-second timeout protection to avoid hanging on corrupted files with infinite script loops
                        if (!process.WaitForExit(5000))
                        {
                            try { process.Kill(); } catch { }
                            return "Error running seq_decoder.py: Disassembly timed out (likely due to an infinite jump/call loop in the corrupted sequence script).";
                        }

                        if (process.ExitCode != 0)
                        {
                            return $"Error running seq_decoder.py (Exit Code {process.ExitCode}):\n\n{errorBuilder.ToString()}";
                        }
                        return outputBuilder.ToString();
                    }
                });

                DisassemblyTextBox.Text = result;
                PathLabel.Text = $"{Path.GetFileName(_filePath)} - Disassembled successfully";
            }
            catch (Exception ex)
            {
                DisassemblyTextBox.Text = $"Exception during disassembly: {ex.Message}\n\n{ex.StackTrace}";
                PathLabel.Text = "Disassembly failed";
            }
        }

        private string ConvertToWslPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";

            if (path.StartsWith(@"\\wsl.localhost\", StringComparison.OrdinalIgnoreCase))
            {
                int index = path.IndexOf(@"\Ubuntu", StringComparison.OrdinalIgnoreCase);
                if (index != -1)
                {
                    int nextSlash = path.IndexOf('\\', index + 2);
                    if (nextSlash != -1)
                    {
                        return "/" + path.Substring(nextSlash + 1).Replace('\\', '/');
                    }
                }
                index = path.IndexOf(@"\wsl.localhost\", StringComparison.OrdinalIgnoreCase);
                if (index != -1)
                {
                    int nextSlash = path.IndexOf('\\', index + 15);
                    if (nextSlash != -1)
                    {
                        return "/" + path.Substring(nextSlash + 1).Replace('\\', '/');
                    }
                }
            }

            if (path.Length >= 2 && path[1] == ':')
            {
                char drive = char.ToLower(path[0]);
                return $"/mnt/{drive}{path.Substring(2).Replace('\\', '/')}";
            }

            return path.Replace('\\', '/');
        }

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(DisassemblyTextBox.Text))
            {
                Clipboard.SetText(DisassemblyTextBox.Text);
                MessageBox.Show("Disassembly copied to clipboard!", "Copied", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadDisassemblyAsync();
        }
    }
}
