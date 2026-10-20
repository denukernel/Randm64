using System.IO;
using System.Text.RegularExpressions;
using Sm64DecompLevelViewer.Models;

namespace Sm64DecompLevelViewer.Services;

public sealed class LevelActorCatalog
{
    private readonly string _projectRoot;
    private readonly Dictionary<string, string> _models = new();
    private readonly Dictionary<string, string> _actors = new();
    private readonly HashSet<string> _directModels = new();
    public IEnumerable<string> RegisteredModels => _models.Keys;
    private readonly Dictionary<string, string> _levelGeos = new();
    private readonly string? _levelDirectory;
    private readonly Lazy<string> _levelSource;
    private readonly Lazy<VisualMesh> _levelMaterials;
    private readonly Dictionary<string, (VisualMesh Mesh, MarioAnimation? Animation)?> _cache = new();
    public LevelActorCatalog(string root, string? levelDirectory,LevelActorCatalog? sharedCatalog = null)
    {
        _projectRoot = root;
        _levelDirectory = levelDirectory;
        _levelSource=new Lazy<string>(()=>levelDirectory==null ? "" : string.Join("\n",LevelSceneLoader.GetModelSourceFiles(levelDirectory).Select(path=>ActorSourceReader.Read(path))));
        _levelMaterials=new Lazy<VisualMesh>(()=> { var mesh=new VisualMesh(); if(levelDirectory!=null) LevelSceneLoader.ResolveLevelMaterials(root,levelDirectory,mesh); return mesh; });
        var parser = new ObjectParser();
        foreach (var pair in parser.ParseLoadModels(Path.Combine(root, "levels", "scripts.c"))) _models[pair.Key] = pair.Value;
        LoadDirectModels(Path.Combine(root,"levels","scripts.c"));
        if (levelDirectory != null)
        {
            foreach (var pair in parser.ParseLoadModels(Path.Combine(levelDirectory, "script.c"))) { _models[pair.Key] = pair.Value; _directModels.Remove(pair.Key); }
            LoadDirectModels(Path.Combine(levelDirectory,"script.c"));
            foreach (string file in Directory.GetFiles(levelDirectory, "geo.inc.c", SearchOption.AllDirectories)
                .Concat(File.Exists(Path.Combine(levelDirectory,"geo.c")) ? new[]{Path.Combine(levelDirectory,"geo.c")} : Array.Empty<string>()))
                foreach (Match layout in Regex.Matches(File.ReadAllText(file), @"GeoLayout\s+(\w+)\s*\["))
                    _levelGeos[layout.Groups[1].Value] = file;
        }
        // Model names are level-specific aliases for the same runtime byte ID.
        var ids=parser.ParseModelIds(Path.Combine(root,"include","model_ids.h"));
        var byId=new Dictionary<int,(string Geo,bool Direct)>();
        foreach(var pair in _models)
            if(ids.TryGetValue(pair.Key,out int id)) byId[id]=(pair.Value,_directModels.Contains(pair.Key));
        foreach(var pair in ids)
            if(!_models.ContainsKey(pair.Key) && byId.TryGetValue(pair.Value,out var loaded))
            { _models[pair.Key]=loaded.Geo; if(loaded.Direct) _directModels.Add(pair.Key); }
        if(sharedCatalog!=null && string.Equals(sharedCatalog._projectRoot,root,StringComparison.OrdinalIgnoreCase))
            foreach(var pair in sharedCatalog._actors) _actors[pair.Key]=pair.Value;
        else foreach (string directory in Directory.GetDirectories(Path.Combine(root, "actors")))
        {
            string geo = Path.Combine(directory, "geo.inc.c");
            if (!File.Exists(geo)) continue;
            foreach (Match layout in Regex.Matches(File.ReadAllText(geo), @"GeoLayout\s+(\w+)\s*\["))
                _actors[layout.Groups[1].Value] = Path.GetFileName(directory);
        }
    }
    private void ResolveLevelMaterials(VisualMesh mesh)
    {
        foreach(var pair in _levelMaterials.Value.TexturePaths) mesh.TexturePaths[pair.Key]=pair.Value;
        if(mesh.Triangles.Any(t=>t.TextureName!=null && !mesh.TexturePaths.ContainsKey(t.TextureName) && !mesh.LightGroupColors.ContainsKey(t.TextureName)))
            LevelSceneLoader.ResolveLevelMaterials(_projectRoot,_levelDirectory!,mesh);
    }
    private void LoadDirectModels(string path)
    {
        if(!File.Exists(path)) return;
        foreach(Match m in Regex.Matches(ActorSourceReader.Read(path),@"LOAD_MODEL_FROM_DL\s*\(\s*(\w+)\s*,\s*(\w+)\s*,"))
        { _models[m.Groups[1].Value]=m.Groups[2].Value; _directModels.Add(m.Groups[1].Value); }
    }

    public (VisualMesh Mesh, MarioAnimation? Animation)? Resolve(string model)
    {
        if (_cache.TryGetValue(model, out var cached)) return cached;
        _cache[model] = null;
        if (model == "MODEL_NONE" || !_models.TryGetValue(model, out var geo)) return null;
        try
        {
            if(_directModels.Contains(model))
            {
                if(_levelDirectory!=null && Regex.IsMatch(_levelSource.Value,@"\bGfx\s+"+Regex.Escape(geo)+@"\s*\["))
                {
                    var directMesh=new ModelParser().ParseModelFile(Path.Combine(_levelDirectory,"script.c"),"",model,
                        displayListInstances:new List<GeoDisplayListInstance>{new(){DisplayListName=geo}},sourceText:_levelSource.Value);
                    if(directMesh==null || directMesh.Triangles.Count==0) return null;
                    ResolveLevelMaterials(directMesh);
                    return _cache[model]=(directMesh,null);
                }
                foreach(string directory in Directory.GetDirectories(Path.Combine(_projectRoot,"actors")))
                {
                    string file=Path.Combine(directory,"model.inc.c");
                    if(File.Exists(file) && Regex.IsMatch(File.ReadAllText(file),@"\bGfx\s+"+Regex.Escape(geo)+@"\s*\["))
                        return _cache[model]=(ActorModelLoader.LoadDisplayList(_projectRoot,Path.GetFileName(directory),geo),null);
                }
                return null;
            }
            if (!_actors.TryGetValue(geo, out var actor))
            {
                if (_levelDirectory == null || !_levelGeos.TryGetValue(geo, out var geoFile)) return null;
                var geoParser = new GeoLayoutParser();
                var transforms = geoParser.ParseActorGeoLayout(geoFile, geo, _levelGeos.Values.Distinct());
                string source = _levelSource.Value;
                if(geoParser.DisplayListInstances.Count==0)
                {
                    var generated=Regex.Match(File.ReadAllText(geoFile),@"GEO_ASM\(\s*(0x80[12])\s*,\s*geo_movtex_draw_colored_2_no_update\)");
                    if(generated.Success)
                    {
                        int area=int.Parse(Regex.Match(Path.GetRelativePath(_levelDirectory,geoFile),@"areas[/\\](\d+)").Groups[1].Value);
                        var scene=new VisualMesh { LevelDirectory=_levelDirectory,AreaIndex=area };
                        foreach(var pair in _levelMaterials.Value.TexturePaths) scene.TexturePaths[pair.Key]=pair.Value;
                        string vertices=generated.Groups[1].Value=="0x801" ? "ssl_movtex_tris_quicksand_pit" : "ssl_movtex_tris_pyramid_quicksand_pit";
                        var generatedMesh=LevelWaterBuilder.Build(_projectRoot,scene,new CollisionMesh(),vertices,_levelSource.Value);
                        if(generatedMesh.Triangles.Count==0) return null;
                        generatedMesh.LevelName=model;
                        return _cache[model]=(generatedMesh,null);
                    }
                    if(!File.ReadAllText(geoFile).Contains("geo_exec_flying_carpet_create")) return null;
                    source=LevelGeneratedGeometry.FlyingCarpet(source) ?? "";
                    if(source.Length==0) return null;
                    geoParser.DisplayListInstances.Add(new GeoDisplayListInstance { DisplayListName="preview_flying_carpet" });
                }
                var modelParser = new ModelParser();
                var levelMesh = modelParser.ParseModelFile(geoFile, "", model, transforms, geoParser.DlToJointIndex,
                    geoParser.DisplayListInstances, geoParser.RootScale, source);
                if (levelMesh == null || levelMesh.Triangles.Count == 0) return null;
                ResolveLevelMaterials(levelMesh);
                levelMesh.Joints = geoParser.ParsedJoints;
                return _cache[model] = (levelMesh, null);
            }
            var mesh = ActorModelLoader.Load(_projectRoot, actor, geo);
            var parser = new AnimationParser();
            var animations = parser.ScanActorAnimations(_projectRoot, actor, mesh.Joints.Count);
            var animation = AnimationParser.SelectDefaultAnimation(animations, actor);
            if (animation != null && animation.Indices.Length == 0) parser.LoadAnimationData(animation);
            parser.ApplyFrame(mesh, animation, 0);
            return _cache[model] = (mesh, animation);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Actor preview unavailable for {model}: {ex.Message}");
            return null;
        }
    }
}
