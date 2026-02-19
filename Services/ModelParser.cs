using System.IO;
using System.Text.RegularExpressions;
using OpenTK.Mathematics;
using Sm64DecompLevelViewer.Models;

namespace Sm64DecompLevelViewer.Services;

public class ModelParser
{
    // Regex patterns for parsing model data
    private static readonly Regex VtxArrayPattern = new(@"static\s+const\s+Vtx\s+(\w+)\[\]\s*=\s*\{", RegexOptions.Compiled);
    
    
    private static readonly Regex VtxDataPattern = new(
        @"\{\{\{\s*(-?\d+),\s*(-?\d+),\s*(-?\d+)\},\s*\d+,\s*\{\s*(-?\d+),\s*(-?\d+)\},\s*\{(0x[0-9a-fA-F]{2}),\s*(0x[0-9a-fA-F]{2}),\s*(0x[0-9a-fA-F]{2}),\s*(0x[0-9a-fA-F]{2})\}\}\}",
        RegexOptions.Compiled);
    
    private static readonly Regex SpVertexPattern = new(@"gsSPVertex\((\w+),\s*(\d+),\s*(\d+)\)", RegexOptions.Compiled);
    private static readonly Regex Sp2TrianglesPattern = new(@"gsSP2Triangles\(\s*(\d+),\s*(\d+),\s*(\d+),\s*0x[0-9a-fA-F]+,\s*(\d+),\s*(\d+),\s*(\d+),\s*0x[0-9a-fA-F]+\)", RegexOptions.Compiled);
    private static readonly Regex Sp1TrianglePattern = new(@"gsSP1Triangle\(\s*(\d+),\s*(\d+),\s*(\d+),\s*0x[0-9a-fA-F]+\)", RegexOptions.Compiled);
    private static readonly Regex DisplayListPattern = new(@"const\s+Gfx\s+(\w+)\[\]\s*=\s*\{", RegexOptions.Compiled);
    private static readonly Regex SetTextureImagePattern = new(@"gsDPSetTextureImage\(G_IM_FMT_RGBA,\s*G_IM_SIZ_16b,\s*1,\s*(\w+)\)", RegexOptions.Compiled);
    private static readonly Regex TextureSymbolPattern = new(@"ALIGNED8\s+static\s+const\s+Texture\s+(\w+)\[\]\s*=\s*\{", RegexOptions.Compiled);
    private static readonly Regex IncludePattern = new(@"#include\s+""([^""]+)""", RegexOptions.Compiled);

    public VisualMesh? ParseModelFile(string modelFilePath, string areaName, string levelName)
    {
        try
        {
            if (!File.Exists(modelFilePath))
            {
                Console.WriteLine($"Model file not found: {modelFilePath}");
                return null;
            }

            var fileContent = File.ReadAllText(modelFilePath);
            var mesh = new VisualMesh
            {
                AreaName = areaName,
                LevelName = levelName
            };

            var vertexArrays = ParseVertexArrays(fileContent);

            ParseDisplayLists(fileContent, vertexArrays, mesh);

            Console.WriteLine($"Parsed visual mesh: {mesh}");
            return mesh;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error parsing model file {modelFilePath}: {ex.Message}");
            return null;
        }
    }

    private Dictionary<string, List<ModelVertex>> ParseVertexArrays(string content)
    {
        var vertexArrays = new Dictionary<string, List<ModelVertex>>();

        var arrayMatches = VtxArrayPattern.Matches(content);
        
        foreach (Match arrayMatch in arrayMatches)
        {
            string arrayName = arrayMatch.Groups[1].Value;
            int startIndex = arrayMatch.Index + arrayMatch.Length;
            
            // Find the closing brace
            int braceCount = 1;
            int endIndex = startIndex;
            for (int i = startIndex; i < content.Length && braceCount > 0; i++)
            {
                if (content[i] == '{') braceCount++;
                if (content[i] == '}') braceCount--;
                endIndex = i;
            }

            string arrayContent = content.Substring(startIndex, endIndex - startIndex);
            
            var vertices = new List<ModelVertex>();
            var vtxMatches = VtxDataPattern.Matches(arrayContent);
            
            foreach (Match vtxMatch in vtxMatches)
            {
                int x = int.Parse(vtxMatch.Groups[1].Value);
                int y = int.Parse(vtxMatch.Groups[2].Value);
                int z = int.Parse(vtxMatch.Groups[3].Value);
                int s = int.Parse(vtxMatch.Groups[4].Value);
                int t = int.Parse(vtxMatch.Groups[5].Value);
                byte nx = Convert.ToByte(vtxMatch.Groups[6].Value, 16);
                byte ny = Convert.ToByte(vtxMatch.Groups[7].Value, 16);
                byte nz = Convert.ToByte(vtxMatch.Groups[8].Value, 16);
                byte alpha = Convert.ToByte(vtxMatch.Groups[9].Value, 16);

                vertices.Add(new ModelVertex(x, y, z, s, t, nx, ny, nz, alpha));
            }

            if (vertices.Count > 0)
            {
                vertexArrays[arrayName] = vertices;
                Console.WriteLine($"  {arrayName}: {vertices.Count} vertices");
            }
        }

        return vertexArrays;
    }

    private void ParseDisplayLists(string content, Dictionary<string, List<ModelVertex>> vertexArrays, VisualMesh mesh)
    {
        var dlMatches = DisplayListPattern.Matches(content);
        
        foreach (Match dlMatch in dlMatches)
        {
            string dlName = dlMatch.Groups[1].Value;
            mesh.DisplayListNames.Add(dlName);
            if (mesh.MainDisplayListName == null)
            {
                mesh.MainDisplayListName = dlName;
            }

            int startIndex = dlMatch.Index + dlMatch.Length;
            
            // Find the closing brace
            int braceCount = 1;
            int endIndex = startIndex;
            for (int i = startIndex; i < content.Length && braceCount > 0; i++)
            {
                if (content[i] == '{') braceCount++;
                if (content[i] == '}') braceCount--;
                endIndex = i;
            }

            string dlContent = content.Substring(startIndex, endIndex - startIndex);
            
            ProcessDisplayList(dlContent, vertexArrays, mesh);
        }
    }

    private void ProcessDisplayList(string dlContent, Dictionary<string, List<ModelVertex>> vertexArrays, VisualMesh mesh)
    {
        List<ModelVertex>? currentVertexBuffer = null;
        int vertexBufferOffset = 0;
        string? currentTextureSymbol = null;

        var spVertexMatches = SpVertexPattern.Matches(dlContent);
        var sp2TriMatches = Sp2TrianglesPattern.Matches(dlContent);
        var sp1TriMatches = Sp1TrianglePattern.Matches(dlContent);
        var setTexImgMatches = SetTextureImagePattern.Matches(dlContent);

        var commands = new List<(int pos, string type, Match match)>();
        
        foreach (Match m in spVertexMatches)
            commands.Add((m.Index, "vertex", m));
        foreach (Match m in sp2TriMatches)
            commands.Add((m.Index, "tri2", m));
        foreach (Match m in sp1TriMatches)
            commands.Add((m.Index, "tri1", m));
        foreach (Match m in setTexImgMatches)
            commands.Add((m.Index, "texture", m));

        commands.Sort((a, b) => a.pos.CompareTo(b.pos));

        foreach (var (pos, type, match) in commands)
        {
            if (type == "texture")
            {
                currentTextureSymbol = match.Groups[1].Value;
            }
            else if (type == "vertex")
            {
                string arrayName = match.Groups[1].Value;
                int count = int.Parse(match.Groups[2].Value);
                vertexBufferOffset = int.Parse(match.Groups[3].Value);

                if (vertexArrays.ContainsKey(arrayName))
                {
                    currentVertexBuffer = vertexArrays[arrayName];
                    vertexBufferOffset = mesh.Vertices.Count;
                    
                    for (int i = 0; i < count && i < currentVertexBuffer.Count; i++)
                    {
                        mesh.Vertices.Add(currentVertexBuffer[i]);
                    }
                }
            }
            else if (type == "tri2" && currentVertexBuffer != null)
            {
                int v1 = int.Parse(match.Groups[1].Value);
                int v2 = int.Parse(match.Groups[2].Value);
                int v3 = int.Parse(match.Groups[3].Value);
                int v4 = int.Parse(match.Groups[4].Value);
                int v5 = int.Parse(match.Groups[5].Value);
                int v6 = int.Parse(match.Groups[6].Value);

                int baseIndex = vertexBufferOffset;
                
                mesh.Triangles.Add(new ModelTriangle(baseIndex + v1, baseIndex + v2, baseIndex + v3, currentTextureSymbol));
                mesh.Triangles.Add(new ModelTriangle(baseIndex + v4, baseIndex + v5, baseIndex + v6, currentTextureSymbol));
            }
            else if (type == "tri1" && currentVertexBuffer != null)
            {
                int v1 = int.Parse(match.Groups[1].Value);
                int v2 = int.Parse(match.Groups[2].Value);
                int v3 = int.Parse(match.Groups[3].Value);

                int baseIndex = vertexBufferOffset;
                mesh.Triangles.Add(new ModelTriangle(baseIndex + v1, baseIndex + v2, baseIndex + v3, currentTextureSymbol));
            }
        }
    }

    public void ParseTextureMapping(string levelPath, VisualMesh mesh, string projectRoot)
    {
        try
        {
            string textureIncPath = Path.Combine(levelPath, "texture.inc.c");
            if (!File.Exists(textureIncPath)) return;

            string content = File.ReadAllText(textureIncPath);
            var symbolMatches = TextureSymbolPattern.Matches(content);

            foreach (Match symbolMatch in symbolMatches)
            {
                string symbol = symbolMatch.Groups[1].Value;
                int startIndex = symbolMatch.Index + symbolMatch.Length;
                
                // Find next symbol or end of file to limit search for #include
                int nextIndex = content.Length;
                var nextMatch = TextureSymbolPattern.Match(content, startIndex);
                if (nextMatch.Success) nextIndex = nextMatch.Index;

                string subContent = content.Substring(startIndex, nextIndex - startIndex);
                var includeMatch = IncludePattern.Match(subContent);

                if (includeMatch.Success)
                {
                    string includePath = includeMatch.Groups[1].Value; // e.g., "levels/castle_inside/12.rgba16.inc.c"
                    
                    // Convert .inc.c to .png
                    string pngPath = includePath.Replace(".inc.c", ".png");
                    
                    // Try to resolve the actual file path
                    string resolvedPath = ResolveTexturePath(pngPath, levelPath, projectRoot);
                    if (resolvedPath != null)
                    {
                        mesh.TexturePaths[symbol] = resolvedPath;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error parsing texture mapping: {ex.Message}");
        }
    }

    private string? ResolveTexturePath(string relativePath, string levelPath, string projectRoot)
    {
        // 1. Check build/us/textures (as requested)
        string buildPath = Path.Combine(projectRoot, "build", "us", "textures", relativePath);
        if (File.Exists(buildPath)) return buildPath;

        // 2. Check leveleditor/textures (seen in project structure)
        // Need to extract the filename part e.g. "levels/castle_inside/12.rgba16.png" -> "inside/inside_castle_textures.08000.rgba16.png"
        // This is tricky because the naming convention changes. 
        // Let's try to match by filename if possible or check common locations.
        
        string fileName = Path.GetFileName(relativePath);
        
        // Check in texturelevels/textures
        string textureLevelsPath = Path.Combine(projectRoot, "texturelevels", "textures");
        if (Directory.Exists(textureLevelsPath))
        {
            var matchedFiles = Directory.GetFiles(textureLevelsPath, fileName, SearchOption.AllDirectories);
            if (matchedFiles.Length > 0) return matchedFiles[0];
        }

        // Check in leveleditor/textures
        string localTexturesPath = Path.Combine(projectRoot, "leveleditor", "textures");
        if (Directory.Exists(localTexturesPath))
        {
            var matchedFiles = Directory.GetFiles(localTexturesPath, fileName, SearchOption.AllDirectories);
            if (matchedFiles.Length > 0) return matchedFiles[0];
        }
        
        // 3. Check relative to project root
        string rootPath = Path.Combine(projectRoot, relativePath);
        if (File.Exists(rootPath)) return rootPath;

        return null;
    }

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
                
                var subAreaDirectories = Directory.GetDirectories(areaDir);
                foreach (var subAreaDir in subAreaDirectories)
                {
                    var modelFilePath = Path.Combine(subAreaDir, "model.inc.c");
                    if (File.Exists(modelFilePath))
                    {
                        areaModelFiles.Add(modelFilePath);
                    }
                }
                
                if (areaModelFiles.Count > 0)
                {
                    modelFiles[areaName] = areaModelFiles;
                    Console.WriteLine($"Found {areaModelFiles.Count} model file(s) for {areaName}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error finding model files: {ex.Message}");
        }

        return modelFiles;
    }

    public VisualMesh? ParseMultipleModelFiles(
        List<string> modelFilePaths, 
        string areaName, 
        string levelName,
        Dictionary<string, Matrix4>? transformations = null)
    {
        if (modelFilePaths == null || modelFilePaths.Count == 0)
            return null;

        var mergedMesh = new VisualMesh
        {
            AreaName = areaName,
            LevelName = levelName
        };

        int totalVertices = 0;
        int totalTriangles = 0;
        int subModelIndex = 0;

        foreach (var filePath in modelFilePaths)
        {
            var subMesh = ParseModelFile(filePath, areaName, levelName);
            if (subMesh != null)
            {
                var pathParts = filePath.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
                int subModelNumber = subModelIndex + 1;
                for (int i = pathParts.Length - 1; i >= 0; i--)
                {
                    if (int.TryParse(pathParts[i], out int num))
                    {
                        subModelNumber = num;
                        break;
                    }
                }

                if (transformations != null && transformations.Count > 0)
                {
                    bool transformed = false;
                    string? matchingDl = null;
                    foreach (var dlName in subMesh.DisplayListNames)
                    {
                        if (transformations.ContainsKey(dlName))
                        {
                            matchingDl = dlName;
                            break;
                        }
                    }

                    if (matchingDl != null)
                    {
                        Console.WriteLine($"Applying transformation to sub-model by matching DL: {matchingDl}");
                        ApplyTransform(subMesh.Vertices, transformations[matchingDl]);
                        transformed = true;
                    }

                    if (!transformed)
                    {
                        var transformKey = $"SubModel_{subModelNumber}";
                        if (transformations.ContainsKey(transformKey))
                        {
                            Console.WriteLine($"Applying transformation to sub-model {subModelNumber} by index");
                            ApplyTransform(subMesh.Vertices, transformations[transformKey]);
                        }
                    }
                }

                var subMeshEntry = new SubMesh
                {
                    SourceFile = filePath,
                    SubModelNumber = subModelNumber,
                    Vertices = new List<ModelVertex>(subMesh.Vertices),
                    Triangles = new List<ModelTriangle>(subMesh.Triangles),
                    IsVisible = true
                };
                
                int vertexOffset = mergedMesh.Vertices.Count;
                
                mergedMesh.Vertices.AddRange(subMesh.Vertices);
                
                foreach (var tri in subMesh.Triangles)
                {
                    var adjustedTri = new ModelTriangle(
                        tri.V1 + vertexOffset,
                        tri.V2 + vertexOffset,
                        tri.V3 + vertexOffset,
                        tri.TextureName
                    );
                    subMeshEntry.Triangles.Add(new ModelTriangle(tri.V1, tri.V2, tri.V3, tri.TextureName));
                    mergedMesh.Triangles.Add(adjustedTri);
                }
                
                mergedMesh.SubMeshes.Add(subMeshEntry);
                totalVertices += subMesh.VertexCount;
                totalTriangles += subMesh.TriangleCount;
                subModelIndex++;
            }
        }

        Console.WriteLine($"Merged {modelFilePaths.Count} model files: {totalVertices} vertices, {totalTriangles} triangles");
        Console.WriteLine($"Sub-models: {string.Join(", ", mergedMesh.SubMeshes.Select(sm => sm.SubModelNumber))}");
        return mergedMesh;
    }

    private void ApplyTransform(List<ModelVertex> vertices, Matrix4 transform)
    {
        for (int i = 0; i < vertices.Count; i++)
        {
            var vertex = vertices[i];
            var pos = new Vector3(vertex.X, vertex.Y, vertex.Z);
            var transformed = Vector3.TransformPosition(pos, transform);
            
            vertices[i] = new ModelVertex(
                (int)transformed.X,
                (int)transformed.Y,
                (int)transformed.Z,
                vertex.S,
                vertex.T,
                vertex.NX,
                vertex.NY,
                vertex.NZ,
                vertex.Alpha
            );
        }
        
        Console.WriteLine($"Applied transformation to {vertices.Count} vertices");
    }
}
