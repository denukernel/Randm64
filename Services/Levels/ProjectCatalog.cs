using System.IO;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Sm64DecompLevelViewer.Models;
using Sm64DecompLevelViewer.Services;
using OpenTK.Mathematics;

namespace Sm64DecompLevelViewer.Services.Levels;
internal static class ProjectCatalog
{
    public static List<LevelMetadata> ScanLevels(string root)
    {
        var levelsDir = Path.Combine(root, "levels");
        if (!Directory.Exists(levelsDir)) levelsDir = Path.Combine(root, "howtomake", "levels");
        return Directory.Exists(levelsDir) ? new LevelScanner().ScanLevels(Path.GetDirectoryName(levelsDir)!) : new();
    }
    internal static string? FindGeoLayoutForModel(string areaPath, string geoLayoutName)
{
    try
    {
        var geoFiles = Directory.GetFiles(areaPath, "geo.inc.c", SearchOption.AllDirectories);
        foreach (var file in geoFiles)
        {
            if (File.ReadAllText(file).Contains(geoLayoutName)) return file;
        }
    }
        catch { }
        return null;
    }
    internal static string? ResolveIncludePath(string? levelPath, string? _projectRootPath, string fileName)
    {
        if (levelPath == null) return null;

        // 1. Search upwards from level path
        string? current = levelPath;
        while (!string.IsNullOrEmpty(current))
        {
            // Check in include, howtomake/include, leveleditor/include
            var subDirs = new[] { "include", "howtomake/include", "leveleditor/include", "levels/include" };
            foreach (var sub in subDirs)
            {
                var path = Path.Combine(current, sub.Replace('/', Path.DirectorySeparatorChar), fileName);
                if (File.Exists(path)) return path;
            }

            current = Path.GetDirectoryName(current);
            if (current == null || current.Length < 3) break; // Don't go above drive root
        }

        // 2. Try project root directly as fallback
        if (!string.IsNullOrEmpty(_projectRootPath))
        {
            var rootSubDirs = new[] { "include", "leveleditor/include", "howtomake/include" };
            foreach (var sub in rootSubDirs)
            {
                var path = Path.Combine(_projectRootPath, sub.Replace('/', Path.DirectorySeparatorChar), fileName);
                if (File.Exists(path)) return path;
            }
        }

        return null;
    }
}
