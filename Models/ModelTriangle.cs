namespace Sm64DecompLevelViewer.Models;

/// <summary>
/// Represents a triangle in a visual mesh.
/// </summary>
public class ModelTriangle
{
    public int V1 { get; set; }
    public int V2 { get; set; }
    public int V3 { get; set; }
    public string? TextureName { get; set; }

    public ModelTriangle(int v1, int v2, int v3, string? textureName = null)
    {
        V1 = v1;
        V2 = v2;
        V3 = v3;
        TextureName = textureName;
    }
}
