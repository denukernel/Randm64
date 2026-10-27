using System.IO;
using System.Text.RegularExpressions;

namespace Sm64DecompLevelViewer.Services;

public static class GraphicsSourceValidation
{
    private static readonly Dictionary<string, int> Arity = new()
    {
        ["gsDPLoadBlock"] = 5, ["gsDPLoadTile"] = 5, ["gsDPSetTileSize"] = 5,
        ["gsDPSetTile"] = 12, ["gsDPLoadTextureBlock"] = 12, ["gsDPLoadTextureBlock_4b"] = 11,
        ["gsSP1Triangle"] = 4, ["gsSP2Triangles"] = 8
    };
    public static void Validate(string source)
    {
        string masked = CSourceCalls.MaskComments(source);
        var calls = CSourceCalls.Read(source, n => n.StartsWith("gs"));
        var starts = calls.ToDictionary(c => c.Start);
        foreach (Match token in Regex.Matches(masked, @"\bgs[A-Z]\w*\s*\("))
        {
            if (!starts.TryGetValue(token.Index, out var call)) throw new InvalidDataException("unbalanced graphics command near " + token.Value);
            if (Arity.TryGetValue(call.Name, out int count) && call.Arguments.Count != count)
                throw new InvalidDataException($"{call.Name} has {call.Arguments.Count} arguments; expected {count}");
            if (Regex.IsMatch(call.Text(source)[(call.Name.Length + 1)..], @"\bgs[A-Z]\w*\s*\("))
                throw new InvalidDataException("a graphics command was swallowed by an unbalanced argument list: " + call.Name);
        }
    }
}
