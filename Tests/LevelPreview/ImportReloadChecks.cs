using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using OpenTK.Mathematics;
using Sm64DecompLevelViewer;
using Sm64DecompLevelViewer.Models;
using Sm64DecompLevelViewer.Services;

internal static class ImportReloadChecks
{
    [DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr handle);
    public static void Run(string modelFile,bool shutdown=false,bool modelessPlacement=false)
    {
        string root=Path.Combine(Path.GetTempPath(),"Randm64ImportReload-"+Guid.NewGuid().ToString("N"));
        string level=Path.Combine(root,"levels","fixture"), area=Path.Combine(level,"areas","1");
        Directory.CreateDirectory(area);
        Directory.CreateDirectory(Path.Combine(root,"actors"));
        string model=Path.Combine(area,"model.inc.c"),geo=Path.Combine(area,"geo.inc.c"),collision=Path.Combine(area,"collision.inc.c");
        File.WriteAllText(model,"/* fixture */\n"); File.WriteAllText(Path.Combine(level,"level.yaml"),"texture-bin: outside\n");
        File.WriteAllText(Path.Combine(level,"script.c"),"AREA(1, fixture_geo), END_AREA(),");
        File.WriteAllText(geo,"const GeoLayout fixture_geo[] = { GEO_CAMERA(0,0,0,0,0,0,0,camera_main), GEO_OPEN_NODE(), GEO_CLOSE_NODE(), GEO_END(), };");
        File.WriteAllText(collision,"const Collision fixture_collision[] = { COL_INIT(), COL_VERTEX_INIT(0), COL_TRI_STOP(), COL_END(), };");
        LevelMeshCreation.Append(model,geo,collision,LevelMeshCreation.Primitive(Vector3.Zero,500,false),true,root);
        Exception? failure=null;
        OpenTK.Windowing.Desktop.GLFWProvider.CheckForMainThread=false;
        var thread=new Thread(()=>
        {
            LevelEditorWindow? window=null;
            try
            {
                Application? app=null; Window? host=null;
                if(shutdown)
                {
                    // Exercise the shutdown setting from the application source without acquiring its user mutex.
                    var sourceDirectory=new DirectoryInfo(AppContext.BaseDirectory);
                    while(sourceDirectory!=null && !File.Exists(Path.Combine(sourceDirectory.FullName,"App.xaml"))) sourceDirectory=sourceDirectory.Parent;
                    string mode=System.Xml.Linq.XDocument.Load(Path.Combine(sourceDirectory!.FullName,"App.xaml")).Root!.Attribute("ShutdownMode")!.Value;
                    app=new Application { ShutdownMode=Enum.Parse<ShutdownMode>(mode) };
                    if(app.ShutdownMode!=ShutdownMode.OnMainWindowClose) throw new Exception("App.xaml must shut down when its main window closes");
                    host=new Window { Width=200,Height=100,Left=-16000,Top=0,ShowInTaskbar=false,ShowActivated=false };
                    app.MainWindow=host;
                    host.Show();
                }
                var scene=LevelSceneLoader.Load(root,level,1,"outside");
                var col=new CollisionParser().ParseCollisionFile(collision,"Area 1","fixture")!;
                var objects=new List<LevelObject> { new() { ModelName="MODEL_NONE", X=123, Y=456, Z=789 } };
                window=new LevelEditorWindow(objects,col,scene,root,1,new(),new AppSettings(),collision)
                    { Left=-16000,Top=0,ShowActivated=false,ShowInTaskbar=false };
                int destructiveReloads=0,geometryChanges=0,closes=0;
                window.RequestReload+=(_,_)=> { destructiveReloads++; window.Close(); };
                window.GeometryChanged+=(_,_)=>geometryChanges++;
                window.Closed+=(_,_)=>closes++;
                window.Show();
                if(host!=null) window.Owner=host;
                var timer=new DispatcherTimer { Interval=TimeSpan.FromSeconds(1) }; int phase=0;
                ModelImportPreviewWindow? placement=null;
                Task<bool>? placementResult=null;
                byte[]? modelBefore=null;
                timer.Tick+=(_,_)=>
                {
                    try
                    {
                        object? Field(string name)=>typeof(LevelEditorWindow).GetField(name,BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(window);
                        if(phase++==0)
                        {
                            typeof(LevelEditorWindow).GetMethod("LevelMeshEditorButton_Click",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(window,new object[]{window,new RoutedEventArgs()});
                            var imported=ModelImportService.Import(modelFile,new Vector3(100,0,100),100);
                            string importedSymbol=LevelMeshCreation.Append(model,geo,collision,imported,true,root);
                            // Invoke the exact embedded import/save callback, including the host's destructive reload subscription.
                            typeof(LevelEditorWindow).GetMethod("ReloadEmbeddedGeometry",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(window,null);
                            if(destructiveReloads!=0 || closes!=0) throw new Exception("Import requested closing the level editor");
                            if(geometryChanges!=1) throw new Exception("Main window geometry cache was not notified");
                            if(((VisualMesh)Field("_visualScene")!).Triangles.Count!=scene.Triangles.Count+imported.Triangles.Count) throw new Exception("Imported triangles missing after reload");
                            if(!ReferenceEquals(Field("_objects"),objects)||objects[0].X!=123) throw new Exception("Object edits lost on mesh reload");
                            if(modelessPlacement)
                            {
                                var editor=(LevelMeshEditorWindow)Field("_embeddedMeshEditor")!;
                                placement=new ModelImportPreviewWindow(ImportedModelPlacement.AtBase(imported).Local,"fixture.glb",new Vector3(50,100,50),1)
                                    { Owner=window,ShowInTaskbar=false,ShowActivated=false,Left=-16000,Top=0,WindowStartupLocation=WindowStartupLocation.Manual };
                                modelBefore=File.ReadAllBytes(model);
                                placementResult=(Task<bool>)typeof(LevelMeshEditorWindow).GetMethod("ShowPlacementAsync",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(editor,new object?[]{placement,importedSymbol})!;
                                if(!IsWindowEnabled(new WindowInteropHelper(window).Handle)) throw new Exception("Production preview session disabled level editor");
                                placement.BeginPickInLevel();
                                var renderer=(Sm64DecompLevelViewer.Rendering.GeometryRenderer)Field("_renderer")!;
                                if(!renderer.TerrainPlacementEnabled || !renderer.TryPlaceOnTerrainAtScreenPosition(new Vector2(renderer.ClientSize.X/2f,renderer.ClientSize.Y/2f),true))
                                    throw new Exception("Production editor did not activate terrain picking");
                            }
                        }
                        else
                        {
                            if(modelessPlacement && phase==2)
                            {
                                if(placement==null || !placement.IsVisible || placement.Result==null) throw new Exception("Actual renderer terrain-pick callback failed to reopen and position preview");
                                placement.Close();
                                return;
                            }
                            if(modelessPlacement)
                            {
                                if(placementResult==null || !placementResult.IsCompletedSuccessfully || placementResult.Result) throw new Exception("Production cancel failed to return a canceled preview");
                                if(((Sm64DecompLevelViewer.Rendering.GeometryRenderer)Field("_renderer")!).TerrainPlacementEnabled) throw new Exception("Canceled placement kept intercepting terrain clicks");
                                if(!File.ReadAllBytes(model).SequenceEqual(modelBefore!)) throw new Exception("Terrain placement wrote model before confirmation");
                                Console.WriteLine("INTEGRATED PLACEMENT PASS: production session remains nonmodal, native level enabled, real renderer pick delivered on UI dispatcher, cancel disarms picking and leaves source unchanged");
                            }
                            if(!window.IsVisible || Field("_renderer")==null || closes!=0) throw new Exception("Editor or renderer closed after imported scene rendered");
                            timer.Stop();
                            if(shutdown) host!.Close();
                            else { window.Close(); Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
                            Console.WriteLine("IMPORT RELOAD PASS: textured user GLB appended, scene/collision reloaded, renderer survived, editor and mesh dock remained open, object state preserved, no destructive reload");
                        }
                    }
                    catch(Exception ex) { failure=ex; timer.Stop(); window.Close(); Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
                };
                timer.Start();
                if(app!=null) app.Run(host!); else Dispatcher.Run();
            }
            catch(Exception ex) { failure=ex; window?.Close(); }
        });
        try { thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join(); if(failure!=null) throw failure; }
        finally { Directory.Delete(root,true); }
    }
}
