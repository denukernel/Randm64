using System.IO;
using Sm64DecompLevelViewer.Services;

internal static class LevelObjectMaterialAudit
{
    public static void CheckSharedReferences(string root)
    {
        var catalog=new LevelActorCatalog(root,null);
        foreach(string model in new[]{"MODEL_NUMBER","MODEL_BOWLING_BALL","MODEL_TRAJECTORY_MARKER_BALL"})
        {
            var mesh=catalog.Resolve(model)?.Mesh ?? throw new Exception("Shared reference failed: "+model);
            if(mesh.Triangles.Any(t=>t.TextureName!=null && !mesh.TexturePaths.ContainsKey(t.TextureName) && !mesh.LightGroupColors.ContainsKey(t.TextureName))) throw new Exception("Shared material failed: "+model);
        }
        Console.WriteLine("SHARED REFERENCE PASS: numbers, bowling balls, and trajectory markers");
        var ssl=new LevelActorCatalog(root,Path.Combine(root,"levels","ssl"));
        foreach(string model in new[]{"MODEL_LEVEL_GEOMETRY_03","MODEL_LEVEL_GEOMETRY_04"})
        {
            var mesh=ssl.Resolve(model)?.Mesh ?? throw new Exception("Generated sand pit failed: "+model);
            if(mesh.TexturePaths.Count==0 || mesh.Vertices.Count!=8) throw new Exception("Generated sand pit material failed: "+model);
        }
        Console.WriteLine("GENERATED MATERIAL PASS: both Shifting Sand Land sand pits");
    }
    public static void CaptureExamples(string root,string output)
    {
        foreach(var pair in new[]{("ccm","MODEL_CCM_ROPEWAY_LIFT"),("ddd","MODEL_DDD_BOWSER_SUB"),("rr","MODEL_RR_FLYING_CARPET"),("lll","MODEL_LLL_BOWSER_PIECE_1"),("wf","MODEL_WF_KICKABLE_BOARD")})
        {
            var mesh=new LevelActorCatalog(root,Path.Combine(root,"levels",pair.Item1)).Resolve(pair.Item2)?.Mesh ?? throw new Exception("Missing audit example "+pair.Item2);
            if(mesh.Triangles.Any(t=>t.TextureName!=null && !mesh.TexturePaths.ContainsKey(t.TextureName) && !mesh.LightGroupColors.ContainsKey(t.TextureName))) throw new Exception("Unresolved example material "+pair.Item2);
            LevelRenderCapture.Capture(root,mesh,new(),new(),Path.Combine(output,pair.Item1+"-object-textures.png"));
        }
    }
    public static void Run(string root)
    {
        int levels=0,objects=0;
        var failures=new List<string>();
        var shared=new LevelActorCatalog(root,null);
        foreach(string directory in Directory.GetDirectories(Path.Combine(root,"levels")).Order())
        {
            string script=Path.Combine(directory,"script.c");
            if(!File.Exists(script)) continue;
            var models=new ObjectParser().ParseLoadModels(script);
            foreach(System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(LevelSceneLoader.Clean(File.ReadAllText(script)),@"LOAD_MODEL_FROM_DL\s*\(\s*(\w+)\s*,\s*(\w+)\s*,")) models[m.Groups[1].Value]=m.Groups[2].Value;
            var catalog=new LevelActorCatalog(root,directory,shared);
            foreach(var pair in models)
            {
                try
                {
                    var mesh=catalog.Resolve(pair.Key)?.Mesh ?? throw new Exception("No model geometry: "+pair.Value);
                    if(pair.Key=="MODEL_RR_FLYING_CARPET" && (mesh.Triangles.Count!=24 || mesh.Vertices.Max(v=>Math.Abs(v.X))>400)) throw new Exception("Generated carpet geometry is incorrect");
                    var unresolved=mesh.Triangles.Select(t=>t.TextureName).Where(s=>s!=null && !mesh.TexturePaths.ContainsKey(s) && !mesh.LightGroupColors.ContainsKey(s)).Distinct().ToList();
                    if(unresolved.Count>0) throw new Exception("Unresolved textures: "+string.Join(",",unresolved));
                    if(mesh.TexturePaths.Any(p=>!File.Exists(p.Value))) throw new Exception("Texture file missing");
                    objects++;
                    Console.WriteLine($"OBJECT PASS: {Path.GetFileName(directory)} {pair.Key}");
                }
                catch(Exception ex) { failures.Add($"{Path.GetFileName(directory)} {pair.Key}: {ex.Message}"); Console.WriteLine("OBJECT FAIL: "+failures[^1]); }
            }
            levels++;
        }
        foreach(string model in shared.RegisteredModels)
        {
            var mesh=shared.Resolve(model)?.Mesh;
            if(mesh==null) { failures.Add("shared "+model+": missing geometry"); Console.WriteLine("OBJECT FAIL: "+failures[^1]); continue; }
            var unknown=mesh.Triangles.Select(t=>t.TextureName).Where(s=>s!=null && !mesh.TexturePaths.ContainsKey(s) && !mesh.LightGroupColors.ContainsKey(s)).Distinct().ToList();
            if(unknown.Count>0) { failures.Add("shared "+model+": "+string.Join(",",unknown)); Console.WriteLine("OBJECT FAIL: "+failures[^1]); }
            else objects++;
        }
        Console.WriteLine($"OBJECT TOTAL: {levels} levels, {objects} registered objects, {failures.Count} failures");
        if(failures.Count>0) throw new Exception(string.Join(Environment.NewLine,failures));
    }
}
