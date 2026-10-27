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
    internal sealed class LevelActorAsset
    {
        public VisualMesh Mesh = null!;
        public MarioAnimation? Animation;
        public MeshGpu Gpu = new();
        public int LastFrame = -1;
    }
