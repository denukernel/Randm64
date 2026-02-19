using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Sm64DecompLevelViewer.Services
{
    public class SpecialObjectParser
    {
        private static readonly Regex PresetPattern = new Regex(
            @"\{\s*([a-zA-Z0-9_]+)\s*,\s*(?:SPTYPE_|SP[A-Z0-9_]+|[a-zA-Z0-9_]+)\s*,\s*(?:0x[a-fA-F0-9]+|\d+)\s*,\s*([a-zA-Z0-9_]+)",
            RegexOptions.Compiled);

        private static readonly Regex AliasPattern = new Regex(
            @"([a-zA-Z0-9_]+)\s*=\s*([a-zA-Z0-9_]+)",
            RegexOptions.Compiled);

        public Dictionary<string, string> ParsePresets(string filePath)
        {
            var mapping = new Dictionary<string, string>();
            
            if (!File.Exists(filePath))
            {
                Console.WriteLine($"Special presets file not found: {filePath}");
                return mapping;
            }

            try
            {
                string content = File.ReadAllText(filePath);
                var matches = PresetPattern.Matches(content);

                foreach (Match match in matches)
                {
                    string presetName = match.Groups[1].Value;
                    string modelId = match.Groups[2].Value;
                    
                    if (!mapping.ContainsKey(presetName))
                    {
                        mapping[presetName] = modelId;
                    }
                }

                // Try to find the header file for aliases
                string headerPath = filePath.Replace(".inc.c", ".h");
                if (!File.Exists(headerPath)) headerPath = Path.ChangeExtension(filePath, ".h");
                
                if (File.Exists(headerPath))
                {
                    string headerContent = File.ReadAllText(headerPath);
                    var aliasMatches = AliasPattern.Matches(headerContent);
                    foreach (Match m in aliasMatches)
                    {
                        string alias = m.Groups[1].Value;
                        string target = m.Groups[2].Value;
                        if (mapping.TryGetValue(target, out string? modelId) && !mapping.ContainsKey(alias))
                        {
                            mapping[alias] = modelId;
                        }
                    }
                }
                
                Console.WriteLine($"Parsed {mapping.Count} special object presets from {Path.GetFileName(filePath)}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing special presets: {ex.Message}");
            }

            return mapping;
        }
    }
}
