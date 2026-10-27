using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Sm64DecompLevelViewer.Services.Chaos;

internal sealed class HudCorruptor
{
    private readonly Random _random;
    public HudCorruptor(Random random) { _random = random; }
        internal void CorruptHudFile(string filePath, double intensity, int hudMode)
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

                switch (hudMode)
                {
                    case 0: // 1. Position Scramble
                        int newTopY = _random.Next(30, 220);
                        content = Regex.Replace(content, @"#define HUD_TOP_Y \d+", $"#define HUD_TOP_Y {newTopY}");
                        
                        content = Regex.Replace(content, @"print_text\(([^,]+),[ \t]*([^,]+),[ \t]*(""[^""]+"")\)", m => {
                            if (_random.NextDouble() < rate)
                            {
                                int dx = _random.Next(-60, 61);
                                int dy = _random.Next(-40, 41);
                                return $"print_text(({m.Groups[1].Value}) + {dx}, ({m.Groups[2].Value}) + {dy}, {m.Groups[3].Value})";
                            }
                            return m.Value;
                        });
                        break;

                    case 1: // 2. Glyph Corruption
                        string[] glyphs = { ",", "+", "*", "-", "$", "/" };
                        content = Regex.Replace(content, @"print_text\(([^,]+),[ \t]*([^,]+),[ \t]*""([^""]+)""\)", m => {
                            if (_random.NextDouble() < rate)
                            {
                                string textVal = m.Groups[3].Value;
                                var sb = new StringBuilder();
                                foreach (char c in textVal)
                                {
                                    if (glyphs.Contains(c.ToString()))
                                    {
                                        sb.Append(glyphs[_random.Next(0, glyphs.Length)]);
                                    }
                                    else
                                    {
                                        sb.Append(c);
                                    }
                                }
                                return $"print_text({m.Groups[1].Value}, {m.Groups[2].Value}, \"{sb.ToString()}\")";
                            }
                            return m.Value;
                        });
                        break;

                    case 2: // 3. Int Format Break
                        content = Regex.Replace(content, @"""%d""", m => {
                            if (_random.NextDouble() < rate)
                            {
                                string[] formats = { "\"%x\"", "\"%o\"", "\"CRASH%d\"", "\"%d?\"", "\"???\"" };
                                return formats[_random.Next(0, formats.Length)];
                            }
                            return m.Value;
                        });
                        break;

                    case 3: // 4. Health Meter Warp
                        content = Regex.Replace(content, @"guTranslate\(mtx,[ \t]*\(f32\) sPowerMeterHUD\.x,[ \t]*\(f32\) sPowerMeterHUD\.y,[ \t]*0\)", m => {
                            int dx = _random.Next(-80, 81);
                            int dy = _random.Next(-60, 61);
                            return $"guTranslate(mtx, (f32) sPowerMeterHUD.x + {dx}, (f32) sPowerMeterHUD.y + {dy}, 0)";
                        });
                        
                        content = Regex.Replace(content, @"\(\*healthLUT\)\[numHealthWedges - 1\]", m => {
                            int offset = _random.Next(0, 8);
                            return $"(*healthLUT)[(numHealthWedges - 1 + {offset}) % 8]";
                        });
                        break;

                    case 4: // 5. Strobe Stutter
                        content = Regex.Replace(content, @"void render_hud\(([^)]*)\)[ \t]*\{", m => {
                            return m.Value + "\n    if (gGlobalTimer & 4) return;";
                        });
                        content = Regex.Replace(content, @"void render_hud_mario_lives\(void\)[ \t]*\{", m => {
                            return m.Value + "\n    if (gGlobalTimer & 8) return;";
                        });
                        content = Regex.Replace(content, @"void render_hud_coins\(void\)[ \t]*\{", m => {
                            return m.Value + "\n    if (gGlobalTimer & 8) return;";
                        });
                        break;
                }

                File.WriteAllText(filePath, content);
            }
            catch (Exception ex)
            {
                throw new IOException($"Error corrupting HUD file {filePath}: {ex.Message}", ex);
            }
        }
}
