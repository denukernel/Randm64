using System.IO;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using Sm64DecompLevelViewer.Services;

namespace Sm64DecompLevelViewer.Rendering;

internal sealed class LevelSkyboxRenderer : IDisposable
{
    private int _program, _vao;
    public void Draw(string projectRoot, string? name, TextureLoader textures, Vector3 position, Vector3 target, float aspect)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        string path = Path.Combine("textures", "skyboxes", name + ".png");
        if (!File.Exists(Path.Combine(projectRoot, path))) return;
        int texture = textures.LoadTexture(path);
        if (texture == 0) return;
        if (_program == 0)
        {
            _program = GeometryRenderer.CompileShaderProgram("""
                #version 330 core
                out vec2 screen;
                void main() {
                    vec2 p = vec2((gl_VertexID << 1) & 2, gl_VertexID & 2);
                    screen = p * 2.0 - 1.0;
                    gl_Position = vec4(screen, 1.0, 1.0);
                }
                """, """
                #version 330 core
                in vec2 screen;
                uniform sampler2D panorama;
                uniform vec3 forward, right, up;
                uniform float aspect;
                out vec4 color;
                void main() {
                    vec3 ray = normalize(forward + 0.41421356 * (screen.x * aspect * right + screen.y * up));
                    vec2 uv = vec2(atan(ray.x, ray.z) / 6.2831853 + 0.5,
                        clamp(0.5 - asin(clamp(ray.y, -1.0, 1.0)) / 3.14159265, 0.002, 0.998));
                    color = vec4(texture(panorama, uv).rgb, 1.0);
                }
                """);
            _vao = GL.GenVertexArray();
        }
        var forward = Vector3.Normalize(target - position);
        var right = Vector3.Cross(forward, Vector3.UnitY);
        if (right.LengthSquared < 0.001f) right = Vector3.UnitX;
        right.Normalize();
        GL.Disable(EnableCap.DepthTest);
        GL.DepthMask(false);
        GL.UseProgram(_program);
        GL.Uniform3(GL.GetUniformLocation(_program, "forward"), forward);
        GL.Uniform3(GL.GetUniformLocation(_program, "right"), right);
        GL.Uniform3(GL.GetUniformLocation(_program, "up"), Vector3.Cross(right, forward));
        GL.Uniform1(GL.GetUniformLocation(_program, "aspect"), aspect);
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, texture);
        GL.Uniform1(GL.GetUniformLocation(_program, "panorama"), 0);
        GL.BindVertexArray(_vao);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
        GL.BindVertexArray(0);
        GL.DepthMask(true);
        GL.Enable(EnableCap.DepthTest);
    }
    public void Dispose()
    {
        if (_vao != 0) GL.DeleteVertexArray(_vao);
        if (_program != 0) GL.DeleteProgram(_program);
    }
}
