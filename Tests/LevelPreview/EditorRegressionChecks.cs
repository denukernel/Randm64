using System.IO;
using OpenTK.Mathematics;
using OpenTK.Windowing.Desktop;
using Sm64DecompLevelViewer.Models;
using Sm64DecompLevelViewer.Rendering;
using Sm64DecompLevelViewer.Services;

internal static class EditorRegressionChecks
{
    private static void Check(bool pass,string message) { if (!pass) throw new Exception(message); }
    public static void Run()
    {
        var points = Enumerable.Range(0,6).Select(i => new SplinePoint { Index=i,Speed=10,X=i*100 }).ToList();
        points[^1].Index=-1;
        Check(Math.Abs(CameraSplinePlayback.Evaluate(points,0,0).X-100)<0.001,"Cubic spline starts at the game's weighted point");
        var cursor = new CameraSplinePlayback();
        Check(Math.Abs(cursor.Advance(points).X-100)<0.001 && Math.Abs(cursor.Progress-0.1)<0.001,"Spline uses reciprocal frame speed");
        for (int i=0;i<30 && !cursor.Finished;i++) cursor.Advance(points);
        Check(cursor.Finished,"Spline honors terminal sentinel");
        points.ForEach(p => p.Speed=0);
        cursor=new CameraSplinePlayback(); cursor.Advance(points); cursor.Advance(points);
        Check(cursor.Progress == 0,"Zero speed holds camera position");
        Check(CameraSplinePlayback.ToWorld("sIntroStartToPipePosition",new Vector3(100,200,300))==new Vector3(-1428,460,4364),"Intro camera applies the game's rotation and world offset");
        string directory = Path.Combine(Path.GetTempPath(),"Randm64Mesh-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string model=Path.Combine(directory,"model.inc.c"),geo=Path.Combine(directory,"geo.inc.c"),collision=Path.Combine(directory,"collision.inc.c");
            string camera=Path.Combine(directory,"camera.c");
            File.WriteAllText(camera,"#ifdef VERSION_EU\nstruct CutsceneSplinePoint regional[] = { {0,25,{1,2,3}} };\n#else\nstruct CutsceneSplinePoint regional[] = { {0,20,{4,5,6}} };\n#endif\n");
            var cutscenes=new CutsceneService();
            var regional=cutscenes.LoadSpline(camera,"regional");
            Check(regional.Single().Speed==20,"Camera uses the active regional spline");
            regional[0].Speed=30;
            cutscenes.SaveSpline(camera,"regional",regional);
            Check(File.ReadAllText(camera).Contains("{0,25,{1,2,3}}") && cutscenes.LoadSpline(camera,"regional")[0].Speed==30,"Camera saving preserves the inactive regional branch");
            File.WriteAllText(model,"/* original model */\n");
            File.WriteAllText(geo,"const GeoLayout test_geo[] = { GEO_CAMERA(0, 0, 0, 0, 0, 0, 0, geo_camera_main), GEO_OPEN_NODE(), GEO_CLOSE_NODE(), GEO_END(), };");
            File.WriteAllText(collision,"const Collision test_collision[] = { COL_INIT(), COL_VERTEX_INIT(0), COL_TRI_STOP(), COL_END(), };");
            var box=LevelMeshCreation.Primitive(Vector3.Zero,500,true);
            string symbol=LevelMeshCreation.Append(model,geo,collision,box,true);
            Check(File.ReadAllText(geo).Contains($"extern const Gfx {symbol}_m0[];"),"Added geometry has a declaration for the separate geo compilation unit");
            var parser=new GeoLayoutParser();
            var transforms=parser.ParseActorGeoLayout(geo,"test_geo");
            var mesh=new ModelParser().ParseModelFile(model,"","",transforms,parser.DlToJointIndex,parser.DisplayListInstances)!;
            Check(mesh.Vertices.Count == 36 && mesh.Triangles.Count == 12,"Generated box renders all twelve triangles");
            Check(mesh.Vertices.All(v=>v.SourceArrayName!=null),"Preview vertices retain editable source identity");
            var col=new CollisionParser().ParseCollisionFile(collision,"","")!;
            Check(col.Vertices.Count==8 && col.Triangles.Count==12,"Added mesh has matching collision");
            var obj=LevelMeshCreation.ReadObj("v 0 0 0\nv 10 0 0\nv 10 0 10\nv 0 0 10\nf -4 -3 -2 -1",Vector3.Zero);
            Check(obj.Triangles.Count==2,"OBJ polygons triangulate and resolve relative indices");
            string before=File.ReadAllText(model);
            box.Vertices[0]=new Vector3(40000,0,0);
            try { LevelMeshCreation.Append(model,geo,collision,box,true); throw new Exception("Invalid mesh was accepted"); }
            catch (ArgumentException) { }
            Check(File.ReadAllText(model)==before,"Invalid geometry leaves source untouched");
            LevelMeshCreation.Append(model,geo,collision,obj,false);
            Check(File.ReadAllText(model+".bak")=="/* original model */\n","Repeated additions preserve first backup");
        }
        finally { Directory.Delete(directory,true); }
        Picking();
        Console.WriteLine("EDITOR PASS: cubic camera, speed/sentinel/hold, primitive/OBJ creation, source identity, collision, backups, and marker picking");
    }
    private static void Picking()
    {
        using var renderer=new GeometryRenderer(new GameWindowSettings { UpdateFrequency=30 },new NativeWindowSettings { Size=new Vector2i(800,600),StartVisible=false,Title="Picking check" },"");
        renderer.SetObjects(new List<LevelObject> { new() { ModelName="MODEL_NONE",X=0,Y=0,Z=0 }, new() { ModelName="MODEL_NONE",X=120,Y=0,Z=500,RY=45 } });
        renderer.ShowObjectMarkers=true; renderer.ShowVisual=false;
        renderer.SetCameraPositionAndTarget(new Vector3(0,0,1000),Vector3.Zero);
        int frame=0;
        renderer.RenderFrame+=_=>
        {
            if (++frame != 3) return;
            var center=new Vector2(renderer.ClientSize.X/2f,renderer.ClientSize.Y/2f);
            Check(renderer.GetObjectAtScreenPosition(center)==0,"Nearby marker must not steal center click with an oversized hitbox");
            renderer.ShowObjectMarkers=false;
            Check(renderer.GetObjectAtScreenPosition(center)==-1,"Hidden markers must not intercept clicks");
            renderer.Close();
        };
        renderer.Run();
    }
}
