using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Linq;

namespace Sm64DecompLevelViewer.Services
{
    public class BehaviorService
    {
        private static readonly Regex BehaviorPattern = new Regex(
            @"extern\s+const\s+BehaviorScript\s+(\w+)\s*\[\s*\]\s*;",
            RegexOptions.Compiled);

        private readonly string _includePath;
        private readonly string _projectRoot;

        public string ProjectRoot => _projectRoot;

        public BehaviorService(string projectRoot)
        {
            _projectRoot = projectRoot;
            _includePath = Path.Combine(projectRoot, "leveleditor", "include", "behavior_data.h");
        }

        public List<string> GetBehaviors()
        {
            var behaviors = new List<string>();
            
            // Try to find the real include folder
            string includeDir = _includePath;
            if (!Directory.Exists(Path.GetDirectoryName(includeDir)))
            {
                // If the path we have is wrong (e.g. nested leveleditor/leveleditor), try to fix it
                string altPath = Path.Combine(_projectRoot, "include");
                if (Directory.Exists(altPath)) includeDir = Path.Combine(altPath, "behavior_data.h");
                else
                {
                    // Search for it
                    var dirs = Directory.GetDirectories(_projectRoot, "include", SearchOption.AllDirectories);
                    if (dirs.Length > 0) includeDir = Path.Combine(dirs[0], "behavior_data.h");
                }
            }

            string realIncludeDir = Path.GetDirectoryName(includeDir) ?? "";
            if (!Directory.Exists(realIncludeDir))
            {
                Console.WriteLine($"Could not find include directory starting from {_projectRoot}");
                return behaviors;
            }

            try
            {
                // Scan all header and include files in the include directory
                var files = Directory.GetFiles(realIncludeDir, "*.*", SearchOption.AllDirectories)
                    .Where(f => f.EndsWith(".h") || f.EndsWith(".inc.c"));

                var bhvRegex = new Regex(@"\b(bhv\w+)\b", RegexOptions.Compiled);

                foreach (var file in files)
                {
                    try
                    {
                        string content = File.ReadAllText(file);
                        var matches = bhvRegex.Matches(content);
                        foreach (Match match in matches)
                        {
                            behaviors.Add(match.Groups[1].Value);
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error scanning include folder: {ex.Message}");
            }
            
            // Filter out junk and ensure unique
            behaviors = behaviors
                .Where(b => b.Length > 3)
                .Distinct()
                .OrderBy(b => b)
                .ToList();

            Console.WriteLine($"Scanned {behaviors.Count} behaviors from {realIncludeDir}");
            return behaviors;
        }
    }
}
