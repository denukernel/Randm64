using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Sm64DecompLevelViewer.Models;

namespace Sm64DecompLevelViewer.Services;

public static class CustomObjectAnimations
{
    public const string Registry = "include/randm64_model_animations.json";
    public const string Payload = "src/game/behaviors/randm64_model_animations.inc.c";
    private sealed record Binding(string Model, string Behavior, string Actor);
    public static bool IsInstalled(string root, string model, string behavior)
    {
        try
        {
            string path = Path.Combine(root, Registry);
            return File.Exists(path) && File.Exists(Path.Combine(root, Payload)) &&
                JsonSerializer.Deserialize<List<Binding>>(File.ReadAllText(path))?.Any(b => b.Model == model && b.Behavior == behavior) == true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return false; }
    }

    public static SourceEditPlan Prepare(string root, string? levelDirectory, IEnumerable<LevelObject> objects)
    {
        var plan = new SourceEditPlan();
        var candidates = objects.Where(o => !o.IsDeleted && o.SourceType == ObjectSourceType.Normal && o.ModelName != "MODEL_NONE")
            .DistinctBy(o => (o.ModelName, o.Behavior)).ToList();
        string registry = Path.Combine(root, Registry);
        string original = plan.Read(registry);
        var bindings = string.IsNullOrWhiteSpace(original) ? new List<Binding>() : JsonSerializer.Deserialize<List<Binding>>(original)
            ?? throw new InvalidDataException("Invalid custom animation registry.");
        if (candidates.Count > 0)
        {
            var compatibility = new BehaviorCompatibility(root, candidates.Select(o => o.ModelName), levelDirectory);
            var parser = new AnimationParser();
            var catalog = new Lazy<LevelActorCatalog>(() => new LevelActorCatalog(root, levelDirectory));
            foreach (var obj in candidates)
            {
                string? actor = compatibility.CustomAnimationActor(obj.Behavior, obj.ModelName);
                if (actor == null || parser.LoadDefaultActorAnimation(root, actor, catalog.Value.GetJointCount(obj.ModelName) ?? 0) == null) continue;
                bindings.RemoveAll(b => b.Model == obj.ModelName && b.Behavior == obj.Behavior);
                bindings.Add(new(obj.ModelName, obj.Behavior, actor));
            }
        }
        if (bindings.Count == 0)
        {
            CustomObjectRespawnSafety.Stage(root, plan);
            return plan;
        }
        bindings = bindings.OrderBy(b => b.Model, StringComparer.Ordinal).ThenBy(b => b.Behavior, StringComparer.Ordinal).ToList();
        foreach (var binding in bindings)
            if (!Regex.IsMatch(binding.Model, @"^MODEL_\w+$") || !Regex.IsMatch(binding.Behavior, @"^bhv\w+$") || !Regex.IsMatch(binding.Actor, @"^\w+$"))
                throw new InvalidDataException("Invalid custom animation binding.");
        plan.Write(registry, JsonSerializer.Serialize(bindings, new JsonSerializerOptions { WriteIndented = true }).Replace("\r\n", "\n") + "\n");
        plan.Write(Path.Combine(root, Payload), Generate(root, levelDirectory, bindings));
        plan.Write(Path.Combine(root, "include/randm64_model_animations.h"), "#ifndef RANDM64_MODEL_ANIMATIONS_H\n#define RANDM64_MODEL_ANIMATIONS_H\nstruct Object;\nstruct Animation;\nstruct GraphNodeObject;\nstruct Animation *randm64_resolve_model_animation(struct GraphNodeObject *graphNode);\nvoid randm64_update_model_animation(struct Object *obj);\n#endif\n");
        string graph = Path.Combine(root, "src/engine/graph_node.c");
        string graphText = plan.Read(graph).Replace("\r\n", "\n");
        graphText = graphText.Replace("#include \"randm64/model_animations.inc.c\"", "#include \"randm64_model_animations.h\"");
        if (!graphText.Contains("struct Animation *modelAnim = randm64_resolve_model_animation(graphNode);"))
        {
            graphText = IncludeAfter(graphText, "#include \"geo_layout.h\"", "#include \"randm64_model_animations.h\"");
            const string before = "    struct Animation **animSegmented = segmented_to_virtual(animPtrAddr);\n    struct Animation *anim = segmented_to_virtual(*animSegmented);";
            const string after = "    struct Animation **animSegmented;\n    struct Animation *anim;\n    struct Animation *modelAnim = randm64_resolve_model_animation(graphNode);\n\n    if (modelAnim != NULL) {\n        anim = modelAnim;\n    } else {\n        animSegmented = segmented_to_virtual(animPtrAddr);\n        anim = segmented_to_virtual(*animSegmented);\n    }";
            if (graphText.Split(before).Length != 3) throw new InvalidDataException("The animation initializers have changed; no source files were saved.");
            graphText = graphText.Replace(before, after);
            foreach (string name in new[] { "geo_obj_init_animation", "geo_obj_init_animation_accel" })
            {
                var range = FunctionBody(graphText, name);
                string body = graphText[range.Start..range.End];
                if (body.Split("graphNode->animInfo.animYTrans = 0;").Length != 2) throw new InvalidDataException("Animation translation initializer changed: " + name);
                body = body.Replace("graphNode->animInfo.animYTrans = 0;", "graphNode->animInfo.animYTrans = modelAnim != NULL ? anim->animYTransDivisor : 0;");
                graphText = graphText[..range.Start] + body + graphText[range.End..];
            }
        }
        else if (graphText.Split("struct Animation *modelAnim = randm64_resolve_model_animation(graphNode);").Length != 3)
            throw new InvalidDataException("The custom animation initializer hooks are incomplete.");
        plan.Write(graph, graphText);
        string behavior = Path.Combine(root, "src/engine/behavior_script.c");
        string behaviorText = plan.Read(behavior).Replace("\r\n", "\n");
        if (!behaviorText.Contains("randm64_update_model_animation(gCurrentObject);"))
        {
            behaviorText = IncludeAfter(behaviorText, "#include \"graph_node.h\"", "#include \"randm64_model_animations.h\"");
            var body = FunctionBody(behaviorText, "cur_obj_update");
            behaviorText = behaviorText.Insert(body.End, "    randm64_update_model_animation(gCurrentObject);\n");
        }
        plan.Write(behavior, behaviorText);
        string actions = Path.Combine(root, "src/game/behavior_actions.c");
        string actionsText = plan.Read(actions);
        if (!actionsText.Contains("#include \"behaviors/randm64_model_animations.inc.c\""))
        {
            if (!actionsText.Contains("#include \"behaviors/")) throw new InvalidDataException("The native behavior compilation unit could not be identified.");
            plan.Write(actions, actionsText.TrimEnd() + "\n\n#include \"behaviors/randm64_model_animations.inc.c\"\n");
        }
        CustomObjectPickupSafety.Stage(root, plan);
        CustomObjectRespawnSafety.Stage(root, plan);
        return plan;
    }

    private static string IncludeAfter(string source, string anchor, string include)
    {
        if (source.Split(anchor).Length != 2) throw new InvalidDataException("Cannot identify the source include anchor: " + anchor);
        return source.Replace(anchor, anchor + "\n" + include);
    }
    private static (int Start, int End) FunctionBody(string source, string name)
    {
        string mask = CSourceCalls.MaskComments(source);
        var match = Regex.Match(mask, @"\bvoid\s+" + name + @"\s*\([^)]*\)\s*\{");
        if (!match.Success) throw new InvalidDataException("Animation update function not found: " + name);
        int depth = 1, end = match.Index + match.Length;
        for (; end < mask.Length && depth > 0; end++) { if (mask[end] == '{') depth++; else if (mask[end] == '}') depth--; }
        if (depth != 0) throw new InvalidDataException("Unbalanced animation update function.");
        return (match.Index + match.Length, end - 1);
    }

    private static string Generate(string root, string? levelDirectory, List<Binding> bindings)
    {
        var source = new StringBuilder("/* Generated model-owned animations for custom objects. */\n#include \"sm64.h\"\n#include \"behavior_data.h\"\n#include \"model_ids.h\"\n#include \"engine/graph_node.h\"\n#include \"game/area.h\"\n#include \"game/memory.h\"\n#include \"game/object_list_processor.h\"\n#include \"randm64_model_animations.h\"\n\n");
        var parser = new AnimationParser();
        var catalog = new LevelActorCatalog(root, levelDirectory);
        var actors = bindings.Select(b => b.Actor).Distinct().Order(StringComparer.Ordinal).ToList();
        var names = new Dictionary<string, (string Idle, string Move)>();
        foreach (string actor in actors)
        {
            var animations = parser.ScanActorAnimations(root, actor);
            int joints = bindings.Where(b => b.Actor == actor).Select(b => catalog.GetJointCount(b.Model) ?? 0).DefaultIfEmpty().Max();
            var idle = parser.LoadDefaultActorAnimation(root, actor, joints) ?? throw new InvalidDataException("No animations for custom model: " + actor);
            var move = animations.FirstOrDefault(a => a.Name.EndsWith(": walking") || a.Name.EndsWith(": walk")) ?? idle;
            parser.LoadAnimationData(move);
            if ((move.Indices.Length - 6) / 6 < joints) move = idle;
            foreach (var clip in new[] { idle, move }.DistinctBy(a => a.Symbol))
            {
                parser.LoadAnimationData(clip);
                string name = "randm64_" + actor + "_" + clip.Symbol;
                source.AppendLine("static const s16 " + name + "_values[] = { " + string.Join(",", clip.Values) + " };");
                source.AppendLine("static const u16 " + name + "_indices[] = { " + string.Join(",", clip.Indices) + " };");
                source.AppendLine($"static struct Animation {name} = {{ {clip.Flags}, {clip.TranslationDivisor}, {clip.StartFrame}, {clip.LoopStart}, {clip.FrameCount}, {(clip.Indices.Length - 6) / 6}, NULL, NULL, 0 }};");
            }
            names[actor] = ("randm64_" + actor + "_" + idle.Symbol, "randm64_" + actor + "_" + move.Symbol);
        }
        source.AppendLine("""

struct Animation *randm64_resolve_model_animation(struct GraphNodeObject *graphNode) {
    struct Object *obj;
    struct Animation *anim;
    uintptr_t address = (uintptr_t) graphNode;
    uintptr_t base = (uintptr_t) &gObjectPool[0];

    if (address < base || address >= base + sizeof(struct Object) * OBJECT_POOL_CAPACITY
        || (address - base) % sizeof(struct Object) != 0) {
        return NULL;
    }
    obj = (struct Object *) graphNode;
    if (obj == gMarioObject) {
        return NULL;
    }
""");
        foreach (var binding in bindings)
        {
            var (idle, move) = names[binding.Actor];
            source.AppendLine($"    if (gLoadedGraphNodes[{binding.Model}] != NULL && graphNode->sharedChild == gLoadedGraphNodes[{binding.Model}]\n        && obj->behavior == segmented_to_virtual({binding.Behavior})) {{");
            source.AppendLine($"        anim = obj->oForwardVel > 0.5f || obj->oForwardVel < -0.5f ? &{move} : &{idle};");
            foreach (string name in new[] { idle, move }.Distinct())
            {
                source.AppendLine($"        {name}.values = {name}_values;\n        {name}.index = {name}_indices;");
                source.AppendLine($"#ifndef NO_SEGMENTED_MEMORY\n        {name}.values = (const s16 *) VIRTUAL_TO_PHYSICAL({name}_values);\n        {name}.index = (const u16 *) VIRTUAL_TO_PHYSICAL({name}_indices);\n#endif");
            }
            source.AppendLine("        return anim;\n    }");
        }
        source.AppendLine("""
    return NULL;
}

void randm64_update_model_animation(struct Object *obj) {
    if (randm64_resolve_model_animation(&obj->header.gfx) != NULL) {
        geo_obj_init_animation(&obj->header.gfx, NULL);
    }
}
""");
        return source.ToString().Replace("\r\n", "\n");
    }
}
