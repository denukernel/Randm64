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
        private bool _hasRetriedInstall = false;

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

        private void CloseButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            Close();
        }

        private async Task StartBuildAsync()
        {
            if (_settings == null)
            {
                LogTextBox.AppendText("> ERROR: No settings provided for building.\n");
                return;
            }

            bool isPcPort = _settings.BuildTargetPlatform == "PC";
            bool isMsys2 = isPcPort && _settings.BuildEnvironment == "MSYS2";

            HeaderTitleText.Text = isMsys2 ? "BUILDING PROJECT (MSYS2)..." : "BUILDING PROJECT (WSL)...";
            StatusText.Text = "Building...";
            LogTextBox.AppendText($"> Building project in: {_projectRoot}\n");

            // Scan repository to verify target platform correctness
            string pcMainFilePath = Path.Combine(_projectRoot, "src", "pc", "pc_main.c");
            bool hasPcSource = File.Exists(pcMainFilePath);

            if (isPcPort && !hasPcSource)
            {
                LogTextBox.AppendText("> ERROR: Target platform is set to PC, but this repository does not support PC Port compilation (missing 'src/pc/pc_main.c').\n");
                LogTextBox.AppendText("> Please change your Target Platform to N64 ROM, or select a repository that supports PC Port (like sm64ex or sm64-port).\n");
                StatusText.Text = "Build Failed";
                return;
            }

            if (!isPcPort && hasPcSource)
            {
                LogTextBox.AppendText("> ERROR: Target platform is set to N64, but this repository is a PC Port repository and does not support N64 compilation.\n");
                LogTextBox.AppendText("> Please change your Target Platform to PC, or select a repository that supports N64 ROM builds (like vanilla sm64 decomp or HackerSM64).\n");
                StatusText.Text = "Build Failed";
                return;
            }

            var buildLogs = new System.Text.StringBuilder();

            try
            {
                int jobs = _settings.BuildJobs <= 0 ? 8 : _settings.BuildJobs;
                string version = _settings.BuildVersion;
                string compiler = _settings.BuildCompiler;
                string grucode = _settings.BuildGrucode;
                int compareVal = _settings.BuildCompare ? 1 : 0;
                int nonMatchingVal = _settings.BuildNonMatching ? 1 : 0;

                // Verify compiler availability
                bool compilerExists = true;
                if (compiler == "clang" || compiler == "gcc")
                {
                    string checkCmd = $"command -v {compiler}";
                    ProcessStartInfo checkInfo;

                    if (isMsys2)
                    {
                        string msysPath = _settings.MsysPath;
                        string bashPath = Path.Combine(msysPath, "usr", "bin", "bash.exe");
                        checkInfo = new ProcessStartInfo
                        {
                            FileName = bashPath,
                            Arguments = $"-c \"{checkCmd}\"",
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            CreateNoWindow = true
                        };
                    }
                    else // WSL
                    {
                        checkInfo = new ProcessStartInfo
                        {
                            FileName = "wsl.exe",
                            Arguments = $"bash -c \"{checkCmd}\"",
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            CreateNoWindow = true
                        };
                    }

                    try
                    {
                        using (var proc = Process.Start(checkInfo))
                        {
                            proc.WaitForExit();
                            if (proc.ExitCode != 0)
                            {
                                compilerExists = false;
                            }
                        }
                    }
                    catch
                    {
                        compilerExists = false;
                    }
                }

                if (!compilerExists)
                {
                    bool installed = await TryAutoInstallCompilerAsync(compiler, isMsys2);
                    if (!installed)
                    {
                        StatusText.Text = "Build Failed";
                        return;
                    }
                }

                string uncompressedEnv = "";

                if (isPcPort)
                {
                    await PatchPcPortFilesAsync();
                }

                // Check if build platform or environment changed from the last build. If so, clean build subdirectory
                bool envOrPlatformChanged = _settings.BuildEnvironment != _settings.LastBuildEnvironment || 
                                            _settings.BuildTargetPlatform != _settings.LastBuildPlatform;

                if (envOrPlatformChanged)
                {
                    string buildSubdir = isPcPort ? $"{version}_pc" : version;
                    string buildSubdirPath = Path.Combine(_projectRoot, "build", buildSubdir);
                    if (Directory.Exists(buildSubdirPath))
                    {
                        LogTextBox.AppendText($"> Detected change in build platform or environment. Wiping build folder 'build/{buildSubdir}' to prevent Linux/Windows linking conflicts...\n");
                        try
                        {
                            await Task.Run(() => Directory.Delete(buildSubdirPath, true));
                        }
                        catch (Exception ex)
                        {
                            LogTextBox.AppendText($"> Warning: Failed to delete build subdirectory: {ex.Message}\n");
                        }
                    }
                }

                // Update settings tracking
                _settings.LastBuildEnvironment = _settings.BuildEnvironment;
                _settings.LastBuildPlatform = _settings.BuildTargetPlatform;
                var settingsService = new Sm64DecompLevelViewer.Services.SettingsService();
                settingsService.SaveSettings(_settings);

                // Clean tools first only if build environment/platform changed (avoids rebuilding large tools like armips on every compile)
                string cleanToolsPrefix = envOrPlatformChanged ? "make -C tools clean && " : "";
                string makeArgs = isPcPort
                    ? $"{cleanToolsPrefix}{uncompressedEnv}make -j{jobs} VERSION={version} TARGET_N64=0"
                    : $"{cleanToolsPrefix}{uncompressedEnv}make -j{jobs} VERSION={version} TARGET_N64=1 COMPILER={compiler} GRUCODE={grucode} COMPARE={compareVal} NON_MATCHING={nonMatchingVal}";

                ProcessStartInfo startInfo;

                if (isMsys2)
                {
                    string msysPath = _settings.MsysPath;
                    string bashPath = Path.Combine(msysPath, "usr", "bin", "bash.exe");
                    if (!File.Exists(bashPath))
                    {
                        LogTextBox.AppendText($"> ERROR: MSYS2 bash.exe not found at: {bashPath}\n");
                        LogTextBox.AppendText("> Please check your MSYS2 Path configuration in Build Options.\n");
                        MainStatusBar.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 53, 69)); // Red
                        StatusText.Text = "Build Error: MSYS2 Missing";
                        _isBuildSuccessful = false;
                        return;
                    }

                    string projectRootInMsys = _projectRoot.Replace("\\", "/");
                    if (projectRootInMsys.Length >= 2 && projectRootInMsys[1] == ':')
                    {
                        projectRootInMsys = "/" + char.ToLower(projectRootInMsys[0]) + projectRootInMsys.Substring(2);
                    }

                    string makeCmd = $"cd '{projectRootInMsys}' && {makeArgs}";
                    string bashArgs = $"-l -c \"{makeCmd}\"";

                    LogTextBox.AppendText($"> Running make command inside MSYS2 MinGW64: {makeCmd}\n\n");

                    startInfo = new ProcessStartInfo
                    {
                        FileName = bashPath,
                        Arguments = bashArgs,
                        WorkingDirectory = _projectRoot,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    startInfo.Environment["MSYSTEM"] = "MINGW64";
                }
                else
                {
                    LogTextBox.AppendText($"> Running make command inside WSL: {makeArgs}\n\n");

                    startInfo = new ProcessStartInfo
                    {
                        FileName = "wsl",
                        Arguments = makeArgs,
                        WorkingDirectory = _projectRoot,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                }

                using (var process = new Process { StartInfo = startInfo })
                {
                    process.OutputDataReceived += (s, e) => {
                        if (e.Data != null) {
                            lock (buildLogs) { buildLogs.AppendLine(e.Data); }
                        }
                        AppendLog(e.Data);
                    };
                    process.ErrorDataReceived += (s, e) => {
                        if (e.Data != null) {
                            lock (buildLogs) { buildLogs.AppendLine(e.Data); }
                        }
                        AppendLog(e.Data);
                    };

                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    await Task.Run(() => process.WaitForExit());

                    _isBuildSuccessful = process.ExitCode == 0;
                    
                    if (_isBuildSuccessful)
                    {
                        if (RomExists())
                        {
                            MainStatusBar.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(40, 167, 69)); // Green
                            StatusText.Text = "Build Succeeded!";
                            LogTextBox.AppendText("\n> BUILD SUCCESSFUL!\n");

                            bool shouldAutoRun = _settings.AutoRunEmulator;
                            if (shouldAutoRun)
                            {
                                LogTextBox.AppendText($"> Launching game...\n");
                                LaunchEmulator();
                            }
                            else
                            {
                                LogTextBox.AppendText("> Auto-run skipped (disabled by user).\n");
                            }
                        }
                        else
                        {
                            _isBuildSuccessful = false;
                            MainStatusBar.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 53, 69)); // Red
                            if (isPcPort)
                            {
                                StatusText.Text = "Build Error: Executable Not Found";
                                LogTextBox.AppendText($"\n> ERROR: make reported success, but no PC Port executable was found.\n");
                            }
                            else
                            {
                                StatusText.Text = "Build Error: ROM Not Found";
                                string expectedPath = Path.Combine("build", version, $"sm64.{version}.z64");
                                LogTextBox.AppendText($"\n> ERROR: make reported success, but no ROM file was found at {expectedPath}.\n");
                            }
                        }
                    }
                    else
                    {
                        if (isPcPort && !_hasRetriedInstall)
                        {
                            if (!isMsys2) // WSL automatic dependency retry
                            {
                                string logText = buildLogs.ToString();
                                if (logText.Contains("SDL2/SDL.h") || logText.Contains("pulseaudio.h") || logText.Contains("libusb.h"))
                                {
                                    _hasRetriedInstall = true;
                                    LogTextBox.AppendText("\n> DETECTED MISSING DEVELOPMENT LIBRARIES (SDL2/PulseAudio/libusb).\n");
                                    LogTextBox.AppendText("> Attempting to install required packages automatically in WSL...\n");
                                    StatusText.Text = "Installing WSL dependencies...";

                                    bool installSuccess = await InstallWslDependenciesAsync();
                                    if (installSuccess)
                                    {
                                        LogTextBox.AppendText("\n> WSL dependencies installed successfully! Retrying build...\n\n");
                                        process.Dispose();
                                        await StartBuildAsync();
                                        return;
                                    }
                                    else
                                    {
                                        MainStatusBar.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 53, 69)); // Red
                                        StatusText.Text = "Build Failed";
                                        LogTextBox.AppendText($"\n> BUILD FAILED with exit code {process.ExitCode}\n");
                                        LogTextBox.AppendText("> ERROR: Failed to install packages automatically. Please run manually inside WSL:\n");
                                        LogTextBox.AppendText(">    sudo apt-get update && sudo apt-get install -y libsdl2-dev libpulse-dev libusb-1.0-0-dev\n\n");
                                    }
                                }
                                else
                                {
                                    MainStatusBar.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 53, 69)); // Red
                                    StatusText.Text = "Build Failed";
                                    LogTextBox.AppendText($"\n> BUILD FAILED with exit code {process.ExitCode}\n");
                                }
                            }
                            else // MSYS2 advice
                            {
                                MainStatusBar.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 53, 69)); // Red
                                StatusText.Text = "Build Failed";
                                LogTextBox.AppendText($"\n> BUILD FAILED with exit code {process.ExitCode}\n");
                                LogTextBox.AppendText("> Note: If you have missing dependencies, please open 'MSYS2 MinGW 64-bit' and run:\n");
                                LogTextBox.AppendText(">    pacman -S git make python3 mingw-w64-x86_64-gcc\n\n");
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
            finally
            {
                CloseButton.IsEnabled = true;
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
                            Dispatcher.BeginInvoke(new Action(() => LogTextBox.AppendText($"> Warning deleting file {Path.GetFileName(file)}: {ex.Message}\n")));
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
                            Dispatcher.BeginInvoke(new Action(() => LogTextBox.AppendText($"> Warning deleting directory {Path.GetFileName(dir)}: {ex.Message}\n")));
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
                CloseButton.IsEnabled = true;
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
            finally
            {
                CloseButton.IsEnabled = true;
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

                string? absoluteInputPath = FindBuiltRomPath();
                if (absoluteInputPath == null)
                {
                    LogTextBox.AppendText("> ERROR: Cannot extend ROM because build output ROM was not found.\n");
                    return false;
                }

                string inputDir = Path.GetDirectoryName(absoluteInputPath);
                string inputRomName = Path.GetFileName(absoluteInputPath);
                string extRomName = Path.GetFileNameWithoutExtension(inputRomName) + ".ext" + Path.GetExtension(inputRomName);
                string absoluteOutputPath = Path.Combine(inputDir, extRomName);

                string inputRomPath = Path.GetRelativePath(_projectRoot, absoluteInputPath).Replace('\\', '/');
                string outputRomPath = Path.GetRelativePath(_projectRoot, absoluteOutputPath).Replace('\\', '/');

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

                if (File.Exists(absoluteOutputPath))
                {
                    try
                    {
                        File.Delete(absoluteInputPath);
                        File.Move(absoluteOutputPath, absoluteInputPath);
                    }
                    catch (IOException ioEx)
                    {
                        LogTextBox.AppendText($"\n> ERROR: Cannot replace '{inputRomName}' because it is locked by another process.\n");
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
            
            if (_settings.BuildTargetPlatform == "PC")
            {
                string targetDir = Path.Combine(_projectRoot, "build", $"{version}_pc");
                if (!Directory.Exists(targetDir)) return false;

                if (_settings.BuildEnvironment == "MSYS2")
                {
                    string[] exeFiles = Directory.GetFiles(targetDir, "sm64.*.exe");
                    return exeFiles.Length > 0;
                }
                else
                {
                    string[] files = Directory.GetFiles(targetDir, "sm64.*");
                    foreach (var file in files)
                    {
                        if (!file.EndsWith(".z64") && !file.EndsWith(".png") && !file.EndsWith(".c") && !file.EndsWith(".o"))
                        {
                            return true;
                        }
                    }
                    return false;
                }
            }
            else
            {
                return FindBuiltRomPath() != null;
            }
        }

        private string? FindBuiltRomPath()
        {
            if (_settings == null) return null;
            string version = _settings.BuildVersion;

            // 1. Try exact standard path: build/{version}/sm64.{version}.z64
            string standardPath = Path.Combine(_projectRoot, "build", version, $"sm64.{version}.z64");
            if (File.Exists(standardPath))
            {
                return standardPath;
            }

            // 2. Scan build directory for any subdirectory containing a recently built .z64 file
            string buildPath = Path.Combine(_projectRoot, "build");
            if (Directory.Exists(buildPath))
            {
                try
                {
                    // Find all *.z64 files in build/
                    string[] z64Files = Directory.GetFiles(buildPath, "*.z64", SearchOption.AllDirectories);
                    
                    // Sort by write time descending to find the one we just compiled
                    var fileInfos = z64Files.Select(f => new FileInfo(f))
                                            .OrderByDescending(fi => fi.LastWriteTime)
                                            .ToList();

                    if (fileInfos.Count > 0)
                    {
                        // Check if it's in a subdirectory related to the current version
                        // (e.g. build/us_n64/sm64.z64 contains "us")
                        foreach (var fi in fileInfos)
                        {
                            string pathLower = fi.FullName.ToLower();
                            if (pathLower.Contains(version.ToLower()))
                            {
                                return fi.FullName;
                            }
                        }

                        // Fallback: return the newest .z64 file found anywhere under build/
                        return fileInfos[0].FullName;
                    }
                }
                catch { }
            }

            return null;
        }

        private void AppendLog(string? data)
        {
            if (data == null) return;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                LogTextBox.AppendText(data + "\n");
                LogScrollViewer.ScrollToEnd();
            }));
        }

        private void LaunchEmulator()
        {
            try
            {
                if (_settings == null) return;
                string version = _settings.BuildVersion;

                if (_settings.BuildTargetPlatform == "PC")
                {
                    string targetDir = Path.Combine(_projectRoot, "build", $"{version}_pc");
                    if (!Directory.Exists(targetDir)) return;

                    if (_settings.BuildEnvironment == "MSYS2")
                    {
                        string[] exeFiles = Directory.GetFiles(targetDir, "sm64.*.exe");
                        if (exeFiles.Length > 0)
                        {
                            string exePath = exeFiles[0];
                            var runStartInfo = new ProcessStartInfo
                            {
                                FileName = exePath,
                                WorkingDirectory = Path.GetDirectoryName(exePath),
                                UseShellExecute = true
                            };
                            Process.Start(runStartInfo);
                        }
                        else
                        {
                            Dispatcher.BeginInvoke(new Action(() => LogTextBox.AppendText($"> ERROR: PC Port Windows executable not found under build/{version}_pc/\n")));
                        }
                    }
                    else
                    {
                        string[] files = Directory.GetFiles(targetDir, "sm64.*");
                        string? exePath = null;
                        foreach (var file in files)
                        {
                            if (!file.EndsWith(".z64") && !file.EndsWith(".png") && !file.EndsWith(".c") && !file.EndsWith(".o"))
                            {
                                exePath = Path.GetFileName(file);
                                break;
                            }
                        }

                        if (exePath != null)
                        {
                            var runStartInfo = new ProcessStartInfo
                            {
                                FileName = "wsl",
                                Arguments = $"./build/{version}_pc/{exePath}",
                                WorkingDirectory = _projectRoot,
                                UseShellExecute = true
                            };
                            Process.Start(runStartInfo);
                        }
                        else
                        {
                            Dispatcher.BeginInvoke(new Action(() => LogTextBox.AppendText($"> ERROR: PC Port WSL executable not found under build/{version}_pc/\n")));
                        }
                    }
                }
                else
                {
                    string? romPath = FindBuiltRomPath();
                    if (romPath != null && File.Exists(romPath))
                    {
                        string emulatorPath = _settings.EmulatorPath;
                        Process.Start(emulatorPath, $"\"{romPath}\"");
                    }
                    else
                    {
                        Dispatcher.BeginInvoke(new Action(() => LogTextBox.AppendText($"> ERROR: Built ROM not found in build/ subfolders.\n")));
                    }
                }
            }
            catch (Exception ex)
            {
                Dispatcher.BeginInvoke(new Action(() => LogTextBox.AppendText($"> ERROR launching game: {ex.Message}\n")));
            }
        }

        private async Task<bool> InstallWslDependenciesAsync()
        {
            try
            {
                string installCmd = "sudo apt-get update && sudo apt-get install -y libsdl2-dev libpulse-dev libusb-1.0-0-dev || apt-get update && apt-get install -y libsdl2-dev libpulse-dev libusb-1.0-0-dev";
                
                var startInfo = new ProcessStartInfo
                {
                    FileName = "wsl",
                    Arguments = $"sh -c \"{installCmd}\"",
                    WorkingDirectory = _projectRoot,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                LogTextBox.AppendText($"> Running command: wsl sh -c \"{installCmd}\"\n\n");

                using (var process = new Process { StartInfo = startInfo })
                {
                    process.OutputDataReceived += (s, e) => AppendLog(e.Data);
                    process.ErrorDataReceived += (s, e) => AppendLog(e.Data);

                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    await Task.Run(() => process.WaitForExit());

                    return process.ExitCode == 0;
                }
            }
            catch (Exception ex)
            {
                LogTextBox.AppendText($"> ERROR running dependency installation: {ex.Message}\n");
                return false;
            }
        }

        private async Task PatchPcPortFilesAsync()
        {
            try
            {
                // 1. Patch src/pc/audio/audio_pulse.c
                string pulsePath = Path.Combine(_projectRoot, "src", "pc", "audio", "audio_pulse.c");
                if (File.Exists(pulsePath))
                {
                    string content = await Task.Run(() => File.ReadAllText(pulsePath));
                    
                    // Add null check return if not already present
                    string targetStr = "if (info == NULL) {\r\n        printf(\"pa_stream_get_timing_info failed, state is %d\\n\", pa_stream_get_state(pas.stream));\r\n    }";
                    string targetStrLf = "if (info == NULL) {\n        printf(\"pa_stream_get_timing_info failed, state is %d\\n\", pa_stream_get_state(pas.stream));\n    }";
                    
                    string replacement = "if (info == NULL) {\n        printf(\"pa_stream_get_timing_info failed, state is %d\\n\", pa_stream_get_state(pas.stream));\n        return 0;\n    }";
                    
                    bool patched = false;
                    if (content.Contains(targetStr))
                    {
                        content = content.Replace(targetStr, replacement);
                        patched = true;
                    }
                    else if (content.Contains(targetStrLf))
                    {
                        content = content.Replace(targetStrLf, replacement);
                        patched = true;
                    }

                    if (patched)
                    {
                        await Task.Run(() => File.WriteAllText(pulsePath, content));
                        LogTextBox.AppendText("> Patched src/pc/audio/audio_pulse.c to prevent PulseAudio null timing crash under WSL.\n");
                    }
                }

                // 2. Patch src/pc/gfx/gfx_glx.c
                string glxPath = Path.Combine(_projectRoot, "src", "pc", "gfx", "gfx_glx.c");
                if (File.Exists(glxPath))
                {
                    string content = await Task.Run(() => File.ReadAllText(glxPath));
                    bool patched = false;

                    if (!content.Contains("static bool is_wsl(void)"))
                    {
                        string wslHelper = @"static bool is_wsl(void) {
    static int memo = -1;
    if (memo != -1) return (bool)memo;
    FILE *f = fopen(""/proc/version"", ""r"");
    if (f == NULL) {
        memo = 0;
        return false;
    }
    char buf[1024];
    if (fgets(buf, sizeof(buf), f) != NULL) {
        if (strstr(buf, ""Microsoft"") != NULL || strstr(buf, ""microsoft"") != NULL) {
            memo = 1;
            fclose(f);
            return true;
        }
    }
    fclose(f);
    memo = 0;
    return false;
}

static int64_t get_time(void) {";

                        if (content.Contains("static int64_t get_time(void) {"))
                        {
                            content = content.Replace("static int64_t get_time(void) {", wslHelper);
                            patched = true;
                        }
                    }

                    string omlTarget = "if (glx.glXGetSyncValuesOML != NULL && glx.glXGetSyncValuesOML(glx.dpy, glx.win, &ust, &msc, &sbc))";
                    string omlReplacement = "if (!is_wsl() && glx.glXGetSyncValuesOML != NULL && glx.glXGetSyncValuesOML(glx.dpy, glx.win, &ust, &msc, &sbc))";
                    if (content.Contains(omlTarget) && !content.Contains(omlReplacement))
                    {
                        content = content.Replace(omlTarget, omlReplacement);
                        patched = true;
                    }

                    string sgiTarget = "if (glx.glXGetVideoSyncSGI != NULL)";
                    string sgiReplacement = "if (!is_wsl() && glx.glXGetVideoSyncSGI != NULL)";
                    if (content.Contains(sgiTarget) && !content.Contains(sgiReplacement))
                    {
                        content = content.Replace(sgiTarget, sgiReplacement);
                        patched = true;
                    }

                    string swapTarget = "static void gfx_glx_swap_buffers_begin(void) {\r\n    glx.wanted_ust += FRAME_INTERVAL_US_NUMERATOR;";
                    string swapTargetLf = "static void gfx_glx_swap_buffers_begin(void) {\n    glx.wanted_ust += FRAME_INTERVAL_US_NUMERATOR;";
                    string swapReplacement = "static void gfx_glx_swap_buffers_begin(void) {\n    extern bool gFastForward;\n    uint64_t interval = FRAME_INTERVAL_US_NUMERATOR;\n    if (gFastForward) {\n        interval /= 4;\n    }\n    glx.wanted_ust += interval;";

                    if (content.Contains(swapTarget))
                    {
                        content = content.Replace(swapTarget, swapReplacement);
                        patched = true;
                    }
                    else if (content.Contains(swapTargetLf))
                    {
                        content = content.Replace(swapTargetLf, swapReplacement);
                        patched = true;
                    }

                    if (patched)
                    {
                        await Task.Run(() => File.WriteAllText(glxPath, content));
                        LogTextBox.AppendText("> Patched src/pc/gfx/gfx_glx.c to bypass sync control extension frame drops under WSL.\n");
                    }
                }

                // 3. Patch src/pc/pc_main.c for fast-forward
                string pcMainPath = Path.Combine(_projectRoot, "src", "pc", "pc_main.c");
                if (File.Exists(pcMainPath))
                {
                    string content = await Task.Run(() => File.ReadAllText(pcMainPath));
                    bool patched = false;

                    if (!content.Contains("bool gFastForward = false;"))
                    {
                        string target = "s8 gShowDebugText;\r\n\r\nstatic struct AudioAPI *audio_api;";
                        string targetLf = "s8 gShowDebugText;\n\nstatic struct AudioAPI *audio_api;";
                        string replacement = "s8 gShowDebugText;\n\nbool gFastForward = false;\n\nstatic struct AudioAPI *audio_api;";
                        
                        if (content.Contains(target))
                        {
                            content = content.Replace(target, replacement);
                            patched = true;
                        }
                        else if (content.Contains(targetLf))
                        {
                            content = content.Replace(targetLf, replacement);
                            patched = true;
                        }
                        else
                        {
                            string fallbackTarget = "static struct AudioAPI *audio_api;";
                            string fallbackReplacement = "bool gFastForward = false;\nstatic struct AudioAPI *audio_api;";
                            if (content.Contains(fallbackTarget))
                            {
                                content = content.Replace(fallbackTarget, fallbackReplacement);
                                patched = true;
                            }
                        }
                    }

                    // Clean up loops iteration if present from previous patch
                    if (content.Contains("int loops = gFastForward ? 4 : 1;"))
                    {
                        string loopsTarget = "int loops = gFastForward ? 4 : 1;\r\n    for (int i = 0; i < loops; i++) {\r\n        game_loop_one_iteration();\r\n    }";
                        string loopsTargetLf = "int loops = gFastForward ? 4 : 1;\n    for (int i = 0; i < loops; i++) {\n        game_loop_one_iteration();\n    }";
                        if (content.Contains(loopsTarget))
                        {
                            content = content.Replace(loopsTarget, "game_loop_one_iteration();");
                            patched = true;
                        }
                        else if (content.Contains(loopsTargetLf))
                        {
                            content = content.Replace(loopsTargetLf, "game_loop_one_iteration();");
                            patched = true;
                        }
                    }

                    // Patch audio playback block to skip on fast-forward
                    if (!content.Contains("if (!gFastForward) {"))
                    {
                        string playTarget = "audio_api->play((u8 *)audio_buffer, 2 * num_audio_samples * 4);";
                        string playReplacement = "if (!gFastForward) {\n        audio_api->play((u8 *)audio_buffer, 2 * num_audio_samples * 4);\n    }";
                        if (content.Contains(playTarget))
                        {
                            content = content.Replace(playTarget, playReplacement);
                            patched = true;
                        }
                    }

                    if (patched)
                    {
                        await Task.Run(() => File.WriteAllText(pcMainPath, content));
                        LogTextBox.AppendText("> Patched src/pc/pc_main.c to support 4x fast-forward mode.\n");
                    }
                }

                // 4. Patch src/pc/controller/controller_keyboard.c for F4 input detection
                string kbPath = Path.Combine(_projectRoot, "src", "pc", "controller", "controller_keyboard.c");
                if (File.Exists(kbPath))
                {
                    string content = await Task.Run(() => File.ReadAllText(kbPath));
                    bool patched = false;

                    if (!content.Contains("extern bool gFastForward;"))
                    {
                        string target = "bool keyboard_on_key_down(int scancode) {";
                        string replacement = "extern bool gFastForward;\n\nbool keyboard_on_key_down(int scancode) {\n    if (scancode == 0x3e) { // F4\n        gFastForward = !gFastForward;\n        return true;\n    }";
                        if (content.Contains(target))
                        {
                            content = content.Replace(target, replacement);
                            patched = true;
                        }
                    }

                    if (patched)
                    {
                        await Task.Run(() => File.WriteAllText(kbPath, content));
                        LogTextBox.AppendText("> Patched src/pc/controller/controller_keyboard.c to toggle fast-forward on F4.\n");
                    }
                }

                // 5. Patch src/pc/gfx/gfx_dxgi.cpp for DirectX fast-forward frame limiter
                string dxgiPath = Path.Combine(_projectRoot, "src", "pc", "gfx", "gfx_dxgi.cpp");
                if (File.Exists(dxgiPath))
                {
                    string content = await Task.Run(() => File.ReadAllText(dxgiPath));
                    bool patched = false;

                    // Clean up bad patch if present
                    string badPatch = "extern \"C\" bool gFastForward;\n    uint64_t interval = FRAME_INTERVAL_US_NUMERATOR;\n    if (gFastForward) {\n        interval /= 4;\n    }\n    dxgi.frame_timestamp += interval;";
                    string badPatchLf = "extern \"C\" bool gFastForward;\n    uint64_t interval = FRAME_INTERVAL_US_NUMERATOR;\n    if (gFastForward) {\n        interval /= 4;\n    }\n    dxgi.frame_timestamp += interval;";
                    string badPatchCrLf = "extern \"C\" bool gFastForward;\r\n    uint64_t interval = FRAME_INTERVAL_US_NUMERATOR;\r\n    if (gFastForward) {\r\n        interval /= 4;\r\n    }\r\n    dxgi.frame_timestamp += interval;";

                    if (content.Contains(badPatch) || content.Contains(badPatchLf) || content.Contains(badPatchCrLf))
                    {
                        content = content.Replace(badPatch, "dxgi.frame_timestamp += FRAME_INTERVAL_US_NUMERATOR;");
                        content = content.Replace(badPatchLf, "dxgi.frame_timestamp += FRAME_INTERVAL_US_NUMERATOR;");
                        content = content.Replace(badPatchCrLf, "dxgi.frame_timestamp += FRAME_INTERVAL_US_NUMERATOR;");
                        patched = true;
                    }

                    // Add global extern if missing
                    if (!content.Contains("using namespace Microsoft::WRL; // For ComPtr\n\nextern \"C\" bool gFastForward;") &&
                        !content.Contains("using namespace Microsoft::WRL; // For ComPtr\r\n\r\nextern \"C\" bool gFastForward;"))
                    {
                        // Remove any other global declarations if present to avoid duplicates
                        content = content.Replace("\nextern \"C\" bool gFastForward;", "");
                        content = content.Replace("\r\nextern \"C\" bool gFastForward;", "");

                        string usingNamespace = "using namespace Microsoft::WRL; // For ComPtr";
                        if (content.Contains(usingNamespace))
                        {
                            content = content.Replace(usingNamespace, "using namespace Microsoft::WRL; // For ComPtr\n\nextern \"C\" bool gFastForward;");
                            patched = true;
                        }
                    }

                    // Apply correct function patch
                    if (!content.Contains("uint64_t interval = FRAME_INTERVAL_US_NUMERATOR;"))
                    {
                        string target = "dxgi.frame_timestamp += FRAME_INTERVAL_US_NUMERATOR;";
                        string replacement = "uint64_t interval = FRAME_INTERVAL_US_NUMERATOR;\n    if (gFastForward) {\n        interval /= 4;\n    }\n    dxgi.frame_timestamp += interval;";
                        if (content.Contains(target))
                        {
                            content = content.Replace(target, replacement);
                            patched = true;
                        }
                    }

                    if (patched)
                    {
                        await Task.Run(() => File.WriteAllText(dxgiPath, content));
                        LogTextBox.AppendText("> Patched src/pc/gfx/gfx_dxgi.cpp to support DirectX fast-forward frame limits.\n");
                    }
                }

                // 6. Patch src/pc/gfx/gfx_sdl2.c for SDL2 fast-forward frame limits
                string sdl2Path = Path.Combine(_projectRoot, "src", "pc", "gfx", "gfx_sdl2.c");
                if (File.Exists(sdl2Path))
                {
                    string content = await Task.Run(() => File.ReadAllText(sdl2Path));
                    bool patched = false;

                    if (!content.Contains("extern bool gFastForward;"))
                    {
                        string target = "static void sync_framerate_with_timer(void) {\r\n    // Number of milliseconds a frame should take (30 fps)\r\n    const Uint32 FRAME_TIME = 1000 / 30;\r\n    static Uint32 last_time;\r\n    Uint32 elapsed = SDL_GetTicks() - last_time;\r\n\r\n    if (elapsed < FRAME_TIME)\r\n        SDL_Delay(FRAME_TIME - elapsed);\r\n    last_time += FRAME_TIME;\r\n}";
                        string targetLf = "static void sync_framerate_with_timer(void) {\n    // Number of milliseconds a frame should take (30 fps)\n    const Uint32 FRAME_TIME = 1000 / 30;\n    static Uint32 last_time;\n    Uint32 elapsed = SDL_GetTicks() - last_time;\n\n    if (elapsed < FRAME_TIME)\n        SDL_Delay(FRAME_TIME - elapsed);\n    last_time += FRAME_TIME;\n}";

                        string replacement = "static void sync_framerate_with_timer(void) {\n    extern bool gFastForward;\n    Uint32 frame_time = 1000 / 30;\n    if (gFastForward) {\n        frame_time /= 4;\n    }\n    static Uint32 last_time;\n    Uint32 elapsed = SDL_GetTicks() - last_time;\n\n    if (elapsed < frame_time)\n        SDL_Delay(frame_time - elapsed);\n    last_time += frame_time;\n}";

                        if (content.Contains(target))
                        {
                            content = content.Replace(target, replacement);
                            patched = true;
                        }
                        else if (content.Contains(targetLf))
                        {
                            content = content.Replace(targetLf, replacement);
                            patched = true;
                        }
                    }

                    if (patched)
                    {
                        await Task.Run(() => File.WriteAllText(sdl2Path, content));
                        LogTextBox.AppendText("> Patched src/pc/gfx/gfx_sdl2.c to support SDL2 fast-forward frame limits.\n");
                    }
                }

                // 7. Patch tools/armips.cpp to include standard integer headers on MSYS2
                string armipsPath = Path.Combine(_projectRoot, "tools", "armips.cpp");
                if (File.Exists(armipsPath))
                {
                    string content = await Task.Run(() => File.ReadAllText(armipsPath));
                    bool patched = false;

                    if (!content.Contains("#include <stdint.h>"))
                    {
                        string target = "#define _CRT_SECURE_NO_WARNINGS";
                        string replacement = "#define _CRT_SECURE_NO_WARNINGS\n#include <stdint.h>\n#include <cstdint>\n#include <inttypes.h>";
                        if (content.Contains(target))
                        {
                            content = content.Replace(target, replacement);
                            patched = true;
                        }
                    }

                    if (patched)
                    {
                        await Task.Run(() => File.WriteAllText(armipsPath, content));
                        LogTextBox.AppendText("> Patched tools/armips.cpp to include stdint headers under MSYS2.\n");
                    }
                }
            }
            catch (Exception ex)
            {
                LogTextBox.AppendText($"> Warning: Failed to automatically patch PC Port files: {ex.Message}\n");
            }
        }

        private async Task<bool> TryAutoInstallCompilerAsync(string compiler, bool isMsys2)
        {
            LogTextBox.AppendText($"> Selected compiler '{compiler}' is not installed in the target environment.\n");
            LogTextBox.AppendText($"> Attempting to autoinstall '{compiler}'...\n");

            ProcessStartInfo installInfo;

            if (isMsys2)
            {
                string msysPath = _settings.MsysPath;
                string bashPath = Path.Combine(msysPath, "usr", "bin", "bash.exe");
                string pacmanPackage = compiler == "clang" ? "mingw-w64-x86_64-clang" : "mingw-w64-x86_64-gcc";
                
                LogTextBox.AppendText($"> Running: pacman -S --noconfirm {pacmanPackage}...\n");
                
                installInfo = new ProcessStartInfo
                {
                    FileName = bashPath,
                    Arguments = $"-c \"pacman -S --noconfirm {pacmanPackage}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
            }
            else // WSL
            {
                LogTextBox.AppendText($"> Running: sudo apt-get update && sudo apt-get install -y {compiler}...\n");
                
                installInfo = new ProcessStartInfo
                {
                    FileName = "wsl.exe",
                    Arguments = $"bash -c \"sudo apt-get update && sudo apt-get install -y {compiler}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
            }

            try
            {
                using (var proc = new Process { StartInfo = installInfo })
                {
                    proc.OutputDataReceived += (s, ev) => {
                        if (!string.IsNullOrEmpty(ev.Data))
                        {
                            Dispatcher.Invoke(() => LogTextBox.AppendText($"  [install] {ev.Data}\n"));
                        }
                    };
                    proc.ErrorDataReceived += (s, ev) => {
                        if (!string.IsNullOrEmpty(ev.Data))
                        {
                            Dispatcher.Invoke(() => LogTextBox.AppendText($"  [install-err] {ev.Data}\n"));
                        }
                    };

                    proc.Start();
                    proc.BeginOutputReadLine();
                    proc.BeginErrorReadLine();
                    
                    await Task.Run(() => proc.WaitForExit());

                    if (proc.ExitCode == 0)
                    {
                        LogTextBox.AppendText($"> Successfully autoinstalled '{compiler}'!\n");
                        return true;
                    }
                    else
                    {
                        LogTextBox.AppendText($"> ERROR: Autoinstall failed with exit code {proc.ExitCode}.\n");
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                LogTextBox.AppendText($"> ERROR: Failed to start autoinstall process: {ex.Message}\n");
                return false;
            }
        }
    }
}
