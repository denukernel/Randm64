using System.IO;
using OpenTK.Mathematics;
using Sm64DecompLevelViewer.Models;
using Sm64DecompLevelViewer.Services;

internal static class GeneralActorChecks
{
    public static void Run(string root, string? captureDirectory)
    {
        var failures = new List<string>();
        var parser = new AnimationParser();
        int actors = 0, animations = 0;
        foreach (string directory in Directory.GetDirectories(Path.Combine(root, "actors")).Order())
        {
            string name = Path.GetFileName(directory);
            if (name == "mario" || !File.Exists(Path.Combine(directory, "geo.inc.c")) ||
                !File.Exists(Path.Combine(directory, "model.inc.c"))) continue;
            try
            {
                var mesh = ActorModelLoader.Load(root, name);
                if (mesh.Triangles.Count == 0) throw new Exception("No triangles");
                var unresolved = mesh.Triangles.Select(triangle => triangle.TextureName).Where(symbol => symbol != null &&
                    !mesh.TexturePaths.ContainsKey(symbol) && !mesh.LightGroupColors.ContainsKey(symbol)).Distinct().ToList();
                if (unresolved.Count > 0) throw new Exception("Unresolved materials: " + string.Join(", ", unresolved));
                if (mesh.Triangles.Any(triangle => triangle.V1 < 0 || triangle.V2 < 0 || triangle.V3 < 0 ||
                    Math.Max(triangle.V1, Math.Max(triangle.V2, triangle.V3)) >= mesh.Vertices.Count)) throw new Exception("Invalid triangle indices");
                var clips = parser.ScanActorAnimations(root, name, mesh.Joints.Count);
                foreach (var clip in clips)
                {
                    parser.LoadAnimationData(clip);
                    if ((clip.Indices.Length - 6) / 6 < mesh.Joints.Count)
                        throw new Exception($"Animation {clip.Symbol} has {(clip.Indices.Length - 6) / 6} bones, model has {mesh.Joints.Count}");
                    foreach (int frame in new[] { 0, clip.FrameCount / 2, clip.FrameCount - 1 })
                    {
                        parser.ApplyFrame(mesh, clip, frame);
                        if (mesh.Vertices.Any(vertex => Math.Abs((long)vertex.X) > 10000000 || Math.Abs((long)vertex.Y) > 10000000 ||
                            Math.Abs((long)vertex.Z) > 10000000)) throw new Exception("Exploded animation geometry");
                    }
                    animations++;
                }
                parser.ApplyFrame(mesh, clips.FirstOrDefault(), 0);
                if (name is "goomba" or "bobomb")
                {
                    if (!mesh.HasBillboards || !mesh.Vertices.Any(vertex => vertex.Billboard != null)) throw new Exception("Missing billboard body");
                    if (!mesh.Triangles.Any(triangle => !triangle.LightingEnabled && triangle.DecalTexture)) throw new Exception("Sprite must use unlit decal material");
                    var sprite = mesh.Vertices.First(vertex => vertex.Billboard != null && Math.Abs(vertex.BillboardPosition.X) > 1);
                    var initial = new Vector3(sprite.X, sprite.Y, sprite.Z);
                    parser.ApplyFrame(mesh, clips.FirstOrDefault(), 0, Vector3.UnitX);
                    if ((new Vector3(sprite.X, sprite.Y, sprite.Z) - initial).Length < 1) throw new Exception("Billboard did not face the camera");
                }
                if (captureDirectory != null && name is "goomba" or "bobomb" or "koopa" or "bowser" or "boo")
                    ActorRenderCapture.Capture(root, mesh, AnimationParser.SelectDefaultAnimation(clips, name), Path.Combine(captureDirectory, name + "-preview.png"));
                Console.WriteLine($"ACTOR PASS: {name}; {mesh.Triangles.Count} triangles, {mesh.Joints.Count} bones, {clips.Count} animations");
                actors++;
            }
            catch (Exception ex)
            {
                failures.Add(name + ": " + ex.Message);
                Console.WriteLine("ACTOR FAIL: " + failures[^1]);
            }
        }
        Console.WriteLine($"ACTOR TOTAL: {actors} actors, {animations} non-Mario animations; {failures.Count} failures");
        if (failures.Count > 0) throw new Exception(string.Join(Environment.NewLine, failures));
    }
}
