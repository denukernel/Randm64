using System.IO;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Sm64DecompLevelViewer.Models;
using Sm64DecompLevelViewer.Services;
using OpenTK.Mathematics;

namespace Sm64DecompLevelViewer.Services.Levels;
internal static class LevelEditorLaunchData
{
    private static readonly Regex SpecialObjectPattern = new Regex(
        @"SPECIAL_OBJECT\s*\(\s*(?:/\*.*?\*/\s*)?([^,]+)\s*,\s*(?:/\*.*?\*/\s*)?(-?\d+)\s*,\s*(?:/\*.*?\*/\s*)?(-?\d+)\s*,\s*(?:/\*.*?\*/\s*)?(-?\d+)\s*\)[\s,]*",
        RegexOptions.Compiled
    );
    private static readonly Regex SpecialObjectWithYawPattern = new Regex(
        @"SPECIAL_OBJECT_WITH_YAW\s*\(\s*(?:/\*.*?\*/\s*)?([^,]+)\s*,\s*(?:/\*.*?\*/\s*)?(-?\d+)\s*,\s*(?:/\*.*?\*/\s*)?(-?\d+)\s*,\s*(?:/\*.*?\*/\s*)?(-?\d+)\s*,\s*(?:/\*.*?\*/\s*)?(-?\d+)\s*\)[\s,]*",
        RegexOptions.Compiled
    );
    private static readonly Regex SpecialObjectWithYawAndParamPattern = new Regex(
        @"SPECIAL_OBJECT_WITH_YAW_AND_PARAM\s*\(\s*(?:/\*.*?\*/\s*)?([^,]+)\s*,\s*(?:/\*.*?\*/\s*)?(-?\d+)\s*,\s*(?:/\*.*?\*/\s*)?(-?\d+)\s*,\s*(?:/\*.*?\*/\s*)?(-?\d+)\s*,\s*(?:/\*.*?\*/\s*)?(-?\d+)\s*,\s*(?:/\*.*?\*/\s*)?([^)]+)\)[\s,]*",
        RegexOptions.Compiled
    );
    public static List<LevelObject> LoadObjects(LevelMetadata selectedLevel, string? projectRoot, string? areaName, string? collisionFilePath, int areaIndex, MacroObjectParser macroParser)
    {
        var scriptPath = Path.Combine(selectedLevel.LevelPath, "script.c");
        var objectParser = new ObjectParser();
        List<LevelObject> objects;
                    objects = objectParser.ParseScriptFile(scriptPath, areaIndex);

                    // Load model IDs for resolving aliases
                    var modelIdsPath = ProjectCatalog.ResolveIncludePath(selectedLevel.LevelPath, projectRoot, "model_ids.h");
                    var modelIdMapping = modelIdsPath != null ? objectParser.ParseModelIds(modelIdsPath) : new Dictionary<string, int>();

                    // Map numeric ID to geo layout name
                    var modelToGeo = objectParser.ParseLoadModels(scriptPath);
                    var idToGeo = new Dictionary<int, string>();
                    foreach (var kvp in modelToGeo)
                    {
                        if (modelIdMapping.TryGetValue(kvp.Key, out int id))
                        {
                            idToGeo[id] = kvp.Value;
                        }
                    }

                    try
                    {
                        var macroListName = objectParser.ParseMacroListName(scriptPath, areaIndex);
                        if (macroListName != null && !string.IsNullOrEmpty(projectRoot))
                        {
                            Dictionary<string, MacroPreset> presets = new();
                            var selectedPresetsPath = Path.Combine(projectRoot, "include", "macro_presets.inc.c");
                            if (!File.Exists(selectedPresetsPath)) selectedPresetsPath = Path.Combine(projectRoot, "howtomake", "include", "macro_presets.inc.c");

                            if (File.Exists(selectedPresetsPath))
                            {
                                presets = macroParser.ParsePresets(selectedPresetsPath);
                            }

                            var levelPath = selectedLevel.LevelPath;
                            var macroFiles = Directory.GetFiles(levelPath, "macro.inc.c", SearchOption.AllDirectories);
                            foreach (var macroFile in macroFiles)
                            {
                                if (File.ReadAllText(macroFile).Contains(macroListName))
                                {
                                    var macroObjects = macroParser.ParseMacroFile(macroFile, presets);
                                    objects.AddRange(macroObjects);
                                    break;
                                }
                            }
                        }
                    }
                    catch (Exception ex) { Console.WriteLine($"Error loading macro objects: {ex.Message}"); }

                    try
                    {
                        if (areaName != null && collisionFilePath != null)
                        {
                            var specialParser = new SpecialObjectParser();
                            Dictionary<string, string> presetMapping = new();
                            var selectedIncludePath = ProjectCatalog.ResolveIncludePath(selectedLevel.LevelPath, projectRoot, "special_presets.inc.c");
                            if (selectedIncludePath != null) presetMapping = specialParser.ParsePresets(selectedIncludePath);

                            string colContent = ActorSourceReader.Read(collisionFilePath,preserveOffsets:true);
                            var specialMatches = SpecialObjectPattern.Matches(colContent);
                            var specialWithYawMatches = SpecialObjectWithYawPattern.Matches(colContent);
                            var specialWithYawAndParamMatches = SpecialObjectWithYawAndParamPattern.Matches(colContent);

                            void ProcessSpecialObject(string preset, int x, int y, int z, int ry, string? rawParams, string sourceFile, int sourceIndex, int sourceLength)
                            {
                                if (presetMapping.TryGetValue(preset, out string? modelId))
                                {
                                    string resolvedModelName = modelId;
                                    
                                    // Handle numeric model IDs (e.g. "0x1D" or "29")
                                    int numericId = -1;
                                    if (modelId.StartsWith("0x"))
                                    {
                                        int.TryParse(modelId.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out numericId);
                                    }
                                    else if (int.TryParse(modelId, out int parsedId))
                                    {
                                        numericId = parsedId;
                                    }

                                    // Try to resolve alias if direct match fails
                                    if (!modelToGeo.ContainsKey(modelId))
                                    {
                                        int id = numericId;
                                        if (id == -1 && modelIdMapping.TryGetValue(modelId, out int mId))
                                        {
                                            id = mId;
                                        }

                                        if (id != -1 && idToGeo.TryGetValue(id, out string? loadedModelName))
                                        {
                                            resolvedModelName = modelToGeo.Keys.FirstOrDefault(k => modelIdMapping.TryGetValue(k, out int lid) && lid == id) ?? modelId;
                                        }
                                    }

                                    uint paramValue = 0;
                                    if (rawParams != null)
                                    {
                                        if (rawParams.StartsWith("0x"))
                                        {
                                            uint.TryParse(rawParams.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out paramValue);
                                        }
                                        else
                                        {
                                            uint.TryParse(rawParams, out paramValue);
                                        }
                                    }

                                    objects.Add(new LevelObject 
                                    { 
                                        ModelName = resolvedModelName, 
                                        PresetName = preset,
                                        Behavior = "(Special Object)", 
                                        X = x, 
                                        Y = y, 
                                        Z = z, 
                                        RY = ry, 
                                        Params = paramValue,
                                        RawParams = rawParams,
                                        SourceFile = sourceFile, 
                                        SourceIndex = sourceIndex, 
                                        SourceLength = sourceLength,
                                        SourceType = ObjectSourceType.Special
                                    });
                                }
                            }

                            foreach(var call in CSourceCalls.Read(colContent,n=>n is "SPECIAL_OBJECT" or "SPECIAL_OBJECT_WITH_YAW" or "SPECIAL_OBJECT_WITH_YAW_AND_PARAM"))
                            {
                                string Arg(int i)=>call.Argument(colContent,i);
                                int yaw=call.Arguments.Count>4 ? (int)Math.Round(int.Parse(Arg(4))*360.0/256) : 0;
                                ProcessSpecialObject(Arg(0),int.Parse(Arg(1)),int.Parse(Arg(2)),int.Parse(Arg(3)),yaw,
                                    call.Arguments.Count>5 ? Arg(5) : null,collisionFilePath,call.Start,call.End-call.Start);
                            }
                        }
                    }
                    catch (Exception ex) { Console.WriteLine($"Error loading special objects: {ex.Message}"); }


        return objects;
    }
}
