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
    internal sealed class MeshGpu
    {
        public int Vao, Vbo, VertexCount;
        public List<DrawCall> DrawCalls = new();
        public void Dispose() { if (Vao != 0) GL.DeleteVertexArray(Vao); if (Vbo != 0) GL.DeleteBuffer(Vbo); }
    }
