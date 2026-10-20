using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using OpenTK.Mathematics;
using Sm64DecompLevelViewer.Models;

namespace Sm64DecompLevelViewer.Services;

public sealed class EditableGeometry
{
    public List<Vector3> Vertices { get; } = new();
    public List<(int A,int B,int C)> Triangles { get; } = new();
    public List<Vector2> TextureCoordinates { get; } = new();
    public List<Vector4> VertexColors { get; } = new();
    public List<int> TriangleMaterials { get; } = new();
    public List<ImportedMaterial> Materials { get; } = new();
}

public static class LevelMeshCreation
{
    public static EditableGeometry Primitive(Vector3 center, float size, bool box)
    {
        if (!float.IsFinite(size) || size <= 0) throw new ArgumentException("Size must be positive.");
        var result = new EditableGeometry();
        float s = size/2;
        foreach (var p in new[]{new Vector3(-s,0,-s),new Vector3(s,0,-s),new Vector3(s,0,s),new Vector3(-s,0,s)}) result.Vertices.Add(center+p);
        result.Triangles.Add((0,2,1)); result.Triangles.Add((0,3,2));
        if (box)
        {
            for (int i=0;i<4;i++) result.Vertices.Add(result.Vertices[i]+Vector3.UnitY*size);
            result.Triangles.Clear();
            result.Triangles.AddRange(new[]{(4,6,5),(4,7,6),(0,1,2),(0,2,3),(0,4,5),(0,5,1),(1,5,6),(1,6,2),(2,6,7),(2,7,3),(3,7,4),(3,4,0)});
        }
        return result;
    }
    public static EditableGeometry ReadObj(string source, Vector3 offset, float scale=1)
    {
        var result = new EditableGeometry();
        foreach (string raw in source.Split('\n'))
        {
            var fields = raw.Split('#')[0].Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length == 0) continue;
            if (fields[0] == "v" && fields.Length >= 4)
                result.Vertices.Add(new Vector3(float.Parse(fields[1],CultureInfo.InvariantCulture),float.Parse(fields[2],CultureInfo.InvariantCulture),float.Parse(fields[3],CultureInfo.InvariantCulture))*scale+offset);
            if (fields[0] == "f" && fields.Length >= 4)
            {
                int Index(string field) { int i = int.Parse(field.Split('/')[0],CultureInfo.InvariantCulture); return i > 0 ? i-1 : result.Vertices.Count+i; }
                int first = Index(fields[1]);
                for (int i=2;i<fields.Length-1;i++) result.Triangles.Add((first,Index(fields[i]),Index(fields[i+1])));
            }
        }
        Validate(result);
        return result;
    }
    public static void Validate(EditableGeometry geometry)
    {
        if(geometry.TextureCoordinates.Count!=0 && geometry.TextureCoordinates.Count!=geometry.Vertices.Count ||
           geometry.VertexColors.Count!=0 && geometry.VertexColors.Count!=geometry.Vertices.Count ||
           geometry.TriangleMaterials.Count!=0 && geometry.TriangleMaterials.Count!=geometry.Triangles.Count)
            throw new ArgumentException("Model attributes do not match the geometry.");
        if(geometry.TriangleMaterials.Any(i=>i<0 || i>=geometry.Materials.Count)) throw new ArgumentException("Invalid material index.");
        if(geometry.Materials.Any(m=>m.Pixels!=null && (m.Width<=0 || m.Height<=0 || m.Pixels.Length!=m.Width*m.Height*4))) throw new ArgumentException("Invalid texture pixels.");
        if (geometry.Triangles.Count == 0 || geometry.Triangles.Count > 20000) throw new ArgumentException("Mesh must contain 1–20,000 triangles.");
        if (geometry.Vertices.Any(v => !float.IsFinite(v.X+v.Y+v.Z) || Math.Abs(v.X)>32767 || Math.Abs(v.Y)>32767 || Math.Abs(v.Z)>32767))
            throw new ArgumentException("Mesh coordinates must fit signed 16-bit SM64 vertices.");
        foreach (var (a,b,c) in geometry.Triangles)
            if (Math.Min(a,Math.Min(b,c))<0 || Math.Max(a,Math.Max(b,c))>=geometry.Vertices.Count) throw new ArgumentException("Invalid mesh triangle index.");
    }
    public static string Append(string modelPath, string geoPath, string collisionPath, EditableGeometry geometry, bool addCollision, string? projectRoot = null)
    {
        Validate(geometry);
        string model = File.ReadAllText(modelPath), geo = File.ReadAllText(geoPath);
        var camera = Regex.Match(geo,@"\bGEO_CAMERA\([^\r\n]*\),\s*GEO_OPEN_NODE\(\),");
        if (!camera.Success) throw new InvalidDataException("This area has no camera geometry node to attach a mesh.");
        string symbol = "randm64_mesh_" + Guid.NewGuid().ToString("N")[..12];
        var output=ImportedMeshWriter.Build(modelPath,projectRoot,geometry,symbol);
        string declarations=string.Join("\n",output.Lists.Select(list=>$"extern const Gfx {list.Symbol}[];"))+"\n";
        string calls=string.Join("",output.Lists.Select(list=>$"\n                    GEO_DISPLAY_LIST({list.Layer}, {list.Symbol}),"));
        var staged=new Dictionary<string,byte[]>(output.Assets)
        {
            [modelPath]=Encoding.UTF8.GetBytes(model+output.Source),
            [geoPath]=Encoding.UTF8.GetBytes(declarations+geo.Insert(camera.Index+camera.Length,calls))
        };
        int collisionStart=-1;
        if (addCollision)
        {
            if(geometry.Vertices.Any(v=>Math.Abs(v.X)>32500 || Math.Abs(v.Y)>32500 || Math.Abs(v.Z)>32500))
                throw new ArgumentException("Collision placement must stay within ±32500 level units.");
            string collision = File.ReadAllText(collisionPath);
            var parsed = new CollisionParser().ParseCollisionFile(collisionPath,"","nonpersistent") ?? throw new InvalidDataException("Collision file could not be parsed.");
            int start = parsed.Vertices.Count;
            collisionStart=start;
            if (start+geometry.Vertices.Count > 32767) throw new ArgumentException("Added collision exceeds the signed 16-bit vertex index limit.");
            foreach (var v in geometry.Vertices) parsed.Vertices.Add(new CollisionVertex((int)MathF.Round(v.X),(int)MathF.Round(v.Y),(int)MathF.Round(v.Z)));
            foreach (var (a,b,c) in geometry.Triangles) parsed.Triangles.Add(new CollisionTriangle(start+a,start+b,start+c,"SURFACE_DEFAULT"));
            foreach(var item in CollisionSourceWriter.Stage(collisionPath,parsed)) staged[item.Key]=item.Value;
        }
        string ownership=$"/* Imported collision: {symbol} {(collisionStart<0 ? "none" : collisionStart+" "+geometry.Vertices.Count)} */";
        staged[modelPath]=Encoding.UTF8.GetBytes(model+output.Source.Replace($"/* Imported level geometry: {symbol} */",$"/* Imported level geometry: {symbol} */\n{ownership}"));
        var originals = staged.Keys.ToDictionary(path => path, path => File.Exists(path) ? File.ReadAllBytes(path) : null);
        SourceFileTransaction.Commit(staged,originals);
        return symbol;
    }
}
