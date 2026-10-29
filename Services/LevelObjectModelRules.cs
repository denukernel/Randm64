using System.IO;
using System.Text.RegularExpressions;
using Sm64DecompLevelViewer.Models;

namespace Sm64DecompLevelViewer.Services;

public sealed class LevelObjectModelRules
{
    private readonly Dictionary<string, string> _scripts = new(StringComparer.Ordinal);
    private readonly Lazy<BehaviorCompatibility> _compatibility;

    public LevelObjectModelRules(string root, IEnumerable<string> supportedModels, string? levelDirectory)
    {
        _compatibility = new(() => new BehaviorCompatibility(root, supportedModels, levelDirectory));
        string path = Path.Combine(root, "data", "behavior_data.c");
        if (File.Exists(path))
            foreach (Match match in Regex.Matches(ActorSourceReader.Read(path), @"\bBehaviorScript\s+(\w+)\s*\[\s*\]\s*=\s*\{([\s\S]*?)\};"))
                _scripts[match.Groups[1].Value] = match.Groups[2].Value;
    }

    public static string? RecordRestriction(LevelObject obj) => obj.SourceType switch
    {
        ObjectSourceType.Mario => "Mario uses the level Mario definition; the spawn record cannot change its model or behavior",
        ObjectSourceType.Macro or ObjectSourceType.Special => "This model is defined by its preset; change the preset or convert to a standard object",
        _ => obj.Behavior == "bhvMario" ? "The Mario behavior requires the Mario model" : null
    };

    public string? Restriction(LevelObject obj)
    {
        if (RecordRestriction(obj) is { } reason) return reason;
        return SetsModel(obj.Behavior, new HashSet<string>(StringComparer.Ordinal))
            ? "This behavior sets its own model in the game" : null;
    }

    private bool SetsModel(string behavior, HashSet<string> visited)
    {
        if (!visited.Add(behavior) || !_scripts.TryGetValue(behavior, out string? source)) return false;
        foreach (var call in CSourceCalls.Read(source, name => name is "SET_MODEL" or "GOTO" or "CALL"))
        {
            if (call.Name == "SET_MODEL") return true;
            if (call.Arguments.Count > 0 && SetsModel(call.Argument(source, 0), visited)) return true;
        }
        return false;
    }

    public string? Rejection(LevelObject obj, string model)
    {
        if (model == obj.ModelName) return null;
        return Restriction(obj) ?? _compatibility.Value.Rejection(obj.Behavior, model);
    }
    public string? PairRejection(string behavior, string model) => _compatibility.Value.Rejection(behavior, model);
    public string? ExistingModelWarning(LevelObject obj) => obj.SourceType == ObjectSourceType.Normal
        ? _compatibility.Value.ModelWarning(obj.Behavior, obj.ModelName) : null;
}
