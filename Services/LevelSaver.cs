using System.IO;
using System.Text.RegularExpressions;
using Sm64DecompLevelViewer.Models;

namespace Sm64DecompLevelViewer.Services;

public class LevelSaver
{
    public bool SaveLevel(List<LevelObject> objects)
    {
        bool allSaved = true;

        // 1. Group existing objects by their source file (exclude NEW objects for now)
        var objectsByFile = objects
            .Where(o => !string.IsNullOrEmpty(o.SourceFile) && !o.IsNew)
            .GroupBy(o => o.SourceFile)
            .ToList();

        foreach (var group in objectsByFile)
        {
            string filePath = group.Key!;
            var fileObjects = group.OrderByDescending(o => o.SourceIndex).ToList();

            if (!File.Exists(filePath))
            {
                Console.WriteLine($"File not found: {filePath}");
                allSaved = false;
                continue;
            }

            try
            {
                string content = File.ReadAllText(filePath);

                foreach (var obj in fileObjects)
                {
                    if (obj.IsDeleted)
                    {
                        content = content.Remove(obj.SourceIndex, obj.SourceLength);
                        continue;
                    }

                    string oldMacro = content.Substring(obj.SourceIndex, obj.SourceLength);
                    string newMacro = UpdateMacro(oldMacro, obj);
                    
                    if (oldMacro != newMacro)
                    {
                        content = content.Remove(obj.SourceIndex, obj.SourceLength)
                                         .Insert(obj.SourceIndex, newMacro);
                        
                        obj.SourceLength = newMacro.Length;
                    }
                }

                File.WriteAllText(filePath, content);
                
                if (filePath.Contains("collision.inc.c")) UpdateSpecialObjectCount(filePath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to save to {filePath}: {ex.Message}");
                allSaved = false;
            }
        }

        // 2. Handle new objects and UPDATE their metadata so they are "existing" next time
        var newObjects = objects.Where(o => o.IsNew && !o.IsDeleted).ToList();
        foreach (var obj in newObjects)
        {
            if (string.IsNullOrEmpty(obj.SourceFile)) continue;

            try
            {
                string content = File.ReadAllText(obj.SourceFile);
                string insertion = "";

                if (obj.SourceType == ObjectSourceType.Normal)
                {
                    insertion = $"\n    OBJECT({obj.ModelName}, {obj.X}, {obj.Y}, {obj.Z}, {obj.RX}, {obj.RY}, {obj.RZ}, {obj.Params}, {obj.Behavior}),";
                    
                    int index = -1;
                    int areaStart = -1;
                    var areaMatches = Regex.Matches(content, @"AREA\s*\(\s*(\d+)");
                    foreach (Match areaMatch in areaMatches)
                    {
                        if (int.Parse(areaMatch.Groups[1].Value) == obj.AreaIndex)
                        {
                            areaStart = areaMatch.Index;
                            break;
                        }
                    }

                    if (areaStart != -1)
                    {
                        var m = Regex.Match(content.Substring(areaStart), @"END_AREA\s*\(\s*\)");
                        if (m.Success) index = areaStart + m.Index;
                    }

                    if (index == -1) index = content.LastIndexOf("END_AREA");
                    if (index == -1) index = content.LastIndexOf("};");
                    
                    if (index != -1)
                    {
                        content = content.Insert(index, insertion);
                        obj.SourceIndex = index + 1; // +1 for the newline
                    }
                }
                else if (obj.SourceType == ObjectSourceType.Macro)
                {
                    string macroName = string.IsNullOrEmpty(obj.PresetName) ? obj.ModelName : obj.PresetName;
                    insertion = $"\n    MACRO_OBJECT({macroName}, {obj.RY}, {obj.X}, {obj.Y}, {obj.Z}),";
                    int index = content.LastIndexOf("MACRO_OBJECT_END");
                    if (index == -1) index = content.LastIndexOf("};");
                    if (index != -1)
                    {
                        content = content.Insert(index, insertion);
                        obj.SourceIndex = index + 1;
                    }
                }
                else if (obj.SourceType == ObjectSourceType.Special)
                {
                    string presetName = string.IsNullOrEmpty(obj.PresetName) ? obj.ModelName : obj.PresetName;
                    insertion = $"\n    SPECIAL_OBJECT({presetName}, {obj.X}, {obj.Y}, {obj.Z}),";
                    int index = -1;
                    var specialMatches = Regex.Matches(content, @"SPECIAL_OBJECT(?:_WITH_YAW(?:_AND_PARAM)?)?");
                    if (specialMatches.Count > 0)
                    {
                        var lastMatch = specialMatches[specialMatches.Count - 1];
                        index = content.IndexOf(",", lastMatch.Index);
                        if (index != -1) index++;
                    }
                    else
                    {
                        var initMatch = Regex.Match(content, @"COL_SPECIAL_INIT\s*\(\s*(\d+)\s*\)");
                        if (initMatch.Success)
                        {
                            index = content.IndexOf(",", initMatch.Index);
                            if (index != -1) index++;
                        }
                    }
                    
                    if (index == -1) index = content.LastIndexOf("COL_END");
                    if (index == -1) index = content.LastIndexOf("};");
                    if (index != -1)
                    {
                        content = content.Insert(index, insertion);
                        obj.SourceIndex = index + 1;
                    }
                }

                if (obj.SourceIndex > 0)
                {
                    obj.SourceLength = insertion.Trim().Length;
                    File.WriteAllText(obj.SourceFile, content);
                    obj.IsNew = false; // It's now a persistent object
                    
                    if (obj.SourceFile.Contains("collision.inc.c")) UpdateSpecialObjectCount(obj.SourceFile);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to insert object: {ex.Message}");
                allSaved = false;
            }
        }

        return allSaved;
    }

    private void UpdateSpecialObjectCount(string filePath)
    {
        try
        {
            string content = File.ReadAllText(filePath);
            int count = Regex.Matches(content, @"SPECIAL_OBJECT(?:_WITH_YAW(?:_AND_PARAM)?)?\s*\(").Count;
            var match = Regex.Match(content, @"COL_SPECIAL_INIT\s*\(\s*(\d+)\s*\)");
            if (match.Success)
            {
                content = content.Replace(match.Value, $"COL_SPECIAL_INIT({count})");
                File.WriteAllText(filePath, content);
            }
        }
        catch { }
    }

    private string UpdateMacro(string macro, LevelObject obj)
    {
        if (obj.ModelName.Contains("ERROR") || string.IsNullOrEmpty(obj.ModelName)) return macro;

        string ws = @"(?:\s|/\*.*?\*/)*";

        // General Pattern: Identifier(Args)Tail
        // Identifier is group 1
        // Args is group 2
        // Tail (comma, whitespace, etc) is group 3
        var genericPattern = $@"^({ws})(\w+){ws}\({ws}(.*?){ws}\)({ws}[,;]?{ws})$";
        var genericMatch = Regex.Match(macro, genericPattern, RegexOptions.Singleline);
        if (!genericMatch.Success) return macro;

        string prefix = genericMatch.Groups[1].Value;
        string identifier = genericMatch.Groups[2].Value;
        string tail = genericMatch.Groups[4].Value;

        if (identifier == "MARIO_POS")
        {
            return $"{prefix}MARIO_POS(0x01, {obj.RY}, {obj.X}, {obj.Y}, {obj.Z}){tail}";
        }

        if (identifier.Contains("MACRO_OBJECT"))
        {
            string macroName = string.IsNullOrEmpty(obj.PresetName) ? obj.ModelName : obj.PresetName;
            if (identifier.Contains("WITH_BHV_PARAM"))
            {
                var args = genericMatch.Groups[3].Value.Split(',');
                string param = args.Length >= 6 ? args[5].Trim() : "0";
                return $"{prefix}MACRO_OBJECT_WITH_BHV_PARAM({macroName}, {obj.RY}, {obj.X}, {obj.Y}, {obj.Z}, {param}){tail}";
            }
            return $"{prefix}MACRO_OBJECT({macroName}, {obj.RY}, {obj.X}, {obj.Y}, {obj.Z}){tail}";
        }

        if (identifier.Contains("SPECIAL_OBJECT"))
        {
            int byteYaw = (int)Math.Round(((obj.RY % 360 + 360) % 360) * 256.0 / 360.0) % 256;
            string presetName = string.IsNullOrEmpty(obj.PresetName) ? obj.ModelName : obj.PresetName;
            
            if (identifier.Contains("WITH_YAW_AND_PARAM"))
            {
                var args = genericMatch.Groups[3].Value.Split(',');
                string param = args.Length >= 6 ? args[5].Trim() : "0x00";
                return $"{prefix}SPECIAL_OBJECT_WITH_YAW_AND_PARAM({presetName}, {obj.X}, {obj.Y}, {obj.Z}, {byteYaw}, {param}){tail}";
            }
            if (identifier.Contains("WITH_YAW"))
            {
                return $"{prefix}SPECIAL_OBJECT_WITH_YAW({presetName}, {obj.X}, {obj.Y}, {obj.Z}, {byteYaw}){tail}";
            }
            return $"{prefix}SPECIAL_OBJECT({presetName}, {obj.X}, {obj.Y}, {obj.Z}){tail}";
        }

        if (identifier.Contains("OBJECT"))
        {
            var pattern = $@"OBJECT(?:_WITH_ACTS)?{ws}\({ws}([^,]+?){ws},{ws}(-?\d+?|[^,]+?){ws},{ws}(-?\d+?|[^,]+?){ws},{ws}(-?\d+?|[^,]+?){ws},{ws}(-?\d+?|[^,]+?){ws},{ws}(-?\d+?|[^,]+?){ws},{ws}(-?\d+?|[^,]+?){ws},{ws}([^,]+?){ws},{ws}([^,)]+?){ws}(?:,{ws}([^)]+?){ws})?\)";
            var match = Regex.Match(macro, pattern);
            if (match.Success)
            {
                string acts = match.Groups[10].Success ? $", {match.Groups[10].Value}" : "";
                string bhvParam = match.Groups[8].Value;
                string newId = identifier.Contains("WITH_ACTS") ? "OBJECT_WITH_ACTS" : "OBJECT";
                return $"{prefix}{newId}({obj.ModelName}, {obj.X}, {obj.Y}, {obj.Z}, {obj.RX}, {obj.RY}, {obj.RZ}, {bhvParam}, {obj.Behavior}{acts}){tail}";
            }
        }

        return macro;
    }
}
