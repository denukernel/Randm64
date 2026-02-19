using System.IO;
using System.Text.RegularExpressions;
using Sm64DecompLevelViewer.Models;

namespace Sm64DecompLevelViewer.Services;

public class ObjectParser
{
    private static readonly Regex ObjectPattern = new Regex(
        @"OBJECT(?:_WITH_ACTS)?(?:\s|/\*.*?\*/)*\((?:\s|/\*.*?\*/)*([^,]*?(?:\([^)]*?\)[^,]*?)?)(?:\s|/\*.*?\*/)*,(?:\s|/\*.*?\*/)*([^,]*?(?:\([^)]*?\)[^,]*?)?)(?:\s|/\*.*?\*/)*,(?:\s|/\*.*?\*/)*([^,]*?(?:\([^)]*?\)[^,]*?)?)(?:\s|/\*.*?\*/)*,(?:\s|/\*.*?\*/)*([^,]*?(?:\([^)]*?\)[^,]*?)?)(?:\s|/\*.*?\*/)*,(?:\s|/\*.*?\*/)*([^,]*?(?:\([^)]*?\)[^,]*?)?)(?:\s|/\*.*?\*/)*,(?:\s|/\*.*?\*/)*([^,]*?(?:\([^)]*?\)[^,]*?)?)(?:\s|/\*.*?\*/)*,(?:\s|/\*.*?\*/)*([^,]*?(?:\([^)]*?\)[^,]*?)?)(?:\s|/\*.*?\*/)*,(?:\s|/\*.*?\*/)*([^,]*?(?:\([^)]*?\)[^,]*?)?)(?:\s|/\*.*?\*/)*,(?:\s|/\*.*?\*/)*([^,)]*?(?:\([^)]*?\)[^,)]*?)?)(?:\s|/\*.*?\*/)*(?:,(?:\s|/\*.*?\*/)*([^)]+?)(?:\s|/\*.*?\*/)*)?\)[\s,]*",
        RegexOptions.Compiled
    );

    private static readonly Regex MarioPosPattern = new Regex(
        @"MARIO_POS(?:\s|/\*.*?\*/)*\((?:\s|/\*.*?\*/)*([^,]+?)(?:\s|/\*.*?\*/)*,(?:\s|/\*.*?\*/)*(-?\d+?|[^,]+?)(?:\s|/\*.*?\*/)*,(?:\s|/\*.*?\*/)*(-?\d+?|[^,]+?)(?:\s|/\*.*?\*/)*,(?:\s|/\*.*?\*/)*(-?\d+?|[^,]+?)(?:\s|/\*.*?\*/)*,(?:\s|/\*.*?\*/)*(-?\d+?|[^,]+?)(?:\s|/\*.*?\*/)*\)",
        RegexOptions.Compiled
    );

    private static readonly Regex LoadModelPattern = new Regex(
        @"LOAD_MODEL_FROM_GEO\s*\(\s*([^,]+),\s*([^)]+)\)",
        RegexOptions.Compiled
    );

    private static readonly Regex AreaPattern = new Regex(
        @"AREA\s*\((?:\s|/\*.*?\*/)*(\d+)",
        RegexOptions.Compiled
    );

    private static readonly Regex EndAreaPattern = new Regex(
        @"END_AREA\s*\(\s*\)",
        RegexOptions.Compiled
    );

    private static readonly Regex GeoLayoutNamePattern = new Regex(
        @"(?:extern\s+const\s+GeoLayout\s+)?([a-zA-Z0-9_]+_geo)(?:\[\])?",
        RegexOptions.Compiled
    );

    private static readonly Regex JumpLinkPattern = new Regex(
        @"JUMP_LINK\s*\((?:\s|/\*.*?\*/)*([^)]+?)\)",
        RegexOptions.Compiled
    );

    private static readonly Regex ScriptBlockPattern = new Regex(
        @"const\s+LevelScript\s+([a-zA-Z0-9_]+)\s*\[\s*\]\s*=\s*\{([\s\S]+?)\};",
        RegexOptions.Compiled
    );

    private static readonly Regex ModelIdPattern = new Regex(
        @"#define\s+([a-zA-Z0-9_]+)\s+(0x[a-fA-F0-9]+|\d+|[a-zA-Z0-9_]+)",
        RegexOptions.Compiled
    );

    public List<LevelObject> ParseScriptFile(string filePath, int targetArea = -1)
    {
        var objects = new List<LevelObject>();

        try
        {
            if (!File.Exists(filePath))
            {
                Console.WriteLine($"Script file not found: {filePath}");
                return objects;
            }

            string content = File.ReadAllText(filePath);
            
            // Map all script blocks for jumping
            var scriptBlocks = new Dictionary<string, string>();
            var scriptMatches = ScriptBlockPattern.Matches(content);
            foreach (Match m in scriptMatches)
            {
                scriptBlocks[m.Groups[1].Value] = m.Groups[2].Value;
            }

            string contentToParse = "";

            if (targetArea != -1)
            {
                // Find target area block
                var areaMatches = AreaPattern.Matches(content);
                foreach (Match areaMatch in areaMatches)
                {
                    if (int.Parse(areaMatch.Groups[1].Value) == targetArea)
                    {
                        int areaStart = areaMatch.Index;
                        var endMatch = EndAreaPattern.Match(content, areaStart);
                        if (endMatch.Success)
                        {
                            contentToParse = content.Substring(areaStart, endMatch.Index + endMatch.Length - areaStart);
                            break;
                        }
                    }
                }
            }
            else
            {
                contentToParse = content;
            }

            if (string.IsNullOrEmpty(contentToParse) && targetArea != -1)
            {
                Console.WriteLine($"Area {targetArea} not found in {filePath}");
                return objects;
            }

            // Function to recursively or iteratively parse content and its jumps
            void ParseContent(string currentContent, int baseOffset, int areaIndex, HashSet<string> visitedScripts)
            {
                // Parse direct objects
                var matches = ObjectPattern.Matches(currentContent);
                foreach (Match match in matches)
                {
                    try
                    {
                        var obj = new LevelObject
                        {
                            ModelName = match.Groups[1].Value.Trim(),
                            X = int.TryParse(match.Groups[2].Value, out int x) ? x : 0,
                            Y = int.TryParse(match.Groups[3].Value, out int y) ? y : 0,
                            Z = int.TryParse(match.Groups[4].Value, out int z) ? z : 0,
                            RX = int.TryParse(match.Groups[5].Value, out int rx) ? rx : 0,
                            RY = int.TryParse(match.Groups[6].Value, out int ry) ? ry : 0,
                            RZ = int.TryParse(match.Groups[7].Value, out int rz) ? rz : 0,
                            Behavior = match.Groups[9].Value.Trim(),
                            SourceFile = filePath,
                            SourceIndex = baseOffset + match.Index,
                            SourceLength = match.Length,
                            SourceType = ObjectSourceType.Normal,
                            AreaIndex = areaIndex
                        };

                        string paramsStr = match.Groups[8].Value.Trim();
                        if (paramsStr.StartsWith("0x"))
                        {
                            obj.Params = Convert.ToUInt32(paramsStr, 16);
                        }
                        else if (uint.TryParse(paramsStr, out uint paramValue))
                        {
                            obj.Params = paramValue;
                        }

                        objects.Add(obj);
                    }
                    catch { }
                }

                // Follow JUMP_LINKs
                var jumpMatches = JumpLinkPattern.Matches(currentContent);
                foreach (Match jump in jumpMatches)
                {
                    string scriptName = jump.Groups[1].Value.Trim();
                    if (scriptBlocks.TryGetValue(scriptName, out string? jumpContent) && !visitedScripts.Contains(scriptName))
                    {
                        visitedScripts.Add(scriptName);
                        // Find the actual offset of jumpContent in the file
                        int blockOffset = content.IndexOf($"const LevelScript {scriptName}[]");
                        if (blockOffset != -1)
                        {
                            int contentStart = content.IndexOf('{', blockOffset) + 1;
                            ParseContent(jumpContent, contentStart, areaIndex, visitedScripts);
                        }
                    }
                }
            }

            int parseOffset = 0;
            if (targetArea != -1)
            {
                // Find target area block
                var areaMatches = AreaPattern.Matches(content);
                foreach (Match areaMatch in areaMatches)
                {
                    if (int.Parse(areaMatch.Groups[1].Value) == targetArea)
                    {
                        parseOffset = areaMatch.Index;
                        break;
                    }
                }
            }

            ParseContent(contentToParse, parseOffset, targetArea, new HashSet<string>());

            // Mario Position (filtered by area if specified)
            var marioMatches = MarioPosPattern.Matches(content);
            foreach (Match match in marioMatches)
            {
                int marioArea = int.TryParse(match.Groups[1].Value, out int ma) ? ma : -1;
                if (targetArea == -1 || marioArea == targetArea)
                {
                    objects.Add(new LevelObject
                    {
                        ModelName = "MODEL_MARIO",
                        X = int.TryParse(match.Groups[3].Value, out int mx) ? mx : 0,
                        Y = int.TryParse(match.Groups[4].Value, out int my) ? my : 0,
                        Z = int.TryParse(match.Groups[5].Value, out int mz) ? mz : 0,
                        RY = int.TryParse(match.Groups[2].Value, out int mry) ? mry : 0,
                        Behavior = "bhvMario",
                        SourceFile = filePath,
                        SourceIndex = match.Index,
                        SourceLength = match.Length,
                        SourceType = ObjectSourceType.Mario,
                        AreaIndex = marioArea
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error parsing script file {filePath}: {ex.Message}");
        }

        return objects;
    }

    private static readonly Regex MacroObjectsPattern = new Regex(
        @"MACRO_OBJECTS\s*\(\s*/\*objList\*/\s*([^)]+)\)",
        RegexOptions.Compiled
    );

    public string? ParseMacroListName(string scriptFilePath, int targetArea = -1)
    {
        if (!File.Exists(scriptFilePath)) return null;

        try
        {
            string content = File.ReadAllText(scriptFilePath);
            string contentToSearch = content;

            if (targetArea != -1)
            {
                var areaMatches = AreaPattern.Matches(content);
                foreach (Match areaMatch in areaMatches)
                {
                    if (int.Parse(areaMatch.Groups[1].Value) == targetArea)
                    {
                        int areaStart = areaMatch.Index;
                        var endMatch = EndAreaPattern.Match(content, areaStart);
                        if (endMatch.Success)
                        {
                            contentToSearch = content.Substring(areaStart, endMatch.Index + endMatch.Length - areaStart);
                            break;
                        }
                    }
                }
            }

            var match = MacroObjectsPattern.Match(contentToSearch);
            if (match.Success)
            {
                return match.Groups[1].Value.Trim();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error parsing MACRO_OBJECTS in script {scriptFilePath}: {ex.Message}");
        }

        return null;
    }

    public Dictionary<string, string> ParseLoadModels(string filePath)
    {
        var mapping = new Dictionary<string, string>();
        if (!File.Exists(filePath)) return mapping;

        try
        {
            string content = File.ReadAllText(filePath);
            var matches = LoadModelPattern.Matches(content);
            foreach (Match match in matches)
            {
                mapping[match.Groups[1].Value.Trim()] = match.Groups[2].Value.Trim();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error parsing LOAD_MODEL_FROM_GEO: {ex.Message}");
        }

        return mapping;
    }

    public Dictionary<string, int> ParseModelIds(string filePath)
    {
        var mapping = new Dictionary<string, int>();
        if (!File.Exists(filePath)) return mapping;

        try
        {
            string content = File.ReadAllText(filePath);
            var matches = ModelIdPattern.Matches(content);
            
            // First pass: direct values
            foreach (Match match in matches)
            {
                string name = match.Groups[1].Value;
                string valStr = match.Groups[2].Value;

                if (valStr.StartsWith("0x"))
                {
                    mapping[name] = Convert.ToInt32(valStr, 16);
                }
                else if (int.TryParse(valStr, out int val))
                {
                    mapping[name] = val;
                }
            }

            // Second pass: resolve aliases
            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (Match match in matches)
                {
                    string name = match.Groups[1].Value;
                    string valStr = match.Groups[2].Value;

                    if (!mapping.ContainsKey(name) && mapping.TryGetValue(valStr, out int resolvedVal))
                    {
                        mapping[name] = resolvedVal;
                        changed = true;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error parsing model IDs: {ex.Message}");
        }

        return mapping;
    }
    public List<string> ParseSupportedModels(string levelPath, string projectRoot)
    {
        var supportedModels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Models explicitly LOADED in script.c
        string scriptPath = Path.Combine(levelPath, "script.c");
        var loadedModels = ParseLoadModels(scriptPath);
        foreach (var model in loadedModels.Keys) supportedModels.Add(model);

        // 2. Find Actors Folder (Multiple possible locations)
        string[] potentialActorPaths = {
            Path.Combine(projectRoot, "actors"),
            Path.Combine(projectRoot, "leveleditor", "actors"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "actors"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "leveleditor", "actors")
        };

        string? actorsPath = potentialActorPaths.FirstOrDefault(Directory.Exists);

        if (actorsPath == null)
        {
            // Signal that folder is missing
            supportedModels.Add("MISSING_ACTORS_FOLDER");
            supportedModels.Add("MODEL_MARIO"); // At least Mario is always safe
            return supportedModels.ToList();
        }

        // 3. Models from level.yaml bins
        string yamlPath = Path.Combine(levelPath, "level.yaml");
        var yamlParser = new YamlLevelParser();
        var metadata = yamlParser.ParseLevelYaml(yamlPath);

        if (metadata != null)
        {
            var bins = new List<string>();
            bins.AddRange(metadata.ActorBins);
            bins.AddRange(metadata.CommonBin);

            foreach (var bin in bins)
            {
                // 1. Check for bin header file (e.g. actors/common0.h)
                string binHeaderPath = Path.Combine(actorsPath, $"{bin}.h");
                if (File.Exists(binHeaderPath))
                {
                    var modelsInBin = ParseModelsFromHeader(binHeaderPath);
                    foreach (var model in modelsInBin) supportedModels.Add(model);
                }

                // 2. Check for bin source file to find included actor folders (e.g. actors/common0.c)
                // This is crucial because bins like common0 include bobomb, goomba, etc.
                string binSourcePath = Path.Combine(actorsPath, $"{bin}.c");
                if (File.Exists(binSourcePath))
                {
                    try
                    {
                        string sourceContent = File.ReadAllText(binSourcePath);
                        // Regex to find #include "folder/..."
                        var includeMatches = Regex.Matches(sourceContent, @"#include\s+""([^/""]+)/");
                        foreach (Match m in includeMatches)
                        {
                            string actorFolder = m.Groups[1].Value;
                            string actorDirPath = Path.Combine(actorsPath, actorFolder);
                            if (Directory.Exists(actorDirPath))
                            {
                                // Support MODEL_FOLDERNAME
                                supportedModels.Add("MODEL_" + actorFolder.ToUpper());

                                // Scan headers in that folder
                                foreach (var hFile in Directory.GetFiles(actorDirPath, "*.h"))
                                {
                                    var modelsInHeader = ParseModelsFromHeader(hFile);
                                    foreach (var mod in modelsInHeader) supportedModels.Add(mod);
                                }
                            }
                        }
                    }
                    catch { }
                }

                // 3. Fallback: check for bin subdirectory directly
                string binDirPath = Path.Combine(actorsPath, bin);
                if (Directory.Exists(binDirPath))
                {
                    supportedModels.Add("MODEL_" + bin.ToUpper());
                    foreach (var file in Directory.GetFiles(binDirPath, "*.h"))
                    {
                        var modelsInHeader = ParseModelsFromHeader(file);
                        foreach (var model in modelsInHeader) supportedModels.Add(model);
                    }
                }
            }
        }

        // 4. Always support Mario
        supportedModels.Add("MODEL_MARIO");

        return supportedModels.OrderBy(m => m).ToList();
    }

    private List<string> ParseModelsFromHeader(string headerPath)
    {
        var models = new List<string>();
        try
        {
            string content = File.ReadAllText(headerPath);
            var matches = GeoLayoutNamePattern.Matches(content);
            foreach (Match match in matches)
            {
                string geoName = match.Groups[1].Value.Trim();
                models.Add(geoName);
                
                string modelName = "MODEL_" + geoName.Replace("_geo", "").ToUpper();
                models.Add(modelName);
            }
        }
        catch { }
        return models;
    }
}
