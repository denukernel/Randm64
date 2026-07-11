using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Sm64DecompLevelViewer.Services
{
    public class SplinePoint
    {
        public int Index { get; set; }
        public int Speed { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Z { get; set; }

        public override string ToString()
        {
            return $"Point {Index} (Speed: {Speed}) -> ({X}, {Y}, {Z})";
        }
    }

    public class CutsceneService
    {
        private static readonly Regex SplinePointRegex = new Regex(
            @"\{\s*(-?\d+)\s*,\s*(-?\d+)\s*,\s*\{\s*(-?\d+)\s*,\s*(-?\d+)\s*,\s*(-?\d+)\s*\}\s*\}",
            RegexOptions.Compiled
        );

        public List<SplinePoint> LoadSpline(string cameraCPath, string splineName)
        {
            var points = new List<SplinePoint>();

            try
            {
                if (!File.Exists(cameraCPath))
                {
                    Console.WriteLine($"camera.c file not found: {cameraCPath}");
                    return points;
                }

                string content = File.ReadAllText(cameraCPath);

                // Find array definition start
                string searchHeader = $"struct CutsceneSplinePoint {splineName}[]";
                int arrayStart = content.IndexOf(searchHeader);
                if (arrayStart == -1)
                {
                    Console.WriteLine($"Spline {splineName} not found in camera.c");
                    return points;
                }

                int openingBrace = content.IndexOf('{', arrayStart);
                if (openingBrace == -1) return points;

                int closingBrace = content.IndexOf("};", openingBrace);
                if (closingBrace == -1) return points;

                string arrayBody = content.Substring(openingBrace + 1, closingBrace - openingBrace - 1);
                var matches = SplinePointRegex.Matches(arrayBody);

                foreach (Match m in matches)
                {
                    points.Add(new SplinePoint
                    {
                        Index = int.Parse(m.Groups[1].Value),
                        Speed = int.Parse(m.Groups[2].Value),
                        X = int.Parse(m.Groups[3].Value),
                        Y = int.Parse(m.Groups[4].Value),
                        Z = int.Parse(m.Groups[5].Value)
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading spline {splineName}: {ex.Message}");
            }

            return points;
        }

        public void SaveSpline(string cameraCPath, string splineName, List<SplinePoint> points)
        {
            try
            {
                if (!File.Exists(cameraCPath)) return;

                string content = File.ReadAllText(cameraCPath);

                string searchHeader = $"struct CutsceneSplinePoint {splineName}[]";
                int arrayStart = content.IndexOf(searchHeader);
                if (arrayStart == -1) return;

                int closingBrace = content.IndexOf("};", arrayStart);
                if (closingBrace == -1) return;

                // Build the new C array string representation
                var sb = new StringBuilder();
                sb.AppendLine($"struct CutsceneSplinePoint {splineName}[] = {{");
                for (int i = 0; i < points.Count; i++)
                {
                    var p = points[i];
                    sb.Append($"    {{ {p.Index}, {p.Speed}, {{ {p.X}, {p.Y}, {p.Z} }} }}");
                    if (i < points.Count - 1)
                        sb.AppendLine(",");
                    else
                        sb.AppendLine();
                }
                sb.Append("};");

                string targetText = content.Substring(arrayStart, (closingBrace + 2) - arrayStart);
                string updatedContent = content.Replace(targetText, sb.ToString());

                File.WriteAllText(cameraCPath, updatedContent);
                Console.WriteLine($"Successfully saved spline {splineName} to {cameraCPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving spline {splineName}: {ex.Message}");
            }
        }
    }
}
