using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Sm64DecompLevelViewer.Services.Chaos;

internal static class AlternativeExclusionService
{
    internal static readonly string[] Descriptions =
    [
        "Seeded nested display-list exclusions.",
        "Hide all gameplay overlays, pause menus, dialog graphics and text. Menu input and pause logic still run.",
        "Hide overlays, warp transition masks (including Bowser), textured actor parts, level texture details, object shadows and waterfalls. Preserve transition timing, solid level surfaces and Mario.",
        "Also hide object models, water surfaces and other moving colored surfaces, keeping Mario visible.",
        "Also hide Mario left arm, hand, leg and foot across normal, metal and distance variants.",
        "Keep only Mario torso and hips. HUD and other actors remain missing.",
        "Keep only Mario torso; exclude its button details and level decal/alpha texture details. Main surface textures remain. Earlier exclusions remain active.",
        "Hide Mario completely. Keep skybox and surviving level geometry, with additional missing mesh batches (about one quarter).",
        "Hide roughly half the level mesh batches, preserving the stock castle exterior. Earlier overlay, actor, Mario and texture-detail exclusions remain active. The seed chooses the surviving batches."
    ];
    private static readonly Regex Saved = new(@"/\* RANDM64_ALT_EXCLUSION ([A-Za-z0-9+/=]+) ([A-F0-9]+) \*/([\s\S]*?)/\* RANDM64_ALT_END \*/");
    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    private static string Save(string before, string after) => before == after ? before :
        $"/* RANDM64_ALT_EXCLUSION {Convert.ToBase64String(Encoding.UTF8.GetBytes(before))} {Hash(after)} */{after}/* RANDM64_ALT_END */";

    internal static string Restore(string text)
    {
        string restored = Saved.Replace(text, m =>
        {
            if (Hash(m.Groups[3].Value) != m.Groups[2].Value) throw new InvalidDataException("An alternative exclusion was externally edited; restore refused.");
            return Encoding.UTF8.GetString(Convert.FromBase64String(m.Groups[1].Value));
        });
        if (restored.Contains("RANDM64_ALT_EXCLUSION")) throw new InvalidDataException("Invalid alternative exclusion restore marker.");
        return restored;
    }

    internal static void RestoreStage(string root, CancellationToken token)
    {
        foreach (string tree in new[] { "src", "actors", "levels" })
        {
            string dir = Path.Combine(root, tree);
            if (!Directory.Exists(dir)) continue;
            foreach (string path in Directory.EnumerateFiles(dir, "*.c", SearchOption.AllDirectories))
            {
                token.ThrowIfCancellationRequested();
                string source = File.ReadAllText(path);
                string clean = Restore(source);
                if (clean != source) File.WriteAllText(path, clean);
            }
        }
    }

    internal static string Mario(string source, int preset)
    {
        return Regex.Replace(source, @"\b(?:static\s+)?const\s+Gfx\s+(?<name>\w+)\s*\[\s*\]\s*=\s*\{(?<body>[\s\S]*?)\};", m =>
        {
            string name = m.Groups["name"].Value;
            bool keep = preset == 4 ? !Regex.IsMatch(name, @"_left_(?:arm|forearm|hand|thigh|leg|foot)") :
                Regex.IsMatch(name, @"_(?:torso|tshirt|pants_overalls|yellow_button)\b|_(?:torso|tshirt|pants_overalls|yellow_button)_") ||
                (preset == 5 && name.Contains("_butt"));
            if (preset == 6 && name.Contains("yellow_button")) keep = false;
            if (keep) return m.Value;
            return Regex.Replace(m.Value, @"\bgsSP(?:1Triangle|2Triangles)\s*\([^()]*\)", t => Save(t.Value, "gsDPNoOp()"));
        });
    }

    internal static DisplayListExclusionResult ApplyToStage(string root, int preset, uint seed, CancellationToken token)
    {
        if (preset is < 1 or > 8) throw new ArgumentOutOfRangeException(nameof(preset));
        int files = 0, edits = 0;
        void Edit(string relative, Func<string, string> transform, bool required = true)
        {
            token.ThrowIfCancellationRequested();
            string path = Path.Combine(root, relative);
            if (!File.Exists(path)) { if (required) throw new FileNotFoundException("Required exclusion source is missing", path); return; }
            string source = File.ReadAllText(path);
            string result = transform(source);
            if (source == result) return;
            if (!File.Exists(path + ".bak")) File.Copy(path, path + ".bak");
            File.WriteAllText(path, result); files++;
            edits += Regex.Matches(result, "RANDM64_ALT_EXCLUSION").Count;
        }
        string InsertAfterLocals(string source, string anchor, string statement)
        {
            if (!source.Contains(anchor)) throw new InvalidDataException("Unsupported target source: exclusion anchor not found: " + anchor);
            return source.Replace(anchor, Save(anchor, anchor + "\n" + statement));
        }
        if (preset >= 1)
        {
            Edit("src/game/hud.c", s => InsertAfterLocals(s, "s16 hudDisplayFlags = gHudDisplay.flags;", "    return;"));
            Edit("src/game/ingame_menu.c", s => DiscardOverlay(s, "render_menus_and_dialogs"));
            Edit("src/game/print.c", s => DiscardOverlay(s, "render_text_labels"));
        }
        if (preset >= 3)
            Edit("src/game/rendering_graph_node.c", s =>
            {
                const string anchor = "static void geo_append_display_list(void *displayList, s16 layer) {";
                // Object traversal advances door and actor animation frames. Suppress only submission.
                string condition = preset is 7 or 8
                    ? "    if (gCurGraphNodeObject != NULL) {\n        return;\n    }"
                    : "    if (gCurGraphNodeObject != NULL\n        && (gMarioObject == NULL || gCurGraphNodeObject != &gMarioObject->header.gfx)) {\n        return;\n    }";
                s = InsertAfterLocals(s, anchor, condition);
                if (preset <= 6)
                {
                    const string include = "#include \"rendering_graph_node.h\"";
                    if (!s.Contains(include)) throw new InvalidDataException("Renderer include anchor missing.");
                    s = s.Replace(include, Save(include, include + "\n#include \"object_list_processor.h\""));
                }
                return s;
            });
        if (preset is 4 or 5 or 6)
            Edit("actors/mario/model.inc.c", s => Mario(s, preset));
        if (preset >= 2)
        {
            Edit("src/game/screen_transition.c", HideWarpTransition);
            Edit("src/game/rendering_graph_node.c", s =>
            {
                var shadow = new Regex(@"\bshadowList\s*=\s*create_shadow_below_xyz\s*\([^;]*\);");
                if (shadow.Matches(s).Count != 1) throw new InvalidDataException("Unsupported shadow render source.");
                return shadow.Replace(s, m => Save(m.Value, "shadowList = NULL;"));
            });
            Edit("src/game/moving_texture.c", s =>
            {
                s = HideMovingSurface(s, "geo_movtex_draw_nocolor");
                if (preset >= 3)
                    foreach (string callback in new[] { "geo_movtex_draw_water_regions", "geo_movtex_draw_colored", "geo_movtex_draw_colored_no_update", "geo_movtex_draw_colored_2_no_update" })
                        s = HideMovingSurface(s, callback);
                return s;
            });
            string actors = Path.Combine(root, "actors");
            if (!Directory.Exists(actors)) throw new DirectoryNotFoundException(actors);
            foreach (string path in Directory.EnumerateFiles(actors, "*.c", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                string relative = Path.GetRelativePath(root, path);
                if (relative.Replace('\\', '/').StartsWith("actors/mario/", StringComparison.Ordinal)) continue;
                Edit(relative, ActorTextureParts);
            }
        }
        if (preset >= 2)
        {
            string levels = Path.Combine(root, "levels");
            if (!Directory.Exists(levels)) throw new DirectoryNotFoundException(levels);
            var batchRandom = new Random(unchecked((int)seed));
            foreach (string path in Directory.EnumerateFiles(levels, "*.c", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                string relative = Path.GetRelativePath(root, path);
                Edit(relative, s =>
                {
                    if (preset >= 7 && !relative.Replace('\\', '/').Contains("castle_grounds/areas/1/2/"))
                        s = Holes(s, batchRandom, true, preset == 8 ? 0.5 : 0.25);
                    return TextureParts(s, true);
                });
            }
        }
        if (edits == 0) throw new InvalidDataException("No compatible draw targets found for this preset.");
        return new(edits, edits, files);
    }

    internal static string Untexture(string source) => Regex.Replace(source, @"\bgsDPSetCombineMode\s*\([^()]*\)", m => Save(m.Value, "gsDPSetCombineMode(G_CC_SHADE, G_CC_SHADE)"));

    internal static string HideWarpTransition(string source)
    {
        const string signature = "s32 render_screen_transition(s8 fadeTimer, s8 transType, u8 transTime, struct WarpTransitionData *transData)";
        string mask = DisplayListExclusionService.CodeMask(source);
        int start = mask.IndexOf(signature, StringComparison.Ordinal);
        if (start < 0) throw new InvalidDataException("Unsupported screen transition function.");
        int open = mask.IndexOf('{', start), end = open + 1, depth = 1;
        if (open < 0) throw new InvalidDataException("Screen transition body missing.");
        for (; end < mask.Length && depth > 0; end++)
        {
            if (mask[end] == '{') depth++;
            if (mask[end] == '}') depth--;
        }
        if (depth != 0) throw new InvalidDataException("Unbalanced screen transition body.");
        string original = source[start..end];
        string hidden = original.Replace(signature, signature.Replace("render_screen_transition", "randm64_transition_step"));
        string wrapper = "\n\n" + signature + " {\n"
            + "    Gfx *savedHead = gDisplayListHead;\n"
            + "    s32 completed;\n\n"
            + "    completed = randm64_transition_step(fadeTimer, transType, transTime, transData);\n"
            + "    gDisplayListHead = savedHead;\n"
            + "    return completed;\n}";
        return source[..start] + Save(original, hidden + wrapper) + source[end..];
    }

    internal static string DiscardOverlay(string source, string function)
    {
        string mask = DisplayListExclusionService.CodeMask(source);
        Match signature = Regex.Match(mask, @"\b(?:void|s16)\s+" + Regex.Escape(function) + @"\s*\(void\)\s*\{");
        if (!signature.Success) throw new InvalidDataException("Overlay function not found: " + function);
        int open = mask.IndexOf('{', signature.Index), end = open + 1, depth = 1;
        for (; end < mask.Length && depth > 0; end++)
        {
            if (mask[end] == '{') depth++;
            if (mask[end] == '}') depth--;
        }
        if (depth != 0) throw new InvalidDataException("Unbalanced overlay function: " + function);
        string body = source[(open + 1)..(end - 1)];
        string bodyMask = DisplayListExclusionService.CodeMask(body);
        var rewritten = new StringBuilder(body);
        foreach (Match ret in Regex.Matches(bodyMask, @"\breturn\b[^;]*;").Cast<Match>().Reverse())
            rewritten.Insert(ret.Index, "gDisplayListHead = randm64OverlayStart;\n    ");
        string replacement = source[signature.Index..(open + 1)]
            + "\n    Gfx *randm64OverlayStart = gDisplayListHead;" + rewritten
            + "\n    gDisplayListHead = randm64OverlayStart;\n}";
        string original = source[signature.Index..end];
        return source[..signature.Index] + Save(original, replacement) + source[end..];
    }

    internal static string ActorTextureParts(string source) => TextureParts(source, false);

    internal static string HideMovingSurface(string source, string callback)
    {
        // Keep callback updates and allocations running, but never submit its generated surface list.
        var function = new Regex(@"^Gfx\s*\*\s*" + Regex.Escape(callback) + @"\b[^{}]*\{[\s\S]*?^\}", RegexOptions.Multiline);
        if (function.Matches(source).Count != 1) throw new InvalidDataException("Unsupported moving surface callback: " + callback);
        return function.Replace(source, m =>
        {
            var terminal = new Regex(@"\breturn\s+(?:gfx|gfxHead)\s*;");
            if (terminal.Matches(m.Value).Count != 1) throw new InvalidDataException("Moving surface return anchor missing: " + callback);
            return terminal.Replace(m.Value, ret => Save(ret.Value, "return NULL;"));
        });
    }

    internal static string TextureParts(string source, bool detailsOnly)
    {
        var arrays = new Regex(@"\b(?:static\s+)?const\s+Gfx\s+(?<name>\w+)\s*\[\s*\]\s*=\s*\{(?<body>[\s\S]*?)\};");
        // Regional #if branches can define the same symbol more than once in source.
        // Retain every variant when finding material roots and child calls.
        var lists = arrays.Matches(source).Cast<Match>().GroupBy(m => m.Groups["name"].Value)
            .ToDictionary(g => g.Key, g => string.Join("\n", g.Select(m => m.Value)));
        var hidden = new HashSet<string>();
        var pending = new Stack<string>(lists.Where(p => detailsOnly
            ? Regex.IsMatch(p.Value, @"\bgsDPSetCombineMode\s*\(\s*G_CC_(?:DECAL\w*|MODULATE(?:RGB|I)(?:A|DECALA|FADEA)\w*|BLENDRGBDECALA|BLENDRGBA)\b")
            : Regex.IsMatch(p.Value, @"\bgsDPSetTextureImage\s*\(")).Select(p => p.Key));
        while (pending.TryPop(out string? name))
        {
            if (!hidden.Add(name) || !lists.TryGetValue(name, out string? list)) continue;
            foreach (Match call in Regex.Matches(list, @"\bgsSP(?:DisplayList|BranchList)\s*\(\s*(\w+)\s*\)"))
                pending.Push(call.Groups[1].Value);
        }
        // Keep texture loads, material reverts, branches and transforms intact; suppress only draws.
        return arrays.Replace(source, m => hidden.Contains(m.Groups["name"].Value)
            ? Regex.Replace(m.Value, @"\bgsSP(?:1Triangle|2Triangles)\s*\([^()]*\)", t => Save(t.Value, "gsDPNoOp()")) : m.Value);
    }

    internal static string Holes(string source, Random random, bool batches, double density = 0.5)
    {
        return Regex.Replace(source, @"\b(?:static\s+)?const\s+Gfx\s+\w+\s*\[\s*\]\s*=\s*\{[\s\S]*?\};", list =>
        {
            bool remove = batches && random.NextDouble() < density;
            return Regex.Replace(list.Value, @"\bgsSP(?:1Triangle|2Triangles)\s*\([^()]*\)", m =>
                (batches ? remove : random.NextDouble() < 0.2) ? Save(m.Value, "gsDPNoOp()") : m.Value);
        });
    }
}
