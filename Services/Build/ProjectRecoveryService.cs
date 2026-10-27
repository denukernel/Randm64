using System.Diagnostics;
using System.IO;
namespace Sm64DecompLevelViewer.Services.Build;
internal sealed class ProjectRecoveryService
{
    private readonly string _projectRoot;
    private readonly string? _gitUrlToClone;
    private readonly List<string>? _bakFilesToRestore;
    private readonly bool _runGitRevert;
    private bool _isBuildSuccessful;
    private readonly Action<BuildProgress> _report;
    public bool IsSuccessful => _isBuildSuccessful;
    public ProjectRecoveryService(string root, string? url, List<string>? files, bool revert, Action<BuildProgress> report)
    { _projectRoot = root; _gitUrlToClone = url; _bakFilesToRestore = files; _runGitRevert = revert; _report = report; }
    private void ReportLog(string text) => _report(new BuildProgress(Log: text));
    private void AppendLog(string? text) { if (text != null) ReportLog(text + "\n"); }
    private static void Dispatch(Action action) => action();
        public async Task StartCleanAndCloneAsync()
        {
            if (string.IsNullOrEmpty(_gitUrlToClone))
            {
                ReportLog("> ERROR: No Git repository URL provided.\n");
                return;
            }

            _report(new BuildProgress(Title: "CLEAN & RE-CLONE PROJECT (WSL)..."));
            _report(new BuildProgress(Status: "Cleaning project..."));
            ReportLog($"> Cleaning project directory: {_projectRoot}\n");
            ReportLog($"> Git repository URL: {_gitUrlToClone}\n");

            string tempBackupPath = Path.Combine(Path.GetTempPath(), "sm64_baserom_backup_" + Guid.NewGuid().ToString("N"));
            bool hasBackup = false;

            try
            {
                // Step 1: Find and backup baserom files
                Directory.CreateDirectory(tempBackupPath);
                ReportLog("> Backing up baserom files...\n");
                
                string[] baseromFiles = Directory.GetFiles(_projectRoot, "baserom.*");
                foreach (string file in baseromFiles)
                {
                    string fileName = Path.GetFileName(file);
                    string destFile = Path.Combine(tempBackupPath, fileName);
                    File.Copy(file, destFile, true);
                    ReportLog($"> Backed up: {fileName}\n");
                    hasBackup = true;
                }

                if (!hasBackup)
                {
                    ReportLog("> WARNING: No baserom files (baserom.*) found to back up.\n");
                }

                // Step 2: Delete everything in the project root
                ReportLog("> Deleting existing files and directories...\n");
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
                            Dispatch(new Action(() => ReportLog($"> Warning deleting file {Path.GetFileName(file)}: {ex.Message}\n")));
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
                            Dispatch(new Action(() => ReportLog($"> Warning deleting directory {Path.GetFileName(dir)}: {ex.Message}\n")));
                        }
                    }
                });

                // Step 3: Run git clone via WSL
                ReportLog($"> Running 'git clone {_gitUrlToClone} .' inside WSL...\n");
                _report(new BuildProgress(Status: "Cloning repository..."));

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
                        ReportLog($"> Git clone completed successfully.\n");
                    }
                    else
                    {
                        ReportLog($"> ERROR: Git clone failed with exit code {process.ExitCode}\n");
                    }
                }

                if (!cloneSuccess)
                {
                    throw new Exception("Git clone failed.");
                }

                // Step 4: Restore baserom files
                if (hasBackup)
                {
                    ReportLog("> Restoring baserom files...\n");
                    string[] backedFiles = Directory.GetFiles(tempBackupPath);
                    foreach (string file in backedFiles)
                    {
                        string fileName = Path.GetFileName(file);
                        string destFile = Path.Combine(_projectRoot, fileName);
                        File.Copy(file, destFile, true);
                        ReportLog($"> Restored: {fileName}\n");
                    }
                }

                _report(new BuildProgress(Tone: BuildTone.Success)); // Green
                _report(new BuildProgress(Status: "Clean & Re-clone Succeeded!"));
                ReportLog("\n> FRESH START COMPLETE SUCCESSFULLY!\n");
                _isBuildSuccessful = true;
            }
            catch (Exception ex)
            {
                _report(new BuildProgress(Tone: BuildTone.Error)); // Red
                _report(new BuildProgress(Status: "Failed to Clean & Re-clone"));
                ReportLog($"\n> FATAL ERROR: {ex.Message}\n");
                if (BuildFailureDiagnostics.ShouldSuggestWslInstall(ex))
                {
                    ReportLog("\n> WSL (Windows Subsystem for Linux) might not be installed or configured on your system.\n");
                    ReportLog("> Please install WSL (e.g. Ubuntu 20.04 or newer from the Microsoft Store) to run build tools.\n");
                    ReportLog("> To install WSL, run: 'wsl --install' in an administrator Command Prompt/PowerShell.\n");
                }

                // CRITICAL safety fallback: try to restore baseroms even if clone failed
                if (hasBackup)
                {
                    ReportLog("> Attempting to restore original baserom files after failure...\n");
                    try
                    {
                        string[] backedFiles = Directory.GetFiles(tempBackupPath);
                        foreach (string file in backedFiles)
                        {
                            string fileName = Path.GetFileName(file);
                            string destFile = Path.Combine(_projectRoot, fileName);
                            File.Copy(file, destFile, true);
                            ReportLog($"> Restored: {fileName}\n");
                        }
                    }
                    catch (Exception restoreEx)
                    {
                        ReportLog($"> ERROR restoring baserom backup: {restoreEx.Message}\n");
                    }
                }
            }
            finally
            {
                _report(new BuildProgress(CanClose: true));
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
        public async Task StartRevertSourceAsync()
        {
            _report(new BuildProgress(Title: "REVERTING SOURCE CODE..."));
            _report(new BuildProgress(Status: "Reverting code..."));
            ReportLog($"> Reverting local changes in project: {_projectRoot}\n");

            try
            {
                if (!_runGitRevert)
                {
                    var choices = _bakFilesToRestore ?? Services.LevelSourceRecovery.FindChoices(_projectRoot).ToList();
                    int restored = await Task.Run(() => Services.LevelSourceRecovery.Restore(_projectRoot, choices));
                    ReportLog($"> restored {restored} source files; selected recovery groups completed.\n");
                }
                bool checkoutSuccess = true;
                if (_runGitRevert)
                {
                    // Run wsl git checkout -f
                    ReportLog("> Running 'git checkout -f' inside WSL...\n");

                    var startInfo = SourceRevertCommand.Create(_projectRoot);

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
                    _report(new BuildProgress(Tone: BuildTone.Success)); // Green
                    _report(new BuildProgress(Status: "Revert Succeeded!"));
                    ReportLog("\n> SOURCE REVERT COMPLETE SUCCESSFULLY!\n");
                    _isBuildSuccessful = true;
                }
                else
                {
                    throw new Exception("source reset or asset extraction failed; see the command output above for the completed steps");
                }
            }
            catch (Exception ex)
            {
                _report(new BuildProgress(Tone: BuildTone.Error)); // Red
                _report(new BuildProgress(Status: "Failed to Revert Changes"));
                ReportLog($"\n> FATAL ERROR: {ex.Message}\n");
                if (BuildFailureDiagnostics.ShouldSuggestWslInstall(ex))
                {
                    ReportLog("\n> WSL (Windows Subsystem for Linux) might not be installed or configured on your system.\n");
                    ReportLog("> Please install WSL (e.g. Ubuntu 20.04 or newer from the Microsoft Store) to run build tools.\n");
                    ReportLog("> To install WSL, run: 'wsl --install' in an administrator Command Prompt/PowerShell.\n");
                }
            }
            finally
            {
                _report(new BuildProgress(CanClose: true));
            }
        }
}
