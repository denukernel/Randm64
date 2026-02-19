using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Sm64DecompLevelViewer.Models;

namespace Sm64DecompLevelViewer.Services
{
    public class MacroPreset
    {
        public string Behavior { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public int Param { get; set; }
    }

    public class MacroObjectParser
    {
        private static readonly Regex PresetPattern = new Regex(
            @"\/\*\s*([a-zA-Z0-9_]+)\s*\*\/\s*\{\s*([^,]+)\s*,\s*([^,]+)\s*,\s*([^}]+)\}",
            RegexOptions.Compiled);

        private static readonly Regex MacroObjectPattern = new Regex(
            @"MACRO_OBJECT\s*\(\s*(?:/\*.*?\*/\s*)?([^,]+)\s*,\s*(?:/\*.*?\*/\s*)?(-?\d+)\s*,\s*(?:/\*.*?\*/\s*)?(-?\d+)\s*,\s*(?:/\*.*?\*/\s*)?(-?\d+)\s*,\s*(?:/\*.*?\*/\s*)?(-?\d+)\s*\)[\s,]*",
            RegexOptions.Compiled);

        private static readonly Regex MacroObjectWithParamPattern = new Regex(
            @"MACRO_OBJECT_WITH_BHV_PARAM\s*\(\s*(?:/\*.*?\*/\s*)?([^,]+)\s*,\s*(?:/\*.*?\*/\s*)?(-?\d+)\s*,\s*(?:/\*.*?\*/\s*)?(-?\d+)\s*,\s*(?:/\*.*?\*/\s*)?(-?\d+)\s*,\s*(?:/\*.*?\*/\s*)?(-?\d+)\s*,\s*(?:/\*.*?\*/\s*)?([^)]+)\)[\s,]*",
            RegexOptions.Compiled);

        public Dictionary<string, MacroPreset> ParsePresets(string filePath)
        {
            var presets = new Dictionary<string, MacroPreset>();
            if (!File.Exists(filePath))
            {
                Console.WriteLine($"Macro presets file not found: {filePath}");
                return presets;
            }

            try
            {
                string content = File.ReadAllText(filePath);
                var matches = PresetPattern.Matches(content);

                foreach (Match match in matches)
                {
                    string presetName = match.Groups[1].Value.Trim();
                    string behavior = match.Groups[2].Value.Trim();
                    string model = match.Groups[3].Value.Trim();
                    string paramStr = match.Groups[4].Value.Trim();

                    int paramValue = 0;
                    if (paramStr.Contains("|"))
                    {
                        // Logic for bitwise OR params could go here if needed
                    }
                    else if (int.TryParse(paramStr, out int p))
                    {
                        paramValue = p;
                    }

                    presets[presetName] = new MacroPreset
                    {
                        Behavior = behavior,
                        Model = model,
                        Param = paramValue
                    };
                }
                Console.WriteLine($"Parsed {presets.Count} macro presets from {Path.GetFileName(filePath)}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing macro presets: {ex.Message}");
            }

            return presets;
        }

        public List<LevelObject> ParseMacroFile(string filePath, Dictionary<string, MacroPreset> presets)
        {
            var objects = new List<LevelObject>();
            if (!File.Exists(filePath)) return objects;

            try
            {
                string content = File.ReadAllText(filePath);
                var matchedRanges = new List<(int Start, int End)>();

                // 1. Parse Valid Macros with Behavior Parameters
                var paramMatches = MacroObjectWithParamPattern.Matches(content);
                foreach (Match match in paramMatches)
                {
                    string presetName = match.Groups[1].Value.Trim();
                    if (presets.TryGetValue(presetName, out var preset))
                    {
                        string paramStr = match.Groups[6].Value.Trim();
                        uint paramValue = 0;
                        if (paramStr.StartsWith("0x")) paramValue = Convert.ToUInt32(paramStr, 16);
                        else uint.TryParse(paramStr, out paramValue);

                        objects.Add(new LevelObject
                        {
                            ModelName = preset.Model,
                            Behavior = preset.Behavior,
                            X = int.Parse(match.Groups[3].Value),
                            Y = int.Parse(match.Groups[4].Value),
                            Z = int.Parse(match.Groups[5].Value),
                            RY = int.Parse(match.Groups[2].Value),
                            Params = paramValue | (uint)preset.Param,
                            SourceFile = filePath,
                            SourceIndex = match.Index,
                            SourceLength = match.Length,
                            SourceType = ObjectSourceType.Macro
                        });
                        matchedRanges.Add((match.Index, match.Index + match.Length));
                    }
                }

                // 2. Parse Valid Standard Macros (ensuring they don't overlap with above)
                var macroMatches = MacroObjectPattern.Matches(content);
                foreach (Match match in macroMatches)
                {
                    if (IsOverlapping(match, matchedRanges)) continue;

                    string presetName = match.Groups[1].Value.Trim();
                    if (presets.TryGetValue(presetName, out var preset))
                    {
                        objects.Add(new LevelObject
                        {
                            ModelName = preset.Model,
                            Behavior = preset.Behavior,
                            X = int.Parse(match.Groups[3].Value),
                            Y = int.Parse(match.Groups[4].Value),
                            Z = int.Parse(match.Groups[5].Value),
                            RY = int.Parse(match.Groups[2].Value),
                            Params = (uint)preset.Param,
                            SourceFile = filePath,
                            SourceIndex = match.Index,
                            SourceLength = match.Length,
                            SourceType = ObjectSourceType.Macro
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing macro file {filePath}: {ex.Message}");
            }

            return objects;
        }

        private bool IsOverlapping(Match match, List<(int Start, int End)> ranges)
        {
            int start = match.Index;
            int end = match.Index + match.Length;
            foreach (var range in ranges)
            {
                if (start < range.End && end > range.Start) return true;
            }
            return false;
        }

        [Obsolete("The C source already uses degrees.")]
        public static float ConvertMacroRotation(int rawRotation)
        {
            return rawRotation;
        }
    }
}
