using System.IO;
using System.Text;
using System.Text.Json;
using Sm64DecompLevelViewer.Services;
using Sm64DecompLevelViewer.Models;

internal static class LevelRecoveryChecks
{
    private static void Check(bool value,string message) { if(!value) throw new Exception(message); }
    public static void Run()
    {
        string root=Path.Combine(Path.GetTempPath(),"Randm64Recovery-"+Guid.NewGuid().ToString("N"));
        string level=Path.Combine(root,"levels","fixture"),area=Path.Combine(level,"areas","1");
        Directory.CreateDirectory(area);
        try
        {
            string model=Path.Combine(area,"model.inc.c"),geo=Path.Combine(area,"geo.inc.c"),collision=Path.Combine(area,"collision.inc.c"),rooms=Path.Combine(area,"rooms.inc.c"),script=Path.Combine(level,"script.c");
            File.WriteAllText(model,"// Original model\r\n");
            File.WriteAllText(geo,"const GeoLayout fixture[] = { GEO_CAMERA(1,0,0,0,0,0,0,geo_camera_main), GEO_OPEN_NODE(), GEO_CLOSE_NODE(), GEO_END(), };\n");
            File.WriteAllText(collision,"const Collision fixture[] = { COL_INIT(), COL_VERTEX_INIT(3), COL_VERTEX(0,0,0), COL_VERTEX(0,0,100), COL_VERTEX(100,0,0), COL_TRI_INIT(SURFACE_DEFAULT,1), COL_TRI(0,1,2), COL_TRI_STOP(), COL_SPECIAL_INIT(1), SPECIAL_OBJECT(special_bubble_tree,10,20,30), COL_END(), };\n");
            File.WriteAllText(rooms,"const RoomData fixture_rooms[] = { 7 };\n");
            File.WriteAllText(script,"// Original objects\r\n");
            var baseline=Directory.GetFiles(level,"*",SearchOption.AllDirectories).ToDictionary(p=>p,File.ReadAllBytes);
            var geometry=LevelMeshCreation.Primitive(new(400,0,400),100,true);
            geometry.Materials.Add(new ImportedMaterial { Width=32,Height=32,Pixels=Enumerable.Repeat((byte)255,32*32*4).ToArray() });
            geometry.TriangleMaterials.AddRange(Enumerable.Repeat(0,geometry.Triangles.Count));
            LevelMeshCreation.Append(model,geo,collision,geometry,true,root);
            LevelMeshCreation.Append(model,geo,collision,geometry,false,root);
            SourceFileTransaction.Commit(new Dictionary<string,byte[]> { [script]=Encoding.UTF8.GetBytes("// Edited objects") },new Dictionary<string,byte[]?> { [script]=File.ReadAllBytes(script) });
            string manifest=Path.Combine(level,LevelSourceRecovery.ManifestName);
            Check(File.Exists(manifest),"Imports did not create a shared recovery record");
            Check(LevelSourceRecovery.FindChoices(root).SequenceEqual(new[]{manifest}),"Level recovery is exposed as partial independent files");
            var before=Directory.GetFiles(level,"*",SearchOption.AllDirectories).ToDictionary(p=>p,File.ReadAllBytes);
            using(var locked=new FileStream(rooms,FileMode.Open,FileAccess.Read,FileShare.Read))
            {
                try { LevelSourceRecovery.Restore(root,new[]{manifest}); throw new Exception("Locked restore reported success"); }
                catch(Exception ex) when(ex is IOException or UnauthorizedAccessException) { }
                Check(before.All(p=>File.Exists(p.Key)&&File.ReadAllBytes(p.Key).SequenceEqual(p.Value)),"Failed recovery consumed backups or left partial source/asset writes");
            }
            LevelSourceRecovery.Restore(root,new[]{manifest});
            Check(baseline.All(p=>File.ReadAllBytes(p.Key).SequenceEqual(p.Value)),"Recovery changed original model, geo, collision, rooms or object bytes");
            Check(Directory.GetFiles(level,"*",SearchOption.AllDirectories).Length==baseline.Count,"Imported textures or recovery records remain after recovery");
            // Legacy backups must also restore together when only one is selected.
            foreach(var p in baseline) { File.WriteAllBytes(p.Key+".bak",p.Value); File.WriteAllText(p.Key,"broken"); }
            LevelSourceRecovery.Restore(root,new[]{model+".bak"});
            Check(baseline.All(p=>File.ReadAllBytes(p.Key).SequenceEqual(p.Value)),"Legacy recovery did not group all files in the selected level");
            File.WriteAllText(collision+".bak","const Collision broken[] = { COL_INIT(), COL_VERTEX_INIT(1), COL_TRI_STOP(), COL_END(), };");
            try { LevelSourceRecovery.Restore(root,new[]{collision+".bak"}); throw new Exception("Corrupt collision backup accepted"); } catch(InvalidDataException) { }
            Check(File.Exists(collision+".bak") && File.ReadAllBytes(collision).SequenceEqual(baseline[collision]),"Invalid collision backup was applied or consumed");
            File.Delete(collision+".bak");
            // A corrupted record must never escape its level or overwrite an unrelated file.
            string outside=Path.Combine(root,"outside.txt"); File.WriteAllText(outside,"safe");
            File.WriteAllText(manifest,JsonSerializer.Serialize(new LevelSourceRecovery.Baseline { Files=new() { ["../../outside.txt"]=Encoding.UTF8.GetBytes("bad") } }));
            try { LevelSourceRecovery.Restore(root,new[]{manifest}); throw new Exception("Unsafe recovery path accepted"); } catch(InvalidDataException) { }
            Check(File.ReadAllText(outside)=="safe" && File.Exists(manifest),"Invalid recovery damaged files or consumed the record");
            Console.WriteLine("RECOVERY PASS: repeated textured imports, collision/rooms/objects, created-file cleanup, atomic rollback, legacy grouping, path validation");
        }
        finally { Directory.Delete(root,true); }
    }
    public static void CheckGameObjects(string root,string? capture=null)
    {
        var parser=new ObjectParser();
        var ids=parser.ParseModelIds(Path.Combine(root,"include","model_ids.h"));
        Check(ids.Count>100 && ids["MODEL_BOB_BUBBLY_TREE"]==ids["MODEL_CASTLE_GROUNDS_BUBBLY_TREE"],"Model defines or numeric aliases are missing");
        string level=Path.Combine(root,"levels","castle_grounds"),col=Path.Combine(level,"areas","1","collision.inc.c");
        var catalog=new LevelActorCatalog(root,level);
        var presets=new SpecialObjectParser().ParsePresets(Path.Combine(root,"include","special_presets.inc.c"));
        string source=File.ReadAllText(col);
        var calls=CSourceCalls.Read(source,n=>n.StartsWith("SPECIAL_OBJECT",StringComparison.Ordinal));
        int trees=0,doors=0;
        foreach(var call in calls)
        {
            string preset=call.Argument(source,0);
            if(preset!="special_bubble_tree" && !preset.Contains("door")) continue;
            var mesh=catalog.Resolve(presets[preset])?.Mesh;
            Check(mesh!=null && mesh.Triangles.Count>0,"Special object's model alias did not resolve: "+preset);
            Check(mesh!.Triangles.All(t=>t.TextureName==null || mesh.TexturePaths.ContainsKey(t.TextureName) || mesh.LightGroupColors.ContainsKey(t.TextureName)),"Unresolved object texture: "+preset);
            if(preset=="special_bubble_tree") trees++; else doors++;
        }
        Check(trees>=20 && doors==3,"Castle special object set is incomplete");
        var macro=new MacroObjectParser();
        var objects=macro.ParseMacroFile(Path.Combine(level,"areas","1","macro.inc.c"),macro.ParsePresets(Path.Combine(root,"include","macro_presets.inc.c")));
        var collision=new CollisionParser().ParseCollisionFile(col,"","")!;
        var placement=new LevelObjectPlacement(root);
        foreach(var obj in objects.Where(o=>o.Behavior=="bhvMessagePanel"))
        {
            int original=obj.Y; var p=placement.Position(obj,collision);
            Check(p.Y<=original+200 && p.Y>original-1000,"Sign preview dropped through the level");
            Check(obj.Y==original,"Preview placement modified saved sign coordinates");
            Console.WriteLine($"SIGN PASS: {obj.X}, {original}, {obj.Z} -> preview Y {p.Y}");
        }
        // Synthetic fixture proves spawn adjustment rather than coincidentally matching an original Y.
        var floor=new CollisionMesh(); floor.Vertices.AddRange(new[]{new CollisionVertex(0,100,0),new CollisionVertex(0,100,100),new CollisionVertex(100,100,0)}); floor.Triangles.Add(new(0,1,2,"SURFACE_DEFAULT"));
        var sign=new LevelObject { X=20,Y=0,Z=20,Behavior="bhvMessagePanel" };
        Check(placement.Position(sign,floor).Y==100 && sign.Y==0,"Game DROP_TO_FLOOR adjustment not applied");
        sign.Behavior="bhvSignOnWall";
        Check(placement.Position(sign,floor).Y==0,"Wall-mounted signs must retain authored height");
        Console.WriteLine($"OBJECT RECOVERY PASS: {trees} trees, {doors} doors, correct model aliases, textures and sign spawn heights");
        if(capture!=null)
        {
            foreach(var call in calls)
            {
                string preset=call.Argument(source,0);
                if(!presets.TryGetValue(preset,out string? model)) continue;
                objects.Add(new LevelObject { ModelName=model,X=int.Parse(call.Argument(source,1)),Y=int.Parse(call.Argument(source,2)),Z=int.Parse(call.Argument(source,3)),
                    RY=call.Arguments.Count>4 ? (int)Math.Round(int.Parse(call.Argument(source,4))*360.0/256) : 0 });
            }
            var scene=LevelSceneLoader.Load(root,level,1);
            LevelRenderCapture.Capture(root,scene,collision,objects,capture,new(0,1100,-1500),10000);
            LevelRenderCapture.Capture(root,scene,collision,objects,Path.Combine(Path.GetDirectoryName(capture)!,"castle-recovery-doors.png"),new(0,1000,-3150),1200,new(0,0.1f,1));
        }
    }
}
