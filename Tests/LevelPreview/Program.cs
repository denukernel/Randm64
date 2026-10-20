using System.IO;
using System.Text.RegularExpressions;
using Sm64DecompLevelViewer.Models;
using Sm64DecompLevelViewer.Services;

if (args.Length == 0) throw new ArgumentException("Pass the decomp repository path.");
string root = args[0];
if(root=="--recovery") { LevelRecoveryChecks.Run(); return; }
if(root=="--recovery-objects") { LevelRecoveryChecks.CheckGameObjects(args[1],args.Length>2 ? args[2] : null); return; }
if(root=="--runtime-fixture") { RuntimeImportFixture.Write(args[1],args[2]); return; }
if(root=="--audit-saving") { LevelSavingAudit.Run(args[1]); return; }
if(root=="--saving") { LevelSaveChecks.Run(); return; }
if(root=="--integrated-placement") { ImportReloadChecks.Run(args[1],modelessPlacement:true); return; }
if(root=="--modeless-placement") { ModelessPlacementChecks.Run(); return; }
if(root=="--existing-placement") { ModelPlacementChecks.CheckExisting(args[1]); return; }
if(root=="--placement-preview") { ImportPreviewChecks.Run(args[2],args[3],args[1]); return; }
if(root=="--shutdown") { ShutdownChecks.Run(args[1]); return; }
if(root=="--shutdown-child") { ImportReloadChecks.Run(args[1],true); return; }
if(root=="--placement") { ModelPlacementChecks.Run(args[1]); return; }
if(root=="--import-reload") { ImportReloadChecks.Run(args[1]); return; }
if(root=="--import-preview") { ImportPreviewChecks.Run(args[1],args[2]); return; }
if(root=="--import") { ModelImportChecks.Run(args.Length>1 ? args[1] : null); return; }
if(args.Length>1 && args[1]=="--audit-objects") { LevelObjectMaterialAudit.Run(root); return; }
if(args.Length>1 && args[1]=="--shared-references") { LevelObjectMaterialAudit.CheckSharedReferences(root); return; }
if(args.Length>2 && args[1]=="--object-captures") { LevelObjectMaterialAudit.CaptureExamples(root,args[2]); return; }
EditorRegressionChecks.Run();
if (root == "--core") return;
if (args.Length > 2 && args[2] == "--ui")
{
    LevelEditorLayoutCheck.Run(root, Path.Combine(args[1], "level-editor-interface.png"));
    return;
}
var failures = new List<string>();
CastleTowerChecks.Run(root,args.Length>1 ? args[1] : null);
if (args.Length>2 && args[2]=="--tower") return;
int areas = 0;
foreach (string level in new[] { "castle_grounds", "bob", "wf", "jrb", "ddd", "wdw", "hmc", "ccm", "ttm", "castle_inside", "ssl", "lll", "thi" })
{
    string directory = Path.Combine(root, "levels", level);
    string yaml = File.ReadAllText(Path.Combine(directory, "level.yaml"));
    string textureBin = Regex.Match(yaml, @"texture-bin:\s*(\w+)").Groups[1].Value;
    foreach (string areaDirectory in Directory.GetDirectories(Path.Combine(directory, "areas")))
    {
        if (!int.TryParse(Path.GetFileName(areaDirectory), out int area)) continue;
        try
        {
            var scene = LevelSceneLoader.Load(root, directory, area, textureBin);
            if (scene.Triangles.Count == 0 || scene.SubMeshes.Count != 0) throw new Exception("No scene graph geometry");
            if (scene.Triangles.Any(t => Math.Max(t.V1, Math.Max(t.V2, t.V3)) >= scene.Vertices.Count)) throw new Exception("Invalid triangle indices");
            var unknown = scene.Triangles.Select(t => t.TextureName).Where(name => name != null &&
                !scene.TexturePaths.ContainsKey(name) && !scene.LightGroupColors.ContainsKey(name)).Distinct().ToList();
            if (unknown.Count > 0) throw new Exception("Unresolved materials: " + string.Join(",", unknown));
            string collisionPath = Path.Combine(areaDirectory, "collision.inc.c");
            var collision = File.Exists(collisionPath) ? new CollisionParser().ParseCollisionFile(collisionPath, "Area " + area, level)! : new CollisionMesh();
            var water = LevelWaterBuilder.Build(root, scene, collision);
            if (water.Triangles.Any(t => t.TextureName == null || !water.TexturePaths.ContainsKey(t.TextureName)))
                throw new Exception("Unresolved moving-surface texture");
            if (level == "ssl" && area == 1 && !water.Vertices.Any(v => v.X == -4096 && v.Y == -256 && v.Z == 1024 && v.S == 0 && v.T == 0))
                throw new Exception("Flowing sand interpreted its normals as texture coordinates");
            if (level == "castle_grounds")
            {
                if (scene.SkyboxBin != "water") throw new Exception("Castle grounds skybox not resolved");
                if (water.Vertices.Count(v => v.Y == -81) < 8) throw new Exception("Moat/lake are not at their actual -81 height");
                if (water.Triangles.Count <= 4) throw new Exception("Vertical waterfall is missing");
                if (water.TexturePaths.Count == 0 || water.Triangles.Any(t => t.Opacity <= 0 || t.Opacity >= 1)) throw new Exception("Water texture/alpha missing");
                int before = water.Vertices.Count(v => v.Y == -81);
                collision.WaterBoxes[0].Y = 123;
                var changed = LevelWaterBuilder.Build(root, scene, collision);
                if (changed.Vertices.Count(v => v.Y == 123) < 4) throw new Exception("Live water-height edit not reflected");
                collision.WaterBoxes[0].Y = -81;
            }
            if (scene.SkyboxBin != null && !File.Exists(Path.Combine(root, "textures", "skyboxes", scene.SkyboxBin + ".png")))
                throw new Exception("Missing skybox asset");
            if (args.Length > 1 && area == 1 && level is "castle_grounds" or "bob" or "jrb")
            {
                var objects = new ObjectParser().ParseScriptFile(Path.Combine(directory, "script.c"), area);
                var macroParser = new MacroObjectParser();
                var presets = macroParser.ParsePresets(Path.Combine(root, "include", "macro_presets.inc.c"));
                foreach (string macro in Directory.GetFiles(areaDirectory, "macro.inc.c", SearchOption.AllDirectories))
                    objects.AddRange(macroParser.ParseMacroFile(macro, presets));
                LevelRenderCapture.Capture(root, scene, collision, objects, Path.Combine(args[1], level + "-level.png"));
            }
            Console.WriteLine($"LEVEL PASS: {level}/{area}; {scene.Triangles.Count} triangles, {water.Triangles.Count} water triangles; sky={scene.SkyboxBin ?? "none"}");
            areas++;
        }
        catch (Exception ex) { failures.Add($"{level}/{area}: {ex.Message}"); Console.WriteLine("LEVEL FAIL: " + failures[^1]); }
    }
}
var catalog = new LevelActorCatalog(root, Path.Combine(root, "levels", "bob"));
var butterfly = ActorModelLoader.Load(root,"butterfly");
if (butterfly.Triangles.Any(t => t.TextureName == null || !butterfly.TexturePaths.ContainsKey(t.TextureName)))
    failures.Add("Butterfly load-texture macro lost its alpha texture");
if (butterfly.Vertices.Any(v => v.S > 1024 || v.T > 2048)) failures.Add("Butterfly half-scale texture coordinates were ignored");
foreach (string model in new[] { "MODEL_GOOMBA", "MODEL_BLACK_BOBOMB", "MODEL_MARIO", "MODEL_BOB_BUBBLY_TREE" })
    if (catalog.Resolve(model) == null) failures.Add("Actor model failed: " + model);
if (catalog.Resolve("MODEL_NONE") != null) failures.Add("Invisible triggers should not render an actor");
Console.WriteLine($"LEVEL TOTAL: {areas} areas; {failures.Count} failures");
if (failures.Count > 0) throw new Exception(string.Join(Environment.NewLine, failures));
