using System.IO;
using OpenTK.Mathematics;
using Sm64DecompLevelViewer.Models;
using Sm64DecompLevelViewer.Services;

internal static class ModelPlacementChecks
{
    public static void CheckExisting(string root)
    {
        string level=Path.Combine(root,"levels","castle_grounds");
        var entries=ImportedModelPlacement.Find(LevelSceneLoader.GetModelSourceFiles(level));
        var collision=new CollisionParser().ParseCollisionFile(Path.Combine(level,"areas","1","collision.inc.c"),"","")!;
        foreach(var entry in entries)
        {
            var geometry=ImportedModelPlacement.Read(entry,root);
            Console.WriteLine($"EXISTING PLACEMENT PASS: {entry.Symbol}, {geometry.Triangles.Count} triangles, {ImportedModelPlacement.CollisionTriangles(geometry,collision,entry).Count} collision triangles");
        }
    }
    private static void Check(bool value,string message) { if(!value) throw new Exception(message); }
    public static void Run(string modelFile)
    {
        string root=Path.Combine(Path.GetTempPath(),"Randm64Placement-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            string model=Path.Combine(root,"model.inc.c"),geo=Path.Combine(root,"geo.inc.c"),collision=Path.Combine(root,"collision.inc.c");
            File.WriteAllText(model,"/* preserve original */\n");
            File.WriteAllText(geo,"const GeoLayout fixture_geo[] = { GEO_CAMERA(0,0,0,0,0,0,0,camera_main), GEO_OPEN_NODE(), GEO_CLOSE_NODE(), GEO_END(), };");
            File.WriteAllText(collision,"const Collision fixture_collision[] = { COL_INIT(), COL_VERTEX_INIT(0), COL_TRI_STOP(), COL_END(), };");
            LevelMeshCreation.Append(model,geo,collision,LevelMeshCreation.Primitive(new Vector3(0,100,0),4000,false),true,root);
            LevelMeshCreation.Append(model,geo,collision,LevelMeshCreation.Primitive(new Vector3(0,500,0),4000,false),true,root);
            var ground=new CollisionParser().ParseCollisionFile(collision,"","")!;
            var raw=ImportedModelPlacement.AtBase(ModelImportService.Load(modelFile)).Local;
            var geometry=ModelImportService.Transform(raw,new Vector3(100,200,100),new Vector3(100),Vector3.Zero);
            Check(Math.Abs(ModelPlacementService.GroundOffset(geometry,ground)+100)<0.01,"Drop must choose nearest floor below, not an overhead roof");
            var below=ModelImportService.Transform(raw,new Vector3(100,-1000,100),new Vector3(100),Vector3.Zero);
            Check(Math.Abs(ModelPlacementService.GroundOffset(below,ground)-1100)<0.01,"Model below the map must recover to the closest floor above");
            var outside=ModelImportService.Transform(raw,new Vector3(10000,200,100),new Vector3(100),Vector3.Zero);
            try { ModelPlacementService.GroundOffset(outside,ground); throw new Exception("Off-map ground was invented"); } catch(InvalidOperationException) { }
            geometry=ModelImportService.Transform(raw,new Vector3(100,100,100),new Vector3(100),Vector3.Zero);
            string symbol=LevelMeshCreation.Append(model,geo,collision,geometry,true,root);
            var entry=ImportedModelPlacement.Find(new[]{model}).Single(e=>e.Symbol==symbol);
            var read=ImportedModelPlacement.Read(entry,root);
            Check(read.Triangles.Count==geometry.Triangles.Count && read.Materials.Any(m=>m.Pixels!=null),"Existing imported GLB geometry and textures must reopen as one model");
            var col=new CollisionParser().ParseCollisionFile(collision,"","")!;
            var selected=ImportedModelPlacement.CollisionTriangles(read,col);
            Check(selected.Count==geometry.Triangles.Count,"Must identify this model's collision only");
            var pivot=ImportedModelPlacement.AtBase(read);
            var moved=ModelImportService.Transform(pivot.Local,new Vector3(300,250,400),new Vector3(2),new Vector3(0,90,0));
            string backup=File.ReadAllText(model+".bak");
            ImportedModelPlacement.Save(entry,geo,collision,read,moved,root);
            var reopened=ImportedModelPlacement.Read(entry,root);
            Check(reopened.Vertices.Zip(moved.Vertices).All(p=>(p.First-new Vector3(MathF.Round(p.Second.X),MathF.Round(p.Second.Y),MathF.Round(p.Second.Z))).Length<0.01),"Whole-model translation, rotation and scale must persist after reopening");
            var movedCollision=new CollisionParser().ParseCollisionFile(collision,"","")!;
            Check(movedCollision.Vertices.Take(8).Select(v=>v.Y).SequenceEqual(new[]{100,100,100,100,500,500,500,500}),"Terrain collision must stay unchanged");
            Check(ImportedModelPlacement.CollisionTriangles(reopened,movedCollision).Count==selected.Count,"Visual and collision geometry must move together");
            Check(File.ReadAllText(model).Contains("preserve original") && File.ReadAllText(model+".bak")==backup,"Preserve unrelated source and first backup");
            Check(ImportedModelPlacement.Find(new[]{model}).Count==3,"Assembly must replace the selected model without duplicating it");
            string duplicateSymbol=LevelMeshCreation.Append(model,geo,collision,reopened,true,root);
            // Simulate files produced before collision ownership was recorded, including identical overlapping copies.
            File.WriteAllText(model,System.Text.RegularExpressions.Regex.Replace(File.ReadAllText(model),@"/\* Imported collision: [^\r\n]+ \*/", ""));
            var duplicate=ImportedModelPlacement.Find(new[]{model}).Single(e=>e.Symbol==duplicateSymbol);
            var beforeMove=new CollisionParser().ParseCollisionFile(collision,"","")!;
            var duplicateTriangles=ImportedModelPlacement.CollisionTriangles(reopened,beforeMove,duplicate);
            var duplicateIndices=duplicateTriangles.SelectMany(i=> { var t=beforeMove.Triangles[i]; return new[]{t.V1,t.V2,t.V3}; }).Distinct().ToArray();
            var next=ModelImportService.Transform(reopened,new Vector3(50,0,0),Vector3.One,Vector3.Zero);
            ImportedModelPlacement.Save(entry,geo,collision,reopened,next,root);
            var afterMove=new CollisionParser().ParseCollisionFile(collision,"","")!;
            Check(duplicateIndices.All(i=>beforeMove.Vertices[i].X==afterMove.Vertices[i].X && beforeMove.Vertices[i].Y==afterMove.Vertices[i].Y && beforeMove.Vertices[i].Z==afterMove.Vertices[i].Z),"Overlapping imported copies must retain separate collision ownership");
            var plane=LevelMeshCreation.Primitive(new Vector3(0,100,0),4000,false);
            string noCollision=LevelMeshCreation.Append(model,geo,collision,plane,false,root);
            var visualOnly=ImportedModelPlacement.Find(new[]{model}).Single(e=>e.Symbol==noCollision);
            var visualRead=ImportedModelPlacement.Read(visualOnly,root);
            string unchanged=File.ReadAllText(collision);
            ImportedModelPlacement.Save(visualOnly,geo,collision,visualRead,ModelImportService.Transform(visualRead,new Vector3(0,50,0),Vector3.One,Vector3.Zero),root);
            Check(File.ReadAllText(collision)==unchanged,"Visual-only import coincident with terrain must not move the terrain's collision");
            Console.WriteLine("PLACEMENT PASS: below-map recovery, nearest floor, overhead rejection, no-floor handling, existing GLB assembly, whole-model transforms, matching collision, textures, backups and reopening");
        }
        finally { Directory.Delete(root,true); }
    }
}
