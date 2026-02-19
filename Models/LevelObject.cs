namespace Sm64DecompLevelViewer.Models;

public enum ObjectSourceType
{
    Normal,
    Macro,
    Special,
    Mario
}

/// <summary>
/// Represents an object placed in a SM64 level.
/// </summary>
public class LevelObject
{
    public string ModelName { get; set; } = string.Empty;
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public int RX { get; set; }
    public int RY { get; set; }
    public int RZ { get; set; }
    public uint Params { get; set; }
    public string Behavior { get; set; } = string.Empty;

    // Level Editor Metadata
    public string? SourceFile { get; set; }
    public int SourceIndex { get; set; }
    public int SourceLength { get; set; }
    public ObjectSourceType SourceType { get; set; }
    public bool IsDeleted { get; set; }
    public bool IsNew { get; set; }
    public int AreaIndex { get; set; } = -1;
}
