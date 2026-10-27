using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Linq;

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
        private readonly Dictionary<string, ProjectFileDocument> _documents = new();
        public string? LastError { get; private set; }
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
            return files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
        }

        public List<PaintingData> LoadPaintings(string filePath)
        {
            var list = new List<PaintingData>();
            if (!File.Exists(filePath)) return list;

            var document = ProjectFileDocument.Open(filePath);
            _documents[filePath] = document;
            string content = document.Text;
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

                LastError = null;
                if (!_documents.TryGetValue(filePath, out var document)) throw new InvalidOperationException("reload the painting source before saving");
                string content = document.Text;

                foreach (var item in updatedList)
                {
                    // Construct updated block by replacing target fields in the original block string
                    if (string.IsNullOrEmpty(item.OriginalBlock) || !content.Contains(item.OriginalBlock)) throw new InvalidOperationException("painting block no longer matches loaded source");
                    foreach (string value in new[] { item.Id, item.Pitch, item.Yaw, item.PosX, item.PosY, item.PosZ, item.Size })
                    {
                        if (!Regex.IsMatch(value, @"^(?:0[xX][0-9a-fA-F]+|[-+]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][-+]?\d+)?[fF]?)$")) throw new InvalidOperationException("painting fields must be finite c numeric literals");
                        if (!value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && (!double.TryParse(value.TrimEnd('f', 'F'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double number) || !double.IsFinite(number) || Math.Abs(number) > float.MaxValue)) throw new InvalidOperationException("painting value is outside the supported numeric range");
                    }
                    if (!double.TryParse(item.Size.TrimEnd('f', 'F'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double size) || !double.IsFinite(size) || size <= 0) throw new InvalidOperationException("painting size must be positive and finite");
                    string updatedBlock = item.OriginalBlock;

                    updatedBlock = UpdateFields(IdRegex, updatedBlock, item.Id);
                    updatedBlock = UpdateFields(RotationRegex, updatedBlock, item.Pitch, item.Yaw);
                    updatedBlock = UpdateFields(PositionRegex, updatedBlock, item.PosX, item.PosY, item.PosZ);
                    updatedBlock = UpdateFields(SizeRegex, updatedBlock, item.Size);

                    // Replace the original block in the file content
                    content = content.Replace(item.OriginalBlock, updatedBlock);
                }

                // Write with Unix line endings
                document.Save(content);
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                Console.WriteLine($"Error saving paintings: {ex.Message}");
                return false;
            }
        }
        private static string UpdateFields(Regex pattern, string source, params string[] values) => pattern.Replace(source, match =>
            values.Select((value, index) => CleanVal(match.Groups[index + 2].Value) == value).All(v => v)
                ? match.Value : match.Groups[1].Value + " " + string.Join(", ", values));
    }
}
