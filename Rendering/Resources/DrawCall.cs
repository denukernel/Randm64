using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using Sm64DecompLevelViewer.Models;
using Sm64DecompLevelViewer.Services;
using System.IO;
using System.Linq;

namespace Sm64DecompLevelViewer.Rendering;
public struct DrawCall
{
    public int StartIndex;
    public int VertexCount;
    public int TextureId;
    public Vector3? Color;
    public bool BlendTextureWithShade;
    public TextureAddressMode TextureAddressS;
    public TextureAddressMode TextureAddressT;
    public bool LightingEnabled;
    public bool DecalTexture;
    public float Opacity;
}
