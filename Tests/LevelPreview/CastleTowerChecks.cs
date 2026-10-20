using System.IO;
using System.Text.RegularExpressions;
using OpenTK.Mathematics;
using Sm64DecompLevelViewer.Models;
using Sm64DecompLevelViewer.Services;

internal static class CastleTowerChecks
{
    public static void Run(string root,string? output = null)
    {
        string directory=Path.Combine(root,"levels","castle_grounds");
        var catalog=new LevelActorCatalog(root,directory);
        var tower=catalog.Resolve("MODEL_LEVEL_GEOMETRY_03")?.Mesh ?? throw new Exception("Castle tower model missing");
        foreach (string symbol in new[]{"outside_09007800","outside_09001000"})
            if (!tower.TexturePaths.TryGetValue(symbol,out var path) || !File.Exists(path) || !tower.Triangles.Any(t=>t.TextureName==symbol))
                throw new Exception("Castle tower shared texture missing: "+symbol);
        foreach (string model in new[]{"MODEL_CASTLE_GROUNDS_CANNON_GRILL","MODEL_CASTLE_GROUNDS_VCUTM_GRILL"})
        {
            var mesh=catalog.Resolve(model)?.Mesh ?? throw new Exception("Castle level model missing: "+model);
            if (mesh.Triangles.Any(t=>t.TextureName!=null && !mesh.TexturePaths.ContainsKey(t.TextureName) && !mesh.LightGroupColors.ContainsKey(t.TextureName)))
                throw new Exception("Unresolved shared material on "+model);
        }
        Console.WriteLine("CASTLE TOWER PASS: stone, roof, and level object shared textures resolve");
        if (output == null) return;
        string collisionPath=Path.Combine(directory,"areas","1","collision.inc.c");
        var placement=Regex.Match(LevelSceneLoader.Clean(File.ReadAllText(collisionPath)),
            @"SPECIAL_OBJECT_WITH_YAW\(\s*special_level_geo_03\s*,\s*(-?\d+)\s*,\s*(-?\d+)\s*,\s*(-?\d+)\s*,\s*(-?\d+)\s*\)");
        if (!placement.Success) throw new Exception("Castle tower special placement missing");
        var obj=new LevelObject { ModelName="MODEL_LEVEL_GEOMETRY_03", X=int.Parse(placement.Groups[1].Value),Y=int.Parse(placement.Groups[2].Value),Z=int.Parse(placement.Groups[3].Value),RY=int.Parse(placement.Groups[4].Value)*360/256 };
        var scene=LevelSceneLoader.Load(root,directory,1);
        var collision=new CollisionParser().ParseCollisionFile(collisionPath,"Area 1","castle_grounds")!;
        var focus=new Vector3(obj.X,obj.Y+2000,obj.Z);
        LevelRenderCapture.Capture(root,scene,collision,new List<LevelObject>{obj},Path.Combine(output,"castle-tower-fixed.png"),focus,6000);
    }
}
