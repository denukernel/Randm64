using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Sm64DecompLevelViewer.Services.Chaos;

internal sealed class SourceCorruptor
{
    private readonly Random _random;
    public SourceCorruptor(Random random) { _random = random; }
        internal void CorruptTextSourceFile(string filePath, double intensity, int mode)
        {
            string content = File.ReadAllText(filePath);
            
            // Regex to find only bracketed coordinate groups like { x, y, z } or { x, y }
            var coordRegex = new Regex(@"\{[ \t]*(-?[0-9]+)[ \t]*,[ \t]*(-?[0-9]+)(?:[ \t]*,[ \t]*(-?[0-9]+))?[ \t]*\}");

            string newContent = coordRegex.Replace(content, match =>
            {
                // Corrupt with probability proportional to intensity
                if (_random.NextDouble() * 100.0 > intensity * 0.2)
                {
                    return match.Value;
                }

                string g1 = match.Groups[1].Value;
                string g2 = match.Groups[2].Value;
                bool hasG3 = match.Groups[3].Success;
                string g3 = hasG3 ? match.Groups[3].Value : "";

                if (int.TryParse(g1, out int x) && int.TryParse(g2, out int y))
                {
                    int z = 0;
                    if (hasG3) int.TryParse(g3, out z);

                    // Apply corruption logic
                    switch (mode)
                    {
                        case 0: // Swap / Negate sign
                            x = -x;
                            y = -y;
                            if (hasG3) z = -z;
                            break;
                        case 1: // Add random offset (vertex stretching!)
                            int range = (int)(intensity * 4);
                            x += _random.Next(-range, range + 1);
                            y += _random.Next(-range, range + 1);
                            if (hasG3) z += _random.Next(-range, range + 1);
                            break;
                        case 2: // XOR mask
                            int mask = _random.Next(255);
                            x ^= mask;
                            y ^= mask;
                            if (hasG3) z ^= mask;
                            break;
                        case 3: // Scale / Shift
                            double scale = 0.5 + (_random.NextDouble() * 1.5);
                            x = (int)(x * scale);
                            y = (int)(y * scale);
                            if (hasG3) z = (int)(z * scale);
                            break;
                        case 4: // Zero out
                            x = 0;
                            y = 0;
                            if (hasG3) z = 0;
                            break;
                        case 5: // Arithmetic Addition / Uniform Offset
                            int tx = _random.Next(-500, 501);
                            int ty = _random.Next(-500, 501);
                            int tz = _random.Next(-500, 501);
                            x += tx;
                            y += ty;
                            if (hasG3) z += tz;
                            break;
                    }

                    if (hasG3)
                    {
                        return $"{{ {x}, {y}, {z} }}";
                    }
                    else
                    {
                        return $"{{ {x}, {y} }}";
                    }
                }

                return match.Value;
            });

            // Also search and corrupt hex literals that look like color codes/flags inside curly brackets
            // e.g. {0x1b, 0x89, 0xe1, 0xff}
            var hexRegex = new Regex(@"\{[ \t]*(0x[0-9a-fA-F]+)[ \t]*,[ \t]*(0x[0-9a-fA-F]+)[ \t]*,[ \t]*(0x[0-9a-fA-F]+)[ \t]*,[ \t]*(0x[0-9a-fA-F]+)[ \t]*\}");
            
            newContent = hexRegex.Replace(newContent, match =>
            {
                if (_random.NextDouble() * 100.0 > intensity * 0.2)
                {
                    return match.Value;
                }

                try
                {
                    uint c1 = Convert.ToUInt32(match.Groups[1].Value.Substring(2), 16);
                    uint c2 = Convert.ToUInt32(match.Groups[2].Value.Substring(2), 16);
                    uint c3 = Convert.ToUInt32(match.Groups[3].Value.Substring(2), 16);
                    uint c4 = Convert.ToUInt32(match.Groups[4].Value.Substring(2), 16);

                    c1 = (uint)((c1 + _random.Next(50)) % 256);
                    c2 = (uint)((c2 + _random.Next(50)) % 256);
                    c3 = (uint)((c3 + _random.Next(50)) % 256);

                    return $"{{0x{c1:X2}, 0x{c2:X2}, 0x{c3:X2}, 0x{c4:X2}}}";
                }
                catch { return match.Value; }
            });

            File.WriteAllText(filePath, newContent);
        }
}
