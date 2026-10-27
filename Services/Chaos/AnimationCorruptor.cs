using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Sm64DecompLevelViewer.Services.Chaos;

internal sealed class AnimationCorruptor
{
    private readonly Random _random;
    public AnimationCorruptor(Random random) { _random = random; }
        internal void CorruptAnimationFile(string filePath, double intensity, int animMode)
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
                
                // Matches the values array definition: static const s16 anim_xxxx_values[] = { ... };
                var regex = new Regex(@"(static const s16 anim_[a-zA-Z0-9_]+_values\[\]\s*=\s*\{)([\s\S]*?)(\};)");
                var match = regex.Match(content);
                if (!match.Success) return;

                string header = match.Groups[1].Value;
                string valuesText = match.Groups[2].Value;
                string footer = match.Groups[3].Value;

                // Split values by comma
                string[] tokens = valuesText.Split(',');
                var parsedValues = new List<short>();

                foreach (var token in tokens)
                {
                    string trimmed = token.Trim();
                    if (string.IsNullOrEmpty(trimmed)) continue;

                    short val = 0;
                    try
                    {
                        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                        {
                            val = (short)Convert.ToInt32(trimmed, 16);
                        }
                        else if (trimmed.StartsWith("-0x", StringComparison.OrdinalIgnoreCase))
                        {
                            val = (short)(-1 * Convert.ToInt32(trimmed.Substring(1), 16));
                        }
                        else
                        {
                            val = short.Parse(trimmed);
                        }
                    }
                    catch
                    {
                        continue;
                    }

                    parsedValues.Add(val);
                }

                if (parsedValues.Count == 0) return;

                // Apply corruption logic
                double rate = (intensity / 500.0); // max rate 1.0 at 500%
                int changesCount = (int)(parsedValues.Count * rate * 0.1); // subtle change per animation to keep it recognizable but glitched
                if (changesCount <= 0) changesCount = 1;

                switch (animMode)
                {
                    case 0: // 1. Joint Jitter
                        for (int i = 0; i < changesCount; i++)
                        {
                            int idx = _random.Next(0, parsedValues.Count);
                            parsedValues[idx] = (short)(parsedValues[idx] + _random.Next(-300, 301));
                        }
                        break;

                    case 1: // 2. Joint Stretch
                        double scale = 1.5 + (_random.NextDouble() * 3.0);
                        for (int i = 0; i < changesCount; i++)
                        {
                            int idx = _random.Next(0, parsedValues.Count);
                            parsedValues[idx] = (short)Math.Clamp(parsedValues[idx] * scale, short.MinValue, short.MaxValue);
                        }
                        break;

                    case 2: // 3. Angle Invert
                        for (int i = 0; i < changesCount; i++)
                        {
                            int idx = _random.Next(0, parsedValues.Count);
                            parsedValues[idx] = (short)(-parsedValues[idx]);
                        }
                        break;

                    case 3: // 4. Stutter Step
                        int stutterLen = _random.Next(2, 6);
                        for (int i = 0; i < changesCount; i++)
                        {
                            int startIdx = _random.Next(0, Math.Max(1, parsedValues.Count - stutterLen));
                            short valToRepeat = parsedValues[startIdx];
                            for (int k = 0; k < stutterLen && startIdx + k < parsedValues.Count; k++)
                            {
                                parsedValues[startIdx + k] = valToRepeat;
                            }
                        }
                        break;

                    case 4: // 5. Pose Freeze (T-Pose)
                        for (int i = 0; i < changesCount; i++)
                        {
                            int idx = _random.Next(0, parsedValues.Count);
                            parsedValues[idx] = 0;
                        }
                        break;

                    case 5: // 6. Extreme Twist
                        for (int i = 0; i < changesCount; i++)
                        {
                            int idx = _random.Next(0, parsedValues.Count);
                            parsedValues[idx] = (short)(_random.NextDouble() < 0.5 ? short.MinValue : short.MaxValue);
                        }
                        break;

                    case 6: // 7. Frame Scrambler (Shuffle indices/values)
                        for (int i = 0; i < changesCount; i++)
                        {
                            int idx1 = _random.Next(0, parsedValues.Count);
                            int idx2 = _random.Next(0, parsedValues.Count);
                            short temp = parsedValues[idx1];
                            parsedValues[idx1] = parsedValues[idx2];
                            parsedValues[idx2] = temp;
                        }
                        break;

                    case 7: // 8. Offset Detach (Limbs)
                        for (int i = 0; i < changesCount; i++)
                        {
                            int idx = _random.Next(0, parsedValues.Count);
                            if (Math.Abs(parsedValues[idx]) > 1000)
                            {
                                parsedValues[idx] = (short)Math.Clamp(parsedValues[idx] + _random.Next(-4000, 4001), short.MinValue, short.MaxValue);
                            }
                        }
                        break;
                }

                // Format values back into C arrays
                var sb = new StringBuilder();
                sb.AppendLine();
                for (int i = 0; i < parsedValues.Count; i++)
                {
                    if (i % 8 == 0) sb.Append("    ");
                    
                    ushort uval = (ushort)parsedValues[i];
                    sb.Append($"0x{uval:X4}");

                    if (i < parsedValues.Count - 1)
                    {
                        sb.Append(", ");
                    }
                    
                    if (i % 8 == 7 && i < parsedValues.Count - 1)
                    {
                        sb.AppendLine();
                    }
                }
                sb.AppendLine();
                sb.Append("    ");

                string newContent = regex.Replace(content, match.Result($"$1{sb.ToString()}$3"));
                File.WriteAllText(filePath, newContent);
            }
            catch (Exception ex)
            {
                throw new IOException($"Error corrupting animation {filePath}: {ex.Message}", ex);
            }
        }
}
