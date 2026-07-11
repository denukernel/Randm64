using System.IO;
using System.Text.RegularExpressions;
using Sm64DecompLevelViewer.Models;

namespace Sm64DecompLevelViewer.Services;

public class CollisionParser
{
    // Regex patterns for parsing collision macros
    private static readonly Regex VertexPattern = new(@"COL_VERTEX\((-?\d+),\s*(-?\d+),\s*(-?\d+)\)", RegexOptions.Compiled);
    private static readonly Regex TriPattern = new(@"\b(COL_TRI|COL_TRI_SPECIAL)\((\d+),\s*(\d+),\s*(\d+)(?:,\s*(0x[0-9a-fA-F]+|\d+))?\)", RegexOptions.Compiled);
    private static readonly Regex TriInitPattern = new(@"COL_TRI_INIT\(([A-Z_]+),\s*(\d+)\)", RegexOptions.Compiled);
    private static readonly Regex WaterBoxPattern = new(@"\bCOL_WATER_BOX\((\d+),\s*(-?\d+),\s*(-?\d+),\s*(-?\d+),\s*(-?\d+),\s*(-?\d+)\)", RegexOptions.Compiled);

    public CollisionMesh? ParseCollisionFile(string collisionFilePath, string areaName, string levelName)
    {
        try
        {
            if (!File.Exists(collisionFilePath))
            {
                Console.WriteLine($"Collision file not found: {collisionFilePath}");
                return null;
            }

            var fileContent = File.ReadAllText(collisionFilePath);
            string originalContent = fileContent;

            // Extract only the first valid collision block to avoid merging JP/US/Object versions
            int initIndex = fileContent.IndexOf("COL_INIT()");
            if (initIndex != -1)
            {
                int stopIndex = fileContent.IndexOf("COL_TRI_STOP()", initIndex);
                if (stopIndex == -1) stopIndex = fileContent.IndexOf("COL_END()", initIndex);
                
                if (stopIndex != -1)
                {
                    // Update content to only include the isolated block
                    fileContent = fileContent.Substring(initIndex, stopIndex - initIndex + 10); // + padding
                }
            }

            var mesh = new CollisionMesh
            {
                AreaName = areaName,
                LevelName = levelName
            };

            ParseVertices(fileContent, mesh);

            ParseTriangles(fileContent, mesh);

            ParseWaterBoxes(originalContent, mesh);

            Console.WriteLine($"Parsed collision: {mesh}");
            return mesh;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error parsing collision file {collisionFilePath}: {ex.Message}");
            return null;
        }
    }

    private void ParseVertices(string content, CollisionMesh mesh)
    {
        var matches = VertexPattern.Matches(content);
        foreach (Match match in matches)
        {
            if (match.Success && match.Groups.Count == 4)
            {
                int x = int.Parse(match.Groups[1].Value);
                int y = int.Parse(match.Groups[2].Value);
                int z = int.Parse(match.Groups[3].Value);
                
                mesh.Vertices.Add(new CollisionVertex(x, y, z));
            }
        }
    }

    private void ParseTriangles(string content, CollisionMesh mesh)
    {
        var triInitMatches = TriInitPattern.Matches(content);
        var currentSurfaceType = "SURFACE_DEFAULT";

        var triMatches = TriPattern.Matches(content);
        int triInitIndex = 0;
        int triCount = 0;
        int expectedTriCount = 0;

        // Get first surface type if available
        if (triInitMatches.Count > 0)
        {
            currentSurfaceType = triInitMatches[0].Groups[1].Value;
            expectedTriCount = int.Parse(triInitMatches[0].Groups[2].Value);
        }

        foreach (Match match in triMatches)
        {
            if (match.Success && match.Groups.Count >= 5)
            {
                // Check if we need to move to next surface type
                if (triInitIndex < triInitMatches.Count - 1 && triCount >= expectedTriCount)
                {
                    triInitIndex++;
                    currentSurfaceType = triInitMatches[triInitIndex].Groups[1].Value;
                    expectedTriCount = int.Parse(triInitMatches[triInitIndex].Groups[2].Value);
                    triCount = 0;
                }

                string macroType = match.Groups[1].Value;
                int v1 = int.Parse(match.Groups[2].Value);
                int v2 = int.Parse(match.Groups[3].Value);
                int v3 = int.Parse(match.Groups[4].Value);

                int? specialParam = null;
                if (macroType == "COL_TRI_SPECIAL" && match.Groups[5].Success)
                {
                    string val = match.Groups[5].Value;
                    try
                    {
                        if (val.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                        {
                            specialParam = Convert.ToInt32(val, 16);
                        }
                        else
                        {
                            specialParam = int.Parse(val);
                        }
                    }
                    catch
                    {
                        specialParam = 0;
                    }
                }

                mesh.Triangles.Add(new CollisionTriangle(v1, v2, v3, currentSurfaceType, specialParam));
                triCount++;
            }
        }
    }

    private void ParseWaterBoxes(string content, CollisionMesh mesh)
    {
        var matches = WaterBoxPattern.Matches(content);
        foreach (Match match in matches)
        {
            if (match.Success && match.Groups.Count >= 7)
            {
                int id = int.Parse(match.Groups[1].Value);
                int x1 = int.Parse(match.Groups[2].Value);
                int z1 = int.Parse(match.Groups[3].Value);
                int x2 = int.Parse(match.Groups[4].Value);
                int z2 = int.Parse(match.Groups[5].Value);
                int y  = int.Parse(match.Groups[6].Value);

                mesh.WaterBoxes.Add(new WaterBox(id, x1, z1, x2, z2, y));
            }
        }
    }

    public Dictionary<string, string> FindCollisionFiles(string levelPath)
    {
        var collisionFiles = new Dictionary<string, string>();

        try
        {
            var areasPath = Path.Combine(levelPath, "areas");
            if (!Directory.Exists(areasPath))
            {
                return collisionFiles;
            }

            var areaDirectories = Directory.GetDirectories(areasPath);
            foreach (var areaDir in areaDirectories)
            {
                var collisionFile = Path.Combine(areaDir, "collision.inc.c");
                if (File.Exists(collisionFile))
                {
                    var areaNumber = Path.GetFileName(areaDir);
                    var areaName = $"Area {areaNumber}";
                    collisionFiles[areaName] = collisionFile;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error finding collision files: {ex.Message}");
        }

        return collisionFiles;
    }
}
