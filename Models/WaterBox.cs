namespace Sm64DecompLevelViewer.Models;

/// <summary>
/// Represents a water box defining a water surface region.
/// </summary>
public class WaterBox
{
    public int Id { get; set; }
    public int X1 { get; set; }
    public int Z1 { get; set; }
    public int X2 { get; set; }
    public int Z2 { get; set; }
    public int Y { get; set; }

    public WaterBox(int id, int x1, int z1, int x2, int z2, int y)
    {
        Id = id;
        X1 = x1;
        Z1 = z1;
        X2 = x2;
        Z2 = z2;
        Y = y;
    }
}
