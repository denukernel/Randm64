using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Sm64DecompLevelViewer.Services
{
    public class PaintingData
    {
        public string Name { get; set; } = string.Empty;
        public string Id { get; set; } = "0x0000";
        public string Pitch { get; set; } = "0.0f";
        public string Yaw { get; set; } = "0.0f";
        public string PosX { get; set; } = "0.0f";
        public string PosY { get; set; } = "0.0f";
        public string PosZ { get; set; } = "0.0f";
        public string Size { get; set; } = "614.0f";

        public string OriginalBlock { get; set; } = string.Empty;
    }

    public class PaintingService
    {
        private static readonly Regex PaintingBlockRegex = new(
            @"struct\s+Painting\s+(\w+)\s*=\s*\{([\s\S]*?)\};",
            RegexOptions.Compiled);

        private static readonly Regex IdRegex = new(@"(\/\*\s*id\s*\*\/)\s*([^\n,]+)", RegexOptions.Compiled);
        private static readonly Regex RotationRegex = new(@"(\/\*\s*Rotation\s*\*\/)\s*([^\n,]+)\s*,\s*([^\n,]+)", RegexOptions.Compiled);
        private static readonly Regex PositionRegex = new(@"(\/\*\s*Position\s*\*\/)\s*([^\n,]+)\s*,\s*([^\n,]+)\s*,\s*([^\n,]+)", RegexOptions.Compiled);
        private static readonly Regex SizeRegex = new(@"(\/\*\s*Size\s*\*\/)\s*([^\n,]+)", RegexOptions.Compiled);

        private static string CleanVal(string val)
        {
            return val.Trim().TrimEnd(',').Trim();
        }

        public string? FindPaintingFile(string levelPath)
        {
            // Search recursively for painting.inc.c
            if (!Directory.Exists(levelPath)) return null;

            var files = Directory.GetFiles(levelPath, "*painting*.inc.c", SearchOption.AllDirectories);
            return files.Length > 0 ? files[0] : null;
        }

        public List<PaintingData> LoadPaintings(string filePath)
        {
            var list = new List<PaintingData>();
            if (!File.Exists(filePath)) return list;

            string content = File.ReadAllText(filePath);
            var matches = PaintingBlockRegex.Matches(content);

            foreach (Match match in matches)
            {
                if (match.Success)
                {
                    string name = match.Groups[1].Value;
                    string body = match.Groups[2].Value;

                    var idMatch = IdRegex.Match(body);
                    var rotMatch = RotationRegex.Match(body);
                    var posMatch = PositionRegex.Match(body);
                    var sizeMatch = SizeRegex.Match(body);

                    if (idMatch.Success && rotMatch.Success && posMatch.Success && sizeMatch.Success)
                    {
                        list.Add(new PaintingData
                        {
                            Name = name,
                            Id = CleanVal(idMatch.Groups[2].Value),
                            Pitch = CleanVal(rotMatch.Groups[2].Value),
                            Yaw = CleanVal(rotMatch.Groups[3].Value),
                            PosX = CleanVal(posMatch.Groups[2].Value),
                            PosY = CleanVal(posMatch.Groups[3].Value),
                            PosZ = CleanVal(posMatch.Groups[4].Value),
                            Size = CleanVal(sizeMatch.Groups[2].Value),
                            OriginalBlock = match.Value
                        });
                    }
                }
            }

            return list;
        }

        public bool SavePaintings(string filePath, List<PaintingData> updatedList)
        {
            try
            {
                if (!File.Exists(filePath)) return false;

                string content = File.ReadAllText(filePath);

                foreach (var item in updatedList)
                {
                    // Construct updated block by replacing target fields in the original block string
                    string updatedBlock = item.OriginalBlock;

                    updatedBlock = IdRegex.Replace(updatedBlock, m => $"{m.Groups[1].Value} {item.Id}");
                    updatedBlock = RotationRegex.Replace(updatedBlock, m => $"{m.Groups[1].Value} {item.Pitch}, {item.Yaw}");
                    updatedBlock = PositionRegex.Replace(updatedBlock, m => $"{m.Groups[1].Value} {item.PosX}, {item.PosY}, {item.PosZ}");
                    updatedBlock = SizeRegex.Replace(updatedBlock, m => $"{m.Groups[1].Value} {item.Size}");

                    // Replace the original block in the file content
                    content = content.Replace(item.OriginalBlock, updatedBlock);
                }

                // Write with Unix line endings
                File.WriteAllText(filePath, content.Replace("\r\n", "\n"));
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving paintings: {ex.Message}");
                return false;
            }
        }
    }
}
