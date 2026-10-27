namespace Sm64DecompLevelViewer.Models;

public enum TextureAddressMode { Repeat, Clamp, Mirror }

/// <summary>
/// Represents a triangle in a visual mesh.
/// </summary>
public class ModelTriangle
{
    public int V1 { get; set; }
    public int V2 { get; set; }
    public int V3 { get; set; }
    public string? TextureName { get; set; }
    // Texel origin of an image loaded from an offset into the source texture array.
    public OpenTK.Mathematics.Vector2 TextureOffset { get; set; }
    public string? SourceDisplayList { get; set; }
    public int SourceCommandIndex { get; set; } = -1;
    public int SourceTrianglePart { get; set; }
    public string? SourceTextureImage { get; set; }
    public bool SourceTextureEnabled { get; set; }
    public string? SourceTextureRestore { get; set; }
    public string? ShadeLightSymbol { get; set; }
    public bool BlendTextureWithShade { get; set; }
    public TextureAddressMode TextureAddressS { get; set; }
    public TextureAddressMode TextureAddressT { get; set; }
    public bool LightingEnabled { get; set; } = true;
    public bool DecalTexture { get; set; }
    public float Opacity { get; set; } = 1;

    public ModelTriangle(int v1, int v2, int v3, string? textureName = null)
    {
        V1 = v1;
        V2 = v2;
        V3 = v3;
        TextureName = textureName;
    }
}
