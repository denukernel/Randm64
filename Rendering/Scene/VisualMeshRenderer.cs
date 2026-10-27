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
internal static class VisualMeshRenderer
{
    internal static void Draw(MeshGpu data, Matrix4 model, bool brightLighting, Vector2 textureOffset, Matrix4 _view, Matrix4 _projection, int _visualShaderProgram, Vector3 _cameraPosition, Vector3 _cameraTarget)
    {
        Matrix4 mvp = model * _view * _projection;
            GL.UseProgram(_visualShaderProgram);
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.Uniform1(GL.GetUniformLocation(_visualShaderProgram, "uTexture"), 0);
            
            int mvpLocation = GL.GetUniformLocation(_visualShaderProgram, "uMVP");
            GL.UniformMatrix4(mvpLocation, false, ref mvp);
            GL.UniformMatrix4(GL.GetUniformLocation(_visualShaderProgram, "uModel"), false, ref model);
            GL.Uniform2(GL.GetUniformLocation(_visualShaderProgram, "uTextureOffset"), textureOffset);
            
            int lightDirLocation = GL.GetUniformLocation(_visualShaderProgram, "uLightDir");
            var facing = Vector3.Normalize(_cameraPosition - _cameraTarget);
            var keyLight = brightLighting ? Vector3.Normalize(facing + Vector3.UnitY * 0.8f) : new Vector3(0.5f, 1.0f, 0.3f);
            GL.Uniform3(lightDirLocation, keyLight);
            GL.Uniform3(GL.GetUniformLocation(_visualShaderProgram, "uFillLightDir"), Vector3.Normalize(facing - Vector3.UnitY * 0.25f));
            GL.Uniform1(GL.GetUniformLocation(_visualShaderProgram, "uActorLighting"), brightLighting ? 1 : 0);

            int useTexLocation = GL.GetUniformLocation(_visualShaderProgram, "uUseTexture");

            GL.BindVertexArray(data.Vao);
            
            int useFallbackLocation = GL.GetUniformLocation(_visualShaderProgram, "uUseFallbackColor");
            int fallbackColorLocation = GL.GetUniformLocation(_visualShaderProgram, "uFallbackColor");
            int blendShadeLocation = GL.GetUniformLocation(_visualShaderProgram, "uBlendTextureWithShade");
            int addressSLocation = GL.GetUniformLocation(_visualShaderProgram, "uTextureAddressS");
            int addressTLocation = GL.GetUniformLocation(_visualShaderProgram, "uTextureAddressT");
            int lightingLocation = GL.GetUniformLocation(_visualShaderProgram, "uLightingEnabled");
            int decalLocation = GL.GetUniformLocation(_visualShaderProgram, "uDecalTexture");

            foreach (var drawCall in data.DrawCalls)
            {
                GL.Uniform1(blendShadeLocation, drawCall.BlendTextureWithShade ? 1 : 0);
                GL.Uniform1(addressSLocation, (int)drawCall.TextureAddressS);
                GL.Uniform1(addressTLocation, (int)drawCall.TextureAddressT);
                GL.Uniform1(lightingLocation, drawCall.LightingEnabled ? 1 : 0);
                GL.Uniform1(decalLocation, drawCall.DecalTexture ? 1 : 0);
                GL.Uniform1(GL.GetUniformLocation(_visualShaderProgram, "uOpacity"), drawCall.Opacity);
                if (drawCall.TextureId > 0)
                {
                    GL.Uniform1(useTexLocation, 1);
                    GL.Uniform1(useFallbackLocation, 0);
                    GL.Uniform3(fallbackColorLocation, drawCall.Color ?? Vector3.One);
                    GL.BindTexture(TextureTarget.Texture2D, drawCall.TextureId);
                }
                else
                {
                    GL.Uniform1(useTexLocation, 0);
                    if (drawCall.Color.HasValue)
                    {
                        GL.Uniform1(useFallbackLocation, 1);
                        GL.Uniform3(fallbackColorLocation, drawCall.Color.Value);
                    }
                    else
                    {
                        GL.Uniform1(useFallbackLocation, 0);
                    }
                    GL.BindTexture(TextureTarget.Texture2D, 0);
                }

                GL.DrawArrays(PrimitiveType.Triangles, drawCall.StartIndex, drawCall.VertexCount);
            }
            
            GL.BindVertexArray(0);
    }
}
