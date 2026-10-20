using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Desktop;
using Sm64DecompLevelViewer.Models;
using Sm64DecompLevelViewer.Rendering;

internal static class LevelRenderCapture
{
    public static void Capture(string root, VisualMesh scene, CollisionMesh collision, List<LevelObject> objects, string output,
        Vector3? focus = null,float distance = 0,Vector3? direction = null)
    {
        using var renderer = new GeometryRenderer(new GameWindowSettings { UpdateFrequency = 30 },
            new NativeWindowSettings { Size = new Vector2i(1000, 700), StartVisible = false, Title = "Level regression capture" }, root);
        renderer.LoadVisualMesh(scene);
        renderer.LoadMesh(collision);
        renderer.SetSkyboxBackground(scene.SkyboxBin);
        renderer.SetObjects(objects);
        renderer.ShowCollision = false;
        renderer.ShowObjectMarkers = false;
        renderer.LevelActorsEnabled = true;
        renderer.AnimateLevelActors = false;
        if (focus.HasValue) renderer.SetCameraPositionAndTarget(focus.Value+Vector3.Normalize(direction ?? new Vector3(1,0.3f,1))*distance,focus.Value);
        else renderer.FrameLevel();
        int frames = 0;
        byte[]? empty = null;
        byte[]? actorsBeforeReload = null;
        renderer.RenderFrame += _ =>
        {
            frames++;
            if (frames is not (7 or 10 or 12 or 14 or 16 or 20)) return;
            int width = renderer.ClientSize.X, height = renderer.ClientSize.Y;
            var pixels = new byte[width * height * 4];
            GL.ReadBuffer(ReadBufferMode.Front);
            GL.ReadPixels(0, 0, width, height, OpenTK.Graphics.OpenGL4.PixelFormat.Bgra, PixelType.UnsignedByte, pixels);
            if (GL.GetError() != ErrorCode.NoError) throw new Exception("OpenGL level render error");
            if (frames == 20)
            {
                if (!pixels.SequenceEqual(actorsBeforeReload!)) throw new Exception("Actor textures changed after a texture-cache reload");
                Console.WriteLine("LEVEL RELOAD PASS: actor material pixels survive texture-cache reload");
                renderer.Close();
                return;
            }
            if (frames == 10)
            {
                empty = pixels;
                renderer.SkyboxEnabled = true;
                return;
            }
            if (frames >= 12)
            {
                string feature = frames == 12 ? "skybox" : frames == 14 ? "water" : "actors";
                bool expected = frames == 12 ? scene.SkyboxBin != null : frames == 14 ? collision.WaterBoxes.Count > 0 : objects.Any(o => o.ModelName != "MODEL_NONE");
                if (expected && pixels.SequenceEqual(empty!)) throw new Exception(feature + " toggle did not change the rendered view");
                Console.WriteLine($"LEVEL TOGGLE PASS: {scene.LevelName} {feature}");
                renderer.SkyboxEnabled = false;
                renderer.WaterEnabled = false;
                if (frames == 12) renderer.WaterEnabled = true;
                if (frames == 14) renderer.LevelActorsEnabled = true;
                if (frames == 16) { actorsBeforeReload = pixels; renderer.ReloadTexturesThreadSafe(); }
                return;
            }
            var upright = new byte[pixels.Length];
            for (int row = 0; row < height; row++) Array.Copy(pixels, row * width * 4, upright, (height - row - 1) * width * 4, width * 4);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, upright, width * 4)));
            using (var stream = File.Create(output)) encoder.Save(stream);
            Console.WriteLine($"LEVEL RENDER: {scene.LevelName}, {renderer.LoadedActorModels} actor models loaded; {output}");
            renderer.ShowVisual = false;
            renderer.LevelActorsEnabled = false;
            renderer.SkyboxEnabled = false;
            renderer.WaterEnabled = false;
        };
        renderer.Run();
    }
}
