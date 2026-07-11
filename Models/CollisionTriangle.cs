namespace Sm64DecompLevelViewer.Models;

/// <summary>
/// Represents a triangle in the collision mesh.
/// </summary>
public class CollisionTriangle
{
    public int V1 { get; set; }  // Vertex index 1
    public int V2 { get; set; }  // Vertex index 2
    public int V3 { get; set; }  // Vertex index 3
    public string SurfaceType { get; set; } = "SURFACE_DEFAULT";
    public int? SpecialParam { get; set; }

    public CollisionTriangle(int v1, int v2, int v3, string surfaceType = "SURFACE_DEFAULT", int? specialParam = null)
    {
        V1 = v1;
        V2 = v2;
        V3 = v3;
        SurfaceType = surfaceType;
        SpecialParam = specialParam;
    }

    public override string ToString() => SpecialParam.HasValue 
        ? $"TriangleSpecial({V1}, {V2}, {V3}, 0x{SpecialParam.Value:X}) [{SurfaceType}]"
        : $"Triangle({V1}, {V2}, {V3}) [{SurfaceType}]";
}
