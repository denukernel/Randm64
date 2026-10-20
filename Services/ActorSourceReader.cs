using System.IO;
using System.Text.RegularExpressions;

namespace Sm64DecompLevelViewer.Services;

// Select one regional source branch before parsing scene nodes or display lists.
internal static class ActorSourceReader
{
    public static string Read(string path, HashSet<string>? visited = null, bool preserveOffsets = false, bool keepIncludes = false, bool keepDefines = false, string? sourceText = null)
    {
        visited ??= new(StringComparer.OrdinalIgnoreCase);
        path = Path.GetFullPath(path);
        if (!visited.Add(path)) return "";
        var output = new System.Text.StringBuilder();
        var stack = new Stack<(bool Parent, bool Taken)>();
        bool active = true;
        bool Defined(string name) => name is "VERSION_US" or "TARGET_N64";
        bool Evaluate(string expression)
        {
            expression = Regex.Replace(expression, @"defined\s*(?:\(\s*(\w+)\s*\)|(\w+))", match =>
                Defined(match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value) ? "1" : "0");
            return expression.Split("||").Any(or => or.Split("&&").All(term =>
            {
                term = term.Trim().Trim('(', ')').Trim();
                bool negate = term.StartsWith('!');
                string value = negate ? term[1..].Trim() : term;
                bool result = value == "1" || Defined(value);
                return negate ? !result : result;
            }));
        }
        foreach (Match row in Regex.Matches(sourceText ?? File.ReadAllText(path), @"([^\r\n]*)(\r\n|\r|\n|$)"))
        {
            if(row.Length==0) continue;
            string line=row.Groups[1].Value,newline=row.Groups[2].Value;
            var directive = Regex.Match(line, @"^\s*#\s*(\w+)\s*(.*)");
            if (directive.Success)
            {
                string command = directive.Groups[1].Value, argument = directive.Groups[2].Value.Trim();
                if (preserveOffsets) output.Append(keepDefines && active && command == "define" ? line : new string(' ',line.Length)).Append(newline);
                else if (keepDefines && active && command == "define") output.Append(line).Append(newline);
                if (command is "ifdef" or "ifndef" or "if")
                {
                    bool selected = command == "if" ? Evaluate(argument) : Defined(argument) ^ (command == "ifndef");
                    stack.Push((active, selected));
                    active &= selected;
                }
                else if (command is "else" or "elif" && stack.Count > 0)
                {
                    var frame = stack.Pop();
                    bool selected = !frame.Taken && (command == "else" || Evaluate(argument));
                    stack.Push((frame.Parent, frame.Taken || selected));
                    active = frame.Parent && selected;
                }
                else if (command == "endif" && stack.Count > 0) active = stack.Pop().Parent;
                else if (command == "include" && active && !preserveOffsets)
                {
                    if(keepIncludes) { output.AppendLine(line); continue; }
                    var include = Regex.Match(argument, @"""([^""]+\.c)""");
                    if (include.Success)
                    {
                        string target = Path.Combine(Path.GetDirectoryName(path)!, include.Groups[1].Value);
                        if (File.Exists(target)) output.AppendLine(Read(target, visited));
                    }
                }
                continue;
            }
            if (preserveOffsets) output.Append(active ? line : new string(' ',line.Length)).Append(newline);
            else if (active) output.AppendLine(line);
        }
        return output.ToString();
    }
}
