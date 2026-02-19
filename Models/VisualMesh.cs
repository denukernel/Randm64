namespace Sm64DecompLevelViewer.Models;

public class SubMesh
{
    public List<ModelVertex> Vertices { get; set; } = new();
    public List<ModelTriangle> Triangles { get; set; } = new();
    public string SourceFile { get; set; } = string.Empty;
    public int SubModelNumber { get; set; }
    public bool IsVisible { get; set; } = true;
}

/// <summary>
/// Container for visual mesh data (vertices and triangles with UVs and normals).
/// </summary>
public class VisualMesh
{
    public List<ModelVertex> Vertices { get; set; } = new();
    public List<ModelTriangle> Triangles { get; set; } = new();
    public List<SubMesh> SubMeshes { get; set; } = new(); // Track individual sub-models
    public string AreaName { get; set; } = string.Empty;
    public string LevelName { get; set; } = string.Empty;
    public string? MainDisplayListName { get; set; } // The first/main display list name found in the file
    public List<string> DisplayListNames { get; set; } = new(); // All display list names found in the file
    public Dictionary<string, string> TexturePaths { get; set; } = new(); // Mapping of texture symbols to PNG paths

    public int VertexCount => Vertices.Count;
    public int TriangleCount => Triangles.Count;

    public override string ToString()
    {
        return $"VisualMesh: {LevelName} - {AreaName} ({VertexCount} vertices, {TriangleCount} triangles)";
    }
}
