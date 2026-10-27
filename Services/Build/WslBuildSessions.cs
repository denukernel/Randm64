using System.Diagnostics;
using System.IO;
using System.Text;

namespace Sm64DecompLevelViewer.Services.Build;

internal sealed record WslBuildResult(int ExitCode, double ConnectionMilliseconds, double CommandMilliseconds, bool Reused);

// A shell owned by this app, not a global WSL setting or service. Every request runs in a
// subshell so failed builds, cd, environment changes and shell exits cannot leak to later jobs.
internal static class WslBuildSessions
{
    private static readonly object Sync = new();
    private static readonly Dictionary<string, Session> Sessions = new(StringComparer.OrdinalIgnoreCase);
    private static bool _stopping;
    private static string? _activeProject;
    private static readonly Timer IdleTimer = new(_ => CloseIdle(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));

    private static Session Get(string root)
    {
        string key = BuildConfigurationState.RepositoryKey(root);
        lock (Sync)
        {
            if (_stopping) throw new ObjectDisposedException(nameof(WslBuildSessions));
            if (!Sessions.TryGetValue(key, out var session)) Sessions[key] = session = new Session(root);
            session.Touch();
            return session;
        }
    }

    public static void Prewarm(string root, AppSettings settings)
    {
        if (settings.BuildTargetPlatform == "PC" && settings.BuildEnvironment == "MSYS2") return;
        _ = Task.Run(async () =>
        {
            try { await Get(root).WarmAsync(); }
            catch { /* The real build reports startup errors; opening or cancelling a dialog does not. */ }
        });
    }

    // Start before browsing repository files over the WSL UNC path. Populate the profile
    // during project loading as well, rather than repeating that cold work at Build time.
    public static void PrewarmProject(string root, AppSettings settings)
    {
        bool native = settings.BuildTargetPlatform == "PC" && settings.BuildEnvironment == "MSYS2";
        string key;
        try { key = BuildConfigurationState.RepositoryKey(root); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return; }
        lock (Sync)
        {
            if (_stopping) return;
            _activeProject = native ? null : key;
        }
        if (native) return;
        _ = Task.Run(async () =>
        {
            try
            {
                await Get(root).WarmAsync();
                RepositoryBuildProfile.Load(root);
            }
            catch { }
        });
    }

    public static Task<WslBuildResult> RunAsync(string root, string command, Action<string> log) => Get(root).RunAsync(command, log);

    internal static void CloseIdle(TimeSpan? maximumIdle = null)
    {
        lock (Sync)
            foreach (var pair in Sessions.ToArray())
                if ((maximumIdle.HasValue || pair.Key != _activeProject) && pair.Value.CloseIfIdle(maximumIdle ?? TimeSpan.FromMinutes(2))) Sessions.Remove(pair.Key);
    }

    public static void StopAll()
    {
        lock (Sync)
        {
            _stopping = true;
            _activeProject = null;
            foreach (var session in Sessions.Values) session.Shutdown();
            Sessions.Clear();
        }
    }

    private sealed class Session
    {
        private readonly string _root;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private Process? _process;
        private DateTime _lastUsed = DateTime.UtcNow;
        private readonly StringBuilder _startupErrors = new();
        private readonly object _lifecycle = new();
        private bool _closed;
        public Session(string root) => _root = root;
        public void Touch() => _lastUsed = DateTime.UtcNow;

        public async Task WarmAsync()
        {
            await _gate.WaitAsync();
            try { await ConnectAsync(); }
            finally { _lastUsed = DateTime.UtcNow; _gate.Release(); }
        }

        private async Task ConnectAsync()
        {
            if (_closed) throw new ObjectDisposedException(nameof(Session));
            if (_process is { HasExited: false }) return;
            Stop();
            var info = BuildProcessFactory.Create(_root, new AppSettings(), "exec sh 2>&1");
            info.RedirectStandardInput = true;
            info.StandardInputEncoding = new UTF8Encoding(false);
            var process = new Process { StartInfo = info };
            lock (_startupErrors) _startupErrors.Clear();
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data == null) return;
                lock (_startupErrors) if (_startupErrors.Length < 4096) _startupErrors.AppendLine(e.Data);
            };
            try
            {
                lock (_lifecycle)
                {
                    if (_closed) { process.Dispose(); throw new ObjectDisposedException(nameof(Session)); }
                    _process = process;
                    process.Start();
                }
                process.BeginErrorReadLine();
                process.StandardInput.AutoFlush = true;
                string ready = "__RANDM64_READY_" + Guid.NewGuid().ToString("N");
                await process.StandardInput.WriteLineAsync("printf '%s\\n' " + BuildCommandFactory.Quote(ready));
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                while (true)
                {
                    string? line = await process.StandardOutput.ReadLineAsync(timeout.Token);
                    if (line == ready) return;
                    if (line == null) throw new IOException("WSL build shell exited during startup. " + StartupError());
                }
            }
            catch { Stop(); throw; }
        }

        public async Task<WslBuildResult> RunAsync(string command, Action<string> log)
        {
            var connection = Stopwatch.StartNew();
            await _gate.WaitAsync();
            bool submitted = false;
            try
            {
                bool reused = _process is { HasExited: false };
                await ConnectAsync();
                connection.Stop();
                var process = _process!;
                string end = "__RANDM64_DONE_" + Guid.NewGuid().ToString("N") + ":";
                // Redirect stdin only for the job, not the persistent shell. A build tool must
                // never consume the protocol or the following request as interactive input.
                string script = "(\n" + command + "\n) </dev/null 2>&1; __randm64_status=$?; printf '\\n"
                    + end + "%s\\n' \"$__randm64_status\"";
                var execution = Stopwatch.StartNew();
                submitted = true;
                await process.StandardInput.WriteLineAsync(script);
                while (true)
                {
                    string? line = await process.StandardOutput.ReadLineAsync();
                    if (line == null) throw new IOException("WSL build connection closed before reporting the result. " + StartupError());
                    if (line.StartsWith(end, StringComparison.Ordinal) && int.TryParse(line[end.Length..], out int exit))
                        return new(exit, connection.Elapsed.TotalMilliseconds, execution.Elapsed.TotalMilliseconds, reused);
                    log(line + "\n");
                }
            }
            catch (Exception ex)
            {
                Stop();
                // Never automatically replay a command that may already have edited outputs.
                throw new IOException(submitted ? "The WSL build session was interrupted. The build was not retried automatically; run Build again to check its outputs. " + ex.Message
                    : "Could not start the WSL build session. " + ex.Message, ex);
            }
            finally { _lastUsed = DateTime.UtcNow; _gate.Release(); }
        }

        private string StartupError() { lock (_startupErrors) return _startupErrors.ToString(); }

        public bool CloseIfIdle(TimeSpan maximumIdle)
        {
            if (!_gate.Wait(0)) return false;
            try
            {
                if (DateTime.UtcNow - _lastUsed < maximumIdle) return false;
                Shutdown();
                return true;
            }
            finally { _gate.Release(); }
        }

        public void Shutdown()
        {
            lock (_lifecycle) { _closed = true; Stop(); }
        }

        private void Stop()
        {
            lock (_lifecycle)
            {
                var process = Interlocked.Exchange(ref _process, null);
                if (process == null) return;
                try
                {
                    if (!process.HasExited)
                    {
                        process.StandardInput.Close();
                        if (!process.WaitForExit(100)) process.Kill(entireProcessTree: true);
                    }
                }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }
                catch (IOException)
                {
                    try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                    catch (InvalidOperationException) { }
                    catch (System.ComponentModel.Win32Exception) { }
                }
                finally { process.Dispose(); }
            }
        }
    }
}
