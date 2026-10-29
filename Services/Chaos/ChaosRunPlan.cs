using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Sm64DecompLevelViewer.Services.Chaos;

internal static class ChaosSeed
{
    // Numeric seeds retain their old meaning. Text uses FNV-1a over UTF-8, not process-randomized GetHashCode.
    internal static int Resolve(string text)
    {
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int seed)) return seed;
        uint hash = 2166136261;
        foreach (byte b in Encoding.UTF8.GetBytes(text)) hash = unchecked((hash ^ b) * 16777619);
        return unchecked((int)hash);
    }
}

internal enum ChaosRunStatus { Completed, Cancelled, Failed, RecoveryRequired }
internal sealed record ChaosRunResult(ChaosRunStatus Status, string Message, string? RecoveryPath = null);
internal sealed record ChaosStagedChange(string RelativePath, string? BeforeHash, string AfterHash);

/// <summary>Runs existing file-based transforms in a private mirror. Live files are only touched by Commit.</summary>
internal sealed class ChaosRunPlan : IDisposable
{
    internal const string EngineVersion = "3";
    private readonly string _root;
    private readonly string _work;
    private readonly Dictionary<string, string> _inputs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _externalInputs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _outputs = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _validatedPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _pendingInputs = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ChaosStagedChange> _changes = new();
    private bool _retain;
    private bool _finished;
    private string? _error;
    internal string StageRoot { get; }
    internal IReadOnlyList<ChaosStagedChange> Changes => _changes.AsReadOnly();
    internal string SettingsJson { get; set; } = "{}";
    internal List<string> Diagnostics { get; } = new();
    // Fault injection for deterministic transaction regression tests.
    internal Action<string, int>? Checkpoint { get; set; }

    private ChaosRunPlan(string root)
    {
        _root = Path.GetFullPath(root);
        if (!Directory.Exists(_root)) throw new DirectoryNotFoundException(_root);
        _work = Path.Combine(Path.GetTempPath(), "Randm64-chaos-" + Guid.NewGuid().ToString("N"));
        StageRoot = Path.Combine(_work, "stage");
        Directory.CreateDirectory(StageRoot);
    }

    internal static ChaosRunPlan Create(string root, CancellationToken token, IEnumerable<string>? trees = null, IProgress<ChaosProgress>? progress = null)
    {
        var plan = new ChaosRunPlan(root);
        try
        {
            // Only trees consumed by chaos transforms. Never copy build output, Git, or editor state.
            string[] supported = ["include", "src", "actors", "levels", "leveleditor", "sound", "textures", "assets", "text"];
            foreach (string tree in (trees ?? supported).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal))
            {
                string path = Path.GetFullPath(Path.Combine(plan._root, tree));
                string relative = Path.GetRelativePath(plan._root, path);
                ValidateRelative(relative);
                string top = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
                if (!supported.Contains(top, StringComparer.OrdinalIgnoreCase)) throw new IOException("unsupported chaos source tree: " + tree);
                if (Directory.Exists(path)) plan.CopyTree(path, token);
                else if (File.Exists(path)) plan.CopyFile(path, token);
            }
            var inputs = plan._pendingInputs.OrderBy(p => p.Key, StringComparer.Ordinal).ToArray();
            var hashes = new string[inputs.Length];
            int copied = 0;
            Parallel.For(0, inputs.Length, new ParallelOptions { CancellationToken = token, MaxDegreeOfParallelism = 4 }, i =>
            {
                string staged = Path.Combine(plan.StageRoot, inputs[i].Key);
                Directory.CreateDirectory(Path.GetDirectoryName(staged)!);
                File.Copy(inputs[i].Value, staged);
                hashes[i] = Hash(staged);
                int count = Interlocked.Increment(ref copied);
                if ((count & 63) == 0 || count == inputs.Length)
                    progress?.Report(new ChaosProgress($"Copying source inputs: {count}/{inputs.Length} files", null));
            });
            for (int i = 0; i < inputs.Length; i++) plan._inputs.Add(inputs[i].Key, hashes[i]);
            plan._pendingInputs.Clear();
            return plan;
        }
        catch { plan.Dispose(); throw; }
    }

    private void CopyTree(string directory, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        ValidateSourcePath(directory);
        Directory.CreateDirectory(Path.Combine(StageRoot, Path.GetRelativePath(_root, directory)));
        foreach (string path in Directory.EnumerateFiles(directory).Order(StringComparer.Ordinal))
        {
            CopyFile(path, token);
        }
        foreach (string child in Directory.EnumerateDirectories(directory).Order(StringComparer.Ordinal))
            if (!new[] { ".git", ".vs", ".agents", ".codex", "bin", "obj", "build" }.Contains(Path.GetFileName(child), StringComparer.OrdinalIgnoreCase)) CopyTree(child, token);
    }

    private void CopyFile(string path, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        string relative = Path.GetRelativePath(_root, path);
        if (_pendingInputs.ContainsKey(relative)) return;
        ValidateSourcePath(path);
        _pendingInputs.Add(relative, path);
    }

    private void ValidateSourcePath(string path)
    {
        for (string? parent = path; parent != null && !string.Equals(parent, _root, StringComparison.OrdinalIgnoreCase); parent = Path.GetDirectoryName(parent))
        {
            if (_validatedPaths.Contains(parent)) break;
            if ((File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0)
                throw new IOException($"linked source path is unsupported: {parent}");
            _validatedPaths.Add(parent);
        }
    }

    internal string Target(string path)
    {
        string relative = Path.GetRelativePath(_root, Path.GetFullPath(path));
        ValidateRelative(relative);
        string staged = Path.Combine(StageRoot, relative);
        if (!File.Exists(staged)) throw new FileNotFoundException("selected target is outside the supported source trees", path);
        return staged;
    }

    internal string Import(string path, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        string absolute = Path.GetFullPath(path);
        string relative = Path.GetRelativePath(_root, absolute);
        if (!Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar))
        {
            string staged = Path.Combine(StageRoot, relative);
            if (File.Exists(staged)) return staged;
        }
        string imported = Path.Combine(_work, "imports", Guid.NewGuid().ToString("N") + Path.GetExtension(path));
        Directory.CreateDirectory(Path.GetDirectoryName(imported)!);
        File.Copy(absolute, imported);
        _externalInputs[absolute] = Hash(imported);
        // External replacement sources are copied once and are never modified or backed up in place.
        return imported;
    }

    internal void Seal(CancellationToken token)
    {
        _changes.Clear(); _outputs.Clear();
        foreach (string path in Directory.EnumerateFiles(StageRoot, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            token.ThrowIfCancellationRequested();
            string relative = Path.GetRelativePath(StageRoot, path);
            if (relative.EndsWith(".m64", StringComparison.OrdinalIgnoreCase))
                Audio.M64Integrity.RequireNonZero(relative, File.ReadAllBytes(path));
            string hash = Hash(path);
            _outputs.Add(relative, hash);
            _inputs.TryGetValue(relative, out string? before);
            if (before != hash) _changes.Add(new(relative, before, hash));
        }
        if (_inputs.Keys.Any(p => !_outputs.ContainsKey(p))) throw new IOException("chaos transforms may not delete source files");
        WriteReport("prepared");
    }

    internal string Preview() => $"Engine version: {EngineVersion}\nSettings: {SettingsJson}\n\nValidation passed\n{_changes.Count} changed files (including new backups)\n\n"
        + string.Join("\n", Diagnostics) + "\n\n" + string.Join("\n", _changes.Select(c => $"{c.RelativePath}\n  {c.BeforeHash ?? "new"} -> {c.AfterHash}"));

    internal ChaosRunResult Commit(CancellationToken token)
    {
        if (_finished) throw new InvalidOperationException("this plan was already consumed");
        var applied = new List<ChaosStagedChange>();
        var createdDirectories = new List<string>();
        try
        {
            token.ThrowIfCancellationRequested();
            // Check every captured input, including backups and unmodified dependencies.
            var inputs = _inputs.ToArray();
            var unchanged = new bool[inputs.Length];
            Parallel.For(0, inputs.Length, new ParallelOptions { CancellationToken = token, MaxDegreeOfParallelism = 4 }, i =>
            {
                string path = Live(inputs[i].Key);
                unchanged[i] = File.Exists(path) && Hash(path) == inputs[i].Value;
            });
            for (int i = 0; i < inputs.Length; i++)
                if (!unchanged[i]) throw new IOException($"source changed after preparation: {inputs[i].Key}");
            foreach (var input in _externalInputs)
                if (!File.Exists(input.Key) || Hash(input.Key) != input.Value) throw new IOException("replacement source changed: " + input.Key);
            WriteReport("committing");
            using var journal = new FileStream(Path.Combine(_work, "intent.jsonl"), FileMode.Create, FileAccess.Write, FileShare.Read);
            foreach (var change in _changes)
            {
                token.ThrowIfCancellationRequested();
                string live = Live(change.RelativePath);
                Checkpoint?.Invoke("before-write", applied.Count);
                if ((change.BeforeHash == null && File.Exists(live)) ||
                    (change.BeforeHash != null && (!File.Exists(live) || Hash(live) != change.BeforeHash)))
                    throw new IOException($"source changed before replacement: {change.RelativePath}");
                if (Hash(Path.Combine(StageRoot, change.RelativePath)) != change.AfterHash)
                    throw new IOException($"staged output changed: {change.RelativePath}");
                string recovery = Path.Combine(_work, "before", change.RelativePath);
                if (change.BeforeHash != null)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(recovery)!);
                    File.Copy(live, recovery);
                }
                string directory = Path.GetDirectoryName(live)!;
                var missing = new Stack<string>();
                for (string? d = directory; d != null && !Directory.Exists(d); d = Path.GetDirectoryName(d)) missing.Push(d);
                while (missing.TryPop(out var d)) { Directory.CreateDirectory(d); createdDirectories.Add(d); }
                // Journal intent before replacement so interrupted runs retain the recovery location.
                byte[] intent = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(change.RelativePath) + "\n");
                journal.Write(intent);
                journal.Flush(flushToDisk: true);
                Replace(Path.Combine(StageRoot, change.RelativePath), live);
                applied.Add(change);
                Checkpoint?.Invoke("after-write", applied.Count);
            }
            token.ThrowIfCancellationRequested();
            WriteReport("completed", applied.Select(c => c.RelativePath));
            _finished = true;
            return new(ChaosRunStatus.Completed, $"completed: {applied.Count} files", Path.Combine(_work, "run.json"));
        }
        catch (Exception error)
        {
            var failures = new List<string>();
            foreach (var change in applied.AsEnumerable().Reverse())
            {
                try
                {
                    Checkpoint?.Invoke("rollback", failures.Count);
                    string live = Live(change.RelativePath);
                    if (!File.Exists(live) || Hash(live) != change.AfterHash)
                        throw new IOException("file changed after commit; refusing to overwrite external edits");
                    if (change.BeforeHash == null) File.Delete(live);
                    else Replace(Path.Combine(_work, "before", change.RelativePath), live);
                }
                catch (Exception ex) { failures.Add($"{change.RelativePath}: {ex.Message}"); }
            }
            foreach (string d in createdDirectories.AsEnumerable().Reverse())
                try { if (!Directory.EnumerateFileSystemEntries(d).Any()) Directory.Delete(d); } catch (IOException) { }
            _retain = failures.Count > 0;
            _finished = true;
            _error = error.Message;
            try { WriteReport(_retain ? "recovery-required" : error is OperationCanceledException ? "cancelled" : "failed", applied.Select(c => c.RelativePath)); }
            catch (Exception ex) { failures.Add("journal: " + ex.Message); _retain = true; }
            return new(_retain ? ChaosRunStatus.RecoveryRequired : error is OperationCanceledException ? ChaosRunStatus.Cancelled : ChaosRunStatus.Failed,
                error.Message + (failures.Count == 0 ? "\nPre-run files restored" : "\n" + string.Join("\n", failures)), _retain ? _work : null);
        }
    }

    internal void CancelPreview() { _finished = true; WriteReport("cancelled"); }

    internal void SaveReport(string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(Path.Combine(_work, "run.json"), destination, true);
    }

    private void WriteReport(string status, IEnumerable<string>? applied = null)
    {
        byte[] data = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { EngineVersion, Root = _root, Status = status, Error = _error,
            Settings = JsonSerializer.Deserialize<JsonElement>(SettingsJson), Inputs = _inputs, ExternalInputs = _externalInputs,
            IntentJournal = status is "committing" or "recovery-required" ? "intent.jsonl" : null,
            Changes = _changes, Applied = applied?.ToArray(), Diagnostics }, new JsonSerializerOptions { WriteIndented = true }));
        string temporary = Path.Combine(_work, "run.json.next");
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(data);
            stream.Flush(flushToDisk: true);
        }
        File.Move(temporary, Path.Combine(_work, "run.json"), true);
    }
    private static void Replace(string source, string destination)
    {
        string temp = destination + ".chaos-" + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.Copy(source, temp); File.Move(temp, destination, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    private string Live(string relative) { ValidateRelative(relative); return Path.Combine(_root, relative); }
    private static void ValidateRelative(string relative)
    {
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar))
            throw new IOException("chaos target must be inside the project");
    }
    private static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
    public void Dispose()
    {
        if (_retain) return;
        string tempRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string resolved = Path.GetFullPath(_work);
        if (!resolved.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(resolved).StartsWith("Randm64-chaos-", StringComparison.Ordinal))
            throw new IOException("invalid staging cleanup path");
        if (Directory.Exists(resolved)) Directory.Delete(resolved, true);
    }
}
