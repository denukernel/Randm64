using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Sm64DecompLevelViewer.Services;

public sealed record MusicSequenceChoice(string Symbol, int Id, string? FilePath)
{
    public override string ToString() => $"{Id:X2}: {Symbol}";
}
public sealed record LevelMusicBinding(string Label, string Path, string? Function, int Area, string? Expression)
{
    public override string ToString() => Label;
}

public sealed class LevelMusicService
{
    private readonly string root, level;
    private readonly int area;
    private readonly Dictionary<string, byte[]?> versions = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<MusicSequenceChoice> Sequences { get; }
    public LevelMusicService(string projectRoot, string levelDirectory, int areaIndex)
    {
        root = projectRoot; level = levelDirectory; area = areaIndex;
        string header = Path.Combine(root, "include", "seq_ids.h");
        string active = ActorSourceReader.Read(header);
        var body = Regex.Match(CSourceCalls.MaskComments(active), @"enum\s+SeqId\s*\{([^}]+)\}").Groups[1].Value;
        var files = Directory.Exists(Path.Combine(root, "sound", "sequences")) ? Directory.GetFiles(Path.Combine(root, "sound", "sequences"), "*.m64", SearchOption.AllDirectories) : Array.Empty<string>();
        int id = 0; var choices = new List<MusicSequenceChoice>();
        foreach (string token in body.Split(','))
        {
            var item = Regex.Match(token.Trim(), @"^(SEQ_\w+)(?:\s*=\s*(0x[\da-fA-F]+|\d+))?$");
            if (!item.Success || item.Groups[1].Value == "SEQ_COUNT") continue;
            if (item.Groups[2].Success) id = item.Groups[2].Value.StartsWith("0x") ? Convert.ToInt32(item.Groups[2].Value, 16) : int.Parse(item.Groups[2].Value);
            string prefix = id.ToString("X2") + "_";
            string? file = files.Where(f => Path.GetFileName(f).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(f => Path.GetFileName(Path.GetDirectoryName(f)) == "us").ThenBy(f => f).FirstOrDefault();
            choices.Add(new(item.Groups[1].Value, id++, file));
        }
        Sequences = choices;
    }

    private string Read(string path)
    {
        var bytes = File.ReadAllBytes(path); versions.TryAdd(path, bytes);
        return Encoding.UTF8.GetString(bytes);
    }
    private static (int Start, int End) AreaBody(string active, int index)
    {
        int start = -1;
        foreach (var call in CSourceCalls.Read(active, n => n is "AREA" or "END_AREA"))
        {
            if (call.Name == "AREA")
            {
                string number = call.Argument(active, 0);
                int parsed = number.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? Convert.ToInt32(number, 16) : int.Parse(number);
                start = parsed == index ? call.End : -1;
            }
            else if (start >= 0) return (start, call.Start);
        }
        throw new InvalidDataException("selected area was not found in the level script");
    }
    public IReadOnlyList<LevelMusicBinding> LoadBindings()
    {
        var result = new List<LevelMusicBinding>();
        string script = Path.Combine(level, "script.c");
        string text = Read(script), active = ActorSourceReader.Read(script, preserveOffsets: true, sourceText: text);
        var range = AreaBody(active, area);
        var background = CSourceCalls.Read(active, n => n == "SET_BACKGROUND_MUSIC").Where(c => c.Start >= range.Start && c.End <= range.End).ToArray();
        if (background.Length > 1) throw new InvalidDataException("this area contains multiple music commands; edit its script to disambiguate");
        result.Add(new("area background music", script, null, area, background.SingleOrDefault()?.Argument(active, 1)));
        if (Path.GetFileName(level) == "castle_grounds")
            foreach (var cue in new[]
            {
                ("peach intro message", "camera.c", "cutscene_intro_peach_play_message_music"),
                ("lakitu intro flight", "camera.c", "cutscene_intro_peach_play_lakitu_flying_music"),
                ("lakitu dialog music", "behaviors/camera_lakitu.inc.c", "camera_lakitu_intro_act_show_dialog")
            })
            {
                string path = Path.Combine(root, "src", "game", cue.Item2);
                if (!File.Exists(path)) continue;
                text = Read(path); active = ActorSourceReader.Read(path, preserveOffsets: true, sourceText: text);
                try
                {
                    var body = CSourceFunctions.Body(active, cue.Item3);
                    var args = CSourceCalls.Read(active, n => n == "SEQUENCE_ARGS").Where(c => c.Start >= body.Start && c.End <= body.End).ToArray();
                    if (args.Length == 1) result.Add(new(cue.Item1, path, cue.Item3, area, args[0].Argument(active, 1)));
                }
                catch (InvalidDataException) { }
            }
        return result;
    }
    public MusicSequenceChoice? Resolve(string? expression) => expression == null ? null : Sequences.FirstOrDefault(s => Regex.IsMatch(expression, @"\b" + Regex.Escape(s.Symbol) + @"\b"));

    public void Save(LevelMusicBinding binding, MusicSequenceChoice sequence)
    {
        if (!Sequences.Contains(sequence)) throw new InvalidDataException("select a sequence defined by this game build");
        string source = File.ReadAllText(binding.Path), active = ActorSourceReader.Read(binding.Path, preserveOffsets: true, sourceText: source);
        CSourceCall? call;
        if (binding.Function == null)
        {
            var range = AreaBody(active, binding.Area);
            var matches = CSourceCalls.Read(active, n => n == "SET_BACKGROUND_MUSIC").Where(c => c.Start >= range.Start && c.End <= range.End).ToArray();
            if (matches.Length > 1) throw new InvalidDataException("multiple area music commands cannot be changed safely");
            call = matches.SingleOrDefault();
            if (call == null)
            {
                string newline = source.Contains("\r\n") ? "\r\n" : "\n";
                source = source.Insert(range.End, "    SET_BACKGROUND_MUSIC(0x0000, " + sequence.Symbol + ")," + newline + "    ");
            }
        }
        else
        {
            var body = CSourceFunctions.Body(active, binding.Function);
            var matches = CSourceCalls.Read(active, n => n == "SEQUENCE_ARGS").Where(c => c.Start >= body.Start && c.End <= body.End).ToArray();
            if (matches.Length != 1) throw new InvalidDataException("this cutscene cue changed; reload before editing");
            call = matches[0];
        }
        if (call != null)
        {
            string old = call.Argument(active, 1);
            var symbol = Regex.Match(old, @"\bSEQ_(?!VARIATION\b|BASE_ID\b)\w+\b");
            string replacement = symbol.Success ? old[..symbol.Index] + sequence.Symbol + old[(symbol.Index + symbol.Length)..] : sequence.Symbol;
            string edited = CSourceCalls.ReplaceArguments(source, call, new() { [1] = replacement });
            source = source[..call.Start] + edited + source[call.End..];
        }
        byte[] bytes = Encoding.UTF8.GetBytes(source);
        SourceFileTransaction.Commit(new Dictionary<string, byte[]> { [binding.Path] = bytes }, new Dictionary<string, byte[]?> { [binding.Path] = versions[binding.Path] });
        versions[binding.Path] = bytes;
    }
}
