using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using Sm64DecompLevelViewer.Models;
using Sm64DecompLevelViewer.Rendering;
using Sm64DecompLevelViewer.Services;

internal static class ActorRenderCapture
{
    public static void Capture(string root, VisualMesh mesh, MarioAnimation? animation, string output)
    {
        using var renderer = new GeometryRenderer(new GameWindowSettings { UpdateFrequency = 30 },
            new NativeWindowSettings { Size = new Vector2i(600, 600), StartVisible = false, Title = "Actor regression capture" }, root);
        renderer.LoadVisualMesh(mesh);
        renderer.UseActorLighting = false;
        renderer.SetCameraPositionAndTarget(new Vector3(200, 140, 310), new Vector3(0, 80, 0));
        if (mesh.LevelName != "mario")
        {
            var facing = Vector3.Normalize(new Vector3(0.3f, 0.2f, 1));
            var bounds = ActorModelLoader.GetPreviewBounds(mesh, animation, facing);
            var center = (bounds.Min + bounds.Max) / 2;
            float distance = Math.Max(80, (bounds.Max - bounds.Min).Length * 1.4f);
            renderer.SetCameraPositionAndTarget(center + facing * distance, center);
        }
        renderer.SetActiveAnimation(animation);
        renderer.SetAnimationPlaying(false);
        int frames = 0;
        int pausedFrame = -1;
        byte[]? dimLighting = null;
        renderer.RenderFrame += _ =>
        {
            frames++;
            if (frames == 4) renderer.SetAnimationPlaying(true);
            if (frames == 14)
            {
                if (animation?.FrameCount > 1 && renderer.AnimationFrame == 0) throw new Exception("Renderer did not advance the animation.");
                renderer.SetAnimationPlaying(false);
                pausedFrame = renderer.AnimationFrame;
            }
            if (frames == 18)
            {
                if (renderer.AnimationFrame != pausedFrame) throw new Exception("Paused animation continued advancing.");
                renderer.SeekAnimationFrame((animation?.FrameCount ?? 1) / 2);
            }
            if (frames != 22 && frames != 26) return;
            if (renderer.AnimationFrame != (animation?.FrameCount ?? 1) / 2) throw new Exception("Animation frame seeking failed.");
            int width = renderer.ClientSize.X, height = renderer.ClientSize.Y;
            byte[] pixels = new byte[width * height * 4];
            GL.ReadBuffer(ReadBufferMode.Front);
            GL.ReadPixels(0, 0, width, height, OpenTK.Graphics.OpenGL4.PixelFormat.Bgra, PixelType.UnsignedByte, pixels);
            if (frames == 22)
            {
                dimLighting = pixels;
                renderer.UseActorLighting = true;
                return;
            }
            long before = 0, after = 0;
            for (int i = 0; i < pixels.Length; i += 4)
            {
                if (pixels[i] == dimLighting![i] && pixels[i + 1] == dimLighting[i + 1] && pixels[i + 2] == dimLighting[i + 2]) continue;
                before += dimLighting[i] + dimLighting[i + 1] + dimLighting[i + 2];
                after += pixels[i] + pixels[i + 1] + pixels[i + 2];
            }
            if (mesh.LevelName == "mario" && (before == 0 || after <= before * 1.1)) throw new Exception("Actor key/fill lighting did not brighten the model.");
            Console.WriteLine($"PASS: actor key/fill lighting brightens the rendered model by {(double)after / before:0.00}x");
            byte[] upright = new byte[pixels.Length];
            for (int row = 0; row < height; row++)
                Array.Copy(pixels, row * width * 4, upright, (height - row - 1) * width * 4, width * 4);
            var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, upright, width * 4);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var file = File.Create(output)) encoder.Save(file);
            renderer.Close();
        };
        renderer.Run();
        Console.WriteLine("PASS: actual renderer play, pause, and frame scrubbing");
        Console.WriteLine("Captured real OpenGL actor view: " + output);
    }
}
