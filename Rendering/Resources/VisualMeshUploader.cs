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
internal static class VisualMeshUploader
{
    internal static void Upload(VisualMesh? _visualMesh, TextureLoader? _textureLoader, VisualMesh? editedScene, List<ModelVertex> meshGpuOrder, ref int _visualVao, ref int _visualVbo, ref int _visualVertexCount, List<DrawCall> _drawCalls, ref float[] meshGpuData, ref bool meshPositionsDirty)
    {
        try
        {
            if (_visualMesh == null || (_visualMesh.Triangles.Count == 0 && _visualMesh.SubMeshes.Count == 0))
            {
                _visualVertexCount = 0;
                _drawCalls.Clear();
                return;
            }

            // Group triangles by texture
            var meshTriangles = new List<ModelTriangle>();
            var meshVertices = new List<ModelVertex>();

            if (_visualMesh.SubMeshes.Count > 0)
            {
                foreach (var subMesh in _visualMesh.SubMeshes)
                {
                    if (!subMesh.IsVisible) continue;
                    
                    int vertexOffset = meshVertices.Count;
                    meshVertices.AddRange(subMesh.Vertices);
                    foreach (var tri in subMesh.Triangles)
                    {
                        meshTriangles.Add(new ModelTriangle(
                            tri.V1 + vertexOffset,
                            tri.V2 + vertexOffset,
                            tri.V3 + vertexOffset,
                            tri.TextureName) { TextureOffset = tri.TextureOffset });
                    }
                }
            }
            else
            {
                meshTriangles = _visualMesh.Triangles;
                meshVertices = _visualMesh.Vertices;
            }

            if (meshTriangles.Count == 0)
            {
                _visualVertexCount = 0;
                _drawCalls.Clear();
                return;
            }

            // Sort triangles by texture to minimize state changes
            var sortedTriangles = meshTriangles.OrderBy(t => t.TextureName ?? "").ToList();
            
            List<float> vertices = new List<float>();
            bool cacheMeshPositions = ReferenceEquals(_visualMesh, editedScene);
            if (cacheMeshPositions) meshGpuOrder.Clear();
            _drawCalls.Clear();

            string? lastTexture = null;
            string? lastShade = null;
            bool lastBlend = false;
            var lastAddressS = TextureAddressMode.Repeat;
            var lastAddressT = TextureAddressMode.Repeat;
            bool lastLighting = true, lastDecal = false;
            float lastOpacity = -1;
            DrawCall? currentDrawCall = null;

            foreach (var tri in sortedTriangles)
            {
                if (tri.V1 >= meshVertices.Count || tri.V2 >= meshVertices.Count || tri.V3 >= meshVertices.Count)
                    continue;

                // Start new draw call if texture changes
                if (tri.TextureName != lastTexture || tri.ShadeLightSymbol != lastShade ||
                    tri.BlendTextureWithShade != lastBlend || tri.TextureAddressS != lastAddressS ||
                    tri.TextureAddressT != lastAddressT || tri.LightingEnabled != lastLighting ||
                    tri.DecalTexture != lastDecal || tri.Opacity != lastOpacity || currentDrawCall == null)
                {
                    if (currentDrawCall != null) _drawCalls.Add(currentDrawCall.Value);

                    int textureId = 0;
                    Vector3? fallbackColor = null;

                    if (tri.TextureName != null)
                    {
                        if (_visualMesh.TexturePaths.TryGetValue(tri.TextureName, out string? relativePath))
                        {
                            textureId = _textureLoader?.LoadTexture(relativePath) ?? 0;
                        }
                        else if (_visualMesh.LightGroupColors.TryGetValue(tri.TextureName, out var col))
                        {
                            fallbackColor = col;
                        }
                    }

                    if (tri.ShadeLightSymbol != null &&
                        _visualMesh.LightGroupColors.TryGetValue(tri.ShadeLightSymbol, out var shadeColor))
                        fallbackColor = shadeColor;

                    currentDrawCall = new DrawCall
                    {
                        StartIndex = vertices.Count / 12,
                        VertexCount = 0,
                        TextureId = textureId,
                        Color = fallbackColor,
                        BlendTextureWithShade = tri.BlendTextureWithShade,
                        TextureAddressS = tri.TextureAddressS, TextureAddressT = tri.TextureAddressT,
                        LightingEnabled = tri.LightingEnabled, DecalTexture = tri.DecalTexture, Opacity = tri.Opacity
                    };
                    lastTexture = tri.TextureName;
                    lastShade = tri.ShadeLightSymbol;
                    lastBlend = tri.BlendTextureWithShade;
                    lastAddressS = tri.TextureAddressS;
                    lastAddressT = tri.TextureAddressT;
                    lastLighting = tri.LightingEnabled;
                    lastDecal = tri.DecalTexture;
                    lastOpacity = tri.Opacity;
                }

                // Add vertices for this triangle
                var vList = new[] { meshVertices[tri.V1], meshVertices[tri.V2], meshVertices[tri.V3] };
                foreach (var v in vList)
                {
                    if (cacheMeshPositions) meshGpuOrder.Add(v);
                    // Position
                    vertices.Add(v.X); vertices.Add(v.Y); vertices.Add(v.Z);
                    // Normal
                    var n = v.GetNormalizedNormal();
                    vertices.Add(n.nx); vertices.Add(n.ny); vertices.Add(n.nz);
                    // UVs
                    // N64 UVs are signed s10.5 texel coordinates; the shader uses the actual image dimensions.
                    vertices.Add(v.S / 32.0f + tri.TextureOffset.X);
                    vertices.Add(v.T / 32.0f + tri.TextureOffset.Y);
                    vertices.Add(v.VertexColor.X); vertices.Add(v.VertexColor.Y);
                    vertices.Add(v.VertexColor.Z); vertices.Add(v.VertexColor.W);
                }
                
                currentDrawCall = new DrawCall 
                { 
                    StartIndex = currentDrawCall.Value.StartIndex,
                    VertexCount = currentDrawCall.Value.VertexCount + 3,
                    TextureId = currentDrawCall.Value.TextureId,
                    Color = currentDrawCall.Value.Color,
                    BlendTextureWithShade = currentDrawCall.Value.BlendTextureWithShade,
                    TextureAddressS = currentDrawCall.Value.TextureAddressS,
                    TextureAddressT = currentDrawCall.Value.TextureAddressT,
                    LightingEnabled = currentDrawCall.Value.LightingEnabled,
                    DecalTexture = currentDrawCall.Value.DecalTexture,
                    Opacity = currentDrawCall.Value.Opacity
                };
            }

            if (currentDrawCall != null) _drawCalls.Add(currentDrawCall.Value);

            _visualVertexCount = vertices.Count / 12; // 12 floats per vertex (position, normal, UV, RGBA)

            if (_visualVertexCount == 0) return;

            // Animation/billboard updates reuse their buffers instead of leaking
            // a new VAO and VBO on every frame.
            if (_visualVao == 0) _visualVao = GL.GenVertexArray();
            if (_visualVbo == 0) _visualVbo = GL.GenBuffer();

            GL.BindVertexArray(_visualVao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _visualVbo);
            if (cacheMeshPositions) { meshGpuData = vertices.ToArray(); meshPositionsDirty = false; }
            GL.BufferData(BufferTarget.ArrayBuffer, vertices.Count * sizeof(float), vertices.ToArray(), BufferUsageHint.DynamicDraw);

            // Position attribute (location = 0)
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 12 * sizeof(float), 0);
            GL.EnableVertexAttribArray(0);

            // Normal attribute (location = 1)
            GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 12 * sizeof(float), 3 * sizeof(float));
            GL.EnableVertexAttribArray(1);

            // UV attribute (location = 2)
            GL.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, 12 * sizeof(float), 6 * sizeof(float));
            GL.EnableVertexAttribArray(2);
            GL.VertexAttribPointer(3, 4, VertexAttribPointerType.Float, false, 12 * sizeof(float), 8 * sizeof(float));
            GL.EnableVertexAttribArray(3);

            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
            GL.BindVertexArray(0);

        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR in UploadVisualMeshData: {ex.Message}");
            _visualVertexCount = 0;
        }
    }
}
