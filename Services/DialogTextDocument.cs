using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Sm64DecompLevelViewer.Services;

public sealed class DialogTextEntry
{
    public string Id { get; init; } = "";
    public int Lines { get; init; }
    public int Start { get; init; }
    public int Length { get; init; }
    public string Text { get; init; } = "";
    public bool Editable { get; init; }
    public string Expression { get; init; } = "";
    public List<string> Macros { get; init; } = new();
    public override string ToString() => Id;
}

public static class DialogTextDocument
{
    private static readonly Regex Header = new(@"DEFINE_DIALOG\s*\(\s*(\w+)\s*,\s*[^,]+,\s*(\d+)\s*,\s*[^,]+,\s*[^,]+,\s*_\s*\(");
    private static readonly Regex Literal = new("\"((?:\\\\[\\s\\S]|[^\"\\\\])*)\"");
    public static List<DialogTextEntry> Parse(string source)
    {
        var entries = new List<DialogTextEntry>();
        foreach (Match header in Header.Matches(source))
        {
            int start = header.Index + header.Length, end = start;
            bool quoted = false, escaped = false;
            int depth = 0;
            for (; end < source.Length; end++)
            {
                char c = source[end];
                if (escaped) { escaped = false; continue; }
                if (quoted && c == '\\') { escaped = true; continue; }
                if (!quoted && c == '/' && end + 1 < source.Length && source[end + 1] == '*')
                {
                    int commentEnd = source.IndexOf("*/", end + 2, StringComparison.Ordinal);
                    if (commentEnd < 0) { end = source.Length; break; }
                    end = commentEnd + 1; continue;
                }
                if (!quoted && c == '/' && end + 1 < source.Length && source[end + 1] == '/')
                {
                    int commentEnd = source.IndexOf('\n', end + 2);
                    if (commentEnd < 0) { end = source.Length; break; }
                    end = commentEnd; continue;
                }
                if (c == '"') quoted = !quoted;
                if (!quoted && c == '(') depth++;
                if (!quoted && c == ')' && depth-- == 0) break;
            }
            if (end == source.Length) continue;
            string expression = source.Substring(start, end - start);
            var macros = new List<string>();
            string text = "";
            int position = 0;
            foreach (Match literal in TopLevelLiterals(expression))
            {
                AddGap(expression.Substring(position, literal.Index - position));
                text += Decode(literal.Groups[1].Value);
                position = literal.Index + literal.Length;
            }
            AddGap(expression.Substring(position));
            void AddGap(string gap)
            {
                if (string.IsNullOrWhiteSpace(gap)) return;
                text += $"[[macro:{macros.Count}]]";
                macros.Add(gap);
            }
            entries.Add(new DialogTextEntry { Id = header.Groups[1].Value, Lines = Math.Clamp(int.Parse(header.Groups[2].Value), 1, 16), Start = start, Length = end - start, Text = text, Editable = true, Expression = expression, Macros = macros });
        }
        return entries;
    }
    private static IEnumerable<Match> TopLevelLiterals(string expression)
    {
        int depth = 0;
        for (int i = 0; i < expression.Length; i++)
        {
            if (i + 1 < expression.Length && expression[i] == '/' && expression[i + 1] == '*')
            {
                int end = expression.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = end < 0 ? expression.Length : end + 1; continue;
            }
            if (i + 1 < expression.Length && expression[i] == '/' && expression[i + 1] == '/')
            {
                int end = expression.IndexOf('\n', i + 2);
                i = end < 0 ? expression.Length : end; continue;
            }
            if (expression[i] == '"')
            {
                var literal = Literal.Match(expression, i);
                if (!literal.Success || literal.Index != i) yield break;
                if (depth == 0) yield return literal;
                i += literal.Length - 1;
            }
            else if (expression[i] == '(') depth++;
            else if (expression[i] == ')') depth--;
        }
    }
    public static string Encode(DialogTextEntry entry, string text)
    {
        if (text == entry.Text) return entry.Expression;
        var markers = Regex.Matches(text, @"\[\[macro:[^\]]*\]\]");
        if (markers.Count != entry.Macros.Count || markers.Cast<Match>().Where((m, i) => m.Value != $"[[macro:{i}]]").Any())
            throw new InvalidOperationException("keep each [[macro:number]] marker unchanged and in its original order");
        int position = 0;
        string expression = "";
        for (int i = 0; i < markers.Count; i++)
        {
            expression += Encode(text.Substring(position, markers[i].Index - position)) + " " + entry.Macros[i] + " ";
            position = markers[i].Index + markers[i].Length;
        }
        return expression + Encode(text.Substring(position));
    }
    private static string Decode(string value)
    {
        value = value.Replace("\\\r\n", "").Replace("\\\n", "");
        return Regex.Replace(value, @"\\(.)", m => m.Groups[1].Value switch { "n" => "\n", "t" => "\t", "r" => "\r", "\\" => "\\", "\"" => "\"", _ => m.Value });
    }
    public static string Encode(string text) => "\"" + text.Replace("\r\n", "\n").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\t", "\\t").Replace("\r", "\\r").Replace("\n", "\\n\"\n\"") + "\"";
}
