using System.IO;
using System.Text.RegularExpressions;

namespace Sm64DecompLevelViewer.Services;

// Inspect animation channel requirements for custom-object diagnostics and export.
internal sealed class BehaviorModelCompatibility(string root, string? levelDirectory)
{
    private readonly Lazy<LevelActorCatalog> _models = new(() => new LevelActorCatalog(root, levelDirectory));
    private readonly Dictionary<string, (string Actor, string?[] Entries)?> _banks = new();
    private readonly Dictionary<string, List<MarioAnimation>> _clips = new();
    private readonly Dictionary<(string Bank, int Index), int?> _channels = new();
    public string? ModelActor(string model) => _models.Value.GetActorName(model);
    public string? BankActor(string bank) => FindBank(bank)?.Actor;

    public string? Rejection(string model, string bank, int index)
    {
        try
        {
            int? channels = Channels(bank, index);
            if (channels == null) return null;
            int? joints = _models.Value.GetJointCount(model);
            return joints > channels
                ? $"animation warning: {model} needs {joints} animated joints, but {bank} animation {index} provides only {channels}; this custom combination may animate incorrectly in game"
                : null;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or FormatException or OverflowException)
        {
            // Unreadable mod data is not evidence that the combination cannot work.
            LogService.Log($"Unable to inspect {model}/{bank} animation {index}: {ex.Message}");
            return null;
        }
    }

    private int? Channels(string bank, int index)
    {
        if (_channels.TryGetValue((bank, index), out var cached)) return cached;
        var source = FindBank(bank);
        if (source == null || index < 0 || index >= source.Value.Entries.Length || source.Value.Entries[index] is not { } symbol)
            return _channels[(bank, index)] = null;
        if (!_clips.TryGetValue(source.Value.Actor, out var clips))
            _clips[source.Value.Actor] = clips = new AnimationParser().ScanActorAnimations(root, source.Value.Actor);
        var clip = clips.FirstOrDefault(c => c.Symbol == symbol);
        if (clip == null) return _channels[(bank, index)] = null;
        new AnimationParser().LoadAnimationData(clip);
        return _channels[(bank, index)] = Math.Max(0, (clip.Indices.Length - 6) / 6);
    }

    private (string Actor, string?[] Entries)? FindBank(string bank)
    {
        if (_banks.TryGetValue(bank, out var cached)) return cached;
        string actors = Path.Combine(root, "actors");
        if (!Directory.Exists(actors)) return _banks[bank] = null;
        string prefix = bank.Split("_seg", StringSplitOptions.None)[0];
        string likely = Path.Combine(actors, prefix, "anims", "table.inc.c");
        // The prefix is only a search hint; the declaration must name the actual bank.
        if (File.Exists(likely)) Read(likely);
        if (_banks.TryGetValue(bank, out cached)) return cached;
        foreach (string file in Directory.EnumerateFiles(actors, "table.inc.c", SearchOption.AllDirectories))
        {
            Read(file);
            if (_banks.TryGetValue(bank, out cached)) return cached;
        }
        return _banks[bank] = null;
    }

    private void Read(string file)
    {
        string directory = Path.GetDirectoryName(file)!;
        string actor = Path.GetFileName(directory) == "anims" ? Path.GetFileName(Path.GetDirectoryName(directory)) : Path.GetFileName(directory);
        foreach (Match table in Regex.Matches(ActorSourceReader.Read(file), @"\b(\w+)\s*\[\s*\]\s*=\s*\{([^}]+)\}"))
        {
            var entries = CSourceCalls.MaskComments(table.Groups[2].Value).Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => Regex.Match(s.Trim(), @"^&\s*(\w+)$"))
                .Select(m => m.Success ? m.Groups[1].Value : null).ToArray();
            _banks[table.Groups[1].Value] = (actor, entries);
        }
    }
}
