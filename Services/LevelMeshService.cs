using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Sm64DecompLevelViewer.Models;

namespace Sm64DecompLevelViewer.Services
{
    public class LevelMeshService
    {
        public bool SaveCollisionMesh(string filePath, CollisionMesh mesh)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    LogService.Log($"Collision file not found for saving: {filePath}");
                    return false;
                }

                string content = File.ReadAllText(filePath);

                int vertexInitIndex = content.IndexOf("COL_VERTEX_INIT");
                int triStopIndex = content.IndexOf("COL_TRI_STOP");

                if (vertexInitIndex == -1 || triStopIndex == -1)
                {
                    LogService.Log("Could not find collision vertex block or triangle stop block markers.");
                    return false;
                }

                // Slice starting exactly after the COL_TRI_STOP(), token to preserve the rest of the line
                int sliceStart = content.IndexOf("COL_TRI_STOP()", triStopIndex);
                if (sliceStart != -1)
                {
                    sliceStart += "COL_TRI_STOP()".Length;
                    if (sliceStart < content.Length && content[sliceStart] == ',')
                    {
                        sliceStart++;
                    }
                }
                else
                {
                    // Fallback to end of line if formatting is different
                    sliceStart = content.IndexOf("\n", triStopIndex);
                    if (sliceStart != -1) sliceStart++;
                    else sliceStart = triStopIndex + 12;
                }

                // Generate new collision geometry block
                var sb = new StringBuilder();
                sb.AppendLine($"COL_VERTEX_INIT({mesh.Vertices.Count}),");
                
                foreach (var v in mesh.Vertices)
                {
                    int clampedX = Math.Clamp(v.X, -32500, 32500);
                    int clampedY = Math.Clamp(v.Y, -32500, 32500);
                    int clampedZ = Math.Clamp(v.Z, -32500, 32500);
                    sb.AppendLine($"    COL_VERTEX({clampedX}, {clampedY}, {clampedZ}),");
                }

                // Group triangles by SurfaceType
                var groupedTriangles = mesh.Triangles
                    .GroupBy(t => t.SurfaceType)
                    .OrderBy(g => g.Key);

                foreach (var group in groupedTriangles)
                {
                    bool isSpecial = IsSpecialSurface(group.Key);
                    sb.AppendLine($"    COL_TRI_INIT({group.Key}, {group.Count()}),");
                    foreach (var tri in group)
                    {
                        if (isSpecial)
                        {
                            int param = tri.SpecialParam ?? 0;
                            sb.AppendLine($"    COL_TRI_SPECIAL({tri.V1}, {tri.V2}, {tri.V3}, 0x{param:X}),");
                        }
                        else
                        {
                            sb.AppendLine($"    COL_TRI({tri.V1}, {tri.V2}, {tri.V3}),");
                        }
                    }
                }

                sb.Append("    COL_TRI_STOP(),");

                // Replace the block
                string before = content.Substring(0, vertexInitIndex);
                string after = content.Substring(sliceStart);

                // Strip existing water boxes from after block
                string cleanedAfter = Regex.Replace(after, @"\s*COL_WATER_BOX_INIT\(\d+\),?", "");
                cleanedAfter = Regex.Replace(cleanedAfter, @"\s*COL_WATER_BOX\([^)]+\),?", "");

                // Generate new water box block
                var waterSb = new StringBuilder();
                if (mesh.WaterBoxes != null && mesh.WaterBoxes.Count > 0)
                {
                    waterSb.AppendLine();
                    waterSb.AppendLine($"    COL_WATER_BOX_INIT({mesh.WaterBoxes.Count}),");
                    foreach (var wb in mesh.WaterBoxes)
                    {
                        waterSb.AppendLine($"    COL_WATER_BOX({wb.Id}, {wb.X1}, {wb.Z1}, {wb.X2}, {wb.Z2}, {wb.Y}),");
                    }
                }

                // Insert right before COL_END() if found
                int colEndIndex = cleanedAfter.IndexOf("COL_END()");
                if (colEndIndex != -1)
                {
                    cleanedAfter = cleanedAfter.Substring(0, colEndIndex) + waterSb.ToString() + "    " + cleanedAfter.Substring(colEndIndex);
                }
                else
                {
                    cleanedAfter = cleanedAfter + waterSb.ToString();
                }

                string newContent = before + sb.ToString() + cleanedAfter;

                File.WriteAllText(filePath, newContent.Replace("\r\n", "\n"));
                LogService.Log($"Successfully saved collision mesh to {filePath}");
                return true;
            }
            catch (Exception ex)
            {
                LogService.Log($"Error saving collision mesh: {ex.Message}");
                return false;
            }
        }

        public bool SaveVisualMesh(string filePath, string arrayName, List<ModelVertex> vertices)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    LogService.Log($"Visual model file not found for saving: {filePath}");
                    return false;
                }

                string content = File.ReadAllText(filePath);

                var pattern = new Regex(@"static\s+const\s+Vtx\s+" + Regex.Escape(arrayName) + @"\[\]\s*=\s*\{", RegexOptions.Compiled);
                var match = pattern.Match(content);

                if (!match.Success)
                {
                    LogService.Log($"Could not find vertex array '{arrayName}' in visual file.");
                    return false;
                }

                int startReplaceIndex = match.Index + match.Length;

                // Find closing brace of the Vtx array declaration
                int braceCount = 1;
                int pos = startReplaceIndex;
                int endReplaceIndex = -1;

                while (pos < content.Length)
                {
                    if (content[pos] == '{') braceCount++;
                    else if (content[pos] == '}')
                    {
                        braceCount--;
                        if (braceCount == 0)
                        {
                            endReplaceIndex = pos;
                            break;
                        }
                    }
                    pos++;
                }

                if (endReplaceIndex == -1)
                {
                    LogService.Log($"Unbalanced braces in vertex array '{arrayName}'.");
                    return false;
                }

                // Generate new visual vertices text block
                var sb = new StringBuilder();
                sb.AppendLine();
                foreach (var v in vertices)
                {
                    sb.AppendLine($"    {{{{{{  {v.X,5},   {v.Y,5},   {v.Z,5}}}, 0, {{  {v.S,5},   {v.T,5}}}, {{0x{v.NX:x2}, 0x{v.NY:x2}, 0x{v.NZ:x2}, 0x{v.Alpha:x2}}}}}}},");
                }
                sb.Append(" ");

                // Replace in content
                string before = content.Substring(0, startReplaceIndex);
                string after = content.Substring(endReplaceIndex);
                string newContent = before + sb.ToString() + after;

                File.WriteAllText(filePath, newContent.Replace("\r\n", "\n"));
                LogService.Log($"Successfully saved visual vertex array '{arrayName}' to {filePath}");
                return true;
            }
            catch (Exception ex)
            {
                LogService.Log($"Error saving visual mesh array '{arrayName}': {ex.Message}");
                return false;
            }
        }

        private static bool IsSpecialSurface(string name)
        {
            return name == "SURFACE_0004" ||
                   name == "SURFACE_FLOWING_WATER" ||
                   name == "SURFACE_DEEP_MOVING_QUICKSAND" ||
                   name == "SURFACE_SHALLOW_MOVING_QUICKSAND" ||
                   name == "SURFACE_MOVING_QUICKSAND" ||
                   name == "SURFACE_HORIZONTAL_WIND" ||
                   name == "SURFACE_INSTANT_MOVING_QUICKSAND";
        }
    }
}
