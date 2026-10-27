using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Sm64DecompLevelViewer.Services.Build;

// Reads build metadata without invoking make: even make -n/-p can run $(shell) and != commands.
internal sealed class RepositoryBuildProfile
{
    private readonly string _source;
    private readonly HashSet<string> _variables;
    private readonly Dictionary<string, string> _defaults;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, IReadOnlyDictionary<string, string>> _evaluations = new();
    private SourceStamp[] _dependencies = Array.Empty<SourceStamp>();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, RepositoryBuildProfile> Profiles = new(StringComparer.OrdinalIgnoreCase);
    private sealed record SourceStamp(string Path, bool Exists, long Length, DateTime Written)
    {
        public static SourceStamp Read(string path)
        {
            var file = new FileInfo(path);
            return file.Exists ? new(path, true, file.Length, file.LastWriteTimeUtc) : new(path, false, 0, DateTime.MinValue);
        }
    }
    public string Root { get; }
    public string Name { get; }
    public string Fingerprint { get; }
    public string? Error { get; }
    public bool Has(string variable) => _variables.Contains(variable);
    public bool SupportsPc { get; }
    public bool SupportsN64 { get; }
    public string[] Versions { get; }
    public string[] Compilers { get; }
    public string[] Microcodes { get; }
    public string Default(string variable, string fallback) => _defaults.GetValueOrDefault(variable, fallback);

    private RepositoryBuildProfile(string root, string source, string? error)
    {
        Root = Path.GetFullPath(root);
        _source = source;
        _variables = Regex.Matches(source, @"(?m)^\s*(?:override\s+|export\s+)?([A-Za-z_]\w*)\s*[:?+]?=")
            .Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        _defaults = new(StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(source, @"(?m)^\s*([A-Za-z_]\w*)\s*\?=\s*([\w.-]+)"))
            _defaults.TryAdd(match.Groups[1].Value, match.Groups[2].Value);
        Fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
        SupportsPc = Has("EXE") && source.Contains("src/pc");
        SupportsN64 = Has("ROM") && (!SupportsPc || Has("TARGET_N64"));
        Name = source.Contains("HACKERSM64") || source.Contains("HackerSM64") || Has("LIBGCCDIR") ? "HackerSM64"
            : SupportsPc && SupportsN64 ? "sm64-port compatible"
            : SupportsPc ? "sm64ex compatible" : SupportsN64 ? "sm64 decomp compatible" : "Unknown repository";
        Versions = Options("VERSION", new[] { "jp", "us", "eu", "sh", "cn" });
        Compilers = Has("COMPILER") ? Options("COMPILER", new[] { "ido", "gcc", "clang" }) : new[] { "gcc" };
        Microcodes = Has("GRUCODE") ? Options("GRUCODE", new[] { "f3d_old", "f3d_new", "f3dex", "f3dex2", "f3dex2e", "f3dzex" }) : Array.Empty<string>();
        Error = error ?? (!SupportsPc && !SupportsN64 ? "Cannot identify a ROM or native executable target. Select a supported SM64 make-based repository." : null);
    }

    public static RepositoryBuildProfile Load(string root)
    {
        try
        {
            string fullRoot = Path.GetFullPath(root);
            if (Profiles.TryGetValue(fullRoot, out var cached) && cached._dependencies.All(stamp => SourceStamp.Read(stamp.Path) == stamp))
                return cached;
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var dependencies = new List<SourceStamp>();
            string Read(string file)
            {
                file = Path.GetFullPath(file);
                if (!file.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    || !visited.Add(file) || visited.Count > 64) return "";
                var stamp = SourceStamp.Read(file);
                dependencies.Add(stamp);
                if (!stamp.Exists) return "";
                var result = new StringBuilder();
                string content = File.ReadAllText(file).Replace("\\\r\n", " ").Replace("\\\n", " ");
                bool inDefinition = false;
                foreach (string raw in content.Split('\n'))
                {
                    // Some forks indent conditionals and global assignments with tabs too.
                    if (raw.StartsWith('\t') && !Regex.IsMatch(raw.TrimStart(),
                        @"^(?:if(?:eq|neq|def|ndef)\b|else\b|endif\b|(?:export\s+)?[A-Za-z_]\w*\s*[:?+]?=)")) continue;
                    string line = raw.Split('#')[0].TrimEnd();
                    if (Regex.IsMatch(line, @"^\s*(?:override\s+)?define\s+")) { inDefinition = true; continue; }
                    if (line.Trim() == "endef") { inDefinition = false; continue; }
                    if (inDefinition) continue;
                    var include = Regex.Match(line, @"^\s*-?include\s+(.+)$");
                    if (include.Success)
                    {
                        foreach (string name in include.Groups[1].Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                            if (!name.Contains('$') && !name.Contains('*')) result.AppendLine(Read(Path.Combine(fullRoot, name)));
                    }
                    else result.AppendLine(line);
                }
                return result.ToString();
            }
            string source = Read(Path.Combine(fullRoot, "Makefile"));
            var profile = new RepositoryBuildProfile(root, source, source.Length == 0 ? "Cannot read the project Makefile. Check the project path and access permissions." : null)
                { _dependencies = dependencies.ToArray() };
            if (profile.Error == null)
            {
                if (Profiles.Count > 32) Profiles.Clear();
                Profiles[fullRoot] = profile;
            }
            return profile;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new(root, "", "Cannot inspect the project Makefile: " + ex.Message);
        }
    }

    private string[] Options(string name, string[] candidates)
    {
        var validation = Regex.Match(_source, @"validate-option,\s*" + name + @",\s*([^)$]+)\)");
        if (!validation.Success)
            validation = Regex.Match(_source, @"Value of " + name + @" must be one of(?: the following)?:\s*([^\r\n.)]+)");
        if (validation.Success)
            return Regex.Matches(validation.Groups[1].Value, @"[A-Za-z0-9_.-]+").Select(m => m.Value).Distinct().ToArray();
        var found = candidates.Where(c => Regex.IsMatch(_source, @"\$\(" + name + @"\)\s*,\s*" + Regex.Escape(c) + @"\s*\)")).ToList();
        string defaultValue = Default(name, "");
        if (defaultValue.Length > 0 && !found.Contains(defaultValue)) found.Insert(0, defaultValue);
        return found.Count > 0 ? found.ToArray() : candidates;
    }

    public bool RequiresNonMatching(AppSettings settings) => settings.BuildTargetPlatform == "PC"
        || settings.BuildCompiler is "gcc" or "clang";

    public List<string> Normalize(AppSettings settings)
    {
        var changes = new List<string>();
        if (settings.BuildTargetPlatform == "PC" ? !SupportsPc : !SupportsN64)
        {
            settings.BuildTargetPlatform = SupportsN64 ? "N64" : "PC";
            changes.Add("Selected the supported target: " + settings.BuildTargetPlatform);
        }
        string Choose(string current, string[] allowed, string preferred, string label)
        {
            if (allowed.Contains(current)) return current;
            string selected = allowed.Contains(preferred) ? preferred : allowed.FirstOrDefault() ?? "";
            if (current != selected) changes.Add(label + " adjusted to repository default: " + selected);
            return selected;
        }
        settings.BuildVersion = Choose(settings.BuildVersion, Versions, Default("VERSION", "us"), "Version");
        settings.BuildCompiler = settings.BuildTargetPlatform == "PC" ? "gcc"
            : Choose(settings.BuildCompiler, Compilers, Default("COMPILER", "ido"), "Compiler");
        settings.BuildGrucode = settings.BuildTargetPlatform == "PC" && Has("GRUCODE") ? "f3dex2e"
            : Choose(settings.BuildGrucode, Microcodes, Default("GRUCODE", "f3d_old"), "Microcode");
        if (RequiresNonMatching(settings)) settings.BuildNonMatching = true;
        if (!Has("COMPARE") || settings.BuildTargetPlatform == "PC") settings.BuildCompare = false;
        return changes;
    }

    public string? Validate(AppSettings settings)
    {
        if (Error != null) return Error;
        if (settings.BuildTargetPlatform is not ("N64" or "PC")) return "Unknown build target.";
        if (settings.BuildTargetPlatform == "PC" ? !SupportsPc : !SupportsN64) return "This repository does not support the selected target.";
        if (!Versions.Contains(settings.BuildVersion)) return "The selected version is not supported by this repository.";
        if (settings.BuildTargetPlatform == "N64" && !Compilers.Contains(settings.BuildCompiler)) return "The selected compiler is not supported by this repository.";
        if (settings.BuildTargetPlatform == "PC" && settings.BuildCompiler != "gcc") return "Native PC builds require GCC in this build profile.";
        if (settings.BuildTargetPlatform == "PC" && Has("GRUCODE") && settings.BuildGrucode != "f3dex2e") return "Native PC builds require the repository PC microcode (f3dex2e).";
        if (settings.BuildTargetPlatform == "PC" && settings.BuildCompare) return "ROM hash comparison is unavailable for native PC builds.";
        if (settings.BuildTargetPlatform == "N64" && Has("GRUCODE") && !Microcodes.Contains(settings.BuildGrucode)) return "The selected microcode is not supported by this repository.";
        if (RequiresNonMatching(settings) && !settings.BuildNonMatching) return "This compiler/target requires non-matching mode.";
        return null;
    }

    // Deliberately limited evaluator for paths and simple conditions, never a shell or full make interpreter.
    // Unresolved expressions remain visible and cause an actionable failure rather than a guessed output.
    public IReadOnlyDictionary<string, string> Evaluate(AppSettings settings)
    {
        string key = string.Join("|", settings.BuildTargetPlatform, settings.BuildVersion, settings.BuildCompiler,
            settings.BuildGrucode, settings.BuildNonMatching, settings.BuildCompare, settings.BuildEnvironment);
        if (_evaluations.Count > 64) _evaluations.Clear();
        return _evaluations.GetOrAdd(key, _ => EvaluateUncached(settings));
    }

    private IReadOnlyDictionary<string, string> EvaluateUncached(AppSettings settings)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["VERSION"] = settings.BuildVersion, ["TARGET_N64"] = settings.BuildTargetPlatform == "PC" ? "0" : "1",
            ["OS"] = settings.BuildTargetPlatform == "PC" && settings.BuildEnvironment == "MSYS2" ? "Windows_NT" : "Linux",
            ["HOST_OS"] = settings.BuildEnvironment == "MSYS2" ? "Windows" : "Linux",
            ["TARGET_WEB"] = "0", ["TARGET_RPI"] = "0", ["MAKECMDGOALS"] = "",
        };
        if (Has("COMPILER")) values["COMPILER"] = settings.BuildCompiler;
        if (Has("GRUCODE")) values["GRUCODE"] = settings.BuildGrucode;
        if (Has("NON_MATCHING")) values["NON_MATCHING"] = settings.BuildNonMatching ? "1" : "0";
        if (Has("COMPARE")) values["COMPARE"] = settings.BuildCompare ? "1" : "0";
        var commandVariables = values.Keys.Where(k => k is "VERSION" or "TARGET_N64" or "COMPILER" or "GRUCODE" or "NON_MATCHING" or "COMPARE").ToHashSet();
        var simple = new HashSet<string>(values.Keys);
        string Expand(string text) => ExpandVariables(text, values);
        var stack = new Stack<(bool Parent, bool Taken, bool Unknown)>();
        bool active = true;
        foreach (string raw in _source.Split('\n'))
        {
            string line = raw.Trim();
            if (line == "endif") { if (stack.Count > 0) active = stack.Pop().Parent; continue; }
            bool isElse = line == "else" || line.StartsWith("else ");
            if (isElse)
            {
                if (stack.Count == 0) continue;
                var previous = stack.Pop();
                active = previous.Parent && !previous.Taken && !previous.Unknown;
                if (line == "else") { stack.Push((previous.Parent, true, previous.Unknown)); continue; }
                line = line[5..].TrimStart();
                // else if belongs to the same conditional, not a nested block
                bool? condition = Condition(line, Expand);
                stack.Push((previous.Parent, previous.Taken || condition == true, previous.Unknown || condition == null));
                active &= condition == true;
                continue;
            }
            if (Regex.IsMatch(line, @"^if(eq|neq|def|ndef)\b"))
            {
                bool? condition = Condition(line, Expand);
                stack.Push((active, condition == true, condition == null));
                active &= condition == true;
                continue;
            }
            if (!active) continue;
            var match = Regex.Match(line, @"^(?:export\s+)?([A-Za-z_][A-Za-z_0-9]*)\s*(:=|\?=|\+=|=)\s*(.*)$");
            if (!match.Success) continue;
            string key = match.Groups[1].Value, op = match.Groups[2].Value, value = match.Groups[3].Value.Trim();
            if (commandVariables.Contains(key) || op == "?=" && values.ContainsKey(key)) continue;
            if (op == "+=") value = values.GetValueOrDefault(key, "") + " " + (simple.Contains(key) ? Expand(value) : value);
            else if (op == ":=") { value = Expand(value); simple.Add(key); }
            else simple.Remove(key);
            values[key] = value;
        }
        return new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(values.ToDictionary(p => p.Key, p => Expand(p.Value)));
    }

    private static string ExpandVariables(string text, Dictionary<string, string> values)
    {
        for (int i = 0; i < 16; i++)
        {
            string next = Regex.Replace(text, @"\$\(([A-Za-z_][A-Za-z_0-9]*)\)|\$\{([A-Za-z_][A-Za-z_0-9]*)\}",
                m => values.GetValueOrDefault(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value, ""));
            if (next == text) break;
            text = next;
        }
        return text;
    }

    private static bool? Condition(string line, Func<string, string> expand)
    {
        var defined = Regex.Match(line, @"^if(n?def)\s+(\w+)$");
        if (defined.Success) return (expand("$(" + defined.Groups[2].Value + ")").Length > 0) != (defined.Groups[1].Value == "ndef");
        var match = Regex.Match(line, @"^if(eq|neq)\s*\((.*)\)$");
        if (!match.Success) return null;
        string args = expand(match.Groups[2].Value);
        if (args.Contains('$')) return null;
        int comma = args.IndexOf(',');
        if (comma < 0) return null;
        return (args[..comma].Trim() == args[(comma + 1)..].Trim()) != (match.Groups[1].Value == "neq");
    }

    public string ArtifactPath(AppSettings settings)
    {
        var values = Evaluate(settings);
        string variable = settings.BuildTargetPlatform == "PC" ? "EXE" : "ROM";
        string path = values.GetValueOrDefault(variable, "");
        if (string.IsNullOrWhiteSpace(path) || path.Contains('$'))
            throw new InvalidOperationException($"Cannot resolve {variable} from this repository Makefile. Use simple BUILD_DIR and {variable} definitions for this target.");
        string full = Path.GetFullPath(Path.Combine(Root, path.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The build output must be inside the selected repository.");
        string extension = Path.GetExtension(full);
        if (settings.BuildTargetPlatform == "N64" ? !extension.Equals(".z64", StringComparison.OrdinalIgnoreCase)
            : settings.BuildEnvironment == "MSYS2" ? !extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) : extension is ".elf" or ".o" or ".map" or ".html" or ".z64")
            throw new InvalidOperationException("The resolved output is not a supported ROM or native game executable.");
        return full;
    }
}
