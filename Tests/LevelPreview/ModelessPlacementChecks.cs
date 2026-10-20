using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using OpenTK.Mathematics;
using OpenTK.Windowing.Desktop;
using Sm64DecompLevelViewer;
using Sm64DecompLevelViewer.Models;
using Sm64DecompLevelViewer.Rendering;
using Sm64DecompLevelViewer.Services;

internal static class ModelessPlacementChecks
{
    [DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr handle);
    private static void Check(bool value,string message) { if(!value) throw new Exception(message); }
    public static void Run()
    {
        GLFWProvider.CheckForMainThread=false;
        Exception? failure=null;
        var thread=new Thread(()=>
        {
            Window? owner=null; ModelImportPreviewWindow? preview=null;
            try
            {
                owner=new Window { Width=800,Height=600,Left=-16000,Top=0,ShowInTaskbar=false,ShowActivated=false }; owner.Show();
                var floor=LevelMeshCreation.Primitive(Vector3.Zero,2000,false);
                var collision=new CollisionMesh();
                collision.Vertices.AddRange(floor.Vertices.Select(v=>new CollisionVertex((int)v.X,(int)v.Y,(int)v.Z)));
                collision.Triangles.AddRange(floor.Triangles.Select(t=>new CollisionTriangle(t.A,t.B,t.C,"SURFACE_DEFAULT")));
                var terrain=new VisualMesh();
                terrain.Vertices.AddRange(floor.Vertices.Select(v=>new ModelVertex((int)v.X,(int)v.Y,(int)v.Z,0,0,255,255,255,255) { SourceArrayName="fixture_v0" }));
                terrain.Triangles.AddRange(floor.Triangles.Select(t=>new ModelTriangle(t.A,t.B,t.C)));
                preview=new ModelImportPreviewWindow(LevelMeshCreation.Primitive(Vector3.Zero,20,true),"fixture.obj",new Vector3(300,200,300),1,floor,collision,autoGround:false)
                    { Left=-16000,Top=0,ShowInTaskbar=false,ShowActivated=false,WindowStartupLocation=WindowStartupLocation.Manual };
                var completion=preview.ShowForPlacementAsync(owner);
                Check(IsWindowEnabled(new WindowInteropHelper(owner).Handle),"Model preview disabled the level editor's native window");
                Check(!completion.IsCompleted,"Modeless placement completed before confirmation");
                preview.BeginPickInLevel();
                Check(!preview.IsVisible && IsWindowEnabled(new WindowInteropHelper(owner).Handle),"Pick-in-level mode did not uncover the enabled level editor");
                using var renderer=new GeometryRenderer(new GameWindowSettings { UpdateFrequency=30 },new NativeWindowSettings { Size=new Vector2i(800,600),StartVisible=false,Title="Terrain placement regression" },"");
                renderer.LoadVisualMesh(terrain); renderer.SetCameraPositionAndTarget(new Vector3(0,1000,1000),Vector3.Zero);
                int picks=0;
                renderer.TerrainPointPicked+=point=> { picks++; preview.PlaceOnTerrain(point); };
                int frame=0;
                renderer.RenderFrame+=_=>
                {
                    if(++frame!=3) return;
                    var screen=new Vector2(renderer.ClientSize.X/2f,renderer.ClientSize.Y/2f);
                    Check(!renderer.TryPlaceOnTerrainAtScreenPosition(screen,true),"Inactive placement intercepted a normal editor click");
                    renderer.TerrainPlacementEnabled=true;
                    Check(!renderer.TryPlaceOnTerrainAtScreenPosition(screen,false),"Normal clicks must not move a model");
                    renderer.TerrainPlacementExcludedModel="fixture";
                    Check(renderer.GetTerrainPointAtScreenPosition(screen)==null,"Picking must exclude the model being assembled");
                    renderer.TerrainPlacementExcludedModel=null;
                    var hit=renderer.GetTerrainPointAtScreenPosition(screen);
                    Check(hit.HasValue && hit.Value.Length<0.01,"Screen-space terrain ray did not hit the visible ground");
                    Check(renderer.TryPlaceOnTerrainAtScreenPosition(screen,true) && picks==1,"Shift-click did not deliver a terrain point to the placement preview");
                    Check(preview.IsVisible && preview.Result!=null,"Picking did not reopen and update the model preview");
                    var min=preview.Result!.Vertices.Aggregate(Vector3.ComponentMin); var max=preview.Result.Vertices.Aggregate(Vector3.ComponentMax);
                    Check(Math.Abs((min.X+max.X)/2)<0.01 && Math.Abs((min.Z+max.Z)/2)<0.01 && Math.Abs(min.Y)<0.01,"Picked terrain point did not set model position and snap its base to ground");
                    Check(IsWindowEnabled(new WindowInteropHelper(owner).Handle),"Returning to the preview locked the owner window");
                    var confirm=(Button)typeof(ModelImportPreviewWindow).GetField("confirm",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(preview)!;
                    confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Check(completion.IsCompletedSuccessfully && completion.Result,"Modeless confirmation did not return an accepted model");
                    renderer.Close();
                };
                renderer.Run();
                var canceled=new ModelImportPreviewWindow(floor,"fixture.obj",Vector3.Zero,1) { ShowInTaskbar=false,Left=-16000,Top=0,WindowStartupLocation=WindowStartupLocation.Manual };
                var cancelResult=canceled.ShowForPlacementAsync(owner); canceled.BeginPickInLevel();
                owner.Close();
                Check(cancelResult.IsCompletedSuccessfully && !cancelResult.Result,"Closing the level editor left a hidden placement session alive");
                Console.WriteLine("MODELESS PLACEMENT PASS: native owner enabled, hide/resume, real terrain ray, Shift-click routing, self-exclusion, ground position, confirmation and owner-close cancellation");
            }
            catch(Exception ex) { failure=ex; preview?.Close(); owner?.Close(); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if(failure!=null) throw failure;
    }
}
