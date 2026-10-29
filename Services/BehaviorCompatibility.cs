using System.IO;
using System.Text.RegularExpressions;

namespace Sm64DecompLevelViewer.Services;

// Conservative placement checks based on actual source usage and loaded model IDs.
// A positive result verifies these dependencies, not arbitrary native behavior logic.
public sealed class BehaviorCompatibility
{
    private readonly Dictionary<string, string> scripts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> placements = new(StringComparer.Ordinal);
    private readonly HashSet<string> loaded;
    private readonly Dictionary<string, int> ids;
    private readonly BehaviorModelCompatibility modelCompatibility;
    private readonly Dictionary<(string Behavior, string Model), string?> modelChecks = new();
    private readonly string projectRoot;
    public BehaviorCompatibility(string root, IEnumerable<string> supportedModels, string? levelDirectory = null)
    {
        projectRoot = root;
        modelCompatibility = new(root, levelDirectory);
        loaded = LoadedModels(root, levelDirectory, supportedModels);
        ids = new ObjectParser().ParseModelIds(Path.Combine(root, "include", "model_ids.h"));
        string data = Path.Combine(root, "data", "behavior_data.c");
        if (File.Exists(data))
            foreach (Match match in Regex.Matches(ActorSourceReader.Read(data), @"\bBehaviorScript\s+(\w+)\s*\[\s*\]\s*=\s*\{([\s\S]*?)\};"))
                scripts[match.Groups[1].Value] = match.Groups[2].Value;
        string levels = Path.Combine(root, "levels");
        if (Directory.Exists(levels))
            foreach (string file in Directory.EnumerateFiles(levels, "script.c", SearchOption.AllDirectories))
            {
                string source = ActorSourceReader.Read(file);
                foreach (var call in CSourceCalls.Read(source, name => name is "OBJECT" or "OBJECT_WITH_ACTS"))
                    if (call.Arguments.Count >= 9) Register(call.Argument(source, 8), call.Argument(source, 0));
            }
        string presets = Path.Combine(root, "include", "macro_presets.inc.c");
        if (File.Exists(presets))
            foreach (var preset in new MacroObjectParser().ParsePresets(presets).Values) Register(preset.Behavior, preset.Model);
        foreach (var behavior in scripts.Where(pair => pair.Key.StartsWith("bhvCustom", StringComparison.Ordinal)))
            foreach (var call in CSourceCalls.Read(behavior.Value, name => name == "SET_MODEL"))
                if (call.Arguments.Count == 1) Register(behavior.Key, call.Argument(behavior.Value, 0));
    }
    private static HashSet<string> LoadedModels(string root, string? levelDirectory, IEnumerable<string> fallback)
    {
        string? levelScript = levelDirectory == null ? null : Path.Combine(levelDirectory, "script.c");
        string sharedScript = Path.Combine(root, "levels", "scripts.c");
        if (!File.Exists(levelScript) || !File.Exists(sharedScript)) return fallback.ToHashSet(StringComparer.Ordinal);
        string local = ActorSourceReader.Read(levelScript!), shared = ActorSourceReader.Read(sharedScript);
        var arrays = Regex.Matches(shared, @"\bLevelScript\s+(\w+)\s*\[[^\]]*\]\s*=\s*\{([\s\S]*?)\};")
            .Cast<Match>().GroupBy(match => match.Groups[1].Value)
            .ToDictionary(group => group.Key, group => group.First().Groups[2].Value);
        var result = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        void Scan(string source)
        {
            foreach (var call in CSourceCalls.Read(source, name => name is "LOAD_MODEL_FROM_GEO" or "LOAD_MODEL_FROM_DL" or "JUMP_LINK" or "MARIO"))
            {
                if (call.Arguments.Count == 0) continue;
                string argument = call.Argument(source, 0);
                if (call.Name == "JUMP_LINK")
                {
                    if (visited.Add(argument) && arrays.TryGetValue(argument, out string? linked)) Scan(linked);
                }
                else result.Add(argument);
            }
        }
        // Global baseline models plus only the shared groups called by this level.
        if (arrays.TryGetValue("level_main_scripts_entry", out string? main))
            foreach (var call in CSourceCalls.Read(main, name => name is "LOAD_MODEL_FROM_GEO" or "LOAD_MODEL_FROM_DL"))
                if (call.Arguments.Count > 0) result.Add(call.Argument(main, 0));
        Scan(local);
        return result;
    }
    private void Register(string behavior, string model)
    {
        if (!placements.TryGetValue(behavior, out var models)) placements[behavior] = models = new(StringComparer.Ordinal);
        models.Add(model);
    }
    private bool SameModel(string a, string b) => a == b || ids.TryGetValue(a, out int left) && ids.TryGetValue(b, out int right) && left == right;
    private bool Loaded(string model) => model == "MODEL_NONE" || loaded.Any(value => SameModel(model, value));
    public string? SuggestedModel(string behavior) => placements.TryGetValue(behavior, out var models)
        ? models.OrderBy(value => value).FirstOrDefault(Loaded) : null;
    public string? Rejection(string behavior, string model)
    {
        if (!scripts.ContainsKey(behavior)) return "This behavior has no definition in the active source";
        if (!Loaded(model)) return "The selected model is not loaded by this level";
        if (behavior == "bhvMario") return "The Mario behavior requires the player spawn record; use the level Mario spawn instead of a standard object";
        return Dependencies(behavior, new HashSet<string>(StringComparer.Ordinal));
    }
    public string? ModelWarning(string behavior, string model)
    {
        if (CustomObjectAnimations.IsInstalled(projectRoot, model, behavior)) return null;
        if (modelChecks.TryGetValue((behavior, model), out string? cached)) return cached;
        var commands = new List<(string Name, string[] Args)>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        void Scan(string name)
        {
            if (!visited.Add(name) || !scripts.TryGetValue(name, out string? source)) return;
            foreach (var call in CSourceCalls.Read(source, n => n is "LOAD_ANIMATIONS" or "ANIMATE" or "SET_MODEL" or "GOTO" or "CALL"))
            {
                var args = Enumerable.Range(0, call.Arguments.Count).Select(i => call.Argument(source, i)).ToArray();
                if (call.Name is "GOTO" or "CALL")
                {
                    if (args.Length > 0) Scan(args[0]);
                    if (call.Name == "GOTO") return;
                }
                else commands.Add((call.Name, args));
            }
        }
        Scan(behavior);
        string? bank = null;
        string activeModel = model;
        foreach (var command in commands)
        {
            if (command.Name == "SET_MODEL" && command.Args.Length > 0 && Regex.IsMatch(command.Args[0], @"^MODEL_\w+$")) activeModel = command.Args[0];
            if (command.Name == "LOAD_ANIMATIONS" && command.Args.Length > 1 && command.Args[0] == "oAnimations") bank = command.Args[1];
            if (command.Name == "ANIMATE" && bank != null && command.Args.Length > 0 && int.TryParse(command.Args[0], out int index))
            {
                string? problem = modelCompatibility.Rejection(activeModel, bank, index);
                if (problem != null) return modelChecks[(behavior, model)] = problem;
            }
        }
        return modelChecks[(behavior, model)] = null;
    }
    public string? CustomAnimationActor(string behavior, string model)
    {
        if (behavior == "bhvMario" || !scripts.ContainsKey(behavior)) return null;
        string? actor = modelCompatibility.ModelActor(model);
        if (actor == null) return null;
        var banks = new List<string>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        void Scan(string name)
        {
            if (!visited.Add(name) || !scripts.TryGetValue(name, out var source)) return;
            foreach (var call in CSourceCalls.Read(source, n => n is "LOAD_ANIMATIONS" or "GOTO" or "CALL"))
            {
                if (call.Name == "LOAD_ANIMATIONS" && call.Arguments.Count > 1 && call.Argument(source, 0) == "oAnimations") banks.Add(call.Argument(source, 1));
                else if (call.Arguments.Count > 0) Scan(call.Argument(source, 0));
                if (call.Name == "GOTO") return;
            }
        }
        Scan(behavior);
        return behavior == "bhvStaticObject" || banks.Any(bank => modelCompatibility.BankActor(bank) is { } owner && owner != actor) ? actor : null;
    }
    private string? Dependencies(string behavior, HashSet<string> visited)
    {
        if (!visited.Add(behavior) || !scripts.TryGetValue(behavior, out string? source)) return null;
        foreach (var call in CSourceCalls.Read(source, _ => true))
        {
            for (int index = 0; index < call.Arguments.Count; index++)
            {
                string argument = call.Argument(source, index);
                if (Regex.IsMatch(argument, @"^MODEL_\w+$") && !Loaded(argument)) return "This behavior requires a model that is not loaded: " + argument;
                if (scripts.ContainsKey(argument))
                {
                    string? problem = Dependencies(argument, visited);
                    if (problem != null) return problem;
                }
            }
        }
        return null;
    }
}
