using System.IO;
using System.Text.RegularExpressions;
using OpenTK.Mathematics;
using Sm64DecompLevelViewer.Models;
using static Sm64DecompLevelViewer.Services.Models.ModelSourcePatterns;
using static Sm64DecompLevelViewer.Services.Models.ModelParsingPrimitives;

namespace Sm64DecompLevelViewer.Services.Models;
internal sealed class ModelFileCatalog
{
    public Dictionary<string, List<string>> FindModelFiles(string levelPath)
    {
        var modelFiles = new Dictionary<string, List<string>>();

        try
        {
            var areasPath = Path.Combine(levelPath, "areas");
            if (!Directory.Exists(areasPath))
            {
                return modelFiles;
            }

            var areaDirectories = Directory.GetDirectories(areasPath);
            foreach (var areaDir in areaDirectories)
            {
                var areaNumber = Path.GetFileName(areaDir);
                var areaName = $"Area {areaNumber}";
                var areaModelFiles = new List<string>();
                
                var geoDlRegex = new Regex(@"(?:GEO_DISPLAY_LIST|GEO_TRANSLATE_WITH_DL|GEO_ROTATE_WITH_DL|GEO_SCALE_WITH_DL|GEO_TRANSLATE_ROTATE_WITH_DL)\s*\(\s*(?:\w+\s*,\s*)*(\w+)\s*\)", RegexOptions.Compiled);
                
                var geoFiles = Directory.GetFiles(areaDir, "geo.inc.c", SearchOption.AllDirectories).ToList();
                var levelGeoC = Path.Combine(levelPath, "geo.c");
                if (File.Exists(levelGeoC)) geoFiles.Add(levelGeoC);

                var geoDls = new HashSet<string>();
                foreach (var gFile in geoFiles)
                {
                    try
                    {
                        string geoText = File.ReadAllText(gFile);
                        foreach (Match m in geoDlRegex.Matches(geoText))
                        {
                            geoDls.Add(m.Groups[1].Value);
                        }
                    }
                    catch { }
                }

                var subAreaDirectories = Directory.GetDirectories(areaDir)
                    .OrderBy(d => int.TryParse(Path.GetFileName(d), out int n) ? n : 999)
                    .ToArray();

                foreach (var subAreaDir in subAreaDirectories)
                {
                    bool hasOwnGeo = File.Exists(Path.Combine(subAreaDir, "geo.inc.c"));
                    var files = Directory.GetFiles(subAreaDir, "*.inc.c");
                    foreach (var file in files)
                    {
                        var fileName = Path.GetFileName(file);
                        if (fileName == "collision.inc.c" || fileName == "geo.inc.c" || fileName == "macro.inc.c")
                            continue;

                        if (geoDls.Count > 0 && !hasOwnGeo)
                        {
                            string fileContent = File.ReadAllText(file);
                            bool matchesGeo = geoDls.Any(dl => fileContent.Contains(dl));
                            if (!matchesGeo)
                            {
                                LogService.Log($"[ScanDebugger] Skipping object subfolder file: {file}");
                                continue;
                            }
                        }

                        LogService.Log($"[ScanDebugger] Included level model file: {file}");
                        areaModelFiles.Add(file);
                    }
                }
                
                var movtextCandidates = new List<string>
                {
                    Path.Combine(areaDir, "movtext.inc.c"),
                    Path.Combine(levelPath, "movtext.inc.c")
                };
                foreach (var otherAreaDir in areaDirectories)
                {
                    movtextCandidates.Add(Path.Combine(otherAreaDir, "movtext.inc.c"));
                }

                foreach (var movtextPath in movtextCandidates.Distinct())
                {
                    if (File.Exists(movtextPath) && !areaModelFiles.Contains(movtextPath))
                    {
                        LogService.Log($"[ScanDebugger] Included Movtex file: {movtextPath}");
                        areaModelFiles.Add(movtextPath);
                    }
                }

                if (areaModelFiles.Count > 0)
                {
                    modelFiles[areaName] = areaModelFiles;
                    LogService.Log($"[ScanDebugger] Found {areaModelFiles.Count} model file(s) for {areaName}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error finding model files: {ex.Message}");
        }

        return modelFiles;
    }
}
