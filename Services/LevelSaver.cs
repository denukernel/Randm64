using System.IO;
using System.Text.RegularExpressions;
using Sm64DecompLevelViewer.Models;

namespace Sm64DecompLevelViewer.Services;

public class LevelSaver
{
    public void SaveLevel(List<LevelObject> objects)
    {
        // Group objects by their source file
        var objectsByFile = objects
            .Where(o => !string.IsNullOrEmpty(o.SourceFile))
            .GroupBy(o => o.SourceFile)
            .ToList();

        foreach (var group in objectsByFile)
        {
            string filePath = group.Key!;
            var fileObjects = group.OrderByDescending(o => o.SourceIndex).ToList();

            if (!File.Exists(filePath))
            {
                Console.WriteLine($"Cannot save to missing file: {filePath}");
                continue;
            }

            try
            {
                string content = File.ReadAllText(filePath);

                foreach (var obj in fileObjects)
                {
                    if (obj.IsDeleted)
                    {
                        int deltaDeleted = -obj.SourceLength;
                        content = content.Remove(obj.SourceIndex, obj.SourceLength);
                        continue;
                    }

                    string oldMacro = content.Substring(obj.SourceIndex, obj.SourceLength);
                    string newMacro = UpdateMacro(oldMacro, obj);
                    
                    if (oldMacro != newMacro)
                    {
                        int delta = newMacro.Length - obj.SourceLength;
                        
                        content = content.Remove(obj.SourceIndex, obj.SourceLength)
                                         .Insert(obj.SourceIndex, newMacro);
                        
                        // Update this object's metadata
                        obj.SourceLength = newMacro.Length;

                        // Shift indices for all other objects in THIS file that appear LATER in the file
                        // Note: Index shifting is not needed when processing in descending order of SourceIndex.
                        // However, we update obj.SourceLength and keep the original SourceIndex for earlier objects.
                    }
                }

                File.WriteAllText(filePath, content);
                
                // After saving a collision file, update the SPECIAL_OBJECT count if it exists
                if (filePath.Contains("collision.inc.c"))
                {
                    UpdateSpecialObjectCount(filePath);
                }

                Console.WriteLine($"Saved changes to {filePath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to save to {filePath}: {ex.Message}");
            }
        }

        // Handle new objects
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

                    int index = -1;
                    if (areaStart != -1)
                    {
                        var allEndAreas = Regex.Matches(content, @"END_AREA\s*\(\s*\)");
                        foreach (Match m in allEndAreas)
                        {
                            if (m.Index > areaStart)
                            {
                                index = m.Index;
                                break;
                            }
                        }
                    }

                    if (index == -1) index = content.LastIndexOf("END_AREA");
                    if (index == -1) index = content.LastIndexOf("};");
                    if (index != -1) content = content.Insert(index, insertion);
                }
                else if (obj.SourceType == ObjectSourceType.Macro)
                {
                    string preset = obj.ModelName.StartsWith("macro_") ? obj.ModelName : "macro_goomba_triplet_formation";
                    insertion = $"\n    MACRO_OBJECT({preset}, {obj.RY}, {obj.X}, {obj.Y}, {obj.Z}),";
                    
                    int index = content.LastIndexOf("MACRO_OBJECT_END");
                    if (index == -1) index = content.LastIndexOf("};");
                    if (index != -1) content = content.Insert(index, insertion);
                }
                else if (obj.SourceType == ObjectSourceType.Special)
                {
                    insertion = $"\n    SPECIAL_OBJECT({obj.ModelName}, {obj.X}, {obj.Y}, {obj.Z}),";
                    
                    // Find the last SPECIAL_OBJECT or COL_SPECIAL_INIT and insert after it
                    var specialMatches = Regex.Matches(content, @"SPECIAL_OBJECT(?:_WITH_YAW(?:_AND_PARAM)?)?");
                    int index = -1;
                    if (specialMatches.Count > 0)
                    {
                        var lastMatch = specialMatches[specialMatches.Count - 1];
                        // Find the end of this line (the comma)
                        index = content.IndexOf(",", lastMatch.Index);
                        if (index != -1) index++; // After the comma
                    }
                    else
                    {
                        // Fallback: look for COL_SPECIAL_INIT
                        var initMatch = Regex.Match(content, @"COL_SPECIAL_INIT\s*\(\s*(\d+)\s*\)");
                        if (initMatch.Success)
                        {
                            index = content.IndexOf(",", initMatch.Index);
                            if (index != -1) index++;
                        }
                    }
                    
                    if (index == -1) index = content.LastIndexOf("COL_END");
                    if (index == -1) index = content.LastIndexOf("};");
                    
                    if (index != -1) content = content.Insert(index, insertion);
                }

                File.WriteAllText(obj.SourceFile, content);
                
                if (obj.SourceFile.Contains("collision.inc.c"))
                {
                    UpdateSpecialObjectCount(obj.SourceFile);
                }

                obj.IsNew = false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to insert new object into {obj.SourceFile}: {ex.Message}");
            }
        }
    }

    private void UpdateSpecialObjectCount(string filePath)
    {
        try
        {
            string content = File.ReadAllText(filePath);
            
            // Re-count all special objects
            int count = Regex.Matches(content, @"SPECIAL_OBJECT(?:_WITH_YAW(?:_AND_PARAM)?)?\s*\(").Count;
            
            // Update COL_SPECIAL_INIT(N)
            var match = Regex.Match(content, @"COL_SPECIAL_INIT\s*\(\s*(\d+)\s*\)");
            if (match.Success)
            {
                string oldLine = match.Value;
                string newLine = $"COL_SPECIAL_INIT({count})";
                content = content.Replace(oldLine, newLine);
                File.WriteAllText(filePath, content);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to update special object count in {filePath}: {ex.Message}");
        }
    }

    private string UpdateMacro(string macro, LevelObject obj)
    {
        // Safety check: Never inject placeholder/error names into the C source.
        if (obj.ModelName.Contains("ERROR") || obj.ModelName.Contains("UNKNOWN") || obj.ModelName.Contains("CORRUPTED") ||
            obj.Behavior.Contains("ERROR") || obj.Behavior.Contains("Unknown") || obj.Behavior.Contains("Corrupted") ||
            string.IsNullOrEmpty(obj.ModelName))
        {
            return macro;
        }

        // Helper regex for arbitrary whitespace and optional comments
        string ws = @"(?:\s|/\*.*?\*/)*";

        // 1. MARIO_POS(area, yaw, x, y, z)
        if (macro.Contains("MARIO_POS"))
        {
            var pattern = $@"MARIO_POS{ws}\({ws}([^,]+?){ws},{ws}(-?\d+?|[^,]+?){ws},{ws}(-?\d+?|[^,]+?){ws},{ws}(-?\d+?|[^,]+?){ws},{ws}(-?\d+?|[^,]+?){ws}\)";
            return Regex.Replace(macro, pattern, m => $"MARIO_POS({m.Groups[1].Value}, {obj.RY}, {obj.X}, {obj.Y}, {obj.Z})");
        }

        // 2. MACRO_OBJECT
        if (macro.Contains("MACRO_OBJECT"))
        {
            if (macro.Contains("WITH_BHV_PARAM"))
            {
                var pattern = $@"MACRO_OBJECT_WITH_BHV_PARAM{ws}\({ws}([^,]+?){ws},{ws}(-?\d+?|[^,]+?){ws},{ws}-?\d+?{ws},{ws}-?\d+?{ws},{ws}-?\d+?{ws},{ws}([^)]+?){ws}\)";
                return Regex.Replace(macro, pattern, m => $"MACRO_OBJECT_WITH_BHV_PARAM({m.Groups[1].Value}, {obj.RY}, {obj.X}, {obj.Y}, {obj.Z}, {m.Groups[3].Value})");
            }
            else
            {
                var pattern = $@"MACRO_OBJECT{ws}\({ws}([^,]+?){ws},{ws}(-?\d+?|[^,]+?){ws},{ws}-?\d+?{ws},{ws}-?\d+?{ws},{ws}-?\d+?{ws}\)";
                return Regex.Replace(macro, pattern, m => $"MACRO_OBJECT({m.Groups[1].Value}, {obj.RY}, {obj.X}, {obj.Y}, {obj.Z})");
            }
        }

        // 3. SPECIAL_OBJECT
        if (macro.Contains("SPECIAL_OBJECT"))
        {
            int byteYaw = (int)Math.Round(((obj.RY % 360 + 360) % 360) * 256.0 / 360.0) % 256;

            if (macro.Contains("WITH_YAW_AND_PARAM"))
            {
                var pattern = $@"SPECIAL_OBJECT_WITH_YAW_AND_PARAM{ws}\({ws}([^,]+?){ws},{ws}-?\d+?{ws},{ws}-?\d+?{ws},{ws}-?\d+?{ws},{ws}-?\d+?{ws},{ws}([^)]+?){ws}\)";
                return Regex.Replace(macro, pattern, m => $"SPECIAL_OBJECT_WITH_YAW_AND_PARAM({m.Groups[1].Value}, {obj.X}, {obj.Y}, {obj.Z}, {byteYaw}, {m.Groups[2].Value})");
            }
            else if (macro.Contains("WITH_YAW"))
            {
                var pattern = $@"SPECIAL_OBJECT_WITH_YAW{ws}\({ws}([^,]+?){ws},{ws}-?\d+?{ws},{ws}-?\d+?{ws},{ws}-?\d+?{ws},{ws}-?\d+?{ws}\)";
                return Regex.Replace(macro, pattern, m => $"SPECIAL_OBJECT_WITH_YAW({m.Groups[1].Value}, {obj.X}, {obj.Y}, {obj.Z}, {byteYaw})");
            }
            else
            {
                var pattern = $@"SPECIAL_OBJECT{ws}\({ws}([^,]+?){ws},{ws}-?\d+?{ws},{ws}-?\d+?{ws},{ws}-?\d+?{ws}\)";
                return Regex.Replace(macro, pattern, m => $"SPECIAL_OBJECT({m.Groups[1].Value}, {obj.X}, {obj.Y}, {obj.Z})");
            }
        }

        // 4. Regular OBJECT (model, x, y, z, rx, ry, rz, params, bhv)
        if (macro.Contains("OBJECT"))
        {
            // Handle balanced parentheses for arguments like BPARAM1(X) | BPARAM2(Y)
            var pattern = $@"OBJECT(?:_WITH_ACTS)?{ws}\({ws}([^,]*?(?:\([^)]*?\)[^,]*?)?){ws},{ws}([^,]*?(?:\([^)]*?\)[^,]*?)?){ws},{ws}([^,]*?(?:\([^)]*?\)[^,]*?)?){ws},{ws}([^,]*?(?:\([^)]*?\)[^,]*?)?){ws},{ws}([^,]*?(?:\([^)]*?\)[^,]*?)?){ws},{ws}([^,]*?(?:\([^)]*?\)[^,]*?)?){ws},{ws}([^,]*?(?:\([^)]*?\)[^,]*?)?){ws},{ws}([^,]*?(?:\([^)]*?\)[^,]*?)?){ws},{ws}([^,)]*?(?:\([^)]*?\)[^,)]*?)?){ws}(?:,{ws}([^)]+?){ws})?\)";
            
            return Regex.Replace(macro, pattern, m => {
                string model = m.Groups[1].Value;
                bool hasActs = macro.Contains("OBJECT_WITH_ACTS");
                
                string bhvParam = m.Groups[8].Value;
                
                if (hasActs)
                {
                    // OBJECT_WITH_ACTS(model, x, y, z, rx, ry, rz, bhvParam, bhv, acts)
                    return $"OBJECT_WITH_ACTS({obj.ModelName}, {obj.X}, {obj.Y}, {obj.Z}, {obj.RX}, {obj.RY}, {obj.RZ}, {bhvParam}, {obj.Behavior}, {m.Groups[10].Value})";
                }
                else
                {
                    // OBJECT(model, x, y, z, rx, ry, rz, bhvParam, bhv)
                    return $"OBJECT({obj.ModelName}, {obj.X}, {obj.Y}, {obj.Z}, {obj.RX}, {obj.RY}, {obj.RZ}, {bhvParam}, {obj.Behavior})";
                }
            });
        }

        return macro;
    }
}
