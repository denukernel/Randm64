using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Sm64DecompLevelViewer.Services.Chaos;

internal sealed class CollisionCorruptor
{
    private readonly Random _random;
    public CollisionCorruptor(Random random) { _random = random; }
        internal void CorruptCollisionFile(string filePath, double intensity, int mode)
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
                double rate = (intensity / 100.0); // max rate at 100%

                if (mode == 7 || mode == 8)
                {
                    // Vertex warping mode
                    var regexVertex = new Regex(@"COL_VERTEX\(\s*(-?\d+)\s*,\s*(-?\d+)\s*,\s*(-?\d+)\s*\)");
                    content = regexVertex.Replace(content, m =>
                    {
                        if (_random.NextDouble() >= rate) return m.Value;

                        int x = int.Parse(m.Groups[1].Value);
                        int y = int.Parse(m.Groups[2].Value);
                        int z = int.Parse(m.Groups[3].Value);

                        if (mode == 7) // Surface Noise / Jitter
                        {
                            x += _random.Next(-150, 151);
                            y += _random.Next(-150, 151);
                            z += _random.Next(-150, 151);
                        }
                        else if (mode == 8) // Giant Slopes
                        {
                            y = (int)(y * 2.5);
                        }

                        // Clamp coordinates to fit in N64 signed 16-bit short limits [-32768, 32767]
                        // preventing IDO compiler integer out-of-bounds/overflow warnings or errors.
                        x = Math.Clamp(x, -32500, 32500);
                        y = Math.Clamp(y, -32500, 32500);
                        z = Math.Clamp(z, -32500, 32500);

                        return $"COL_VERTEX({x}, {y}, {z})";
                    });
                }
                else
                {
                    // Surface type customization modes
                    var regexTriInit = new Regex(@"COL_TRI_INIT\(\s*([A-Za-z0-9_]+)\s*,\s*(\d+)\s*\)");
                    string[] chaosPool = {
                        "SURFACE_DEFAULT", "SURFACE_BURNING", "SURFACE_VERY_SLIPPERY",
                        "SURFACE_NOT_SLIPPERY", "SURFACE_DEATH_PLANE", "SURFACE_VERTICAL_WIND",
                        "SURFACE_ICE", "SURFACE_SHALLOW_QUICKSAND"
                    };

                    bool IsSpecialSurface(string name)
                    {
                        return name == "SURFACE_0004" ||
                               name == "SURFACE_FLOWING_WATER" ||
                               name == "SURFACE_DEEP_MOVING_QUICKSAND" ||
                               name == "SURFACE_SHALLOW_MOVING_QUICKSAND" ||
                               name == "SURFACE_MOVING_QUICKSAND" ||
                               name == "SURFACE_HORIZONTAL_WIND" ||
                               name == "SURFACE_INSTANT_MOVING_QUICKSAND";
                    }

                    content = regexTriInit.Replace(content, m =>
                    {
                        if (_random.NextDouble() >= rate) return m.Value;

                        string originalSurface = m.Groups[1].Value;
                        string count = m.Groups[2].Value;

                        // CRITICAL: Special force surfaces expect 4 parameters per triangle instead of 3.
                        // We must NEVER swap between special and non-special surfaces, as that causes
                        // loader offset shifting and instant game crashes.
                        if (IsSpecialSurface(originalSurface))
                        {
                            string[] specialPool = {
                                "SURFACE_FLOWING_WATER", "SURFACE_DEEP_MOVING_QUICKSAND",
                                "SURFACE_SHALLOW_MOVING_QUICKSAND", "SURFACE_MOVING_QUICKSAND",
                                "SURFACE_HORIZONTAL_WIND", "SURFACE_INSTANT_MOVING_QUICKSAND"
                            };
                            string targetSpecial = specialPool[_random.Next(specialPool.Length)];
                            return $"COL_TRI_INIT({targetSpecial}, {count})";
                        }

                        string targetSurface = originalSurface;
                        switch (mode)
                        {
                            case 0: targetSurface = "SURFACE_VERY_SLIPPERY"; break;
                            case 1: targetSurface = "SURFACE_BURNING"; break;
                            case 2: targetSurface = "SURFACE_DEATH_PLANE"; break;
                            case 3: targetSurface = "SURFACE_DEEP_QUICKSAND"; break;
                            case 4: targetSurface = "SURFACE_NOT_SLIPPERY"; break;
                            case 5: targetSurface = "SURFACE_VERTICAL_WIND"; break;
                            case 6: targetSurface = "SURFACE_QUICKSAND"; break;
                            case 9: targetSurface = chaosPool[_random.Next(chaosPool.Length)]; break;
                        }

                        return $"COL_TRI_INIT({targetSurface}, {count})";
                    });
                }

                File.WriteAllText(filePath, content);
            }
            catch (Exception ex)
            {
                throw new IOException($"Error corrupting collision file {filePath}: {ex.Message}", ex);
            }
        }
}
