using System.IO;
using Sm64DecompLevelViewer.Models;

namespace Sm64DecompLevelViewer.Services;

public class LevelScanner
{
    private readonly YamlLevelParser _yamlParser;
    private static readonly Dictionary<string, string> FriendlyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["bob"] = "bob-omb battlefield", ["wf"] = "whomps fortress", ["jrb"] = "jolly roger bay",
        ["ccm"] = "cool cool mountain", ["bbh"] = "big boos haunt", ["hmc"] = "hazy maze cave",
        ["lll"] = "lethal lava land", ["ssl"] = "shifting sand land", ["ddd"] = "dire dire docks",
        ["sl"] = "snowmans land", ["wdw"] = "wet-dry world", ["ttm"] = "tall tall mountain",
        ["thi"] = "tiny-huge island", ["ttc"] = "tick tock clock", ["rr"] = "rainbow ride",
        ["castle_inside"] = "inside the castle", ["castle_grounds"] = "castle grounds", ["castle_courtyard"] = "castle courtyard",
        ["bitdw"] = "bowser in the dark world", ["bitfs"] = "bowser in the fire sea", ["bits"] = "bowser in the sky",
        ["pss"] = "princess secret slide", ["cotmc"] = "cavern of the metal cap", ["totwc"] = "tower of the wing cap",
        ["vcutm"] = "vanish cap under the moat", ["wmotr"] = "wing mario over the rainbow", ["sa"] = "secret aquarium",
        ["menu"] = "file select", ["intro"] = "title screen", ["ending"] = "ending"
    };

    // Level categorization based on short names
    private static readonly HashSet<string> CourseLevels = new()
    {
        "bob", "wf", "jrb", "ccm", "bbh", "hmc", "lll", "ssl",
        "ddd", "sl", "wdw", "ttm", "thi", "ttc", "rr"
    };

    private static readonly HashSet<string> BowserLevels = new()
    {
        "bitdw", "bitfs", "bits", "bowser_1", "bowser_2", "bowser_3"
    };

    private static readonly HashSet<string> CastleLevels = new()
    {
        "castle_inside", "castle_grounds", "castle_courtyard"
    };

    private static readonly HashSet<string> SpecialLevels = new()
    {
        "pss", "cotmc", "totwc", "vcutm", "wmotr", "sa"
    };

    private static readonly HashSet<string> MenuLevels = new()
    {
        "menu", "intro", "ending"
    };

    public LevelScanner()
    {
        _yamlParser = new YamlLevelParser();
    }

    public List<LevelMetadata> ScanLevels(string howtomakePath)
    {
        var levels = new List<LevelMetadata>();
        var levelsPath = Path.Combine(howtomakePath, "levels");

        if (!Directory.Exists(levelsPath))
        {
            Console.WriteLine($"Levels directory not found: {levelsPath}");
            return levels;
        }

        var yamlFiles = Directory.GetFiles(levelsPath, "level.yaml", SearchOption.AllDirectories);

        foreach (var yamlFile in yamlFiles)
        {
            var levelMetadata = _yamlParser.ParseLevelYaml(yamlFile);
            if (levelMetadata != null)
            {
                // Categorize the level
                levelMetadata.Category = CategorizeLevel(levelMetadata.ShortName);
                levels.Add(levelMetadata);
            }
        }

        // Vanilla and custom decomp trees do not always ship level.yaml metadata.
        foreach (string directory in Directory.GetDirectories(levelsPath).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(Path.Combine(directory, "script.c")) || levels.Any(l => string.Equals(Path.GetFullPath(l.LevelPath), Path.GetFullPath(directory), StringComparison.OrdinalIgnoreCase))) continue;
            string name = Path.GetFileName(directory);
            string script = File.ReadAllText(Path.Combine(directory, "script.c"));
            script = System.Text.RegularExpressions.Regex.Replace(script, @"/\*[\s\S]*?\*/|//[^\r\n]*", " ");
            int areas = System.Text.RegularExpressions.Regex.Matches(script, @"\bAREA\s*\(\s*(\d+)").Select(m => m.Groups[1].Value).Distinct().Count();
            levels.Add(new LevelMetadata { ShortName = name, FullName = FriendlyNames.GetValueOrDefault(name, name.Replace('_', ' ')), LevelPath = directory, AreaCount = areas, Category = CategorizeLevel(name) });
        }

        // Sort levels by category and then by name
        levels = levels
            .OrderBy(l => GetCategoryOrder(l.Category))
            .ThenBy(l => l.FullName)
            .ToList();

        Console.WriteLine($"Discovered {levels.Count} levels");
        return levels;
    }

    private static string CategorizeLevel(string shortName)
    {
        if (CourseLevels.Contains(shortName)) return "Course";
        if (BowserLevels.Contains(shortName)) return "Bowser";
        if (CastleLevels.Contains(shortName)) return "Castle";
        if (SpecialLevels.Contains(shortName)) return "Special";
        if (MenuLevels.Contains(shortName)) return "Menu";
        return "Other";
    }

    private static int GetCategoryOrder(string category)
    {
        return category switch
        {
            "Course" => 0,
            "Bowser" => 1,
            "Castle" => 2,
            "Special" => 3,
            "Menu" => 4,
            _ => 5
        };
    }
}
