using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OpenTK.Mathematics;
using Sm64DecompLevelViewer;
using Sm64DecompLevelViewer.Services;

internal static class ImportPreviewChecks
{
    public static void Run(string file,string output,string? levelRoot=null)
    {
        byte[] original=File.ReadAllBytes(file);
        // Confirm that this reported failure was native filename handling, rather than malformed GLB data.
        using(var native=new Assimp.AssimpContext())
        {
            try { native.ImportFile(file); Console.WriteLine("Native filename import succeeded on this system."); }
            catch(Assimp.AssimpException ex) { Console.WriteLine("Original native importer reproduces: "+ex.Message); }
        }
        var mesh=ModelImportService.Load(file);
        if(mesh.Triangles.Count==0 || !mesh.Materials.Any(m=>m.Pixels!=null)) throw new Exception("User GLB failed to load with textures");
        Exception? failure=null;
        EditableGeometry? terrain=null; Sm64DecompLevelViewer.Models.CollisionMesh? ground=null;
        var placement=Vector3.Zero;
        if(levelRoot!=null)
        {
            string level=Path.Combine(levelRoot,"levels","castle_grounds");
            var scene=LevelSceneLoader.Load(levelRoot,level,1,"outside");
            terrain=ModelPlacementService.FromVisual(scene);
            ground=new CollisionParser().ParseCollisionFile(Path.Combine(level,"areas","1","collision.inc.c"),"","")!;
            var floor=ground.Triangles.Select(t=>
            {
                var a=ground.Vertices[t.V1]; var b=ground.Vertices[t.V2]; var c=ground.Vertices[t.V3];
                return (Point:new Vector3((a.X+b.X+c.X)/3f,(a.Y+b.Y+c.Y)/3f,(a.Z+b.Z+c.Z)/3f),Normal:Vector3.Cross(new Vector3(b.X-a.X,b.Y-a.Y,b.Z-a.Z),new Vector3(c.X-a.X,c.Y-a.Y,c.Z-a.Z)));
            }).Where(t=>t.Normal.Y>0).OrderByDescending(t=>t.Normal.Y).First();
            placement=new Vector3(-1328,760,4664);
            mesh=ImportedModelPlacement.AtBase(mesh).Local;
        }
        var thread=new Thread(()=>
        {
            try
            {
                var window=new ModelImportPreviewWindow(mesh,file,placement,levelRoot==null ? 100 : 1500,terrain,ground) { ShowInTaskbar=false, ShowActivated=false, Left=-16000, Top=0, WindowStartupLocation=WindowStartupLocation.Manual };
                var timer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(700) };
                timer.Tick+=(_,_)=>
                {
                    timer.Stop();
                    try
                    {
                        object Field(string name)=>typeof(ModelImportPreviewWindow).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
                        if(window.Result==null) throw new Exception("Preview cannot confirm valid user GLB");
                        if(ground!=null && Math.Abs(ModelPlacementService.GroundOffset(window.Result,ground))>0.01) throw new Exception("Ground snap preview does not rest its base on terrain");
                        var scale=(TextBox[])Field("scale"); var rotation=(TextBox[])Field("rotation"); var position=(TextBox[])Field("position");
                        scale[0].Text=levelRoot==null ? "150" : "1500"; rotation[1].Text="35"; position[0].Text=(placement.X+10).ToString(System.Globalization.CultureInfo.InvariantCulture);
                        if(window.Result==null || (window.Result.Vertices[0]-mesh.Vertices[0]).Length<1) throw new Exception("Transform edits did not update preview result");
                        ((TextBox)Field("tint")).Text="#FFBBDDFF";
                        if(window.Result==null) throw new Exception("Material tint edit invalidated valid model");
                        scale[0].Text="100000000";
                        if(window.Result!=null || ((Button)Field("confirm")).IsEnabled) throw new Exception("Out-of-range geometry can be confirmed");
                        scale[0].Text=levelRoot==null ? "150" : "1500";
                        ((TextBox)Field("tint")).Text="invalid"; position[1].Text="5";
                        if(window.Result!=null) throw new Exception("Invalid material color can be confirmed after transform edit");
                        ((TextBox)Field("tint")).Text="#FFFFFFFF";
                        if(ground!=null)
                        {
                            var snap=(CheckBox)Field("snapToGround"); snap.IsChecked=false;
                            position[1].Text=(placement.Y+250).ToString(System.Globalization.CultureInfo.InvariantCulture);
                            if(window.Result==null || Math.Abs(window.Result.Vertices.Min(v=>v.Y)-(placement.Y+250))>0.01) throw new Exception("Manual height positioning is stuck on ground");
                            snap.IsChecked=true;
                            if(window.Result==null || Math.Abs(ModelPlacementService.GroundOffset(window.Result,ground))>0.01) throw new Exception("Re-enabling snap did not drop model to terrain");
                        }
                        typeof(ModelImportPreviewWindow).GetMethod("Frame",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null);
                        window.UpdateLayout();
                        var bitmap=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);
                        bitmap.Render(window); var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                        using(var stream=File.Create(output)) encoder.Save(stream);
                        window.Width=window.MinWidth; window.Height=window.MinHeight; window.UpdateLayout();
                        if(((FrameworkElement)Field("viewport")).ActualWidth<400) throw new Exception("Preview viewport collapses at minimum window size");
                    }
                    catch(Exception ex) { failure=ex; }
                    finally { window.DialogResult=false; }
                };
                timer.Start();
                if(window.ShowDialog()!=false) throw new Exception("Cancel did not cancel preview");
            }
            catch(Exception ex) { failure=ex; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if(failure!=null) throw failure;
        if(!File.ReadAllBytes(file).SequenceEqual(original)) throw new Exception("Preview changed source model");
        Console.WriteLine($"PREVIEW PASS: actual user GLB, {mesh.Triangles.Count} triangles, texture, live transforms, material edits, invalid input protection, minimum layout, and cancel without file changes");
    }
}
