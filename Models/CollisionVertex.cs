namespace Sm64DecompLevelViewer.Models;

/// <summary>
/// Represents a vertex in the collision mesh.
/// </summary>
public class CollisionVertex
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }

    public CollisionVertex(int x, int y, int z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public override string ToString() => $"({X}, {Y}, {Z})";
}
