using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Sm64DecompLevelViewer.Services.Chaos;

internal sealed class MarioColorCorruptor
{
    private readonly Random _random;
    public MarioColorCorruptor(Random random) { _random = random; }
        internal void CorruptMarioColorsFile(string filePath, int targetArea, int marioColorsMode)
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
                bool modified = false;

                // Pattern 1: gdSPDefLights1 macro
                var regexGdSP = new Regex(@"(static\s+const\s+)?(Lights1)\s+(\w+)\s*=\s*gdSPDefLights1\s*\(\s*([0-9a-fA-FxX\s,\-\+]+)\s*\)\s*;", RegexOptions.Singleline);
                
                // Pattern 2: Light_t / Lights1 brace structs
                var regexStruct = new Regex(@"(static\s+const\s+)?(Light_t|Lights1)\s+(\w+)\s*=\s*\{([^{}]*\{[^{}]*\}[^{}]*)*\};", RegexOptions.Singleline);

                string newContent = regexGdSP.Replace(content, m =>
                {
                    string structName = m.Groups[3].Value.ToLower();
                    string argsStr = m.Groups[4].Value;
                    string[] args = argsStr.Split(',').Select(a => a.Trim()).ToArray();

                    if (args.Length < 6) return m.Value;

                    bool shouldRandomize = false;
                    switch (targetArea)
                    {
                        case 0: shouldRandomize = true; break;
                        case 1: if (structName.Contains("red")) shouldRandomize = true; break;
                        case 2: if (structName.Contains("blue")) shouldRandomize = true; break;
                        case 3: if (structName.Contains("white")) shouldRandomize = true; break;
                        case 4: if (structName.Contains("brown")) shouldRandomize = true; break;
                        case 5: if (structName.Contains("skin")) shouldRandomize = true; break;
                        case 6: if (structName.Contains("yellow")) shouldRandomize = true; break;
                    }

                    if (!shouldRandomize) return m.Value;

                    modified = true;

                    int avR = 0, avG = 0, avB = 0;
                    int dvR = 0, dvG = 0, dvB = 0;

                    if (marioColorsMode == 1) // Invert Original Colors
                    {
                        avR = 255 - ChaosNumbers.ParseIntHexSafe(args[0]);
                        avG = 255 - ChaosNumbers.ParseIntHexSafe(args[1]);
                        avB = 255 - ChaosNumbers.ParseIntHexSafe(args[2]);
                        dvR = 255 - ChaosNumbers.ParseIntHexSafe(args[3]);
                        dvG = 255 - ChaosNumbers.ParseIntHexSafe(args[4]);
                        dvB = 255 - ChaosNumbers.ParseIntHexSafe(args[5]);
                    }
                    else if (marioColorsMode == 2) // Channel Swap (Hue Shift)
                    {
                        avR = ChaosNumbers.ParseIntHexSafe(args[1]);
                        avG = ChaosNumbers.ParseIntHexSafe(args[2]);
                        avB = ChaosNumbers.ParseIntHexSafe(args[0]);
                        dvR = ChaosNumbers.ParseIntHexSafe(args[4]);
                        dvG = ChaosNumbers.ParseIntHexSafe(args[5]);
                        dvB = ChaosNumbers.ParseIntHexSafe(args[3]);
                    }
                    else if (marioColorsMode == 3) // Psychedelic Neon Glow
                    {
                        dvR = _random.Next(0, 2) * 255;
                        dvG = _random.Next(0, 2) * 255;
                        dvB = _random.Next(0, 2) * 255;
                        if (dvR == 0 && dvG == 0 && dvB == 0) dvR = 255;

                        avR = dvG;
                        avG = dvB;
                        avB = dvR;
                    }
                    else // Full Shaded Random (Mode 0)
                    {
                        dvR = _random.Next(0, 256);
                        dvG = _random.Next(0, 256);
                        dvB = _random.Next(0, 256);

                        avR = dvR / 2;
                        avG = dvG / 2;
                        avB = dvB / 2;
                    }

                    string remaining = string.Join(", ", args.Skip(6));
                    if (!string.IsNullOrEmpty(remaining)) remaining = ", " + remaining;

                    return $"{m.Groups[1].Value}{m.Groups[2].Value} {m.Groups[3].Value} = gdSPDefLights1(\n    0x{avR:X2}, 0x{avG:X2}, 0x{avB:X2},\n    0x{dvR:X2}, 0x{dvG:X2}, 0x{dvB:X2}{remaining}\n);";
                });

                newContent = regexStruct.Replace(newContent, m =>
                {
                    string structName = m.Groups[3].Value.ToLower();
                    string structBody = m.Value;

                    bool shouldRandomize = false;
                    switch (targetArea)
                    {
                        case 0: shouldRandomize = true; break;
                        case 1: if (structName.Contains("red")) shouldRandomize = true; break;
                        case 2: if (structName.Contains("blue")) shouldRandomize = true; break;
                        case 3: if (structName.Contains("white")) shouldRandomize = true; break;
                        case 4: if (structName.Contains("brown")) shouldRandomize = true; break;
                        case 5: if (structName.Contains("skin")) shouldRandomize = true; break;
                        case 6: if (structName.Contains("yellow")) shouldRandomize = true; break;
                    }

                    if (!shouldRandomize) return m.Value;

                    modified = true;

                    byte rv = 0, gv = 0, bv = 0;
                    byte ambR = 0, ambG = 0, ambB = 0;

                    if (marioColorsMode == 1) // Invert Original Colors
                    {
                        var colorRegex = new Regex(@"\{\s*(0x[0-9a-fA-F]+|\d+)\s*,\s*(0x[0-9a-fA-F]+|\d+)\s*,\s*(0x[0-9a-fA-F]+|\d+)\s*\}");
                        var matches = colorRegex.Matches(structBody);
                        if (matches.Count >= 1)
                        {
                            int origR = ChaosNumbers.ParseIntHexSafe(matches[0].Groups[1].Value);
                            int origG = ChaosNumbers.ParseIntHexSafe(matches[0].Groups[2].Value);
                            int origB = ChaosNumbers.ParseIntHexSafe(matches[0].Groups[3].Value);

                            rv = (byte)(255 - origR);
                            gv = (byte)(255 - origG);
                            bv = (byte)(255 - origB);

                            ambR = (byte)(rv / 4);
                            ambG = (byte)(gv / 4);
                            ambB = (byte)(bv / 4);
                        }
                        else return m.Value;
                    }
                    else if (marioColorsMode == 2) // Channel Swap (Hue Shift)
                    {
                        var colorRegex = new Regex(@"\{\s*(0x[0-9a-fA-F]+|\d+)\s*,\s*(0x[0-9a-fA-F]+|\d+)\s*,\s*(0x[0-9a-fA-F]+|\d+)\s*\}");
                        var matches = colorRegex.Matches(structBody);
                        if (matches.Count >= 1)
                        {
                            int origR = ChaosNumbers.ParseIntHexSafe(matches[0].Groups[1].Value);
                            int origG = ChaosNumbers.ParseIntHexSafe(matches[0].Groups[2].Value);
                            int origB = ChaosNumbers.ParseIntHexSafe(matches[0].Groups[3].Value);

                            rv = (byte)origG;
                            gv = (byte)origB;
                            bv = (byte)origR;

                            ambR = (byte)(rv / 4);
                            ambG = (byte)(gv / 4);
                            ambB = (byte)(bv / 4);
                        }
                        else return m.Value;
                    }
                    else if (marioColorsMode == 3) // Psychedelic Neon Glow
                    {
                        rv = (byte)(_random.Next(0, 2) * 255);
                        gv = (byte)(_random.Next(0, 2) * 255);
                        bv = (byte)(_random.Next(0, 2) * 255);
                        if (rv == 0 && gv == 0 && bv == 0) rv = 255;

                        ambR = gv;
                        ambG = bv;
                        ambB = rv;
                    }
                    else // Full Shaded Random (Mode 0)
                    {
                        rv = (byte)_random.Next(0, 256);
                        gv = (byte)_random.Next(0, 256);
                        bv = (byte)_random.Next(0, 256);

                        ambR = (byte)(rv / 4);
                        ambG = (byte)(gv / 4);
                        ambB = (byte)(bv / 4);
                    }

                    var repColorRegex = new Regex(@"\{\s*(0x[0-9a-fA-F]+|\d+)\s*,\s*(0x[0-9a-fA-F]+|\d+)\s*,\s*(0x[0-9a-fA-F]+|\d+)\s*\}");
                    int matchCount = 0;
                    return repColorRegex.Replace(structBody, cm =>
                    {
                        matchCount++;
                        if (matchCount <= 2)
                        {
                            return $"{{ 0x{rv:X2}, 0x{gv:X2}, 0x{bv:X2} }}";
                        }
                        else
                        {
                            return $"{{ 0x{ambR:X2}, 0x{ambG:X2}, 0x{ambB:X2} }}";
                        }
                    });
                });

                if (modified)
                {
                    File.WriteAllText(filePath, newContent);
                }
            }
            catch (Exception ex)
            {
                throw new IOException($"Error corrupting Mario colors in {filePath}: {ex.Message}", ex);
            }
        }
}
