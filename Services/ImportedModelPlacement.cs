using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using OpenTK.Mathematics;
using Sm64DecompLevelViewer.Models;

namespace Sm64DecompLevelViewer.Services;

public sealed record ImportedModelEntry(string Symbol,string ModelPath)
{
    public override string ToString()=>Symbol;
}

public static class ImportedModelPlacement
{
    public static List<ImportedModelEntry> Find(IEnumerable<string> modelFiles) => modelFiles.SelectMany(path=>
        Regex.Matches(File.ReadAllText(path),@"/\* Imported level geometry: (randm64_mesh_[0-9a-f]+) \*/")
        .Select(m=>new ImportedModelEntry(m.Groups[1].Value,path))).ToList();

    public static EditableGeometry Read(ImportedModelEntry entry,string projectRoot)
    {
        string source=File.ReadAllText(entry.ModelPath);
        var lists=Regex.Matches(source,@"const\s+Gfx\s+("+Regex.Escape(entry.Symbol)+@"_m\d+)\[\]");
        var result=new EditableGeometry();
        foreach(Match list in lists)
        {
            var parser=new ModelParser();
            var mesh=parser.ParseModelFile(entry.ModelPath,"","",displayListInstances:new(){new(){DisplayListName=list.Groups[1].Value}})
                ?? throw new InvalidDataException("Unable to read imported model.");
            parser.ParseActorTextureMapping(Path.GetDirectoryName(entry.ModelPath)!,entry.ModelPath,mesh,projectRoot);
            if(mesh.Triangles.Any(t=>t.TextureName!=null && (!mesh.TexturePaths.TryGetValue(t.TextureName,out string? path) || !File.Exists(path))))
                throw new FileNotFoundException("An imported model texture is missing. Restore its PNG before assembling the model.");
            var part=ModelPlacementService.FromVisual(mesh);
            int vertex=result.Vertices.Count,material=result.Materials.Count;
            result.Vertices.AddRange(part.Vertices); result.VertexColors.AddRange(part.VertexColors); result.TextureCoordinates.AddRange(part.TextureCoordinates);
            result.Triangles.AddRange(part.Triangles.Select(t=>(t.A+vertex,t.B+vertex,t.C+vertex)));
            result.TriangleMaterials.AddRange(part.TriangleMaterials.Select(i=>i+material)); result.Materials.AddRange(part.Materials);
        }
        LevelMeshCreation.Validate(result); return result;
    }

    public static (EditableGeometry Local,Vector3 Pivot) AtBase(EditableGeometry model)
    {
        var min=model.Vertices.Aggregate(Vector3.ComponentMin); var max=model.Vertices.Aggregate(Vector3.ComponentMax);
        var pivot=new Vector3((min.X+max.X)/2,min.Y,(min.Z+max.Z)/2);
        return (ModelImportService.Transform(model,-pivot,Vector3.One,Vector3.Zero),pivot);
    }

    private static (int,int,int) Position(Vector3 p)=>((int)MathF.Round(p.X),(int)MathF.Round(p.Y),(int)MathF.Round(p.Z));
    private static string TriangleKey(IEnumerable<(int,int,int)> points)=>string.Join(";",points.OrderBy(p=>p.Item1).ThenBy(p=>p.Item2).ThenBy(p=>p.Item3));
    public static HashSet<int> CollisionTriangles(EditableGeometry model,CollisionMesh collision,ImportedModelEntry? entry=null)
    {
        int? start=null,end=null;
        if(entry!=null)
        {
            var ownership=Regex.Match(File.ReadAllText(entry.ModelPath),@"/\* Imported collision: "+Regex.Escape(entry.Symbol)+@" (none|\d+ \d+) \*/");
            if(ownership.Success)
            {
                if(ownership.Groups[1].Value=="none") return new();
                var range=ownership.Groups[1].Value.Split(' '); start=int.Parse(range[0]); end=start+int.Parse(range[1]);
                if(end>collision.Vertices.Count) throw new InvalidDataException("Imported collision indices changed. Restore its matching collision before moving the model.");
            }
        }
        var keys=model.Triangles.GroupBy(t=>TriangleKey(new[]{Position(model.Vertices[t.A]),Position(model.Vertices[t.B]),Position(model.Vertices[t.C])}))
            .ToDictionary(g=>g.Key,g=>g.Count());
        var matches=new HashSet<int>();
        var matchedCounts=new Dictionary<string,int>();
        for(int i=0;i<collision.Triangles.Count;i++)
        {
            var t=collision.Triangles[i];
            if(start.HasValue && new[]{t.V1,t.V2,t.V3}.Any(v=>v<start || v>=end)) continue;
            var key=TriangleKey(new[]{t.V1,t.V2,t.V3}.Select(v=> { var p=collision.Vertices[v]; return (p.X,p.Y,p.Z); }));
            if(keys.ContainsKey(key)) { matches.Add(i); matchedCounts[key]=matchedCounts.GetValueOrDefault(key)+1; }
        }
        if(!start.HasValue && entry!=null && matches.Count>0)
        {
            string source=File.ReadAllText(entry.ModelPath);
            var arrays=new ModelParser().ParseVertexArrays(source);
            // Older versions appended collision in the same order as their generated C model blocks.
            // Separate identical copies by their independent vertex ranges, and require a complete match.
            foreach(Match known in Regex.Matches(source,@"/\* Imported collision: \w+ (\d+) (\d+) \*/"))
            {
                int first=int.Parse(known.Groups[1].Value),last=first+int.Parse(known.Groups[2].Value);
                matches.RemoveWhere(i=> { var t=collision.Triangles[i]; return new[]{t.V1,t.V2,t.V3}.All(v=>v>=first && v<last); });
            }
            var identical=Find(new[]{entry.ModelPath}).Where(other=>
            {
                if(Regex.IsMatch(source,@"/\* Imported collision: "+Regex.Escape(other.Symbol)+@" ")) return false;
                var triangles=arrays.Where(a=>a.Key.StartsWith(other.Symbol+"_v",StringComparison.Ordinal) && a.Value.Count==3)
                    .Select(a=>TriangleKey(a.Value.Select(v=>(v.X,v.Y,v.Z)))).GroupBy(k=>k).ToDictionary(g=>g.Key,g=>g.Count());
                return triangles.Count==keys.Count && keys.All(k=>triangles.GetValueOrDefault(k.Key)==k.Value);
            }).ToList();
            if(identical.Count>0 && matches.Count==identical.Count*model.Triangles.Count)
            {
                var candidates=matches.OrderBy(i=> { var t=collision.Triangles[i]; return Math.Min(t.V1,Math.Min(t.V2,t.V3)); }).Chunk(model.Triangles.Count).ToList();
                int previousEnd=-1;
                foreach(var candidate in candidates)
                {
                    var indices=candidate.SelectMany(i=> { var t=collision.Triangles[i]; return new[]{t.V1,t.V2,t.V3}; }).ToArray();
                    var actual=candidate.Select(i=> { var t=collision.Triangles[i]; return TriangleKey(new[]{t.V1,t.V2,t.V3}.Select(v=> { var p=collision.Vertices[v]; return (p.X,p.Y,p.Z); })); })
                        .GroupBy(k=>k).ToDictionary(g=>g.Key,g=>g.Count());
                    if(indices.Min()<=previousEnd || keys.Any(k=>actual.GetValueOrDefault(k.Key)!=k.Value))
                        throw new InvalidOperationException("Legacy imported collision cannot be separated into independent models.");
                    previousEnd=indices.Max();
                }
                int owner=identical.FindIndex(e=>e.Symbol==entry.Symbol);
                if(owner>=0) matches=candidates[owner].ToHashSet();
            }
            var terrainPositions=arrays.Where(a=>!a.Key.StartsWith("randm64_mesh_",StringComparison.Ordinal))
                .SelectMany(a=>a.Value).Select(v=>(v.X,v.Y,v.Z)).ToHashSet();
            if(matches.Count>0 && matches.SelectMany(i=> { var t=collision.Triangles[i]; return new[]{t.V1,t.V2,t.V3}; })
                .All(i=> { var v=collision.Vertices[i]; return terrainPositions.Contains((v.X,v.Y,v.Z)); }))
                throw new InvalidOperationException("This legacy model overlaps the original terrain exactly. Its collision ownership cannot be determined safely.");
            matchedCounts=matches.Select(i=> { var t=collision.Triangles[i]; return TriangleKey(new[]{t.V1,t.V2,t.V3}.Select(v=> { var p=collision.Vertices[v]; return (p.X,p.Y,p.Z); })); })
                .GroupBy(k=>k).ToDictionary(g=>g.Key,g=>g.Count());
        }
        // Dedicated collision from the importer has exactly one triangle per visual triangle.
        if(matches.Count!=0 && matches.Count!=model.Triangles.Count)
            throw new InvalidOperationException($"Collision overlaps another mesh or has been edited separately ({matches.Count} matching triangles for {model.Triangles.Count} visual triangles). Resolve the overlapping collision before moving this model.");
        if(start.HasValue && matches.Count!=model.Triangles.Count) throw new InvalidDataException("Imported collision no longer matches this model.");
        if(matches.Count>0 && keys.Any(k=>matchedCounts.GetValueOrDefault(k.Key)!=k.Value)) throw new InvalidDataException("Imported collision triangles no longer match this model uniquely.");
        return matches;
    }

    public static void Save(ImportedModelEntry entry,string geoPath,string collisionPath,EditableGeometry oldGeometry,EditableGeometry geometry,string root)
    {
        LevelMeshCreation.Validate(geometry);
        string original=File.ReadAllText(entry.ModelPath),geo=File.ReadAllText(geoPath);
        var output=ImportedMeshWriter.Build(entry.ModelPath,root,geometry,entry.Symbol);
        string ownership=Regex.Match(original,@"/\* Imported collision: "+Regex.Escape(entry.Symbol)+@" (?:none|\d+ \d+) \*/").Value;
        string replacement=output.Source.Replace($"/* Imported level geometry: {entry.Symbol} */",$"/* Imported level geometry: {entry.Symbol} */\n{ownership}");
        var marker=Regex.Match(original,@"/\* Imported level geometry: "+Regex.Escape(entry.Symbol)+@" \*/");
        var last=Regex.Matches(original,@"const\s+Gfx\s+"+Regex.Escape(entry.Symbol)+@"_m\d+\[\]\s*=\s*\{[\s\S]*?\n\};").LastOrDefault()
            ?? throw new InvalidDataException("Imported display lists are missing.");
        if(!marker.Success || last.Index<marker.Index) throw new InvalidDataException("Imported model source is incomplete.");
        var staged=new Dictionary<string,byte[]>(output.Assets)
        {
            [entry.ModelPath]=Encoding.UTF8.GetBytes(original[..marker.Index]+replacement+original[(last.Index+last.Length)..])
        };
        foreach(var list in output.Lists)
        {
            string pattern=@"GEO_DISPLAY_LIST\(\s*\w+\s*,\s*"+Regex.Escape(list.Symbol)+@"\s*\)";
            if(!Regex.IsMatch(geo,pattern)) throw new InvalidDataException("Imported geometry node is missing: "+list.Symbol);
            geo=Regex.Replace(geo,pattern,$"GEO_DISPLAY_LIST({list.Layer}, {list.Symbol})");
        }
        staged[geoPath]=Encoding.UTF8.GetBytes(geo);
        var collision=new CollisionParser().ParseCollisionFile(collisionPath,"","nonpersistent") ?? throw new InvalidDataException("Unable to read collision.");
        var triangles=CollisionTriangles(oldGeometry,collision,entry);
        if(triangles.Count==0 && ownership.Length==0)
        {
            ownership=$"/* Imported collision: {entry.Symbol} none */";
            replacement=output.Source.Replace($"/* Imported level geometry: {entry.Symbol} */",$"/* Imported level geometry: {entry.Symbol} */\n{ownership}");
            staged[entry.ModelPath]=Encoding.UTF8.GetBytes(original[..marker.Index]+replacement+original[(last.Index+last.Length)..]);
        }
        if(triangles.Count>0)
        {
            var selected=triangles.SelectMany(i=> { var t=collision.Triangles[i]; return new[]{t.V1,t.V2,t.V3}; }).ToHashSet();
            if(ownership.Length==0)
            {
                ownership=$"/* Imported collision: {entry.Symbol} {selected.Min()} {selected.Max()-selected.Min()+1} */";
                replacement=output.Source.Replace($"/* Imported level geometry: {entry.Symbol} */",$"/* Imported level geometry: {entry.Symbol} */\n{ownership}");
                staged[entry.ModelPath]=Encoding.UTF8.GetBytes(original[..marker.Index]+replacement+original[(last.Index+last.Length)..]);
            }
            if(collision.Triangles.Where((t,i)=>!triangles.Contains(i)).Any(t=>selected.Contains(t.V1)||selected.Contains(t.V2)||selected.Contains(t.V3)))
                throw new InvalidOperationException("This model shares collision vertices with another mesh. Split its collision before moving it.");
            if(oldGeometry.Vertices.Count!=geometry.Vertices.Count) throw new InvalidDataException("Moving a model must preserve its vertex count.");
            var positions=Enumerable.Range(0,oldGeometry.Vertices.Count).GroupBy(i=>Position(oldGeometry.Vertices[i]))
                .ToDictionary(g=>g.Key,g=>Position(geometry.Vertices[g.First()]));
            foreach(int i in selected)
            {
                var v=collision.Vertices[i]; var next=positions[(v.X,v.Y,v.Z)];
                if(Math.Max(Math.Abs(next.Item1),Math.Max(Math.Abs(next.Item2),Math.Abs(next.Item3)))>32500)
                    throw new ArgumentException("Collision placement must stay within ±32500 level units.");
                v.X=next.Item1; v.Y=next.Item2; v.Z=next.Item3;
            }
            foreach(var item in CollisionSourceWriter.Stage(collisionPath,collision)) staged[item.Key]=item.Value;
        }
        var originals=staged.Keys.ToDictionary(p=>p,p=>File.Exists(p) ? File.ReadAllBytes(p) : null);
        SourceFileTransaction.Commit(staged,originals);
    }
}
