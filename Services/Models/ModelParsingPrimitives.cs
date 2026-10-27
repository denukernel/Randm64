using System.IO;
using System.Text.RegularExpressions;
using OpenTK.Mathematics;
using Sm64DecompLevelViewer.Models;

namespace Sm64DecompLevelViewer.Services.Models;
internal static class ModelParsingPrimitives
{
    internal static void ApplyTransform(List<ModelVertex> vertices, Matrix4 transform)
    {
        for (int i = 0; i < vertices.Count; i++)
        {
            vertices[i] = ApplySingleTransform(vertices[i], transform);
        }
        
        Console.WriteLine($"Applied transformation to {vertices.Count} vertices");
    }
    internal static ModelVertex ApplySingleTransform(ModelVertex vertex, Matrix4 transform)
    {
        var pos = new Vector3(vertex.X, vertex.Y, vertex.Z);
        var transformed = Vector3.TransformPosition(pos, transform);
        
        return new ModelVertex(
            (int)transformed.X,
            (int)transformed.Y,
            (int)transformed.Z,
            vertex.S,
            vertex.T,
            vertex.NX,
            vertex.NY,
            vertex.NZ,
            vertex.Alpha
        )
        {
            JointIndex = vertex.JointIndex,
            SourceArrayName = vertex.SourceArrayName,
            SourceArrayIndex = vertex.SourceArrayIndex,
            SourceTransform = vertex.SourceTransform * transform,
            SourceUvScale = vertex.SourceUvScale,
            RefX = vertex.RefX,
            RefY = vertex.RefY,
            RefZ = vertex.RefZ
        };
    }
    internal static byte ParseByteHelper(string str)
    {
        if (str.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return Convert.ToByte(str, 16);
        return byte.Parse(str);
    }
}
