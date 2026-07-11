using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Sm64DecompLevelViewer.Services
{
    public class WarpNode
    {
        public string MacroType { get; set; } = "WARP_NODE"; // WARP_NODE or PAINTING_WARP_NODE
        public string Id { get; set; } = "WARP_NODE_00";
        public string DestLevel { get; set; } = "LEVEL_CASTLE";
        public string DestArea { get; set; } = "1";
        public string DestNode { get; set; } = "WARP_NODE_00";
        public string Flags { get; set; } = "WARP_NO_CHECKPOINT";

        // Keep track of the original line index for targeted saving
        public int LineIndex { get; set; }
        public string OriginalText { get; set; } = string.Empty;
    }

    public class WarpService
    {
        private static readonly Regex WarpRegex = new(
            @"\b(WARP_NODE|PAINTING_WARP_NODE)\s*\(\s*([^,]+)\s*,\s*([^,]+)\s*,\s*([^,]+)\s*,\s*([^,]+)\s*,\s*([^)]+)\s*\)",
            RegexOptions.Compiled);

        public static string StripComments(string input)
        {
            return Regex.Replace(input, @"/\*.*?\*/", "").Trim();
        }

        public List<WarpNode> LoadWarps(string scriptPath)
        {
            var nodes = new List<WarpNode>();
            if (!File.Exists(scriptPath))
            {
                return nodes;
            }

            string[] lines = File.ReadAllLines(scriptPath);
            for (int i = 0; i < lines.Length; i++)
            {
                var match = WarpRegex.Match(lines[i]);
                if (match.Success)
                {
                    nodes.Add(new WarpNode
                    {
                        MacroType = match.Groups[1].Value,
                        Id = StripComments(match.Groups[2].Value),
                        DestLevel = StripComments(match.Groups[3].Value),
                        DestArea = StripComments(match.Groups[4].Value),
                        DestNode = StripComments(match.Groups[5].Value),
                        Flags = StripComments(match.Groups[6].Value),
                        LineIndex = i,
                        OriginalText = lines[i]
                    });
                }
            }

            return nodes;
        }

        public bool SaveWarps(string scriptPath, List<WarpNode> updatedNodes)
        {
            try
            {
                if (!File.Exists(scriptPath))
                {
                    return false;
                }

                string[] lines = File.ReadAllLines(scriptPath);

                // For each updated node, replace the corresponding line
                foreach (var node in updatedNodes)
                {
                    if (node.LineIndex >= 0 && node.LineIndex < lines.Length)
                    {
                        // Generate the updated macro text
                        string spaces = lines[node.LineIndex].Substring(0, lines[node.LineIndex].Length - lines[node.LineIndex].TrimStart().Length);
                        string newLine = $"{spaces}{node.MacroType}({node.Id}, {node.DestLevel}, {node.DestArea}, {node.DestNode}, {node.Flags}),";
                        lines[node.LineIndex] = newLine;
                    }
                }

                // Write with Unix line endings
                string outputContent = string.Join("\n", lines) + "\n";
                File.WriteAllText(scriptPath, outputContent);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving warps: {ex.Message}");
                return false;
            }
        }
    }
}
