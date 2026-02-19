using OpenTK.Mathematics;

namespace Sm64DecompLevelViewer.Models;

/// <summary>
/// Represents a node in the SM64 geometry layout scene graph.
/// </summary>
public class GeoNode
{
    public GeoNodeType Type { get; set; }
    public Vector3 Translation { get; set; }
    public Vector3 Rotation { get; set; } // In degrees
    public float Scale { get; set; } = 1.0f;
    public string? DisplayListName { get; set; }
    public List<GeoNode> Children { get; set; } = new();

    public GeoNode(GeoNodeType type)
    {
        Type = type;
    }

    public override string ToString()
    {
        return $"GeoNode[{Type}] T:{Translation} R:{Rotation} S:{Scale} DL:{DisplayListName ?? "none"}";
    }
}

/// <summary>
/// Types of geometry layout nodes.
/// </summary>
public enum GeoNodeType
{
    Root,
    Translate,
    Rotate,
    TranslateRotate,
    Scale,
    DisplayList,
    Other
}
