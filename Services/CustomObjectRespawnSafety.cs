using System.IO;

namespace Sm64DecompLevelViewer.Services;

public static class CustomObjectRespawnSafety
{
    public static SourceEditPlan Prepare(string root)
    {
        var plan = new SourceEditPlan();
        Stage(root, plan);
        return plan;
    }

    public static void Stage(string root, SourceEditPlan plan)
    {
        plan.Write(Path.Combine(root, "include/randm64_respawn.h"), "#ifndef RANDM64_RESPAWN_H\n#define RANDM64_RESPAWN_H\n/* Private fields used only by invisible respawner objects. */\n#define oRandm64RespawnModel OBJECT_FIELD_VPTR(0x1E)\n#define oRandm64RespawnHasModel OBJECT_FIELD_S32(0x1F)\n#endif\n");
        string source;
        string corkbox = Path.Combine(root, "src/game/behaviors/corkbox.inc.c");
        source = plan.Read(corkbox).Replace("\r\n", "\n").Replace("#include \"randm64_model_animations.h\"", "#include \"randm64_respawn.h\"");
        if (!source.Contains("#include \"randm64_respawn.h\""))
            source = "#include \"randm64_respawn.h\"\n\n" + source;
        // Upgrade the earlier animation-only patch in existing projects.
        source = source.Replace("preserve registered custom geometry through respawning.", "preserve custom geometry through respawning.")
            .Replace("    respawner->oRandm64RespawnModel = NULL;\n    if (o->behavior == segmented_to_virtual(behToSpawn)\n        && randm64_resolve_model_animation(&o->header.gfx) != NULL) {", "    respawner->oRandm64RespawnModel = NULL;\n    respawner->oRandm64RespawnHasModel = o->behavior == segmented_to_virtual(behToSpawn);\n    if (respawner->oRandm64RespawnHasModel) {")
            .Replace("if (o->oRandm64RespawnModel != NULL)", "if (o->oRandm64RespawnHasModel)");
        const string store = "    respawner->oRespawnerModelToRespawn = model;";
        if (!source.Contains("/* Randm64: preserve custom geometry through respawning. */"))
        {
            if (source.Split(store).Length != 2) throw new InvalidDataException("Respawner initialization changed; no source files were saved.");
            source = source.Replace(store, "    /* Randm64: preserve custom geometry through respawning. */\n    respawner->oRandm64RespawnModel = NULL;\n    respawner->oRandm64RespawnHasModel = o->behavior == segmented_to_virtual(behToSpawn);\n    if (respawner->oRandm64RespawnHasModel) {\n        respawner->oRandm64RespawnModel = o->header.gfx.sharedChild;\n    }\n" + store);
        }
        const string restore = "        spawnedObject->oBhvParams = o->oBhvParams;";
        if (!source.Contains("/* Randm64: restore geometry before the new object's first behavior update. */"))
        {
            if (source.Split(restore).Length != 2) throw new InvalidDataException("Respawner creation changed; no source files were saved.");
            source = source.Replace(restore, "        /* Randm64: restore geometry before the new object's first behavior update. */\n        if (o->oRandm64RespawnHasModel) {\n            spawnedObject->header.gfx.sharedChild = o->oRandm64RespawnModel;\n        }\n" + restore);
        }
        plan.Write(corkbox, source);
    }
}
