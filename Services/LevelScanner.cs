using System.IO;
using Sm64DecompLevelViewer.Models;

namespace Sm64DecompLevelViewer.Services;

public class LevelScanner
{
    private readonly YamlLevelParser _yamlParser;

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
