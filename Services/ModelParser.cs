using Sm64DecompLevelViewer.Services.Models;
using static Sm64DecompLevelViewer.Services.Models.ModelSourcePatterns;
using static Sm64DecompLevelViewer.Services.Models.ModelParsingPrimitives;
using System.IO;
using System.Text.RegularExpressions;
using OpenTK.Mathematics;
using Sm64DecompLevelViewer.Models;

namespace Sm64DecompLevelViewer.Services;

public class ModelParser
{
    private readonly ModelFileCatalog _modelFileCatalog = new();
    private readonly ModelTextureResolver _modelTextureResolver = new();
    private readonly DisplayListInterpreter _displayListInterpreter = new();
    private readonly ModelVertexParser _modelVertexParser = new();

    // Regex patterns for parsing model data

    public VisualMesh? ParseModelFile(
        string modelFilePath, 
        string areaName, 
        string levelName, 
        Dictionary<string, Matrix4>? transformations = null,
        Dictionary<string, int>? dlToJointIndex = null,
        List<GeoDisplayListInstance>? displayListInstances = null,
        float rootScale = 1.0f,
        string? sourceText = null)
    {
        try
        {
            if (sourceText == null && !File.Exists(modelFilePath))
            {
                Console.WriteLine($"Model file not found: {modelFilePath}");
                return null;
            }

            var fileContent = sourceText ?? ActorSourceReader.Read(modelFilePath);
            var mesh = new VisualMesh
            {
                AreaName = areaName,
                LevelName = levelName,
                DlToJointIndex = dlToJointIndex ?? new(),
                DisplayListInstances = displayListInstances ?? new(),
                RootScale = rootScale
            };

            var vertexArrays = ParseVertexArrays(fileContent);

            // Parse all gdSPDefLights1
            var lightMatches = gdSPLightsPattern.Matches(fileContent);
            foreach (Match match in lightMatches)
            {
                string lightGroupName = match.Groups[1].Value;
                byte r = ParseByteHelper(match.Groups[5].Value);
                byte g = ParseByteHelper(match.Groups[6].Value);
                byte b = ParseByteHelper(match.Groups[7].Value);
                
                mesh.LightGroupColors[lightGroupName] = new Vector3(r / 255.0f, g / 255.0f, b / 255.0f);
            }

            _displayListInterpreter.ParseDisplayLists(fileContent, vertexArrays, mesh, transformations, displayListInstances);

            Console.WriteLine($"Parsed visual mesh: {mesh}");
            return mesh;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error parsing model file {modelFilePath}: {ex.Message}");
            return null;
        }
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
                    ) { TextureOffset = tri.TextureOffset };
                    subMeshEntry.Triangles.Add(new ModelTriangle(tri.V1, tri.V2, tri.V3, tri.TextureName) { TextureOffset = tri.TextureOffset });
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

    public Dictionary<string, List<ModelVertex>> ParseVertexArrays(string content) => _modelVertexParser.ParseVertexArrays(content);
    public void ParseTextureMapping(string levelPath, VisualMesh mesh, string projectRoot) => _modelTextureResolver.ParseTextureMapping(levelPath, mesh, projectRoot);
    public void ParseActorTextureMapping(string actorDir, string modelFilePath, VisualMesh mesh, string projectRoot) => _modelTextureResolver.ParseActorTextureMapping(actorDir, modelFilePath, mesh, projectRoot);
    public void ResolveSharedTextures(VisualMesh mesh, string textureBin, string projectRoot) => _modelTextureResolver.ResolveSharedTextures(mesh, textureBin, projectRoot);
    public Dictionary<string, List<string>> FindModelFiles(string levelPath) => _modelFileCatalog.FindModelFiles(levelPath);
}
