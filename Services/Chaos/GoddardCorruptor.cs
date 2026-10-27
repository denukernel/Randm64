using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Sm64DecompLevelViewer.Services.Chaos;

internal sealed class GoddardCorruptor
{
    private readonly Random _random;
    public GoddardCorruptor(Random random) { _random = random; }
        internal void CorruptGoddardFile(string filePath, double intensity, int goddardMode)
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
                double rate = (intensity / 500.0);

                // Matches 3D vertex coordinate arrays: { x, y, z }
                var regex = new Regex(@"\{\s*(-?\d+),\s*(-?\d+),\s*(-?\d+)\s*\}");

                int vtxCounter = 0;
                string newContent = regex.Replace(content, m =>
                {
                    vtxCounter++;
                    if (_random.NextDouble() >= rate) return m.Value;

                     int x = int.Parse(m.Groups[1].Value);
                     int y = int.Parse(m.Groups[2].Value);
                     int z = int.Parse(m.Groups[3].Value);

                     switch (goddardMode)
                     {
                         case 0: // 1. Face Jitter (Vibration)
                             x += _random.Next(-40, 41);
                             y += _random.Next(-40, 41);
                             z += _random.Next(-40, 41);
                             break;

                         case 1: // 2. Face Stretch (Meltdown)
                             y = (int)(y * (1.5 + _random.NextDouble() * 3.0));
                             break;

                         case 2: // 3. Face Squash
                             // Add a unique offset to prevent Z-plane coordinate collapse
                             z = (int)(z * 0.1) + (vtxCounter % 7 - 3);
                             break;

                         case 3: // 4. Vertex Exploder
                             x += _random.Next(-400, 401);
                             y += _random.Next(-400, 401);
                             z += _random.Next(-400, 401);
                             break;

                         case 4: // 5. Mirror Face (Invert X)
                             x = -x;
                             break;

                         case 5: // 6. Low Poly Voxelizer
                             // Add 3D grid sub-voxel offsets so nearby vertices never share coordinates
                             x = (x / 60) * 60 + (vtxCounter % 7 - 3);
                             y = (y / 60) * 60 + ((vtxCounter / 7) % 7 - 3);
                             z = (z / 60) * 60 + ((vtxCounter / 49) % 7 - 3);
                             break;

                         case 6: // 7. Static Noise
                             x = _random.Next(-1000, 1001);
                             y = _random.Next(-1000, 1001);
                             z = _random.Next(-1000, 1001);
                             break;

                         case 7: // 8. Facial Tremor
                             if (Math.Abs(z) > 500)
                             {
                                 z += _random.Next(-300, 301);
                             }
                             break;

                         case 8: // 9. Mouth/Eye Warp
                             if (Math.Abs(x) > 150)
                             {
                                 x = (int)(x * 2.2);
                             }
                             if (Math.Abs(y) > 150)
                             {
                                 y = (int)(y * 2.2);
                             }
                             break;

                         case 9: // 10. Melt Face (Zero Coords)
                             // Distribute vertices within a tiny grid near zero so they don't overlap
                             x = (vtxCounter % 11 - 5) * 2;
                             y = ((vtxCounter / 11) % 11 - 5) * 2;
                             z = ((vtxCounter / 121) % 11 - 5) * 2;
                             break;
                     }

                     return $"{{ {x}, {y}, {z} }}";
                 });

                File.WriteAllText(filePath, newContent);
            }
            catch (Exception ex)
            {
                throw new IOException($"Error corrupting Goddard file {filePath}: {ex.Message}", ex);
            }
        }
}
