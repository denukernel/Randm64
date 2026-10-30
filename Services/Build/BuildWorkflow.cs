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
            if (!pc && _settings.BuildCorruptorRom)
                await BuildCorruptorRomAsync(profile, _builtArtifact);
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

    private async Task BuildCorruptorRomAsync(RepositoryBuildProfile profile, string originalRom)
    {
        if (_settings == null) throw new InvalidOperationException("Missing build settings.");
        string tools = Path.Combine(AppContext.BaseDirectory, "Tools", "RomCorruptor");
        string encoder = Path.Combine(tools, "literal_mio0.py");
        string reporter = Path.Combine(tools, "write_ranges.py");
        if (!File.Exists(encoder) || !File.Exists(reporter)) throw new FileNotFoundException("Corruptor build tools are missing from the application installation.");
        string identity = BuildConfigurationState.Signature(profile, _settings) + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(encoder)));
        string suffix = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(identity)))[..12].ToLowerInvariant();
        string buildDir = "build/randm64-corruptor-" + _settings.BuildVersion + "-" + suffix;
        string script = BuildProcessFactory.WslLocation(encoder).Path;
        string reportScript = BuildProcessFactory.WslLocation(reporter).Path;
        string target = buildDir + "/sm64." + _settings.BuildVersion + ".z64";
        string map = buildDir + "/sm64." + _settings.BuildVersion + ".map";
        string command = BuildCommandFactory.CreateMakeArguments(profile, _settings)
            + " BUILD_DIR=" + BuildCommandFactory.Quote(buildDir)
            + " COMPARE=0 MIO0TOOL=" + BuildCommandFactory.Quote("python3 " + BuildCommandFactory.Quote(script));
        ReportLog("> Building separate 24 MiB corruptor ROM with literal MIO0 assets.\n");
        _report(new BuildProgress(Status: "Building corruptor ROM..."));
        var info = BuildProcessFactory.Create(_projectRoot, _settings, command);
        int exit = _runProcess != null ? await _runProcess(info, ReportLog) : await RunProcessAsync(info, command);
        if (exit != 0) throw new InvalidOperationException("Corruptor ROM build failed; the normal ROM is still available.");
        string built = Path.Combine(_projectRoot, buildDir.Replace('/', Path.DirectorySeparatorChar), "sm64." + _settings.BuildVersion + ".z64");
        string final = Path.Combine(Path.GetDirectoryName(originalRom)!, Path.GetFileNameWithoutExtension(originalRom) + ".corruptor.z64");
        string report = Path.ChangeExtension(final, ".ranges.txt");
        string staged = final + ".tmp";
        string stagedReport = report + ".tmp";
        File.Copy(built, staged, true);
        try
        {
            string validation = "python3 " + BuildCommandFactory.Quote(reportScript) + " "
                + BuildCommandFactory.Quote(BuildProcessFactory.WslLocation(staged).Path) + " "
                + BuildCommandFactory.Quote(BuildProcessFactory.WslLocation(Path.Combine(_projectRoot, map.Replace('/', Path.DirectorySeparatorChar))).Path) + " "
                + BuildCommandFactory.Quote(BuildProcessFactory.WslLocation(stagedReport).Path);
            var validateInfo = BuildProcessFactory.Create(_projectRoot, _settings, validation);
            int status = _runProcess != null ? await _runProcess(validateInfo, ReportLog) : await RunProcessAsync(validateInfo, validation);
            if (status != 0) throw new InvalidOperationException("Corruptor ROM validation or range generation failed; the normal ROM is still available.");
            File.Move(staged, final, true);
            File.Move(stagedReport, report, true);
            ReportLog($"> Corruptor ROM: {final}\n> Address ranges: {report}\n");
        }
        finally
        {
            if (File.Exists(staged)) File.Delete(staged);
            if (File.Exists(stagedReport)) File.Delete(stagedReport);
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



