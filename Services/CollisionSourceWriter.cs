using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using OpenTK.Mathematics;
using Sm64DecompLevelViewer.Models;

namespace Sm64DecompLevelViewer.Services;

public static class CollisionSourceWriter
{
    public static Dictionary<string,byte[]> Stage(string path,CollisionMesh mesh)
    {
        if(mesh.Vertices.Count>32767 || mesh.Triangles.Any(t=>Math.Min(t.V1,Math.Min(t.V2,t.V3))<0 || Math.Max(t.V1,Math.Max(t.V2,t.V3))>=mesh.Vertices.Count)) throw new InvalidDataException("Invalid collision vertex count or triangle index.");
        if(mesh.Vertices.Any(v=>Math.Abs((long)v.X)>32767 || Math.Abs((long)v.Y)>32767 || Math.Abs((long)v.Z)>32767)) throw new InvalidDataException("Collision coordinates exceed signed 16-bit limits.");
        ValidateRuntimeBudget(path,mesh);
        string source=File.ReadAllText(path),active=ActorSourceReader.Read(path,preserveOffsets:true);
        CollisionSourceValidation.Validate(active,path);
        var calls=CSourceCalls.Read(active,n=>n.StartsWith("COL_",StringComparison.Ordinal));
        var init=calls.FirstOrDefault(c=>c.Name=="COL_INIT") ?? throw new InvalidDataException("COL_INIT is missing.");
        var end=calls.FirstOrDefault(c=>c.Name=="COL_END" && c.Start>init.Start) ?? throw new InvalidDataException("COL_END is missing.");
        calls=calls.Where(c=>c.Start>=init.Start && c.End<=end.End).ToList();
        var vertex=calls.FirstOrDefault(c=>c.Name=="COL_VERTEX_INIT") ?? throw new InvalidDataException("COL_VERTEX_INIT is missing.");
        var stop=calls.FirstOrDefault(c=>c.Name=="COL_TRI_STOP") ?? throw new InvalidDataException("COL_TRI_STOP is missing.");
        if(source[vertex.Start..stop.End].Contains('#')) throw new InvalidDataException("Collision contains regional or included geometry inside its vertex/triangle block. This edit cannot preserve those branches safely.");
        string newline=source.Contains("\r\n",StringComparison.Ordinal) ? "\r\n" : "\n";
        var geometry=new StringBuilder($"COL_VERTEX_INIT({mesh.Vertices.Count}),{newline}");
        foreach(var v in mesh.Vertices) geometry.Append($"    COL_VERTEX({v.X}, {v.Y}, {v.Z}),{newline}");
        for(int i=0;i<mesh.Triangles.Count;)
        {
            string surface=mesh.Triangles[i].SurfaceType;
            if(!Regex.IsMatch(surface,@"^[A-Za-z_]\w*$")) throw new InvalidDataException("Unsupported collision surface: "+surface);
            int last=i+1; while(last<mesh.Triangles.Count && mesh.Triangles[last].SurfaceType==surface) last++;
            geometry.Append($"    COL_TRI_INIT({surface}, {last-i}),{newline}");
            for(;i<last;i++)
            {
                var t=mesh.Triangles[i];
                if(CollisionSourceValidation.HasForce(surface)) geometry.Append($"    COL_TRI_SPECIAL({t.V1}, {t.V2}, {t.V3}, {t.RawSpecialParam ?? (t.SpecialParam ?? 0).ToString(CultureInfo.InvariantCulture)}),{newline}");
                else
                {
                    if(t.SpecialParam.HasValue || t.RawSpecialParam!=null) throw new InvalidDataException("Force parameters are not supported by surface "+surface);
                    geometry.Append($"    COL_TRI({t.V1}, {t.V2}, {t.V3}),{newline}");
                }
            }
        }
        geometry.Append("    COL_TRI_STOP()");
        var edits=new List<(int Start,int End,string Text)> { (vertex.Start,stop.End,geometry.ToString()) };
        var water=calls.Where(c=>c.Name is "COL_WATER_BOX_INIT" or "COL_WATER_BOX").ToList();
        var waterText=new StringBuilder();
        if(mesh.WaterBoxes.Count>0)
        {
            waterText.Append($"COL_WATER_BOX_INIT({mesh.WaterBoxes.Count}),{newline}");
            foreach(var box in mesh.WaterBoxes)
            {
                if(box.X1>box.X2 || box.Z1>box.Z2) throw new InvalidDataException("Water box minimum bounds exceed maximum bounds.");
                foreach(int value in new[]{box.Id,box.X1,box.Z1,box.X2,box.Z2,box.Y}) if(value<short.MinValue || value>short.MaxValue) throw new InvalidDataException("Water box exceeds signed 16-bit limits.");
                waterText.Append($"    COL_WATER_BOX({box.Id}, {box.X1}, {box.Z1}, {box.X2}, {box.Z2}, {box.Y}),{newline}");
            }
        }
        if(water.Count>0)
        {
            var first=water[0]; var last=water[^1]; int finish=last.End;
            if(finish<active.Length && active[finish]==',') finish++;
            edits.Add((first.Start,finish,waterText.ToString()));
        }
        else if(waterText.Length>0) edits.Add((end.Start,end.Start,waterText+"    "));
        var result=new StringBuilder(source);
        foreach(var edit in edits.OrderByDescending(e=>e.Start)) result.Remove(edit.Start,edit.End-edit.Start).Insert(edit.Start,edit.Text);
        string updated=result.ToString();
        // Validate the active branch of the staged text, preserving other regional arrays verbatim.
        string selected=active[..vertex.Start]+geometry+active[stop.End..];
        if(!source.Contains('#')) CollisionSourceValidation.Validate(updated,path);
        else CollisionSourceValidation.Validate(selected,path);
        var staged=new Dictionary<string,byte[]> { [path]=Encoding.UTF8.GetBytes(updated) };
        StageRooms(path,mesh,staged);
        return staged;
    }
    private static string Key(CollisionTriangle t)=>$"{t.V1},{t.V2},{t.V3}";
    private static void StageRooms(string collisionPath,CollisionMesh mesh,Dictionary<string,byte[]> staged)
    {
        string directory=Path.GetDirectoryName(collisionPath)!;
        string? path=new[]{Path.Combine(directory,"room.inc.c"),Path.Combine(directory,"rooms.inc.c")}.FirstOrDefault(File.Exists);
        if(path==null) return;
        string source=File.ReadAllText(path),masked=CSourceCalls.MaskComments(source);
        var array=Regex.Match(masked,@"\b(?:RoomData|s8|u8)\s+\w+\s*\[\s*\]\s*=\s*\{([^}]+)\}");
        if(!array.Success) throw new InvalidDataException("Unable to preserve the area room table: "+path);
        var tokens=array.Groups[1].Value.Split(',').Select(s=>s.Trim()).Where(s=>s.Length>0).ToList();
        if(tokens.Any(t=>!int.TryParse(t,NumberStyles.Integer,CultureInfo.InvariantCulture,out _))) throw new InvalidDataException("Unsupported room table expression: "+path);
        var old=new CollisionParser().ParseCollisionFile(collisionPath,"","nonpersistent") ?? throw new InvalidDataException("Unable to read original collision room order.");
        if(tokens.Count!=old.Triangles.Count) throw new InvalidDataException("Room table count does not match the original collision. Repair its room table before saving.");
        var lookup=new Dictionary<string,Queue<string>>();
        for(int i=0;i<old.Triangles.Count;i++)
        {
            string key=Key(old.Triangles[i]); if(!lookup.TryGetValue(key,out var queue)) lookup[key]=queue=new(); queue.Enqueue(tokens[i]);
        }
        var output=new StringBuilder("\n    ");
        for(int i=0;i<mesh.Triangles.Count;i++)
        {
            string room=lookup.TryGetValue(Key(mesh.Triangles[i]),out var queue) && queue.Count>0 ? queue.Dequeue() : "0";
            output.Append(room).Append(','); output.Append(i%16==15 ? "\n    " : " ");
        }
        output.Append('\n');
        staged[path]=Encoding.UTF8.GetBytes(source[..array.Groups[1].Index]+output+source[(array.Groups[1].Index+array.Groups[1].Length)..]);
    }
    public static void ValidateRuntimeBudget(string path,CollisionMesh mesh)
    {
        int surfaceLimit=2300,nodeLimit=7000;
        for(var directory=new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(path))!);directory!=null;directory=directory.Parent)
        {
            string loader=Path.Combine(directory.FullName,"src","engine","surface_load.c");
            if(!File.Exists(loader)) continue;
            string text=CSourceCalls.MaskComments(File.ReadAllText(loader));
            var surfaces=Regex.Match(text,@"sSurfacePoolSize\s*=\s*(\d+)"); if(surfaces.Success) surfaceLimit=int.Parse(surfaces.Groups[1].Value);
            var nodes=Regex.Match(text,@"sSurfaceNodePool\s*=\s*main_pool_alloc\(\s*(\d+)\s*\*"); if(nodes.Success) nodeLimit=int.Parse(nodes.Groups[1].Value);
            break;
        }
        int surfacesUsed=0,nodesUsed=0;
        int Lower(int value) { int coordinate=Math.Max(0,(int)unchecked((short)(value+8192))); return Math.Max(0,coordinate/1024-(coordinate%1024<50 ? 1 : 0)); }
        int Upper(int value) { int coordinate=Math.Max(0,(int)unchecked((short)(value+8192))); return Math.Min(15,coordinate/1024+(coordinate%1024>974 ? 1 : 0)); }
        foreach(var t in mesh.Triangles)
        {
            var a=mesh.Vertices[t.V1]; var b=mesh.Vertices[t.V2]; var c=mesh.Vertices[t.V3];
            if(Vector3.Cross(new Vector3(b.X-a.X,b.Y-a.Y,b.Z-a.Z),new Vector3(c.X-a.X,c.Y-a.Y,c.Z-a.Z)).LengthSquared<0.000001) continue;
            surfacesUsed++;
            int width=Math.Max(0,Upper(Math.Max(a.X,Math.Max(b.X,c.X)))-Lower(Math.Min(a.X,Math.Min(b.X,c.X)))+1);
            int depth=Math.Max(0,Upper(Math.Max(a.Z,Math.Max(b.Z,c.Z)))-Lower(Math.Min(a.Z,Math.Min(b.Z,c.Z)))+1);
            nodesUsed+=width*depth;
        }
        if(surfacesUsed>surfaceLimit || nodesUsed>nodeLimit) throw new InvalidDataException($"Collision exceeds the game's static pools: {surfacesUsed}/{surfaceLimit} surfaces, {nodesUsed}/{nodeLimit} spatial nodes. Simplify collision or import the model without collision.");
    }
}
