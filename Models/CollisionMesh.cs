namespace Sm64DecompLevelViewer.Models;

/// <summary>
/// Container for collision mesh data (vertices and triangles).
/// </summary>
public class CollisionMesh
{
    public List<CollisionVertex> Vertices { get; set; } = new();
    public List<CollisionTriangle> Triangles { get; set; } = new();
    public string AreaName { get; set; } = string.Empty;
    public string LevelName { get; set; } = string.Empty;

    public int VertexCount => Vertices.Count;
    public int TriangleCount => Triangles.Count;

    public override string ToString() => 
        $"{LevelName} - {AreaName}: {VertexCount} vertices, {TriangleCount} triangles";
}
