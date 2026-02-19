using StbImageSharp;
using OpenTK.Graphics.OpenGL4;
using System.IO;

namespace Sm64DecompLevelViewer.Services;

/// <summary>
/// Loads PNG textures from the SM64 decomp and creates OpenGL textures.
/// </summary>
public class TextureLoader
{
    private readonly Dictionary<string, int> _textureCache = new();
    private readonly string _decompPath;

    public TextureLoader(string decompPath)
    {
        _decompPath = decompPath;
    }

    /// <summary>
    /// Loads a texture from a PNG file and returns the OpenGL texture ID.
    /// </summary>
    /// <param name="relativePath">Path relative to decomp root (e.g., "levels/castle_grounds/0.rgba16.png")</param>
    /// <returns>OpenGL texture ID, or 0 if loading failed</returns>
    public int LoadTexture(string relativePath)
    {
        // Check cache first
        if (_textureCache.TryGetValue(relativePath, out int cachedId))
        {
            return cachedId;
        }

        string fullPath = Path.Combine(_decompPath, relativePath);

        if (!File.Exists(fullPath))
        {
            Console.WriteLine($"Texture not found: {fullPath}");
            return 0;
        }

        try
        {
            // Load PNG using StbImageSharp
            using var stream = File.OpenRead(fullPath);
            ImageResult image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);

            if (image == null)
            {
                Console.WriteLine($"Failed to load image: {fullPath}");
                return 0;
            }

            // Create OpenGL texture
            int textureId = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, textureId);

            // Upload texture data
            GL.TexImage2D(
                TextureTarget.Texture2D,
                0,
                PixelInternalFormat.Rgba,
                image.Width,
                image.Height,
                0,
                PixelFormat.Rgba,
                PixelType.UnsignedByte,
                image.Data
            );

            // Set texture parameters
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);

            GL.BindTexture(TextureTarget.Texture2D, 0);

            // Cache the texture
            _textureCache[relativePath] = textureId;

            Console.WriteLine($"Loaded texture: {relativePath} ({image.Width}x{image.Height}) -> GL ID {textureId}");
            return textureId;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading texture {fullPath}: {ex.Message}");
            return 0;
        }
    }

    /// <summary>
    /// Creates a simple checkerboard test texture.
    /// </summary>
    public int CreateCheckerboardTexture(int size = 64)
    {
        byte[] data = new byte[size * size * 4];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int index = (y * size + x) * 4;
                bool isWhite = ((x / 8) + (y / 8)) % 2 == 0;

                byte color = (byte)(isWhite ? 255 : 64);
                data[index + 0] = color; // R
                data[index + 1] = color; // G
                data[index + 2] = color; // B
                data[index + 3] = 255;   // A
            }
        }

        int textureId = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, textureId);

        GL.TexImage2D(
            TextureTarget.Texture2D,
            0,
            PixelInternalFormat.Rgba,
            size,
            size,
            0,
            PixelFormat.Rgba,
            PixelType.UnsignedByte,
            data
        );

        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);

        GL.BindTexture(TextureTarget.Texture2D, 0);

        Console.WriteLine($"Created checkerboard texture -> GL ID {textureId}");
        return textureId;
    }

    /// <summary>
    /// Cleans up all loaded textures.
    /// </summary>
    public void Dispose()
    {
        foreach (var textureId in _textureCache.Values)
        {
            GL.DeleteTexture(textureId);
        }
        _textureCache.Clear();
    }
}
