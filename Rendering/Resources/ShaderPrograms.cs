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
internal static class ShaderPrograms
{
    internal static int CreateCollisionShader()
    {
        string vertexShaderSource = @"
            #version 330 core
            layout(location = 0) in vec3 aPosition;
            uniform mat4 uMVP;
            void main()
            {
                gl_Position = uMVP * vec4(aPosition, 1.0);
            }
        ";

        string fragmentShaderSource = @"
            #version 330 core
            out vec4 FragColor;
            void main()
            {
                FragColor = vec4(0.0, 1.0, 0.0, 1.0); // Green wireframe
            }
        ";

        return CompileShaderProgram(vertexShaderSource, fragmentShaderSource);
    }
    internal static int CreateVisualShader()
    {
        string vertexShaderSource = @"
            #version 330 core
            layout(location = 0) in vec3 aPosition;
            layout(location = 1) in vec3 aNormal;
            layout(location = 2) in vec2 aTexCoord;
            layout(location = 3) in vec4 aColor;
            
            uniform mat4 uMVP;
            uniform mat4 uModel;
            
            out vec3 vNormal;
            out vec3 vPosition;
            out vec2 vTexCoord;
            out vec4 vColor;
            
            void main()
            {
                gl_Position = uMVP * vec4(aPosition, 1.0);
                vNormal = mat3(uModel) * aNormal;
                vPosition = aPosition;
                vTexCoord = aTexCoord;
                vColor = aColor;
            }
        ";

        string fragmentShaderSource = @"
            #version 330 core
            in vec3 vNormal;
            in vec3 vPosition;
            in vec2 vTexCoord;
            in vec4 vColor;
            
            uniform vec3 uLightDir;
            uniform vec3 uFillLightDir;
            uniform bool uActorLighting;
            uniform sampler2D uTexture;
            uniform bool uUseTexture;
            uniform vec3 uFallbackColor;
            uniform bool uUseFallbackColor;
            uniform bool uBlendTextureWithShade;
            uniform int uTextureAddressS;
            uniform int uTextureAddressT;
            uniform bool uLightingEnabled;
            uniform bool uDecalTexture;
            uniform float uOpacity;
            uniform vec2 uTextureOffset;
            
            out vec4 FragColor;
            
            float addressCoordinate(float value, float texels, int mode)
            {
                if (mode == 2) value = 1.0 - abs(mod(value, 2.0) - 1.0);
                if (mode != 0) value = clamp(value, 0.5 / texels, 1.0 - 0.5 / texels);
                return value;
            }

            void main()
            {
                vec3 norm = normalize(vNormal);
                vec3 lightDir = normalize(uLightDir);
                
                float diff = max(dot(norm, lightDir), 0.0);
                float ambient = 0.3;
                float lighting = ambient + diff * 0.7;
                if (uActorLighting)
                    lighting = min(1.0, 0.55 + diff * 0.35 + max(dot(norm, normalize(uFillLightDir)), 0.0) * 0.2);
                if (!uLightingEnabled || uDecalTexture) lighting = 1.0;
                
                vec4 texColor = uUseFallbackColor ? vec4(uFallbackColor, 1.0) : vec4(0.7, 0.7, 0.8, 1.0);
                if (uUseTexture)
                {
                    vec2 texels = vec2(textureSize(uTexture, 0));
                    vec2 uv = (vTexCoord + vec2(0.5) + uTextureOffset) / texels;
                    uv.x = addressCoordinate(uv.x, texels.x, uTextureAddressS);
                    uv.y = addressCoordinate(uv.y, texels.y, uTextureAddressT);
                    texColor = texture(uTexture, uv);
                    if (!uBlendTextureWithShade && !uDecalTexture)
                        texColor *= uLightingEnabled ? vec4(uFallbackColor, 1.0) : vColor;
                }
                else if (!uLightingEnabled) texColor = vColor;
                if (uUseTexture && uBlendTextureWithShade)
                    texColor = vec4(mix(uFallbackColor, texColor.rgb, texColor.a), 1.0);
                if (texColor.a < 0.1) discard;

                vec3 color = texColor.rgb * lighting;
                FragColor = vec4(color, texColor.a * uOpacity);
            }
        ";

        return CompileShaderProgram(vertexShaderSource, fragmentShaderSource);
    }
    internal static int CreateObjectShader()
    {
        string vertexShaderSource = @"
            #version 330 core
            layout(location = 0) in vec3 aPosition;
            layout(location = 1) in vec3 aColor;
            
            uniform mat4 uMVP;
            
            out vec3 vColor;
            
            void main()
            {
                gl_Position = uMVP * vec4(aPosition, 1.0);
                vColor = aColor;
            }
        ";

        string fragmentShaderSource = @"
            #version 330 core
            in vec3 vColor;
            
            out vec4 FragColor;
            
            void main()
            {
                FragColor = vec4(vColor, 1.0);
            }
        ";

        return CompileShaderProgram(vertexShaderSource, fragmentShaderSource);
    }
    internal static int CompileShaderProgram(string vertexSource, string fragmentSource)
    {
        int vertexShader = GL.CreateShader(ShaderType.VertexShader);
        GL.ShaderSource(vertexShader, vertexSource);
        GL.CompileShader(vertexShader);

        GL.GetShader(vertexShader, ShaderParameter.CompileStatus, out int vertexSuccess);
        if (vertexSuccess == 0)
        {
            string infoLog = GL.GetShaderInfoLog(vertexShader);
            Console.WriteLine($"Vertex Shader Error: {infoLog}");
        }

        int fragmentShader = GL.CreateShader(ShaderType.FragmentShader);
        GL.ShaderSource(fragmentShader, fragmentSource);
        GL.CompileShader(fragmentShader);

        GL.GetShader(fragmentShader, ShaderParameter.CompileStatus, out int fragmentSuccess);
        if (fragmentSuccess == 0)
        {
            string infoLog = GL.GetShaderInfoLog(fragmentShader);
            Console.WriteLine($"Fragment Shader Error: {infoLog}");
        }

        int shaderProgram = GL.CreateProgram();
        GL.AttachShader(shaderProgram, vertexShader);
        GL.AttachShader(shaderProgram, fragmentShader);
        GL.LinkProgram(shaderProgram);

        GL.GetProgram(shaderProgram, GetProgramParameterName.LinkStatus, out int linkSuccess);
        if (linkSuccess == 0)
        {
            string infoLog = GL.GetProgramInfoLog(shaderProgram);
            Console.WriteLine($"Shader Program Link Error: {infoLog}");
        }

        GL.DeleteShader(vertexShader);
        GL.DeleteShader(fragmentShader);

        return shaderProgram;
    }
}
