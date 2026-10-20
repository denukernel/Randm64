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
                foreach(var call in CSourceCalls.Read(content,n=>n is "MACRO_OBJECT" or "MACRO_OBJECT_WITH_BHV_PARAM"))
                {
                    if(call.Arguments.Count!=(call.Name=="MACRO_OBJECT" ? 5 : 6)) continue;
                    string Arg(int i)=>call.Argument(content,i);
                    if(!presets.TryGetValue(Arg(0),out var preset)) continue;
                    string? raw=call.Arguments.Count>5 ? Arg(5) : null;
                    uint value=0;
                    if(raw!=null) uint.TryParse(raw.StartsWith("0x",StringComparison.OrdinalIgnoreCase) ? raw[2..] : raw,
                        raw.StartsWith("0x",StringComparison.OrdinalIgnoreCase) ? System.Globalization.NumberStyles.HexNumber : System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture,out value);
                    objects.Add(new LevelObject
                    {
                        ModelName=preset.Model,PresetName=Arg(0),Behavior=preset.Behavior,
                        X=int.Parse(Arg(2)),Y=int.Parse(Arg(3)),Z=int.Parse(Arg(4)),RY=int.Parse(Arg(1)),
                        Params=value|(uint)preset.Param,RawParams=raw,SourceFile=filePath,
                        SourceIndex=call.Start,SourceLength=call.End-call.Start,SourceType=ObjectSourceType.Macro
                    });
                }
            }
            catch(Exception ex) { Console.WriteLine("Error parsing macro objects: "+ex.Message); }

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
