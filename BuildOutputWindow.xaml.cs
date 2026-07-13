using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Sm64DecompLevelViewer.Services;

namespace Sm64DecompLevelViewer
{
    public partial class BuildOutputWindow : Window
    {
        private readonly string _projectRoot;
        private readonly AppSettings? _settings;
        private readonly string? _gitUrlToClone;
        private readonly bool _isRevertMode = false;
        private readonly List<string>? _bakFilesToRestore;
        private readonly bool _runGitRevert = false;
        private bool _isBuildSuccessful = false;

        public bool IsSuccessful => _isBuildSuccessful;

        // Constructor for compilation mode
        public BuildOutputWindow(string projectRoot, AppSettings settings)
        {
            InitializeComponent();
            _projectRoot = projectRoot;
            _settings = settings;
            _gitUrlToClone = null;
            
            Loaded += async (s, e) => await StartBuildAsync();
        }

        // Constructor for clean & clone mode
        public BuildOutputWindow(string projectRoot, string gitUrlToClone)
        {
            InitializeComponent();
            _projectRoot = projectRoot;
            _settings = null;
            _gitUrlToClone = gitUrlToClone;
            
            Loaded += async (s, e) => await StartCleanAndCloneAsync();
        }

        // Constructor for revert mode
        public BuildOutputWindow(string projectRoot, List<string>? bakFilesToRestore, bool runGitRevert)
        {
            InitializeComponent();
            _projectRoot = projectRoot;
            _settings = null;
            _gitUrlToClone = null;
            _isRevertMode = true;
            _bakFilesToRestore = bakFilesToRestore;
            _runGitRevert = runGitRevert;
            
            Loaded += async (s, e) => await StartRevertSourceAsync();
        }

        // Constructor for revert mode (legacy fallback)
        public BuildOutputWindow(string projectRoot, bool isRevertMode)
            : this(projectRoot, null, true)
        {
        }

        private async Task StartBuildAsync()
        {
            if (_settings == null)
            {
                LogTextBox.AppendText("> ERROR: No settings provided for building.\n");
                return;
            }

            HeaderTitleText.Text = "BUILDING PROJECT (WSL)...";
            StatusText.Text = "Building...";
            LogTextBox.AppendText($"> Building project in: {_projectRoot}\n");

            try
            {
                int jobs = _settings.BuildJobs <= 0 ? 8 : _settings.BuildJobs;
                string version = _settings.BuildVersion;
                string compiler = _settings.BuildCompiler;
                string grucode = _settings.BuildGrucode;
                int compareVal = _settings.BuildCompare ? 1 : 0;
                int nonMatchingVal = _settings.BuildNonMatching ? 1 : 0;

                string uncompressedEnv = "";

                string makeArgs = $"{uncompressedEnv}make -j{jobs} VERSION={version} COMPILER={compiler} GRUCODE={grucode} COMPARE={compareVal} NON_MATCHING={nonMatchingVal}";
                LogTextBox.AppendText($"> Running make command: wsl {makeArgs}\n\n");

                var startInfo = new ProcessStartInfo
                {
                    FileName = "wsl",
                    Arguments = makeArgs,
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
                        if (RomExists())
                        {
                            // ROM padding skipped (BuildExtendRom removed)

                            MainStatusBar.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(40, 167, 69)); // Green
                            StatusText.Text = "Build Succeeded!";
                            LogTextBox.AppendText("\n> BUILD SUCCESSFUL!\n");

                            if (_settings.AutoRunEmulator && !string.IsNullOrEmpty(_settings.EmulatorPath) && File.Exists(_settings.EmulatorPath))
                            {
                                LogTextBox.AppendText($"> Launching emulator: {Path.GetFileName(_settings.EmulatorPath)}\n");
                                LaunchEmulator();
                            }
                            else
                            {
                                LogTextBox.AppendText("> Emulator auto-run skipped (no emulator configured or disabled).\n");
                            }
                        }
                        else
                        {
                            _isBuildSuccessful = false;
                            MainStatusBar.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 53, 69)); // Red
                            StatusText.Text = "Build Error: ROM Not Found";
                            string expectedPath = Path.Combine("build", version, $"sm64.{version}.z64");
                            LogTextBox.AppendText($"\n> ERROR: make reported success, but no ROM file was found at {expectedPath}.\n");
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
                if (ex is System.ComponentModel.Win32Exception || ex.Message.Contains("wsl", StringComparison.OrdinalIgnoreCase))
                {
                    LogTextBox.AppendText("\n> WSL (Windows Subsystem for Linux) might not be installed or configured on your system.\n");
                    LogTextBox.AppendText("> Please install WSL (e.g. Ubuntu 20.04 or newer from the Microsoft Store) to build the ROM.\n");
                    LogTextBox.AppendText("> To install WSL, run: 'wsl --install' in an administrator Command Prompt/PowerShell.\n");
                }
                StatusText.Text = "Execution Error";
            }
        }

        private async Task StartCleanAndCloneAsync()
        {
            if (string.IsNullOrEmpty(_gitUrlToClone))
            {
                LogTextBox.AppendText("> ERROR: No Git repository URL provided.\n");
                return;
            }

            HeaderTitleText.Text = "CLEAN & RE-CLONE PROJECT (WSL)...";
            StatusText.Text = "Cleaning project...";
            LogTextBox.AppendText($"> Cleaning project directory: {_projectRoot}\n");
            LogTextBox.AppendText($"> Git repository URL: {_gitUrlToClone}\n");

            string tempBackupPath = Path.Combine(Path.GetTempPath(), "sm64_baserom_backup_" + Guid.NewGuid().ToString("N"));
            bool hasBackup = false;

            try
            {
                // Step 1: Find and backup baserom files
                Directory.CreateDirectory(tempBackupPath);
                LogTextBox.AppendText("> Backing up baserom files...\n");
                
                string[] baseromFiles = Directory.GetFiles(_projectRoot, "baserom.*");
                foreach (string file in baseromFiles)
                {
                    string fileName = Path.GetFileName(file);
                    string destFile = Path.Combine(tempBackupPath, fileName);
                    File.Copy(file, destFile, true);
                    LogTextBox.AppendText($"> Backed up: {fileName}\n");
                    hasBackup = true;
                }

                if (!hasBackup)
                {
                    LogTextBox.AppendText("> WARNING: No baserom files (baserom.*) found to back up.\n");
                }

                // Step 2: Delete everything in the project root
                LogTextBox.AppendText("> Deleting existing files and directories...\n");
                await Task.Run(() =>
                {
                    foreach (string file in Directory.GetFiles(_projectRoot))
                    {
                        try
                        {
                            File.Delete(file);
                        }
                        catch (Exception ex)
                        {
                            Dispatcher.Invoke(() => LogTextBox.AppendText($"> Warning deleting file {Path.GetFileName(file)}: {ex.Message}\n"));
                        }
                    }

                    foreach (string dir in Directory.GetDirectories(_projectRoot))
                    {
                        try
                        {
                            Directory.Delete(dir, true);
                        }
                        catch (Exception ex)
                        {
                            Dispatcher.Invoke(() => LogTextBox.AppendText($"> Warning deleting directory {Path.GetFileName(dir)}: {ex.Message}\n"));
                        }
                    }
                });

                // Step 3: Run git clone via WSL
                LogTextBox.AppendText($"> Running 'git clone {_gitUrlToClone} .' inside WSL...\n");
                StatusText.Text = "Cloning repository...";

                var startInfo = new ProcessStartInfo
                {
                    FileName = "wsl",
                    Arguments = $"git clone \"{_gitUrlToClone}\" .",
                    WorkingDirectory = _projectRoot,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                bool cloneSuccess = false;
                using (var process = new Process { StartInfo = startInfo })
                {
                    process.OutputDataReceived += (s, e) => AppendLog(e.Data);
                    process.ErrorDataReceived += (s, e) => AppendLog(e.Data);

                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    await Task.Run(() => process.WaitForExit());
                    cloneSuccess = process.ExitCode == 0;
                    
                    if (cloneSuccess)
                    {
                        LogTextBox.AppendText($"> Git clone completed successfully.\n");
                    }
                    else
                    {
                        LogTextBox.AppendText($"> ERROR: Git clone failed with exit code {process.ExitCode}\n");
                    }
                }

                if (!cloneSuccess)
                {
                    throw new Exception("Git clone failed.");
                }

                // Step 4: Restore baserom files
                if (hasBackup)
                {
                    LogTextBox.AppendText("> Restoring baserom files...\n");
                    string[] backedFiles = Directory.GetFiles(tempBackupPath);
                    foreach (string file in backedFiles)
                    {
                        string fileName = Path.GetFileName(file);
                        string destFile = Path.Combine(_projectRoot, fileName);
                        File.Copy(file, destFile, true);
                        LogTextBox.AppendText($"> Restored: {fileName}\n");
                    }
                }

                MainStatusBar.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(40, 167, 69)); // Green
                StatusText.Text = "Clean & Re-clone Succeeded!";
                LogTextBox.AppendText("\n> FRESH START COMPLETE SUCCESSFULLY!\n");
                _isBuildSuccessful = true;
            }
            catch (Exception ex)
            {
                MainStatusBar.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 53, 69)); // Red
                StatusText.Text = "Failed to Clean & Re-clone";
                LogTextBox.AppendText($"\n> FATAL ERROR: {ex.Message}\n");
                if (ex is System.ComponentModel.Win32Exception || ex.Message.Contains("wsl", StringComparison.OrdinalIgnoreCase))
                {
                    LogTextBox.AppendText("\n> WSL (Windows Subsystem for Linux) might not be installed or configured on your system.\n");
                    LogTextBox.AppendText("> Please install WSL (e.g. Ubuntu 20.04 or newer from the Microsoft Store) to run build tools.\n");
                    LogTextBox.AppendText("> To install WSL, run: 'wsl --install' in an administrator Command Prompt/PowerShell.\n");
                }

                // CRITICAL safety fallback: try to restore baseroms even if clone failed
                if (hasBackup)
                {
                    LogTextBox.AppendText("> Attempting to restore original baserom files after failure...\n");
                    try
                    {
                        string[] backedFiles = Directory.GetFiles(tempBackupPath);
                        foreach (string file in backedFiles)
                        {
                            string fileName = Path.GetFileName(file);
                            string destFile = Path.Combine(_projectRoot, fileName);
                            File.Copy(file, destFile, true);
                            LogTextBox.AppendText($"> Restored: {fileName}\n");
                        }
                    }
                    catch (Exception restoreEx)
                    {
                        LogTextBox.AppendText($"> ERROR restoring baserom backup: {restoreEx.Message}\n");
                    }
                }
            }
            finally
            {
                // Clean up backup directory
                try
                {
                    if (Directory.Exists(tempBackupPath))
                    {
                        Directory.Delete(tempBackupPath, true);
                    }
                }
                catch { }
            }
        }

        private async Task StartRevertSourceAsync()
        {
            HeaderTitleText.Text = "REVERTING SOURCE CODE...";
            StatusText.Text = "Reverting code...";
            LogTextBox.AppendText($"> Reverting local changes in project: {_projectRoot}\n");

            try
            {
                // Restore backup files
                try
                {
                    IEnumerable<string> bakFilesToProcess;
                    if (_bakFilesToRestore != null)
                    {
                        bakFilesToProcess = _bakFilesToRestore;
                    }
                    else
                    {
                        bakFilesToProcess = Directory.Exists(_projectRoot) 
                            ? Directory.GetFiles(_projectRoot, "*.bak", SearchOption.AllDirectories) 
                            : Array.Empty<string>();
                    }

                    foreach (var bakFile in bakFilesToProcess)
                    {
                        if (File.Exists(bakFile))
                        {
                            string originalPath = bakFile.Substring(0, bakFile.Length - 4);
                            File.Copy(bakFile, originalPath, true);
                            File.SetLastWriteTime(originalPath, DateTime.Now);
                            File.Delete(bakFile);
                            LogTextBox.AppendText($"> Restored from backup: {Path.GetFileName(originalPath)}\n");
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogTextBox.AppendText($"> Warning restoring backups: {ex.Message}\n");
                }

                bool checkoutSuccess = true;
                if (_runGitRevert)
                {
                    // Run wsl git checkout -f
                    LogTextBox.AppendText("> Running 'git checkout -f' inside WSL...\n");

                    var startInfo = new ProcessStartInfo
                    {
                        FileName = "wsl",
                        Arguments = "sh -c \"git checkout -f && git clean -fd && python3 extract_assets.py us\"",
                        WorkingDirectory = _projectRoot,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    checkoutSuccess = false;
                    using (var process = new Process { StartInfo = startInfo })
                    {
                        process.OutputDataReceived += (s, e) => AppendLog(e.Data);
                        process.ErrorDataReceived += (s, e) => AppendLog(e.Data);

                        process.Start();
                        process.BeginOutputReadLine();
                        process.BeginErrorReadLine();

                        await Task.Run(() => process.WaitForExit());
                        checkoutSuccess = process.ExitCode == 0;
                    }
                }

                if (checkoutSuccess)
                {
                    MainStatusBar.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(40, 167, 69)); // Green
                    StatusText.Text = "Revert Succeeded!";
                    LogTextBox.AppendText("\n> SOURCE REVERT COMPLETE SUCCESSFULLY!\n");
                    _isBuildSuccessful = true;
                }
                else
                {
                    throw new Exception("Git checkout failed.");
                }
            }
            catch (Exception ex)
            {
                MainStatusBar.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 53, 69)); // Red
                StatusText.Text = "Failed to Revert Changes";
                LogTextBox.AppendText($"\n> FATAL ERROR: {ex.Message}\n");
                if (ex is System.ComponentModel.Win32Exception || ex.Message.Contains("wsl", StringComparison.OrdinalIgnoreCase))
                {
                    LogTextBox.AppendText("\n> WSL (Windows Subsystem for Linux) might not be installed or configured on your system.\n");
                    LogTextBox.AppendText("> Please install WSL (e.g. Ubuntu 20.04 or newer from the Microsoft Store) to run build tools.\n");
                    LogTextBox.AppendText("> To install WSL, run: 'wsl --install' in an administrator Command Prompt/PowerShell.\n");
                }
            }
        }

        private async Task<bool> ExtendRomAsync(string version)
        {
            try
            {
                // Ensure setup_extend.sh script is present and compile sm64extend if it doesn't exist
                string setupScriptPath = Path.Combine(_projectRoot, "tools", "sm64tools", "setup_extend.sh");
                // Always write setup_extend.sh to keep it up-to-date and robust
                string scriptContent = @"#!/bin/sh
set -e

CDIR=$(dirname ""$0"")
cd ""$CDIR""

# Patch libmio0.c if not already patched
python3 -c ""
import os
p = 'libmio0.c'
if os.path.exists(p):
    data = open(p).read()
    if 'gMio0Uncompressed' not in data:
        print('> Patching libmio0.c...')
        old_decl = 'unsigned int bytes_proc = 0;'
        new_decl = old_decl + '\n   int gMio0Uncompressed = 0;\n   char *env_uncomp = getenv(\""MIO0_UNCOMPRESSED\"");\n   if (env_uncomp != NULL && strcmp(env_uncomp, \""1\"") == 0) {\n      gMio0Uncompressed = 1;\n   }'
        data = data.replace(old_decl, new_decl)
        
        old_loop = 'while (bytes_proc < length) {'
        new_loop = old_loop + '\n      if (gMio0Uncompressed) {\n         uncomp_buf[uncomp_idx] = in[bytes_proc];\n         uncomp_idx++;\n         PUT_BIT(bit_buf, bit_idx, 1);\n         bytes_proc++;\n         bit_idx++;\n         continue;\n      }'
        data = data.replace(old_loop, new_loop)
        open(p, 'w').write(data)
        
        print('> Rebuilding mio0 tool...')
        import subprocess
        subprocess.run(['make', 'clean'])
        subprocess.run(['make'])
""

# Fetch and patch libsm64.c to support version detection of custom/non-matching ROMs
if [ ! -f ./libsm64.c ]; then
    echo ""sm64extend not found. Fetching source files...""
    curl -L -o sm64extend.c https://raw.githubusercontent.com/queueRAM/sm64tools/master/sm64extend.c
    curl -L -o libsm64.h https://raw.githubusercontent.com/queueRAM/sm64tools/master/libsm64.h
    curl -L -o libsm64.c https://raw.githubusercontent.com/queueRAM/sm64tools/master/libsm64.c
    
    echo ""Patching libsm64.c...""
    sed -i 's/#include ""utils.h""/#include ""utils.h""\n#define INFO_HEX(a,b)/g' libsm64.c
fi

# Apply the version detection fallback patch using a robust heredoc
cat << 'EOF' > patch_libsm64.py
p = 'libsm64.c'
data = open(p).read()
if 'buf[0x3B]' not in data:
    print('> Patching libsm64.c for custom ROM version detection...')
    old_func = 'rom_version sm64_rom_version(unsigned char *buf)\n{'
    new_func = old_func + '''
   if (buf[0x3B] == 'N' && buf[0x3C] == 'S' && buf[0x3D] == 'M') {
      switch (buf[0x3E]) {
         case 'E': return VERSION_SM64_U;
         case 'J': return VERSION_SM64_J;
         case 'P': return VERSION_SM64_E;
         case 'S': return VERSION_SM64_SHINDOU;
         case 'C': return VERSION_SM64_IQUE;
      }
   }'''
    data = data.replace(old_func, new_func)

# Patch the MIO0 header logic to always write fake headers for unmatched ASM references (command == 0)
if 'ptr_table[i].command != 0x18' not in data:
    print('> Patching libsm64.c to enforce fake MIO0 headers on unmatched pointers...')
    data = data.replace('if (ptr_table[i].command == 0x1A || ptr_table[i].command == 0xFF) {', 'if (ptr_table[i].command != 0x18) {')

open(p, 'w').write(data)
EOF
python3 patch_libsm64.py
rm patch_libsm64.py

echo ""Compiling sm64extend...""
gcc -I . sm64extend.c libsm64.c libmio0.c utils.c n64cksum.c -o sm64extend -lm
echo ""Compilation complete!""
";

                string setupScriptDir = Path.GetDirectoryName(setupScriptPath);
                if (!Directory.Exists(setupScriptDir))
                {
                    Directory.CreateDirectory(setupScriptDir);
                }
                File.WriteAllText(setupScriptPath, scriptContent.Replace("\r\n", "\n"));

                // Run setup script
                var setupStartInfo = new ProcessStartInfo
                {
                    FileName = "wsl",
                    Arguments = "sh -c \"chmod +x ./tools/sm64tools/setup_extend.sh && ./tools/sm64tools/setup_extend.sh\"",
                    WorkingDirectory = _projectRoot,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var process = new Process { StartInfo = setupStartInfo })
                {
                    process.OutputDataReceived += (s, e) => AppendLog(e.Data);
                    process.ErrorDataReceived += (s, e) => AppendLog(e.Data);

                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    await Task.Run(() => process.WaitForExit());
                }

                string romName = $"sm64.{version}.z64";
                string extRomName = $"sm64.{version}.ext.z64";
                string buildDir = $"build/{version}";
                string inputRomPath = $"{buildDir}/{romName}";
                string outputRomPath = $"{buildDir}/{extRomName}";

                // Run sm64extend tool compiled in WSL
                // Command: ./tools/sm64tools/sm64extend -s 24 <input> <output>
                string extendArgs = $"./tools/sm64tools/sm64extend -s 24 {inputRomPath} {outputRomPath}";
                LogTextBox.AppendText($"> Running sm64extend command: wsl {extendArgs}\n");

                var startInfo = new ProcessStartInfo
                {
                    FileName = "wsl",
                    Arguments = extendArgs,
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

                    if (process.ExitCode != 0)
                    {
                        LogTextBox.AppendText($"> ERROR: sm64extend failed with exit code {process.ExitCode}\n");
                        return false;
                    }
                }

                // If successful, replace the original ROM with the extended one
                string absoluteInputPath = Path.Combine(_projectRoot, inputRomPath);
                string absoluteOutputPath = Path.Combine(_projectRoot, outputRomPath);

                if (File.Exists(absoluteOutputPath))
                {
                    try
                    {
                        File.Delete(absoluteInputPath);
                        File.Move(absoluteOutputPath, absoluteInputPath);
                    }
                    catch (IOException ioEx)
                    {
                        LogTextBox.AppendText($"\n> ERROR: Cannot replace '{romName}' because it is locked by another process.\n");
                        LogTextBox.AppendText("> Please make sure your emulator (Project64/mupen64) is closed and try building again!\n");
                        LogTextBox.AppendText($"> Technical details: {ioEx.Message}\n");
                        return false;
                    }
                    LogTextBox.AppendText("> Successfully extended ROM and replaced build output with 24MB extended ROM!\n");
                    return true;
                }
                else
                {
                    LogTextBox.AppendText($"> ERROR: Extended ROM file not found at {outputRomPath}\n");
                    return false;
                }
            }
            catch (Exception ex)
            {
                LogTextBox.AppendText($"> ERROR during ROM extension: {ex.Message}\n");
                if (ex is System.ComponentModel.Win32Exception || ex.Message.Contains("wsl", StringComparison.OrdinalIgnoreCase))
                {
                    LogTextBox.AppendText("\n> WSL (Windows Subsystem for Linux) might not be installed or configured on your system.\n");
                    LogTextBox.AppendText("> Please install WSL (e.g. Ubuntu 20.04 or newer from the Microsoft Store) to run build tools.\n");
                    LogTextBox.AppendText("> To install WSL, run: 'wsl --install' in an administrator Command Prompt/PowerShell.\n");
                }
                return false;
            }
        }

        private bool RomExists()
        {
            if (_settings == null) return false;
            string version = _settings.BuildVersion;
            string p = Path.Combine(_projectRoot, "build", version, $"sm64.{version}.z64");
            return File.Exists(p);
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
                if (_settings == null) return;
                string version = _settings.BuildVersion;
                string romPath = Path.Combine(_projectRoot, "build", version, $"sm64.{version}.z64");

                if (File.Exists(romPath))
                {
                    string emulatorPath = _settings.EmulatorPath;
                    Process.Start(emulatorPath, $"\"{romPath}\"");
                }
                else
                {
                    Dispatcher.Invoke(() => LogTextBox.AppendText($"> ERROR: Built ROM not found in build/ subfolders.\n"));
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => LogTextBox.AppendText($"> ERROR launching emulator: {ex.Message}\n"));
            }
        }
    }
}
