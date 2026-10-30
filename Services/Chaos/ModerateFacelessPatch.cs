using System.IO;
using System.Text.RegularExpressions;

namespace Sm64DecompLevelViewer.Services.Chaos;

internal static class ModerateFacelessPatch
{
    internal static string MapMaterial(string source, bool castle)
    {
        if (source.Contains("RANDM64_MODERATE_MATERIAL")) return source;
        source = "/* RANDM64_MODERATE_MATERIAL */\n" + source;
        if (castle)
            return source.Replace("G_CC_MODULATERGB, G_CC_MODULATERGB", "G_CC_FADEA, G_CC_FADEA");
        // Apply mapping where the material enables texturing, then clear it at its own revert.
        source = Regex.Replace(source, @"(gsSPTexture\([^\r\n]*G_ON\),)", "$1\n    gsSPSetGeometryMode(G_TEXTURE_GEN),");
        source = Regex.Replace(source, @"(gsSPTexture\([^\r\n]*G_OFF\),)", "$1\n    gsSPClearGeometryMode(G_TEXTURE_GEN | G_TEXTURE_GEN_LINEAR),");
        source = source.Replace("gsSPClearGeometryMode(G_LIGHTING)", "gsSPSetGeometryMode(G_LIGHTING)");
        return source;
    }

    internal const string Draw = "                gSPDisplayList(gDisplayListHead++, currList->displayList);";
    internal static readonly string CameraDraw = """
                /* RANDM64_MODERATE_FACELESS: colors depend on camera position, never time. */
                {
                    s32 phase = 0;
                    u8 red = 160;
                    u8 green = 160;
                    u8 blue = 160;
                    if (gCurrLevelNum == LEVEL_CASTLE_GROUNDS && gCurGraphNodeCamera != NULL) {
                        phase = (s32) ((gCurGraphNodeCamera->pos[0]
                                     + gCurGraphNodeCamera->pos[1]
                                     + gCurGraphNodeCamera->pos[2]) / 160.0f);
                        if (phase & 1) {
                            red = 192;
                            green = 48;
                            blue = 48;
                        }
                    }
                    if (gCurrLevelNum == LEVEL_CASTLE_GROUNDS) {
                        gDPPipeSync(gDisplayListHead++);
                        gDPSetEnvColor(gDisplayListHead++, red, green, blue, 255);
                    }
                    gSPDisplayList(gDisplayListHead++, currList->displayList);
                    if (gCurrLevelNum == LEVEL_CASTLE_GROUNDS) {
                        gDPPipeSync(gDisplayListHead++);
                        gDPSetEnvColor(gDisplayListHead++, 255, 255, 255, 255);
                    }
                }
""";

    internal static void Apply(string root, ChaosQueueState queue)
    {
        void Edit(string relative, Func<string, string> transform)
        {
            string path = Path.Combine(root, relative);
            string source = File.ReadAllText(path);
            string output = transform(source.Replace("\r\n", "\n"));
            if (output == source) return;
            if (!File.Exists(path + ".bak")) File.Copy(path, path + ".bak");
            File.WriteAllText(path, output);
            queue.FilesModified.Add(path);
        }
        Edit("src/game/rendering_graph_node.c", source =>
        {
            if (source.Contains("RANDM64_MODERATE_FACELESS")) return source;
            if (source.Contains("CHAOS_FACELESS_V2")) throw new InvalidDataException("Restore faceless v2 before applying the moderate variant.");
            if (!source.Contains(Draw)) throw new InvalidDataException("Unsupported renderer: display list draw site was not found.");
            return source.Replace(Draw, CameraDraw);
        });
        Edit("levels/castle_grounds/areas/1/1/model.inc.c", s => MapMaterial(s, false));
        Edit("levels/castle_grounds/areas/1/2/model.inc.c", s => MapMaterial(s, true));
        foreach (string actor in new[] { "wooden_signpost", "cannon_base", "cannon_barrel" })
            Edit($"actors/{actor}/model.inc.c", s => MapMaterial(s, false));
    }
}
