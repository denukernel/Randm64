using System.IO;
using System.Text.RegularExpressions;
using Sm64DecompLevelViewer.Models;

namespace Sm64DecompLevelViewer.Services;

public class CollisionParser
{
    // Regex patterns for parsing collision macros
    private static readonly Regex VertexPattern = new(@"COL_VERTEX\((-?\d+),\s*(-?\d+),\s*(-?\d+)\)", RegexOptions.Compiled);
    private static readonly Regex TriPattern = new(@"COL_TRI\((\d+),\s*(\d+),\s*(\d+)\)", RegexOptions.Compiled);
    private static readonly Regex TriInitPattern = new(@"COL_TRI_INIT\(([A-Z_]+),\s*(\d+)\)", RegexOptions.Compiled);

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
            var mesh = new CollisionMesh
            {
                AreaName = areaName,
                LevelName = levelName
            };

            ParseVertices(fileContent, mesh);

            ParseTriangles(fileContent, mesh);

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
            if (match.Success && match.Groups.Count == 4)
            {
                // Check if we need to move to next surface type
                if (triInitIndex < triInitMatches.Count - 1 && triCount >= expectedTriCount)
                {
                    triInitIndex++;
                    currentSurfaceType = triInitMatches[triInitIndex].Groups[1].Value;
                    expectedTriCount = int.Parse(triInitMatches[triInitIndex].Groups[2].Value);
                    triCount = 0;
                }

                int v1 = int.Parse(match.Groups[1].Value);
                int v2 = int.Parse(match.Groups[2].Value);
                int v3 = int.Parse(match.Groups[3].Value);

                mesh.Triangles.Add(new CollisionTriangle(v1, v2, v3, currentSurfaceType));
                triCount++;
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
