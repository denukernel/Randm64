using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Sm64DecompLevelViewer.Services;

public sealed class WarpScriptDocument
{
    public sealed class Node
    {
        public string MacroType { get; set; } = "WARP_NODE";
        public string Id { get; set; } = "0x0A";
        public string DestLevel { get; set; } = "LEVEL_CASTLE";
        public string DestArea { get; set; } = "1";
        public string DestNode { get; set; } = "0x0A";
        public string Flags { get; set; } = "WARP_NO_CHECKPOINT";
        public string SourceArea { get; set; } = "1";
        public int Offset { get; set; } = -1;
    }
    private sealed record Macro(string Name, int Start, int Length, string[] Arguments, string Area);
    private readonly ProjectFileDocument _document;
    private List<Macro> _macros;
    public string[] Areas => _macros.Where(m => m.Name == "AREA").Select(m => m.Arguments[0]).Distinct().ToArray();
    public List<Node> Nodes => _macros.Where(m => m.Name is "WARP_NODE" or "PAINTING_WARP_NODE").Select(m => new Node
    { MacroType = m.Name, Id = m.Arguments[0], DestLevel = m.Arguments[1], DestArea = m.Arguments[2], DestNode = m.Arguments[3], Flags = m.Arguments[4], SourceArea = m.Area, Offset = m.Start }).ToList();
    public WarpScriptDocument(string path) { _document = ProjectFileDocument.Open(path); _macros = Parse(_document.Text); }
    public static string NextId(IEnumerable<Node> nodes, string area)
    {
        var used = nodes.Where(n => n.SourceArea == area).Select(n => CanonicalId(n.Id)).ToHashSet();
        return Enumerable.Range(10, 230).Where(i => !used.Contains(i.ToString())).Select(i => $"0x{i:X2}").FirstOrDefault()
            ?? throw new InvalidDataException("no free ordinary warp ids remain in this area");
    }
    private static List<Macro> Parse(string source)
    {
        string code = Regex.Replace(source, @"/\*[\s\S]*?\*/|//[^\r\n]*|""(?:\\.|[^""\\])*""", m => new string(m.Value.Select(c => c is '\n' or '\r' ? c : ' ').ToArray()));
        var result = new List<Macro>(); string area = "";
        foreach (Match match in Regex.Matches(code, @"\b(AREA|END_AREA|WARP_NODE|PAINTING_WARP_NODE)\s*\("))
        {
            int start = match.Index + match.Length, pos = start, depth = 1, argumentStart = start;
            var arguments = new List<string>();
            for (; pos < code.Length && depth > 0; pos++)
            {
                if (code[pos] == '(') depth++;
                else if (code[pos] == ')') depth--;
                if (code[pos] == ',' && depth == 1 || depth == 0)
                {
                    arguments.Add(code[argumentStart..pos].Trim()); argumentStart = pos + 1;
                }
            }
            if (depth != 0) throw new InvalidDataException("unbalanced level-script macro");
            string name = match.Groups[1].Value;
            if (name == "AREA") { if (arguments.Count != 2 || area.Length > 0) throw new InvalidDataException("ambiguous area layout"); area = arguments[0]; }
            if (name is "WARP_NODE" or "PAINTING_WARP_NODE" && arguments.Count != 5) throw new InvalidDataException("unsupported warp macro");
            result.Add(new(name, match.Index, pos - match.Index, arguments.ToArray(), area));
            if (name == "END_AREA") area = "";
        }
        return result;
    }
    public void Save(IReadOnlyList<Node> nodes)
    {
        string source = _document.Text;
        var replacements = new List<(int Offset, int Length, string Text)>();
        var oldNodes = Nodes.ToDictionary(n => n.Offset);
        if (nodes.Count == oldNodes.Count && nodes.All(n => oldNodes.TryGetValue(n.Offset, out var old) && old.SourceArea == n.SourceArea && Format(old) == Format(n)))
        {
            _document.Save(source); return;
        }
        foreach (var node in nodes)
        {
            if (node.MacroType is not ("WARP_NODE" or "PAINTING_WARP_NODE")) throw new InvalidDataException("invalid warp type");
            if (node.Offset < 0 && !Areas.Contains(node.SourceArea)) throw new InvalidDataException("select a declared source area for the new warp");
            if (node.Offset >= 0 && (!oldNodes.TryGetValue(node.Offset, out var old) || old.SourceArea != node.SourceArea)) throw new InvalidDataException("moving existing warps between areas is unsupported; add a new node in the desired area");
            foreach (string value in new[] { node.Id, node.DestLevel, node.DestArea, node.DestNode, node.Flags })
            {
                if (string.IsNullOrWhiteSpace(value) || value.IndexOfAny(new[] { ';', '{', '}', '#' }) >= 0 || value.Contains("//") || value.Contains("/*")) throw new InvalidDataException("warp fields must be single c expressions");
                int depth = 0;
                foreach (char c in value)
                {
                    if (c == '(') depth++;
                    if (c == ')') depth--;
                    if (depth < 0 || c == ',' && depth == 0) throw new InvalidDataException("unbalanced warp field expression");
                }
                if (depth != 0) throw new InvalidDataException("unbalanced warp field expression");
            }
            foreach (string value in new[] { node.Id, node.DestNode, node.DestArea })
                if (int.TryParse(CanonicalId(value), out int number) && (number < 0 || number > 255)) throw new InvalidDataException("numeric warp ids and area values must fit one byte");
        }
        foreach (var group in nodes.GroupBy(n => n.SourceArea))
            if (group.GroupBy(n => CanonicalId(n.Id)).Any(g => g.Count() > 1)) throw new InvalidDataException("duplicate warp id in source area " + group.Key);
        foreach (var macro in _macros.Where(m => m.Name is "WARP_NODE" or "PAINTING_WARP_NODE"))
        {
            var updated = nodes.SingleOrDefault(n => n.Offset == macro.Start);
            int length = macro.Length;
            if (updated == null)
            {
                int comma = macro.Start + length;
                while (comma < source.Length && source[comma] is ' ' or '\t') comma++;
                if (comma < source.Length && source[comma] == ',') length = comma - macro.Start + 1;
                replacements.Add((macro.Start, length, ""));
            }
            else
            {
                var prior = oldNodes[macro.Start];
                if (Format(prior) != Format(updated)) replacements.Add((macro.Start, length, Format(updated)));
            }
        }
        foreach (var group in nodes.Where(n => n.Offset < 0).GroupBy(n => n.SourceArea))
        {
            var ends = _macros.Where(m => m.Name == "END_AREA" && m.Area == group.Key).ToArray();
            if (ends.Length != 1) throw new InvalidDataException("source area is ambiguous; no changes were saved");
            replacements.Add((ends[0].Start, 0, string.Join("," + _document.NewLine + "    ", group.Select(Format)) + "," + _document.NewLine + "    "));
        }
        foreach (var item in replacements.OrderByDescending(r => r.Offset)) source = source.Remove(item.Offset, item.Length).Insert(item.Offset, item.Text);
        Parse(source); _document.Save(source); _macros = Parse(_document.Text);
    }
    private static string Format(Node n) => $"{n.MacroType}({n.Id}, {n.DestLevel}, {n.DestArea}, {n.DestNode}, {n.Flags})";
    private static string CanonicalId(string value)
    {
        value = value.Trim();
        if (value.StartsWith("WARP_NODE_")) value = "0x" + value[10..];
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && int.TryParse(value[2..], System.Globalization.NumberStyles.HexNumber, null, out int hex)) return hex.ToString();
        return value;
    }
    public static string NodeKey(string value) => CanonicalId(value);
}
