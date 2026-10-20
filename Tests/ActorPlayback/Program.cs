using System.IO;
using OpenTK.Mathematics;
using Sm64DecompLevelViewer.Models;
using Sm64DecompLevelViewer.Services;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

string fixture = Path.Combine(Path.GetTempPath(), "Randm64-Geo-" + Guid.NewGuid().ToString("N") + ".c");
try
{
    File.WriteAllText(fixture, """
        #ifdef VERSION_JP
        const GeoLayout test_geo[] = { GEO_ANIMATED_PART(0, 0, 0, 0, wrong_region), GEO_END(), };
        #else
        const GeoLayout test_geo[] = {
          GEO_SCALE(0, 16384), GEO_OPEN_NODE(),
            GEO_ANIMATED_PART(0, 0, 0, 0, root), GEO_OPEN_NODE(),
              GEO_ANIMATED_PART(0, 40, 0, 0, left), GEO_OPEN_NODE(),
                GEO_DISPLAY_LIST(0, left_detail),
              GEO_CLOSE_NODE(),
              GEO_ANIMATED_PART(0, -40, 0, 0, right),
              GEO_SWITCH_CASE(0, callback), GEO_OPEN_NODE(),
                GEO_SWITCH_CASE(0, eyes), GEO_OPEN_NODE(),
                  GEO_DISPLAY_LIST(0, face), GEO_DISPLAY_LIST(0, alternate_face),
                GEO_CLOSE_NODE(),
                GEO_ANIMATED_PART(0, 999, 999, 999, alternate_body),
              GEO_CLOSE_NODE(),
            GEO_CLOSE_NODE(),
          GEO_CLOSE_NODE(), GEO_END(),
        };
        #endif
        static const s16 npc_values[] = { 0, 64 };
        static const u16 npc_indices[] = { 1, 0, 1, 0, 1, 0, 1, 1, 1, 0, 1, 0 };
        static const struct Animation npc_animation = {
          0, 0, 0, 0, 4, ANIMINDEX_NUMPARTS(npc_indices), npc_values, npc_indices, 0,
        };
        """);
    var geo = new GeoLayoutParser();
    geo.ParseActorGeoLayout(fixture);
    Check(geo.ParsedJoints.Count == 3, "Only the active switch branch consumes animation channels");
    Check(geo.ParsedJoints[1].ParentIndex == 0 && geo.ParsedJoints[2].ParentIndex == 0, "Sibling joints share their parent");
    Check(geo.ParsedJoints[1].RestWorldMatrix.Row3.X == 10 && geo.ParsedJoints[2].RestWorldMatrix.Row3.X == -10,
        "Sibling translations must not accumulate");
    Check(geo.DisplayListInstances.Count == 5 && geo.DisplayListInstances.All(i => !i.DisplayListName.StartsWith("alternate")),
        "Nested switch selects one face and body");
    Check(geo.DisplayListInstances.Single(i => i.DisplayListName == "face").JointIndex == 0,
        "Switch siblings inherit the containing joint rather than the preceding sibling");
    Console.WriteLine("PASS: hierarchy, sibling transforms, nested switches, and joint attachment");
    var npcAnimation = new MarioAnimation { FilePath = fixture, Symbol = "npc_animation" };
    new AnimationParser().LoadAnimationData(npcAnimation);
    Check(npcAnimation.FrameCount == 4 && npcAnimation.Indices[0] == 1 && npcAnimation.Values[1] == 64,
        "Non-Mario scalar headers use declared frame counts and their own value/index arrays");
    Console.WriteLine("PASS: regional source selection and non-Mario animation headers");

    var animation = new MarioAnimation { FrameCount = 8, LoopStart = 2, Flags = 1 };
    Check(animation.AdvanceFrame(7, 1, false) == 7, "Nonlooping game animation holds its final frame");
    Check(animation.AdvanceFrame(7, 1, true) == 2, "Preview loops at the declared loop start");
    animation.Flags = 2;
    Check(animation.AdvanceFrame(2, 1, false) == 7, "Reverse animation wraps correctly");
    Check(animation.AdvanceFrame(3, 1000, true) is >= 2 and <= 7, "Delayed updates stay within the frame range");
    Console.WriteLine("PASS: animation looping, one-shot flags, reverse playback, and delayed updates");

    if (args.Length > 0)
    {
        string root = args[0];
        var parser = new AnimationParser();
        var animations = parser.ScanAnimations(root);
        Check(animations.Count >= 200, "Find every Mario animation, including shared-file headers");
        foreach (var anim in animations) parser.LoadAnimationData(anim);
        Check(animations.Single(a => a.Symbol == "anim_01").FrameCount == 80 &&
            animations.Single(a => a.Symbol == "anim_02").FrameCount == 1, "Shared arrays retain distinct header frame counts");
        var idle = animations.Single(a => a.Symbol.Equals("anim_C5", StringComparison.OrdinalIgnoreCase));
        string actor = Path.Combine(root, "actors", "mario");
        var transforms = geo.ParseActorGeoLayout(Path.Combine(actor, "geo.inc.c"));
        Check(geo.ParsedJoints.Count == (idle.Indices.Length - 6) / 6, "Mario skeleton matches the animation channel count");
        Check(geo.DisplayListInstances.Count(i => i.DisplayListName.Contains("eyes")) == 1, "Render one Mario face");
        Check(geo.DisplayListInstances.All(i => !i.DisplayListName.Contains("poly") && !i.DisplayListName.Contains("wings")),
            "Normal Mario excludes alternate LODs and wing-cap geometry");
        var modelParser = new ModelParser();
        var mesh = modelParser.ParseModelFile(Path.Combine(actor, "model.inc.c"), "", "mario", transforms,
            geo.DlToJointIndex, geo.DisplayListInstances, geo.RootScale) ?? throw new Exception("Mario mesh failed");
        mesh.Joints = geo.ParsedJoints;
        modelParser.ParseActorTextureMapping(actor, Path.Combine(actor, "model.inc.c"), mesh, root);
        Check(mesh.Triangles.Count > 500 && mesh.Vertices.All(v => v.JointIndex >= 0), "Complete Mario is attached to joints");
        Check(mesh.Triangles.Count(t => t.TextureName == "mario_beige_lights_group") > 50 &&
            mesh.Triangles.Count(t => t.TextureName == "mario_red_lights_group") > 50,
            "Texture-off restores solid skin/cap/shirt materials after textured face details");
        Check(mesh.Triangles.All(t => t.TextureName != null), "Shared forearms, legs, and overalls retain the preceding material state");
        Check(mesh.Triangles.Where(t => t.TextureName == "mario_texture_eyes_front").All(t =>
            t.BlendTextureWithShade && t.ShadeLightSymbol == "mario_beige_lights_group"),
            "Eye decal alpha blends into skin shading instead of exposing the cap behind it");
        var faceDecals = mesh.Triangles.Where(t => t.BlendTextureWithShade && t.ShadeLightSymbol == "mario_beige_lights_group").ToList();
        Check(faceDecals.Count > 0 && faceDecals.All(t => t.TextureAddressS == TextureAddressMode.Clamp &&
            t.TextureAddressT == TextureAddressMode.Clamp), "Face decals preserve the game’s clamped render tile");
        Check(faceDecals.SelectMany(t => new[] { mesh.Vertices[t.V1], mesh.Vertices[t.V2], mesh.Vertices[t.V3] })
            .Any(v => v.S < 0 || v.S > 1024 || v.T < 0 || v.T > 1024),
            "Real out-of-range Mario UVs exercise clamping rather than texture repetition");
        foreach (var anim in animations)
        {
            foreach (int frame in new[] { 0, anim.FrameCount / 2, anim.FrameCount - 1 })
            {
                var pose = parser.GetFrameTransforms(anim, frame, mesh.Joints, mesh.RootScale);
                for (int i = 1; i < mesh.Joints.Count; i++)
                {
                    int parent = mesh.Joints[i].ParentIndex;
                    if (parent < 0) continue;
                    float actualLength = (pose[i].Row3.Xyz - pose[parent].Row3.Xyz).Length;
                    float bindLength = (mesh.Joints[i].RestWorldMatrix.Row3.Xyz - mesh.Joints[parent].RestWorldMatrix.Row3.Xyz).Length;
                    Check(Math.Abs(actualLength - bindLength) < 0.02, $"{anim.Name}: joint {i} remains connected at frame {frame}");
                }
            }
        }
        parser.ApplyFrame(mesh, idle, 0);
        int height = mesh.Vertices.Max(v => v.Y) - mesh.Vertices.Min(v => v.Y);
        Check(height is > 100 and < 250, $"Idle Mario has an in-game sized, assembled body (height {height})");
        Console.WriteLine($"PASS: all {animations.Count} real Mario headers and poses; {mesh.Joints.Count} joints, {mesh.Triangles.Count} triangles, height {height}");
        GeneralActorChecks.Run(root, args.Length > 1 ? Path.GetDirectoryName(args[1]) : null);
        if (args.Length > 1)
        {
            ActorRenderCapture.Capture(root, mesh, idle, args[1]);
            var running = animations.Single(a => a.Symbol.Equals("anim_72", StringComparison.OrdinalIgnoreCase));
            ActorRenderCapture.Capture(root, mesh, running,
                Path.Combine(Path.GetDirectoryName(args[1]) ?? "", Path.GetFileNameWithoutExtension(args[1]) + "-running.png"));
        }
    }
    Console.WriteLine("All actor playback checks passed.");
}
finally
{
    File.Delete(fixture);
}
