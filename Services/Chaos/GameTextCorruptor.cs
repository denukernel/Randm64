using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Sm64DecompLevelViewer.Services.Chaos;

internal sealed class GameTextCorruptor
{
        private static readonly string[] EscapeKeywords = { "wait", "this", "world", "You", "Power", "I", "keep", "all", "col" };
    private readonly Random _random;
    public GameTextCorruptor(Random random) { _random = random; }
        internal string RandomizeGameText(string content, int textMode)
        {
            int index = 0;
            StringBuilder sb = new StringBuilder();

            while (index < content.Length)
            {
                if (index < content.Length - 3 && content[index] == '_' && content[index + 1] == '(' && content[index + 2] == '"')
                {
                    // Found start of _("
                    sb.Append("_(\"");
                    index += 3;

                    bool inString = true;
                    StringBuilder strSegment = new StringBuilder();
                    
                    while (index < content.Length)
                    {
                        if (inString && content[index] == '"' && index + 1 < content.Length && content[index + 1] == ')')
                        {
                            // Found end of _("...")
                            // Corrupt the last collected string segment
                            sb.Append(CorruptTextString(strSegment.ToString(), textMode));
                            sb.Append("\")");
                            index += 2;
                            break;
                        }
                        
                        if (content[index] == '"')
                        {
                            // Check if it is escaped (e.g. \")
                            bool escaped = false;
                            int backslashCount = 0;
                            int k = index - 1;
                            while (k >= 0 && content[k] == '\\')
                            {
                                backslashCount++;
                                k--;
                            }
                            if (backslashCount % 2 != 0)
                            {
                                escaped = true;
                            }
                            
                            if (!escaped)
                            {
                                // Toggle inString
                                if (inString)
                                {
                                    // We were inside, now we go outside a string segment
                                    sb.Append(CorruptTextString(strSegment.ToString(), textMode));
                                    strSegment.Clear();
                                }
                                inString = !inString;
                                sb.Append('"');
                                index++;
                                continue;
                            }
                        }
                        
                        if (inString)
                        {
                            strSegment.Append(content[index]);
                        }
                        else
                        {
                            sb.Append(content[index]);
                        }
                        index++;
                    }
                }
                else
                {
                    sb.Append(content[index]);
                    index++;
                }
            }

            return sb.ToString();
        }
        internal int GetEscapeLength(string str, int index)
        {
            if (str[index] != '\\') return 0;
            if (index + 1 >= str.Length) return 1;

            char next = str[index + 1];
            if (next == 'n' || next == '"' || next == '\\')
            {
                return 2;
            }

            foreach (var kw in EscapeKeywords)
            {
                string target = kw + "\\";
                if (index + 1 + target.Length <= str.Length && str.Substring(index + 1, target.Length) == target)
                {
                    return 1 + target.Length;
                }
            }

            return 2;
        }
        internal string CorruptTextString(string str, int textMode)
        {
            // Build a list of characters that are safe to corrupt
            // Safe characters are letters and numbers.
            // We preserve punctuation, spaces, newlines, backslashes, and [B]/[A] button prompts.
            List<int> safeIndices = new List<int>();
            List<char> safeChars = new List<char>();

            for (int i = 0; i < str.Length; i++)
            {
                char c = str[i];
                
                // Skip [ ... ] blocks completely (these are button tokens like [B], [A])
                if (c == '[')
                {
                    while (i < str.Length && str[i] != ']')
                    {
                        i++;
                    }
                    continue;
                }

                // Skip backslash escape sequences
                if (c == '\\')
                {
                    int len = GetEscapeLength(str, i);
                    i += len - 1;
                    continue;
                }

                // Check if it's a letter or digit
                if (char.IsLetterOrDigit(c))
                {
                    safeIndices.Add(i);
                    safeChars.Add(c);
                }
            }

            if (safeIndices.Count == 0) return str;

            char[] chars = str.ToCharArray();

            switch (textMode)
            {
                case 0: // Scramble Characters
                    int n = safeChars.Count;
                    while (n > 1)
                    {
                        n--;
                        int k = _random.Next(n + 1);
                        char temp = safeChars[k];
                        safeChars[k] = safeChars[n];
                        safeChars[n] = temp;
                    }
                    for (int i = 0; i < safeIndices.Count; i++)
                    {
                        chars[safeIndices[i]] = safeChars[i];
                    }
                    break;

                case 1: // Word Shuffle
                    var words = new List<string>();
                    int start = -1;
                    for (int i = 0; i < str.Length; i++)
                    {
                        if (str[i] == '[')
                        {
                            start = -1;
                            while (i < str.Length && str[i] != ']')
                            {
                                i++;
                            }
                            continue;
                        }

                        if (str[i] == '\\')
                        {
                            start = -1;
                            int len = GetEscapeLength(str, i);
                            i += len - 1;
                            continue;
                        }

                        if (char.IsLetterOrDigit(str[i]))
                        {
                            if (start == -1) start = i;
                        }
                        else
                        {
                            if (start != -1)
                            {
                                words.Add(str.Substring(start, i - start));
                                start = -1;
                            }
                        }
                    }
                    if (start != -1)
                    {
                        words.Add(str.Substring(start, str.Length - start));
                    }

                    if (words.Count > 1)
                    {
                        int wN = words.Count;
                        while (wN > 1)
                        {
                            wN--;
                            int k = _random.Next(wN + 1);
                            string temp = words[k];
                            words[k] = words[wN];
                            words[wN] = temp;
                        }
                    }

                    StringBuilder wSb = new StringBuilder();
                    int wCount = 0;
                    start = -1;
                    for (int i = 0; i < str.Length; i++)
                    {
                        if (str[i] == '[')
                        {
                            start = -1;
                            while (i < str.Length && str[i] != ']')
                            {
                                wSb.Append(str[i]);
                                i++;
                            }
                            if (i < str.Length) wSb.Append(str[i]);
                            continue;
                        }

                        if (str[i] == '\\')
                        {
                            start = -1;
                            int len = GetEscapeLength(str, i);
                            for (int k = 0; k < len; k++)
                            {
                                wSb.Append(str[i + k]);
                            }
                            i += len - 1;
                            continue;
                        }

                        if (char.IsLetterOrDigit(str[i]))
                        {
                            if (start == -1)
                            {
                                start = i;
                                if (wCount < words.Count)
                                {
                                    wSb.Append(words[wCount++]);
                                }
                            }
                        }
                        else
                        {
                            start = -1;
                            wSb.Append(str[i]);
                        }
                    }
                    return wSb.ToString();

                case 2: // Leet Speak
                    for (int i = 0; i < safeIndices.Count; i++)
                    {
                        int idx = safeIndices[i];
                        char orig = char.ToUpper(chars[idx]);
                        switch (orig)
                        {
                            case 'E': chars[idx] = '3'; break;
                            case 'A': chars[idx] = '4'; break;
                            case 'T': chars[idx] = '7'; break;
                            case 'O': chars[idx] = '0'; break;
                            case 'I': chars[idx] = '1'; break;
                            case 'S': chars[idx] = '5'; break;
                            case 'G': chars[idx] = '9'; break;
                            case 'B': chars[idx] = '8'; break;
                        }
                    }
                    break;

                case 3: // Alien/Glitch Symbols
                    // Restricted to compile-safe characters supported in standard charmap.txt
                    char[] glitchSymbols = new char[] { '?', '!', '.', ',', '-', '\'', '(', ')', '~', 'A', 'B', '0', '1', '2' };
                    for (int i = 0; i < safeIndices.Count; i++)
                    {
                        int idx = safeIndices[i];
                        chars[idx] = glitchSymbols[_random.Next(glitchSymbols.Length)];
                    }
                    break;

                case 4: // Mocking Case
                    bool uppercase = true;
                    for (int i = 0; i < safeIndices.Count; i++)
                    {
                        int idx = safeIndices[i];
                        char orig = chars[idx];
                        // Keep full-width letters uppercase to avoid compilation errors due to unmapped lowercase full-width letters in SM64 charmap
                        if (orig >= 0xFF21 && orig <= 0xFF3A)
                        {
                            chars[idx] = orig;
                        }
                        else
                        {
                            chars[idx] = uppercase ? char.ToUpper(orig) : char.ToLower(orig);
                            uppercase = !uppercase;
                        }
                    }
                    break;
            }

            return new string(chars);
        }
}
