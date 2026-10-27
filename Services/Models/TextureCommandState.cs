using System.IO;
using System.Text.RegularExpressions;
using OpenTK.Mathematics;
using Sm64DecompLevelViewer.Models;

namespace Sm64DecompLevelViewer.Services.Models;
    internal sealed class TextureCommandState
    {
        private string? image, load, cached;
        private bool macro;
        private readonly Dictionary<string, string> tiles = new(), sizes = new();
        public Vector2 ImageOffset { get; private set; }

        public string SetImageAddress(string expression, int bitsPerTexel = 0, int width = 0)
        {
            ImageOffset = Vector2.Zero;
            var address = Regex.Match(expression.Trim(), @"^(?:\(\s*(?:const\s+)?(u8|s8|Texture|u16|s16|u32|s32)\s*\*\s*\)\s*)?(\w+)\s*(?:\+\s*(0x[\da-fA-F]+|\d+))?$");
            if (!address.Success) return expression.Trim();
            if (!address.Groups[3].Success) return address.Groups[2].Value;
            string offset = address.Groups[3].Value;
            bool hex = offset.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
            if (!int.TryParse(hex ? offset[2..] : offset, hex ? System.Globalization.NumberStyles.HexNumber : System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out int elements) || elements < 0 || bitsPerTexel <= 0 || width <= 0)
                return expression.Trim();
            int stride = address.Groups[1].Value is "u16" or "s16" ? 2 : address.Groups[1].Value is "u32" or "s32" ? 4 : 1;
            long bits = (long)elements * stride * 8;
            if (bits % bitsPerTexel != 0) return expression.Trim();
            long texels = bits / bitsPerTexel;
            ImageOffset = new Vector2(texels % width, texels / width);
            return address.Groups[2].Value;
        }
        public void Track(string type, string command)
        {
            command = command.TrimEnd().TrimEnd(',');
            if (type == "loadTexture") { image = null; load = command; macro = true; tiles.Clear(); sizes.Clear(); }
            else if (type == "texture") { image = command; load = null; macro = false; }
            else if (type == "tile")
            {
                var args = command[(command.IndexOf('(') + 1)..command.LastIndexOf(')')].Split(',');
                if (args.Length > 4) tiles[args[4].Trim()] = command;
            }
            else if (type == "textureRestore")
            {
                if (command.StartsWith("gsDPSetTileSize"))
                    sizes[command[(command.IndexOf('(') + 1)..].Split(',')[0].Trim()] = command;
                else { load = command; macro = false; }
            }
            else return;
            cached = null;
        }
        public string? Snapshot()
        {
            if (load == null) return null;
            if (cached != null) return cached;
            var commands = new List<string> { "gsDPPipeSync()" };
            if (image != null) commands.Add(image);
            if (!macro) { commands.AddRange(tiles.Values); commands.Add("gsDPLoadSync()"); }
            commands.Add(load);
            commands.Add("gsDPPipeSync()");
            commands.AddRange(tiles.Values);
            commands.AddRange(sizes.Values);
            return cached = string.Join(",\n    ", commands) + ",\n    ";
        }
    }
