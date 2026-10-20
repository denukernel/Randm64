using System.IO;
using System.Text;
using Sm64DecompLevelViewer.Models;
using Sm64DecompLevelViewer.Services;

internal static class LevelSaveChecks
{
    private static void Check(bool pass,string message) { if(!pass) throw new Exception(message); }
    private static List<LevelObject> Specials(string path)
    {
        string source=File.ReadAllText(path);
        return CSourceCalls.Read(source,n=>n.StartsWith("SPECIAL_OBJECT",StringComparison.Ordinal)).Select(c=>new LevelObject
        {
            ModelName="MODEL_NONE",PresetName=c.Argument(source,0),X=int.Parse(c.Argument(source,1)),Y=int.Parse(c.Argument(source,2)),Z=int.Parse(c.Argument(source,3)),
            RY=c.Arguments.Count>4 ? (int)Math.Round(int.Parse(c.Argument(source,4))*360.0/256) : 0,RawParams=c.Arguments.Count>5 ? c.Argument(source,5) : null,
            SourceFile=path,SourceIndex=c.Start,SourceLength=c.End-c.Start,SourceType=ObjectSourceType.Special
        }).ToList();
    }
    public static void Run()
    {
        string root=Path.Combine(Path.GetTempPath(),"Randm64Save-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root); Directory.CreateDirectory(Path.Combine(root,"include"));
        try
        {
            File.WriteAllText(Path.Combine(root,"include","special_presets.inc.c"),"{ special_bubble_tree, SPTYPE_NO_YROT_OR_PARAMS, 0, MODEL_TREE, bhvTree },\n{ special_level_geo_03, SPTYPE_YROT_NO_PARAMS, 0, MODEL_LEVEL_GEOMETRY_03, bhvStaticObject },\n{ special_castle_door_warp, SPTYPE_PARAMS_AND_YROT, 0, MODEL_CASTLE_DOOR, bhvDoorWarp },\n");
            string script=Path.Combine(root,"script.c"),model=Path.Combine(root,"model.inc.c"),geo=Path.Combine(root,"geo.inc.c"),collision=Path.Combine(root,"collision.inc.c"),macro=Path.Combine(root,"macro.inc.c"),rooms=Path.Combine(root,"room.inc.c");
            const string parameters="BPARAM2(WARP_NODE_0A) | BPARAM1(5)";
            File.WriteAllText(script,$"const LevelScript fixture[] = {{\n AREA(2, fixture_geo),\n OBJECT_WITH_ACTS(MODEL_NONE, /* x */ 10, 20, 30, 0, 0, 0, {parameters}, bhvWarp, ACT_1 | ACT_2),\n END_AREA(),\n MARIO_POS(2, 90, 1, 2, 3),\n}};\n");
            File.WriteAllText(model,"/* original model */\n");
            File.WriteAllText(geo,"const GeoLayout fixture_geo[] = { GEO_CAMERA(0,0,0,0,0,0,0,camera_main), GEO_OPEN_NODE(), GEO_CLOSE_NODE(), GEO_END(), };\n");
            File.WriteAllText(collision,"const Collision fixture_collision[] = {\n COL_INIT(),\n COL_VERTEX_INIT(3),\n COL_VERTEX(0,0,0), COL_VERTEX(0,0,100), COL_VERTEX(100,0,0),\n COL_TRI_INIT(SURFACE_0004,1), COL_TRI_SPECIAL(0,1,2,0x33),\n COL_TRI_INIT(SURFACE_DEFAULT,1), COL_TRI(2,1,0),\n COL_TRI_STOP(),\n COL_SPECIAL_INIT(3),\n SPECIAL_OBJECT(special_bubble_tree,10,20,30),\n SPECIAL_OBJECT_WITH_YAW(special_level_geo_03,0,2867,-3924,0),\n SPECIAL_OBJECT_WITH_YAW_AND_PARAM(special_castle_door_warp,77,803,-3155,128,BPARAM2(WARP_NODE_01)),\n COL_WATER_BOX_INIT(1), COL_WATER_BOX(0,0,0,100,100,-81),\n COL_END(),\n};\n");
            File.WriteAllText(rooms,"const RoomData fixture_rooms[] = { 7, 9 };\n");
            File.WriteAllText(macro,"const MacroObject fixture_macro[] = { MACRO_OBJECT_WITH_BHV_PARAM(macro_test,0,1,2,3,BPARAM2(4) | BPARAM1(8)), MACRO_OBJECT_END(), };\n");
            var objects=new ObjectParser().ParseScriptFile(script,2);
            Check(objects.Count==2 && objects[0].RawParams==parameters && objects[0].RawActs=="ACT_1 | ACT_2","Balanced object parser must preserve nested parameters and acts");
            objects.AddRange(Specials(collision));
            objects.AddRange(new MacroObjectParser().ParseMacroFile(macro,new(){["macro_test"]=new(){Model="MODEL_NONE",Behavior="bhvTest"}}));
            Check(objects[^1].RawParams=="BPARAM2(4) | BPARAM1(8)","Macro parser truncated a nested parameter expression");
            var saver=new LevelSaver(); saver.CaptureSources(objects);
            string originalScript=File.ReadAllText(script),originalCollision=File.ReadAllText(collision),originalMacro=File.ReadAllText(macro);
            Check(saver.SaveLevel(objects),saver.LastError ?? "No-op save failed");
            Check(File.ReadAllText(script)==originalScript && File.ReadAllText(collision)==originalCollision && File.ReadAllText(macro)==originalMacro,"No-op saves must preserve all source bytes");
            string imported=LevelMeshCreation.Append(model,geo,collision,LevelMeshCreation.Primitive(new(400,0,400),100,true),true,root);
            var parsed=new CollisionParser().ParseCollisionFile(collision,"","")!;
            Check(parsed.Triangles.Count==14 && parsed.Triangles[0].SurfaceType=="SURFACE_0004" && parsed.Triangles[0].SpecialParam==0x33,"Collision import lost force surface or parameter");
            Check(File.ReadAllText(rooms).Contains("7, 9,"),"Room assignments changed when adding imported collision");
            objects[0].X=99;
            objects.Single(o=>o.PresetName=="special_bubble_tree").IsDeleted=true;
            objects.Single(o=>o.PresetName=="special_level_geo_03").X=123;
            var added=new LevelObject { ModelName="MODEL_NONE",Behavior="bhvWarp",X=5,Y=6,Z=7,RawParams=parameters,RawActs="ACT_3 | ACT_4",AreaIndex=2,SourceFile=script,SourceType=ObjectSourceType.Normal,IsNew=true };
            var added2=new LevelObject { ModelName="MODEL_NONE",Behavior="bhvWarp",X=8,Y=9,Z=10,AreaIndex=2,SourceFile=script,SourceType=ObjectSourceType.Normal,IsNew=true };
            var special=new LevelObject { ModelName="MODEL_LEVEL_GEOMETRY_03",PresetName="special_level_geo_03",RY=90,X=500,SourceFile=collision,SourceType=ObjectSourceType.Special,IsNew=true };
            objects.AddRange(new[]{added,added2,special});
            Check(saver.SaveLevel(objects),saver.LastError ?? "Save after imported geometry failed");
            Check(File.ReadAllText(script).Contains("MARIO_POS(2, 90") && File.ReadAllText(script).Contains("/* x */ 99") && File.ReadAllText(script).Contains(parameters),"Saving changed Mario area, nested parameters or comments");
            Check(File.ReadAllText(collision).Contains("SPECIAL_OBJECT_WITH_YAW(special_level_geo_03, 500, 0, 0, 64)") && File.ReadAllText(collision).Contains("COL_SPECIAL_INIT(3)"),"New special object used the wrong terrain record width or count");
            CollisionSourceValidation.Validate(File.ReadAllText(collision),collision);
            Check(File.ReadAllText(model).Contains(imported),"Object saving destroyed imported display lists");
            var saved=objects.Select(o=>o.SourceFile!).Distinct().ToDictionary(p=>p,File.ReadAllBytes);
            Check(saver.SaveLevel(objects),saver.LastError ?? "Repeat save failed");
            Check(saved.All(p=>File.ReadAllBytes(p.Key).SequenceEqual(p.Value)),"Repeated save duplicated or deleted objects again");
            added.Y=88; special.Z=777;
            Check(saver.SaveLevel(objects),saver.LastError ?? "Second edit failed");
            Check(new ObjectParser().ParseScriptFile(script,2).Single(o=>o.X==5).Y==88,"New object's source index did not survive a subsequent save");
            // Lock a later destination after staging. Earlier file updates must be rolled back and object metadata retained.
            objects[0].Y=555; objects.Single(o=>o.SourceType==ObjectSourceType.Macro).X=100;
            byte[] beforeScript=File.ReadAllBytes(script),beforeMacro=File.ReadAllBytes(macro); int beforeIndex=objects[0].SourceIndex;
            using(var locked=new FileStream(macro,FileMode.Open,FileAccess.Read,FileShare.Read)) Check(!saver.SaveLevel(objects),"Save unexpectedly replaced a locked source");
            Check(File.ReadAllBytes(script).SequenceEqual(beforeScript) && File.ReadAllBytes(macro).SequenceEqual(beforeMacro) && objects[0].SourceIndex==beforeIndex,"A partial transaction changed files or source metadata");
            Check(saver.SaveLevel(objects),saver.LastError ?? "Retry after failed transaction failed");
            Check(File.ReadAllText(script+".bak")==originalScript,"First source backup was overwritten");
            string current=File.ReadAllText(script); File.WriteAllText(script,current.Replace("555","556")); objects[0].Z=999;
            byte[] external=File.ReadAllBytes(script);
            Check(!saver.SaveLevel(objects) && File.ReadAllBytes(script).SequenceEqual(external),"Saving overwrote an external object edit");
            string invalid=File.ReadAllText(collision).Replace("COL_TRI_SPECIAL(0, 1, 2, 0x33)","COL_TRI(0, 1, 2)");
            try { CollisionSourceValidation.Validate(invalid,collision); throw new Exception("Wrong command width was accepted"); } catch(InvalidDataException) { }
            var huge=LevelMeshCreation.Primitive(new(0,0,0),100,false); for(int i=huge.Triangles.Count;i<2301;i++) huge.Triangles.Add((0,2,1));
            var unchanged=new[]{model,geo,collision,rooms}.ToDictionary(p=>p,File.ReadAllBytes);
            try { LevelMeshCreation.Append(model,geo,collision,huge,true,root); throw new Exception("Game surface-pool overflow accepted"); } catch(InvalidDataException) { }
            Check(unchanged.All(p=>File.ReadAllBytes(p.Key).SequenceEqual(p.Value)),"Rejected collision import changed level files");
            var arrays=new ModelParser().ParseVertexArrays(File.ReadAllText(model)); var firstArray=arrays.First();
            string visualSource=File.ReadAllText(model); int count=firstArray.Value.Count;
            try { LevelMeshService.StageVisualMesh(visualSource,firstArray.Key,firstArray.Value.Take(count-1).ToList()); throw new Exception("Unsafe vertex-count change accepted"); } catch(InvalidDataException) { }
            firstArray.Value[0].X=32768;
            Check(!new LevelMeshService().SaveVisualMesh(model,firstArray.Key,firstArray.Value) && File.ReadAllText(model)==visualSource,"Visual overflow was silently truncated into the ROM");
            firstArray.Value[0].X=42;
            string movedVisual=LevelMeshService.StageVisualMesh(visualSource,firstArray.Key,firstArray.Value);
            Check(new ModelParser().ParseVertexArrays(movedVisual)[firstArray.Key][0].X==42 && movedVisual.Contains("gsSPVertex("),"Safe vertex edits failed or lost display lists");
            string regionalDirectory=Path.Combine(root,"regional"); Directory.CreateDirectory(regionalDirectory);
            string regional=Path.Combine(regionalDirectory,"collision.inc.c");
            const string jp="const Collision jp[] = { COL_INIT(), COL_VERTEX_INIT(0), COL_TRI_STOP(), COL_END(), };";
            File.WriteAllText(regional,"#ifdef VERSION_JP\r\n"+jp+"\r\n#else\r\nconst Collision us[] = { COL_INIT(), COL_VERTEX_INIT(3), COL_VERTEX(0,0,0), COL_VERTEX(0,0,100), COL_VERTEX(100,0,0), COL_TRI_INIT(SURFACE_DEFAULT,1), COL_TRI(0,1,2), COL_TRI_STOP(), COL_END(), };\r\n#endif\r\n");
            var regionalMesh=new CollisionParser().ParseCollisionFile(regional,"","")!;
            Check(regionalMesh.Vertices.Count==3,"Collision parser selected inactive regional data");
            regionalMesh.Vertices[0].Y=25;
            Check(new LevelMeshService().SaveCollisionMesh(regional,regionalMesh),"Regional collision save failed");
            Check(File.ReadAllText(regional).Contains(jp) && new CollisionParser().ParseCollisionFile(regional,"","")!.Vertices[0].Y==25,"Regional save changed JP branch or failed to update US branch at CRLF offsets");
            Check(CSourceCalls.Read("// OBJECT(a)\nconst char *s=\"OBJECT(b)\"; OBJECT(c, FN(1,2))",n=>n=="OBJECT").Single().Arguments.Count==2,"Source scanner treated comments, strings or nested commas as commands");
            Console.WriteLine("SAVE PASS: import-then-edit/delete, source rebasing, nested BPARAMs, acts, area preservation, correct special record widths, force surfaces, room tables, no-op/repeated saves, atomic rollback, retries, backups, conflicts and runtime pool limits");
        }
        finally { Directory.Delete(root,true); }
    }
}
