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
                        
                        foreach (var other in group)
                        {
                            if (other != obj && other.SourceIndex > obj.SourceIndex)
                                other.SourceIndex += deltaDeleted;
                        }
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
                        foreach (var other in group)
                        {
                            if (other != obj && other.SourceIndex > obj.SourceIndex)
                            {
                                other.SourceIndex += delta;
                            }
                        }
                    }
                }

                File.WriteAllText(filePath, content);
                Console.WriteLine($"Saved changes to {filePath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to save to {filePath}: {ex.Message}");
            }
        }

        // Handle new objects that don't have a SourceFile or were explicitly marked as new
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
                    // Find END_AREA() or insert before the last };
                    int index = content.LastIndexOf("END_AREA");
                    if (index == -1) index = content.LastIndexOf("};");
                    if (index != -1) content = content.Insert(index, insertion);
                }
                else if (obj.SourceType == ObjectSourceType.Macro)
                {
                    insertion = $"\n    MACRO_OBJECT(macro_goomba_triplet_formation, {obj.RY}, {obj.X}, {obj.Y}, {obj.Z}),";
                    // Realistically we need a preset name. For now using a dummy or the ModelName if it looks like a preset.
                    string preset = obj.ModelName.StartsWith("macro_") ? obj.ModelName : "macro_goomba_triplet_formation";
                    insertion = $"\n    MACRO_OBJECT({preset}, {obj.RY}, {obj.X}, {obj.Y}, {obj.Z}),";
                    
                    int index = content.LastIndexOf("MACRO_OBJECT_END");
                    if (index == -1) index = content.LastIndexOf("};");
                    if (index != -1) content = content.Insert(index, insertion);
                }
                else if (obj.SourceType == ObjectSourceType.Special)
                {
                    insertion = $"\n    SPECIAL_OBJECT({obj.ModelName}, {obj.X}, {obj.Y}, {obj.Z}),";
                    int index = content.LastIndexOf("SPECIAL_OBJECT_END");
                    if (index == -1) index = content.LastIndexOf("0x0045"); // Common end marker for special objects
                    if (index != -1) content = content.Insert(index, insertion);
                }

                File.WriteAllText(obj.SourceFile, content);
                obj.IsNew = false; // Mark as no longer new
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to insert new object into {obj.SourceFile}: {ex.Message}");
            }
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
            // This is the most complex one. We want to preserve the first few args (model, pos, angle) but replace behavior.
            var pattern = $@"OBJECT(?:_WITH_ACTS)?{ws}\({ws}([^,]+?){ws},{ws}(-?\d+?|[^,]+?){ws},{ws}(-?\d+?|[^,]+?){ws},{ws}(-?\d+?|[^,]+?){ws},{ws}(-?\d+?|[^,]+?){ws},{ws}(-?\d+?|[^,]+?){ws},{ws}(-?\d+?|[^,]+?){ws},{ws}([^,]+?){ws},{ws}([^,)]+?){ws}(?:,{ws}([^)]+?){ws})?\)";
            
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
