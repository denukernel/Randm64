using System.IO;
using System.Text.RegularExpressions;
using OpenTK.Mathematics;
using Sm64DecompLevelViewer.Models;
using static Sm64DecompLevelViewer.Services.Models.ModelSourcePatterns;
using static Sm64DecompLevelViewer.Services.Models.ModelParsingPrimitives;

namespace Sm64DecompLevelViewer.Services.Models;
internal sealed class ModelVertexParser
{
    public Dictionary<string, List<ModelVertex>> ParseVertexArrays(string content)
    {
        var vertexArrays = new Dictionary<string, List<ModelVertex>>();

        var arrayMatches = VtxArrayPattern.Matches(content);
        
        foreach (Match arrayMatch in arrayMatches)
        {
            string arrayName = arrayMatch.Groups[1].Value;
            int startIndex = arrayMatch.Index + arrayMatch.Length;
            
            // Find the closing brace
            int braceCount = 1;
            int endIndex = startIndex;
            for (int i = startIndex; i < content.Length && braceCount > 0; i++)
            {
                if (content[i] == '{') braceCount++;
                if (content[i] == '}') braceCount--;
                endIndex = i;
            }

            string arrayContent = content.Substring(startIndex, endIndex - startIndex);
            
            var vertices = new List<ModelVertex>();
            var vtxMatches = VtxDataPattern.Matches(arrayContent);
            
            foreach (Match vtxMatch in vtxMatches)
            {
                int x = int.Parse(vtxMatch.Groups[1].Value);
                int y = int.Parse(vtxMatch.Groups[2].Value);
                int z = int.Parse(vtxMatch.Groups[3].Value);
                int s = int.Parse(vtxMatch.Groups[4].Value);
                int t = int.Parse(vtxMatch.Groups[5].Value);
                byte nx = Convert.ToByte(vtxMatch.Groups[6].Value, 16);
                byte ny = Convert.ToByte(vtxMatch.Groups[7].Value, 16);
                byte nz = Convert.ToByte(vtxMatch.Groups[8].Value, 16);
                byte alpha = Convert.ToByte(vtxMatch.Groups[9].Value, 16);

                vertices.Add(new ModelVertex(x, y, z, s, t, nx, ny, nz, alpha));
            }

            if (vertices.Count > 0)
            {
                vertexArrays[arrayName] = vertices;
                Console.WriteLine($"  {arrayName}: {vertices.Count} vertices");
            }
        }

        var movtexMatches = MovtexArrayPattern.Matches(content);
        foreach (Match arrayMatch in movtexMatches)
        {
            string arrayName = arrayMatch.Groups[1].Value;
            int startIndex = arrayMatch.Index + arrayMatch.Length;
            
            int braceCount = 1;
            int endIndex = startIndex;
            for (int i = startIndex; i < content.Length && braceCount > 0; i++)
            {
                if (content[i] == '{') braceCount++;
                if (content[i] == '}') braceCount--;
                endIndex = i;
            }

            string arrayContent = content.Substring(startIndex, endIndex - startIndex);
            
            var vertices = new List<ModelVertex>();
            var trisMatches = MovtexTrisPattern.Matches(arrayContent);
            
            foreach (Match trisMatch in trisMatches)
            {
                int x = int.Parse(trisMatch.Groups[1].Value);
                int y = int.Parse(trisMatch.Groups[2].Value);
                int z = int.Parse(trisMatch.Groups[3].Value);
                int s = int.Parse(trisMatch.Groups[4].Value) * 1024;
                int t = int.Parse(trisMatch.Groups[5].Value) * 1024;

                vertices.Add(new ModelVertex(x, y, z, s, t, 0, 127, 0, 255));
            }

            if (vertices.Count == 0)
            {
                var rotTrisMatches = MovtexRotTrisPattern.Matches(arrayContent);
                foreach (Match rotMatch in rotTrisMatches)
                {
                    int x = int.Parse(rotMatch.Groups[1].Value);
                    int y = int.Parse(rotMatch.Groups[2].Value);
                    int z = int.Parse(rotMatch.Groups[3].Value);

                    vertices.Add(new ModelVertex(x, y, z, 0, 0, 0, 127, 0, 255));
                }
            }

            if (vertices.Count == 0)
            {
                var boxMatches = MovtexBoxTrisPattern.Matches(arrayContent);
                if (boxMatches.Count >= 4)
                {
                    for (int i = 0; i + 3 < boxMatches.Count; i += 4)
                    {
                        int x1 = int.Parse(boxMatches[i + 0].Groups[1].Value);
                        int z1 = int.Parse(boxMatches[i + 0].Groups[2].Value);
                        int x2 = int.Parse(boxMatches[i + 2].Groups[1].Value);
                        int z2 = int.Parse(boxMatches[i + 2].Groups[2].Value);

                        vertices.Add(new ModelVertex(x1, 0, z1, 0, 0, 0, 127, 0, 255));
                        vertices.Add(new ModelVertex(x1, 0, z2, 0, 2048, 0, 127, 0, 255));
                        vertices.Add(new ModelVertex(x2, 0, z2, 2048, 2048, 0, 127, 0, 255));
                        vertices.Add(new ModelVertex(x2, 0, z1, 2048, 0, 0, 127, 0, 255));
                    }
                }
            }

            if (vertices.Count > 0)
            {
                vertexArrays[arrayName] = vertices;
                Console.WriteLine($"  Movtex {arrayName}: {vertices.Count} vertices");
            }
        }

        return vertexArrays;
    }
}
