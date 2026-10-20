using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Sm64DecompLevelViewer;
using Sm64DecompLevelViewer.Models;
using Sm64DecompLevelViewer.Services;

internal static class LevelEditorLayoutCheck
{
    public static void Run(string root, string output)
    {
        // Match MainWindow's existing dedicated OpenGL thread configuration.
        OpenTK.Windowing.Desktop.GLFWProvider.CheckForMainThread = false;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            LevelEditorWindow? window = null;
            try
            {
                string directory = Path.Combine(root, "levels", "castle_grounds");
                var mesh = LevelSceneLoader.Load(root, directory, 1, "outside");
                string collisionPath = Path.Combine(directory, "areas", "1", "collision.inc.c");
                var collision = new CollisionParser().ParseCollisionFile(collisionPath, "Area 1", "castle_grounds")!;
                var objects = new ObjectParser().ParseScriptFile(Path.Combine(directory, "script.c"), 1);
                window = new LevelEditorWindow(objects, collision, mesh, root, 1, new List<string>(), new AppSettings(), collisionPath)
                { ShowActivated = false, ShowInTaskbar = false, Left = -16000, Top = 0 };
                window.Show();
                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    try
                    {
                        window.UpdateLayout();
                        typeof(LevelEditorWindow).GetMethod("LevelMeshEditorButton_Click",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!
                            .Invoke(window,new object[]{window,new RoutedEventArgs()});
                        window.UpdateLayout();
                        var dock = (Border)window.FindName("MeshDock");
                        if (dock.Child == null || dock.ActualHeight < 100) throw new Exception("Embedded mesh editor did not open");
                        foreach (string name in new[] { "TerrainViewCheckBox", "SkyboxViewCheckBox", "WaterViewCheckBox", "ActorsViewCheckBox", "AnimateActorsCheckBox", "CollisionViewCheckBox", "MarkersViewCheckBox" })
                        {
                            var control = (FrameworkElement)window.FindName(name);
                            if (!control.IsVisible || control.ActualWidth < 20 || control.ActualHeight < 10)
                                throw new Exception("Hidden or clipped view control: " + name);
                        }
                        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                        bitmap.Render(window);
                        var encoder = new PngBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(bitmap));
                        using (var stream = File.Create(output)) encoder.Save(stream);
                        window.Width = window.MinWidth;
                        window.Height = window.MinHeight;
                        window.UpdateLayout();
                        var viewport = (FrameworkElement)window.FindName("RendererContainer");
                        if (viewport.ActualWidth < 450 || viewport.ActualHeight < 100) throw new Exception("Viewport collapsed at minimum window size");
                        Console.WriteLine("LEVEL UI PASS: view controls, default layout, minimum window size, and renderer shutdown");
                    }
                    catch (Exception ex) { failure = ex; }
                    finally { window.Close(); Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
                };
                timer.Start();
                Dispatcher.Run();
            }
            catch (Exception ex) { failure = ex; window?.Close(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null) throw failure;
    }
}
