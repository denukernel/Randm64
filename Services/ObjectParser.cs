using System.IO;
using System.Text.RegularExpressions;
using Sm64DecompLevelViewer.Models;

namespace Sm64DecompLevelViewer.Services;

public class ObjectParser
{
    private static readonly Regex ObjectPattern = new Regex(
        @"OBJECT(?:_WITH_ACTS)?(?:\s|/\*.*?\*/)*\((?:\s|/\*.*?\*/)*([^,]+?)(?:\s|/\*.*?\*/)*,(?:\s|/\*.*?\*/)*(-?\d+?|[^,]+?)(?:\s|/\*.*?\*/)*,(?:\s|/\*.*?\*/)*(-?\d+?|[^,]+?)(?:\s|/\*.*?\*/)*,(?:\s|/\*.*?\*/)*(-?\d+?|[^,]+?)(?:\s|/\*.*?\*/)*,(?:\s|/\*.*?\*/)*(-?\d+?|[^,]+?)(?:\s|/\*.*?\*/)*,(?:\s|/\*.*?\*/)*(-?\d+?|[^,]+?)(?:\s|/\*.*?\*/)*,(?:\s|/\*.*?\*/)*(-?\d+?|[^,]+?)(?:\s|/\*.*?\*/)*,(?:\s|/\*.*?\*/)*([^,]+?)(?:\s|/\*.*?\*/)*,(?:\s|/\*.*?\*/)*([^,)]+?)(?:\s|/\*.*?\*/)*(?:,(?:\s|/\*.*?\*/)*([^)]+?)(?:\s|/\*.*?\*/)*)?\)",
        RegexOptions.Compiled | RegexOptions.Multiline
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
            void ParseContent(string currentContent, HashSet<string> visitedScripts)
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
                            SourceIndex = match.Index,
                            SourceLength = match.Length,
                            SourceType = ObjectSourceType.Normal
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
                        ParseContent(jumpContent, visitedScripts);
                    }
                }
            }

            ParseContent(contentToParse, new HashSet<string>());

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
                        SourceType = ObjectSourceType.Mario
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
}
