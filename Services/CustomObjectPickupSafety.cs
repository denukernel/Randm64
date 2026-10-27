using System.IO;
using System.Text.RegularExpressions;

namespace Sm64DecompLevelViewer.Services;

public static class CustomObjectPickupSafety
{
    public static SourceEditPlan Prepare(string root)
    {
        var plan = new SourceEditPlan();
        Stage(root, plan);
        return plan;
    }

    public static void Stage(string root, SourceEditPlan plan)
    {
        string renderer = Path.Combine(root, "src/game/rendering_graph_node.c");
        string source = plan.Read(renderer);
        source = EditFunction(source, "geo_process_held_object", body =>
        {
            if (body.Contains("/* Randm64: held object traversal is not reentrant. */")) return body;
            const string anchor = "Mtx *mtx = alloc_display_list(sizeof(*mtx));";
            if (body.Split(anchor).Length != 2) throw new InvalidDataException("Held-object renderer has changed; no source files were saved.");
            return body.Replace(anchor, "Mtx *mtx;\n\n    /* Randm64: held object traversal is not reentrant. */\n    if (gCurGraphNodeHeldObject != NULL) {\n        return;\n    }\n    mtx = alloc_display_list(sizeof(*mtx));");
        });
        plan.Write(renderer, source);
        string mario = Path.Combine(root, "src/game/mario_misc.c");
        source = plan.Read(mario);
        source = EditFunction(source, "geo_switch_mario_hand_grab_pos", body =>
        {
            if (body.Contains("/* Randm64: only the player and its mirror own the player's held object. */")) return body;
            const string anchor = "if (marioState->heldObj != NULL) {";
            if (body.Split(anchor).Length != 2) throw new InvalidDataException("Mario held-object callback has changed; no source files were saved.");
            return body.Replace(anchor, "/* Randm64: only the player and its mirror own the player's held object. */\n        if (marioState->heldObj != NULL && marioState->marioObj != NULL\n            && gCurGraphNodeHeldObject == NULL\n            && (gCurGraphNodeObject == &marioState->marioObj->header.gfx\n                || gCurGraphNodeObject == &gMirrorMario)) {");
        });
        plan.Write(mario, source);
    }

    private static string EditFunction(string source, string name, Func<string, string> edit)
    {
        source = source.Replace("\r\n", "\n");
        string mask = CSourceCalls.MaskComments(source);
        var match = Regex.Match(mask, @"\b" + Regex.Escape(name) + @"\s*\([^;{}]*\)\s*\{");
        if (!match.Success) throw new InvalidDataException("Held-object function not found: " + name);
        int start = match.Index + match.Length, end = start, depth = 1;
        for (; end < mask.Length && depth > 0; end++) { if (mask[end] == '{') depth++; else if (mask[end] == '}') depth--; }
        if (depth != 0) throw new InvalidDataException("Unbalanced held-object function: " + name);
        return source[..start] + edit(source[start..(end - 1)]) + source[(end - 1)..];
    }
}
