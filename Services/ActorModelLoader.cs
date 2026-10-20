using System.IO;
using System.Text.RegularExpressions;
using OpenTK.Mathematics;
using Sm64DecompLevelViewer.Models;

namespace Sm64DecompLevelViewer.Services;

public static class ActorModelLoader
{
    public static VisualMesh Load(string projectRoot, string actor, string? geoLayout = null)
    {
        string directory = Path.Combine(projectRoot, "actors", actor);
        string modelPath = Path.Combine(directory, "model.inc.c");
        var geo = new GeoLayoutParser();
        var transforms = geo.ParseActorGeoLayout(Path.Combine(directory, "geo.inc.c"), geoLayout);
        string modelSource=File.Exists(modelPath) ? ActorSourceReader.Read(modelPath) : "";
        var externalSources=new List<string>();
        if (geo.DisplayListInstances.Count == 0)
        {
            // A few original actors have no geo script. Render only top-level Gfx roots,
            // rather than drawing every nested display list again at the origin.
            string source = modelSource;
            var called = Regex.Matches(source, @"gsSP(?:DisplayList|BranchList)\(\s*(\w+)\s*\)")
                .Select(match => match.Groups[1].Value).ToHashSet();
            foreach (Match list in Regex.Matches(source, @"const\s+Gfx\s+(\w+)\s*\[\s*\]\s*="))
                if (!called.Contains(list.Groups[1].Value))
                    geo.DisplayListInstances.Add(new GeoDisplayListInstance { DisplayListName = list.Groups[1].Value });
        }
        var missing=geo.DisplayListInstances.Select(i=>i.DisplayListName).Where(name=>!Regex.IsMatch(modelSource,@"\bGfx\s+"+Regex.Escape(name)+@"\s*\[")).Distinct().ToHashSet();
        if(missing.Count>0)
        {
            foreach(string file in Directory.GetFiles(Path.Combine(projectRoot,"actors"),"model.inc.c",SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(Path.Combine(projectRoot,"bin"),"*.c")))
            {
                string candidate=File.ReadAllText(file);
                var found=missing.Where(name=>Regex.IsMatch(candidate,@"\bGfx\s+"+Regex.Escape(name)+@"\s*\[")).ToList();
                if(found.Count==0) continue;
                modelSource+="\n"+ActorSourceReader.Read(file); externalSources.Add(file);
                foreach(string name in found) missing.Remove(name);
                if(missing.Count==0) break;
            }
        }
        var parser = new ModelParser();
        var mesh = parser.ParseModelFile(modelPath, "", actor, transforms, geo.DlToJointIndex,
            geo.DisplayListInstances, geo.RootScale,modelSource) ?? throw new InvalidDataException($"Unable to load actor: {actor}");
        mesh.Joints = geo.ParsedJoints;
        foreach(string file in externalSources) parser.ParseActorTextureMapping(Path.GetDirectoryName(file)!,file,mesh,projectRoot);
        ResolveMaterials(projectRoot,directory,modelPath,mesh);
        if (mesh.Triangles.Count == 0) throw new InvalidDataException($"No renderable geometry in actor: {actor}");
        return mesh;
    }

    public static VisualMesh LoadDisplayList(string projectRoot,string actor,string displayList)
    {
        string directory=Path.Combine(projectRoot,"actors",actor),modelPath=Path.Combine(directory,"model.inc.c");
        var mesh=new ModelParser().ParseModelFile(modelPath,"",actor,displayListInstances:new List<GeoDisplayListInstance>{new(){DisplayListName=displayList}})
            ?? throw new InvalidDataException("Unable to load display list: "+displayList);
        ResolveMaterials(projectRoot,directory,modelPath,mesh);
        if(mesh.Triangles.Count==0) throw new InvalidDataException("Empty display list: "+displayList);
        return mesh;
    }
    private static void ResolveMaterials(string projectRoot,string directory,string modelPath,VisualMesh mesh)
    {
        var parser=new ModelParser();
        parser.ParseActorTextureMapping(directory, modelPath, mesh, projectRoot);
        var unresolved = mesh.Triangles.Select(triangle => triangle.TextureName).Where(symbol => symbol != null &&
            !mesh.TexturePaths.ContainsKey(symbol) && !mesh.LightGroupColors.ContainsKey(symbol)).Distinct().ToList();
        if (unresolved.Count > 0)
        {
            // Several actor banks intentionally reuse another actor's texture declarations.
            var prefixes = unresolved.Select(symbol => symbol!.Split("_seg")[0]).ToList();
            foreach (string sibling in Directory.GetDirectories(Path.Combine(projectRoot, "actors"))
                .OrderByDescending(path => prefixes.Any(prefix => Path.GetFileName(path).StartsWith(prefix))))
            {
                string siblingModel = Path.Combine(sibling, "model.inc.c");
                if (!File.Exists(siblingModel)) continue;
                string source = File.ReadAllText(siblingModel);
                if (!unresolved.Any(symbol => source.Contains(symbol!))) continue;
                parser.ParseActorTextureMapping(sibling, siblingModel, mesh, projectRoot);
                unresolved.RemoveAll(symbol => mesh.TexturePaths.ContainsKey(symbol!));
                if (unresolved.Count == 0) break;
            }
        }
    }

    public static (Vector3 Min, Vector3 Max) GetPreviewBounds(VisualMesh mesh, MarioAnimation? animation, Vector3 facing)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        var parser = new AnimationParser();
        int last = Math.Max(0, (animation?.FrameCount ?? 1) - 1);
        foreach (int frame in Enumerable.Range(0, 9).Select(sample => sample * last / 8).Distinct())
        {
            parser.ApplyFrame(mesh, animation, frame, facing);
            foreach (var vertex in mesh.Vertices)
            {
                var point = new Vector3(vertex.X, vertex.Y, vertex.Z);
                min = Vector3.ComponentMin(min, point);
                max = Vector3.ComponentMax(max, point);
            }
        }
        return (min, max);
    }
}
