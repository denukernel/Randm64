using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Sm64DecompLevelViewer.Models;

namespace Sm64DecompLevelViewer.Services;

public class LevelMeshService
{
    public string? LastError { get; private set; }
    public bool SaveCollisionMesh(string filePath,CollisionMesh mesh)
    {
        try
        {
            var staged=CollisionSourceWriter.Stage(filePath,mesh);
            var originals=staged.Keys.ToDictionary(path=>path,path=>(byte[]?)File.ReadAllBytes(path));
            SourceFileTransaction.Commit(staged,originals);
            LastError=null; LogService.Log("Successfully saved validated collision mesh to "+filePath); return true;
        }
        catch(Exception ex) { LastError=ex.Message; LogService.Log("Collision save aborted: "+ex.Message); return false; }
    }
    public static string StageVisualMesh(string source,string arrayName,List<ModelVertex> vertices)
    {
        var original=new ModelParser().ParseVertexArrays(source);
        if(!original.TryGetValue(arrayName,out var old)) throw new InvalidDataException("Vertex array not found: "+arrayName);
        if(old.Count!=vertices.Count) throw new InvalidDataException("Changing the vertex count without updating display lists is unsafe. Import a replacement model to change topology.");
        if(vertices.Any(v=>v.X<short.MinValue || v.X>short.MaxValue || v.Y<short.MinValue || v.Y>short.MaxValue || v.Z<short.MinValue || v.Z>short.MaxValue || v.S<short.MinValue || v.S>short.MaxValue || v.T<short.MinValue || v.T>short.MaxValue))
            throw new InvalidDataException("Visual coordinates or UVs exceed signed 16-bit limits.");
        var match=Regex.Match(CSourceCalls.MaskComments(source),@"\bVtx\s+"+Regex.Escape(arrayName)+@"\[\]\s*=\s*\{");
        if(!match.Success) throw new InvalidDataException("Unable to find vertex array source: "+arrayName);
        int start=match.Index+match.Length,depth=1,end=start;
        string masked=CSourceCalls.MaskComments(source);
        while(end<masked.Length && depth>0) { if(masked[end]=='{') depth++; else if(masked[end]=='}') depth--; if(depth>0) end++; }
        if(depth!=0) throw new InvalidDataException("Unbalanced vertex array: "+arrayName);
        string newline=source.Contains("\r\n",StringComparison.Ordinal) ? "\r\n" : "\n";
        var output=new StringBuilder(newline);
        foreach(var v in vertices) output.Append("    {{{"+$"{v.X}, {v.Y}, {v.Z}"+"}, 0, {"+$"{v.S}, {v.T}"+"}, {"+$"0x{v.NX:x2}, 0x{v.NY:x2}, 0x{v.NZ:x2}, 0x{v.Alpha:x2}"+"}}},"+newline);
        return source[..start]+output+source[end..];
    }
    public bool SaveVisualMesh(string path,string arrayName,List<ModelVertex> vertices)
    {
        try
        {
            byte[] original=File.ReadAllBytes(path);
            string staged=StageVisualMesh(File.ReadAllText(path),arrayName,vertices);
            SourceFileTransaction.Commit(new Dictionary<string,byte[]>{[path]=Encoding.UTF8.GetBytes(staged)},new Dictionary<string,byte[]?>{[path]=original});
            LastError=null; return true;
        }
        catch(Exception ex) { LastError=ex.Message; LogService.Log("Visual save aborted: "+ex.Message); return false; }
    }
}
