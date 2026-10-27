using System.IO;
using System.Text;

namespace Sm64DecompLevelViewer.Services;

public sealed class SourceEditPlan
{
    private readonly Dictionary<string, byte[]> writes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, byte[]?> expected = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> originals = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> texts = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, byte[]> Writes => writes;
    public IReadOnlyDictionary<string, byte[]?> ExpectedVersions => expected;
    public string Read(string path)
    {
        path = Path.GetFullPath(path);
        if (!expected.ContainsKey(path))
        {
            var document = File.Exists(path) ? ProjectFileDocument.Open(path) : null;
            expected[path] = document?.Snapshot;
            originals[path] = document?.Text ?? "";
        }
        return texts.TryGetValue(path, out string? staged) ? staged : originals[path];
    }
    public void Write(string path, string text)
    {
        path = Path.GetFullPath(path);
        if (!expected.ContainsKey(path)) Read(path);
        if (expected[path] != null)
        {
            var document = ProjectFileDocument.Open(path);
            if (!document.Snapshot.SequenceEqual(expected[path]!)) throw new IOException("source changed while preparing edits: " + path);
            writes[path] = document.EncodeText(text);
        }
        else writes[path] = new UTF8Encoding(false).GetBytes(text);
        texts[path] = text;
        if (expected[path]?.SequenceEqual(writes[path]) == true) writes.Remove(path);
    }
    public string Preview(string root) => string.Join("\n\n", writes.Keys.Select(path =>
        "file: " + Path.GetRelativePath(root, path) + "\n" + Difference(originals[path], texts[path])));
    private static string Difference(string original, string updated)
    {
        string[] before=original.Replace("\r\n","\n").Split('\n'), after=updated.Replace("\r\n","\n").Split('\n');
        int start=0, suffix=0;
        while(start<before.Length && start<after.Length && before[start]==after[start]) start++;
        while(suffix<before.Length-start && suffix<after.Length-start && before[^(suffix+1)]==after[^(suffix+1)]) suffix++;
        return $"changes near line {start+1}\n" + string.Join("\n",
            before.Skip(Math.Max(0,start-3)).Take(Math.Min(3,start)).Select(line=>"  "+line)
            .Concat(before.Skip(start).Take(before.Length-start-suffix).Select(line=>"- "+line))
            .Concat(after.Skip(start).Take(after.Length-start-suffix).Select(line=>"+ "+line))
            .Concat(after.Skip(after.Length-suffix).Take(3).Select(line=>"  "+line)));
    }
    public void Commit() => SourceFileTransaction.Commit(writes, expected);
}
