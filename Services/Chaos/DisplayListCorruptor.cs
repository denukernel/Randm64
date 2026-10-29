using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Sm64DecompLevelViewer.Services.Chaos;

internal sealed class DisplayListCorruptor
{
    private readonly Random _random;
    public DisplayListCorruptor(Random random) { _random = random; }
        internal void CorruptDisplayListFile(string filePath, double intensity, int dlMode)
        {
            try
            {
                // Create backup first
                string backupPath = filePath + ".bak";
                if (!File.Exists(backupPath))
                {
                    File.Copy(filePath, backupPath);
                }

                string content = File.ReadAllText(filePath);
                double rate = (intensity / 500.0); // max rate 1.0 at 500%

                // Matches N64 Vtx structures: {{{x, y, z}, flag, {u, v}, {r, g, b, a}}}
                var regex = new Regex(@"\{\{\{\s*(-?\d+),\s*(-?\d+),\s*(-?\d+)\s*\},\s*(\d+|0x[0-9a-fA-F]+),\s*\{\s*(-?\d+),\s*(-?\d+)\s*\},\s*\{\s*(\d+|0x[0-9a-fA-F]+|-?\d+),\s*(\d+|0x[0-9a-fA-F]+|-?\d+),\s*(\d+|0x[0-9a-fA-F]+|-?\d+),\s*(\d+|0x[0-9a-fA-F]+|-?\d+)\s*\}\s*\}\}");

                string newContent = regex.Replace(content, m =>
                {
                    double effectiveRate = (dlMode == 7) ? 1.0 : (rate * 0.5);
                    if (_random.NextDouble() >= effectiveRate) return m.Value; // scale rate

                    int x = int.Parse(m.Groups[1].Value);
                    int y = int.Parse(m.Groups[2].Value);
                    int z = int.Parse(m.Groups[3].Value);
                    string flag = m.Groups[4].Value;
                    int u = int.Parse(m.Groups[5].Value);
                    int v = int.Parse(m.Groups[6].Value);
                    string r = m.Groups[7].Value;
                    string g = m.Groups[8].Value;
                    string b = m.Groups[9].Value;
                    string a = m.Groups[10].Value;

                    switch (dlMode)
                    {
                        case 0: // Rainbow World (Vertex Colors)
                            // Spatial rainbow gradient based on vertex coordinates
                            double hue = (x + y + z) / 3000.0;
                            hue = hue - Math.Floor(hue);

                            // HSV/Hue to RGB conversion
                            double r_h = Math.Abs(hue * 6.0 - 3.0) - 1.0;
                            double g_h = 2.0 - Math.Abs(hue * 6.0 - 2.0);
                            double b_h = 2.0 - Math.Abs(hue * 6.0 - 4.0);

                            byte rv = (byte)(Math.Clamp(r_h, 0.0, 1.0) * 255.0);
                            byte gv = (byte)(Math.Clamp(g_h, 0.0, 1.0) * 255.0);
                            byte bv = (byte)(Math.Clamp(b_h, 0.0, 1.0) * 255.0);

                            r = $"0x{rv:X2}";
                            g = $"0x{gv:X2}";
                            b = $"0x{bv:X2}";
                            a = "0xFF";
                            break;

                        case 1: // Faceless Mario (No UVs)
                        case 8: // Faceless Mario V2 (Castle Glow & Crystal Water)
                            u = 0;
                            v = 0;
                            break;

                        case 2: // Normal Inversion
                            int nr = ChaosNumbers.ParseIntHexSafe(r);
                            int ng = ChaosNumbers.ParseIntHexSafe(g);
                            int nb = ChaosNumbers.ParseIntHexSafe(b);
                            r = $"0x{(byte)(-nr):X2}";
                            g = $"0x{(byte)(-ng):X2}";
                            b = $"0x{(byte)(-nb):X2}";
                            break;

                        case 3: // Texture Scrambler (UV Size)
                            double scaleUV = 1.5 + _random.NextDouble() * (intensity / 100.0) * 5.0;
                            u = (int)(u * scaleUV);
                            v = (int)(v * scaleUV);
                            break;

                        case 4: // Polygon Exploder
                            double explScale = 1.0 + (intensity / 50.0);
                            x += (int)(_random.Next(-400, 401) * explScale);
                            y += (int)(_random.Next(-400, 401) * explScale);
                            z += (int)(_random.Next(-400, 401) * explScale);
                            break;

                        case 5: // Voxelizer Snap
                            int snapUnit = (int)(50 + (intensity * 3.0));
                            if (snapUnit < 1) snapUnit = 1;
                            x = (x / snapUnit) * snapUnit;
                            y = (y / snapUnit) * snapUnit;
                            z = (z / snapUnit) * snapUnit;
                            break;

                        case 6: // UV Swap
                            int tempU = u;
                            u = v;
                            v = tempU;
                            u += (int)((_random.NextDouble() - 0.5) * intensity * 10.0);
                            v += (int)((_random.NextDouble() - 0.5) * intensity * 10.0);
                            break;

                        case 7: // Scale Distortion (Funhouse Wave)
                            double waveScale = (intensity / 100.0) * 1.5;
                            x += (int)(Math.Sin(y / 150.0) * 80.0 * waveScale);
                            y += (int)(Math.Cos(x / 150.0) * 80.0 * waveScale);
                            z += (int)(Math.Sin(x / 150.0) * 80.0 * waveScale);
                            break;
                    }

                    // Vtx positions and texture coordinates are signed 16-bit fields.
                    // Preserve the same low bits that the compiler previously kept
                    // when it warned about an overflowing integer initializer.
                    x = unchecked((short)x);
                    y = unchecked((short)y);
                    z = unchecked((short)z);
                    u = unchecked((short)u);
                    v = unchecked((short)v);
                    return $"{{{{{{{x}, {y}, {z}}}, {flag}, {{{u}, {v}}}, {{{r}, {g}, {b}, {a}}}}}}}";
                });

                if (dlMode == 0)
                {
                    // Inject clear lighting geometry mode inside Display Lists to achieve full Bowser boss level bright color glow
                    newContent = Regex.Replace(newContent, @"(static const Gfx [a-zA-Z0-9_]+\[\]\s*=\s*\{)", "$1\n    gsSPClearGeometryMode(G_LIGHTING),");
                }
                else if (dlMode == 1) // Faceless Mario (No UVs)
                {
                    // Enable Environment Mapping (Texture Coordinate Generation) to strip static UVs and force shifting textures/crystal reflections
                    newContent = Regex.Replace(newContent, @"(static const Gfx [a-zA-Z0-9_]+\[\]\s*=\s*\{)", "$1\n    gsSPSetGeometryMode(G_TEXTURE_GEN | G_TEXTURE_GEN_LINEAR),");
                }
                else if (dlMode == 8) // Faceless Mario V2 (Castle Glow & Crystal Water)
                {
                    // Enable Environment Mapping (Texture Coordinate Generation) to strip static UVs and force shifting textures/crystal reflections
                    newContent = Regex.Replace(newContent, @"(static const Gfx [a-zA-Z0-9_]+\[\]\s*=\s*\{)", "$1\n    gsSPSetGeometryMode(G_TEXTURE_GEN | G_TEXTURE_GEN_LINEAR),");
                    newContent = Regex.Replace(newContent, @"\bG_CC_MODULATERGB\b", "G_CC_FADEA");
                    newContent = Regex.Replace(newContent, @"\bG_CC_MODULATERGBA\b", "G_CC_FADEA");
                }
                else if (dlMode == 2) // Normal Inversion
                {
                    // Invert culling modes to draw the model inside-out
                    newContent = Regex.Replace(newContent, @"(static const Gfx [a-zA-Z0-9_]+\[\]\s*=\s*\{)", "$1\n    gsSPClearGeometryMode(G_CULL_BACK),\n    gsSPSetGeometryMode(G_CULL_FRONT),");
                }
                else if (dlMode == 3) // Texture Scrambler (UV Size)
                {
                    // Force a micro texture scale mapping to pixelate/tile textures extensively
                    newContent = Regex.Replace(newContent, @"(static const Gfx [a-zA-Z0-9_]+\[\]\s*=\s*\{)", "$1\n    gsSPTexture(0x0001, 0x0001, 0, G_TX_RENDERTILE, G_ON),");
                }
                else if (dlMode == 4) // Polygon Exploder
                {
                    // Clear both front and back culling so exploded faces are visible from all sides
                    newContent = Regex.Replace(newContent, @"(static const Gfx [a-zA-Z0-9_]+\[\]\s*=\s*\{)", "$1\n    gsSPClearGeometryMode(G_CULL_BOTH),");
                }
                else if (dlMode == 5) // Voxelizer Snap
                {
                    // Disable lighting and shading to enhance blocky retro voxel aesthetic
                    newContent = Regex.Replace(newContent, @"(static const Gfx [a-zA-Z0-9_]+\[\]\s*=\s*\{)", "$1\n    gsSPClearGeometryMode(G_LIGHTING),");
                }
                else if (dlMode == 6) // UV Swap
                {
                    // Force a fixed skewed texture coordinate translation scale
                    newContent = Regex.Replace(newContent, @"(static const Gfx [a-zA-Z0-9_]+\[\]\s*=\s*\{)", "$1\n    gsSPTexture(0x4000, 0x4000, 0, G_TX_RENDERTILE, G_ON),");
                }

                File.WriteAllText(filePath, newContent);
            }
            catch (Exception ex)
            {
                throw new IOException($"Error corrupting DL file {filePath}: {ex.Message}", ex);
            }
        }

}
