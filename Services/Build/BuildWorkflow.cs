using System.Diagnostics;
using System.IO;
namespace Sm64DecompLevelViewer.Services.Build;
internal sealed class BuildWorkflow
{
    private readonly string _projectRoot;
    private readonly AppSettings? _settings;
    private readonly string? _gitUrlToClone;
    private readonly List<string>? _bakFilesToRestore;
    private readonly bool _runGitRevert;
    private bool _isBuildSuccessful;
    private readonly Action<BuildProgress> _report;
    private readonly Func<ProcessStartInfo, Action<string>, Task<int>>? _runProcess;
    private readonly Action<AppSettings>? _saveSettings;
    public bool IsSuccessful => _isBuildSuccessful;
    public BuildWorkflow(string root, AppSettings? settings, string? cloneUrl, List<string>? restoreFiles, bool revert, Action<BuildProgress> report,
        Func<ProcessStartInfo, Action<string>, Task<int>>? runProcess = null, Action<AppSettings>? saveSettings = null)
    {
        _projectRoot = root; _settings = settings; _gitUrlToClone = cloneUrl;
        _bakFilesToRestore = restoreFiles; _runGitRevert = revert; _report = report;
        _runProcess = runProcess;
        _saveSettings = saveSettings;
    }
    private void ReportLog(string text) => _report(new BuildProgress(Log: text));
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> ToolchainChecks = new();
    private string? _builtArtifact;

    public async Task StartBuildAsync()
    {
        var total = Stopwatch.StartNew();
        _isBuildSuccessful = false;
        _builtArtifact = null;
        string? cacheKey = null;
        try
        {
            if (_settings == null) throw new InvalidOperationException("No settings provided for building.");
            var profile = RepositoryBuildProfile.Load(_projectRoot);
            string? error = profile.Validate(_settings);
            if (error != null) throw new InvalidOperationException(error);
            Audio.M64Integrity.ValidateBuildInputs(_projectRoot, _settings.BuildVersion);
            string artifact = profile.ArtifactPath(_settings);
            bool pc = _settings.BuildTargetPlatform == "PC";
            _report(new BuildProgress(Title: "BUILDING " + profile.Name, Status: "Preparing build..."));
            ReportLog($"> Repository: {profile.Name}\n> Output: {artifact}\n");

            if (pc) await new PcPortPatcher(_projectRoot, ReportLog).PatchPcPortFilesAsync();
            bool wsl = !pc || _settings.BuildEnvironment != "MSYS2";
            bool cleaned = wsl && _runProcess == null
                ? await BuildConfigurationState.PrepareWslAsync(profile, _settings, ReportLog)
                : BuildConfigurationState.Prepare(profile, _settings);
            if (cleaned)
                ReportLog("> Build configuration changed; invalidated incompatible target outputs.\n");

            string hostMarker = Path.Combine(_projectRoot, ".randm64-tools-host");
            bool cleanTools = BuildToolState.NeedsClean(_projectRoot, _settings);
            ReportLog(cleanTools ? "> Host tools are incompatible with the selected environment; cleaning host tools.\n"
                : "> Preserving compatible host tools; make will build missing or changed tools.\n");
            // Pending is written only for an actual transition, not first-time adoption.
            if (cleanTools) File.WriteAllText(hostMarker, "pending");
            cacheKey = BuildConfigurationState.RepositoryKey(_projectRoot) + "|" + BuildConfigurationState.Signature(profile, _settings);
            string command = BuildToolState.PreparationCommand(_projectRoot, _settings, cleanTools) + " && "
                + BuildCommandFactory.CreateMakeArguments(profile, _settings);
            string bootstrap = ColdToolBootstrap.Command(profile, _settings, cleanTools);
            if (bootstrap.Length > 0)
            {
                ReportLog("> Bootstrapping missing host tools with the selected parallel job count.\n");
                command = BuildToolState.PreparationCommand(_projectRoot, _settings, cleanTools) + " && " + bootstrap + " && "
                    + BuildCommandFactory.CreateMakeArguments(profile, _settings);
            }
            if (!ToolchainChecks.ContainsKey(cacheKey))
            {
                string requirements = pc ? "command -v make >/dev/null && command -v python3 >/dev/null && command -v gcc >/dev/null"
                    : "command -v make >/dev/null && command -v python3 >/dev/null";
                command = "(" + requirements + ") || { echo 'Required build tools are missing. Install make, python3 and the repository toolchain in the selected environment.' >&2; exit 127; }; " + command;
            }
            ReportLog($"> Preparation: {total.Elapsed.TotalMilliseconds:F0} ms\n> Running: {command}\n");
            _report(new BuildProgress(Status: "Building..."));
            var make = Stopwatch.StartNew();
            var startInfo = BuildProcessFactory.Create(_projectRoot, _settings, command);
            int exitCode = _runProcess != null ? await _runProcess(startInfo, ReportLog) : await RunProcessAsync(startInfo, command);
            ReportLog($"> Make: {make.Elapsed.TotalMilliseconds:F0} ms\n");
            if (exitCode != 0)
            {
                ToolchainChecks.TryRemove(cacheKey, out _);
                throw new InvalidOperationException($"make failed with exit code {exitCode}. See the build output for the repository toolchain or dependency requirements.");
            }
            _builtArtifact = new BuildArtifactLocator(profile, _settings).FindArtifactPath();
            if (_builtArtifact == null) throw new InvalidOperationException("make reported success, but the expected ROM/native executable was not found: " + artifact);
            BuildToolState.Remember(_projectRoot);
            ToolchainChecks[cacheKey] = 0;
            BuildConfigurationState.RecordSuccess(profile, _settings);
            if (_saveSettings != null) _saveSettings(_settings);
            else if (_runProcess == null) new SettingsService().SaveSettings(_settings);
            _isBuildSuccessful = true;
            _report(new BuildProgress(Tone: BuildTone.Success, Status: "Build Succeeded!"));
            ReportLog("> BUILD SUCCESSFUL!\n");
            if (_settings.AutoRunEmulator) LaunchEmulator();
        }
        catch (Exception ex)
        {
            BuildToolState.Invalidate(_projectRoot);
            if (cacheKey != null) ToolchainChecks.TryRemove(cacheKey, out _);
            _isBuildSuccessful = false;
            _report(new BuildProgress(Tone: BuildTone.Error, Status: "Build Failed"));
            ReportLog($"> ERROR: {ex.Message}\n");
            if (BuildFailureDiagnostics.ShouldSuggestWslInstall(ex)) ReportLog("> Install or configure WSL before building this target.\n");
        }
        finally
        {
            ReportLog($"> Total build: {total.Elapsed.TotalMilliseconds:F0} ms\n");
            _report(new BuildProgress(CanClose: true));
        }
    }

    private async Task<int> RunProcessAsync(ProcessStartInfo info, string command)
    {
        if (info.FileName == "wsl.exe")
        {
            var result = await WslBuildSessions.RunAsync(_projectRoot, command, ReportLog);
            ReportLog($"> WSL connection: {result.ConnectionMilliseconds:F0} ms ({(result.Reused ? "reused" : "started")})\n");
            ReportLog($"> Command in WSL: {result.CommandMilliseconds:F0} ms\n");
            return result.ExitCode;
        }
        using var process = new Process { StartInfo = info };
        process.OutputDataReceived += (_, e) => { if (e.Data != null) ReportLog(e.Data + "\n"); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) ReportLog(e.Data + "\n"); };
        process.Start(); process.BeginOutputReadLine(); process.BeginErrorReadLine();
        await Task.Run(() => process.WaitForExit());
        return process.ExitCode;
    }

    private void LaunchEmulator()
    {
        try
        {
            if (_settings == null || _builtArtifact == null) return;
            ProcessStartInfo info;
            if (_settings.BuildTargetPlatform == "PC" && _settings.BuildEnvironment != "MSYS2")
            {
                string relative = Path.GetRelativePath(_projectRoot, _builtArtifact).Replace('\\', '/');
                info = BuildProcessFactory.Create(_projectRoot, _settings, "exec " + BuildCommandFactory.Quote("./" + relative));
                info.RedirectStandardOutput = false; info.RedirectStandardError = false;
                info.StandardOutputEncoding = null; info.StandardErrorEncoding = null;
            }
            else if (_settings.BuildTargetPlatform == "PC")
            {
                info = new ProcessStartInfo(_builtArtifact) { WorkingDirectory = Path.GetDirectoryName(_builtArtifact), UseShellExecute = true };
            }
            else
            {
                if (!File.Exists(_settings.EmulatorPath)) throw new FileNotFoundException("Select an existing emulator executable in Build Options.");
                info = new ProcessStartInfo(_settings.EmulatorPath) { UseShellExecute = false };
                info.ArgumentList.Add(_builtArtifact);
            }
            Process.Start(info);
        }
        catch (Exception ex) { ReportLog($"> Build completed, but launching failed: {ex.Message}\n"); }
    }

    public async Task StartCleanAndCloneAsync()
    {
        var recovery = new ProjectRecoveryService(_projectRoot, _gitUrlToClone, _bakFilesToRestore, _runGitRevert, _report);
        await recovery.StartCleanAndCloneAsync();
        _isBuildSuccessful = recovery.IsSuccessful;
    }
    public async Task StartRevertSourceAsync()
    {
        var recovery = new ProjectRecoveryService(_projectRoot, _gitUrlToClone, _bakFilesToRestore, _runGitRevert, _report);
        await recovery.StartRevertSourceAsync();
        _isBuildSuccessful = recovery.IsSuccessful;
    }
}



