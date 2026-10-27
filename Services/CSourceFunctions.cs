using System.IO;
using System.Text.RegularExpressions;

namespace Sm64DecompLevelViewer.Services;

internal static class CSourceFunctions
{
    public static (int Start, int End) Body(string active, string name)
    {
        string masked = Regex.Replace(CSourceCalls.MaskComments(active), @"""(?:\\.|[^""\\])*""|'(?:\\.|[^'\\])*'", m => new string(' ', m.Length));
        foreach (var call in CSourceCalls.Read(active, n => n == name))
        {
            int start = call.End;
            while (start < masked.Length && char.IsWhiteSpace(masked[start])) start++;
            if (start >= masked.Length || masked[start] != '{') continue;
            int depth = 1, end = start + 1;
            while (end < masked.Length && depth > 0) { if (masked[end] == '{') depth++; if (masked[end] == '}') depth--; if (depth > 0) end++; }
            if (depth != 0) throw new InvalidDataException("unbalanced function: " + name);
            return (start + 1, end);
        }
        throw new InvalidDataException("source function not found: " + name);
    }
}
