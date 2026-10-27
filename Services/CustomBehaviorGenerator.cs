using System.IO;
using System.Text.RegularExpressions;

namespace Sm64DecompLevelViewer.Services;

public static class CustomBehaviorGenerator
{
    public static string Name(string input)
    {
        if (!Regex.IsMatch(input, @"^[A-Za-z_]\w*$")) throw new InvalidDataException("use a C identifier with letters, digits and underscores");
        return input.StartsWith("bhvCustom", StringComparison.Ordinal) ? input : "bhvCustom" + (input.StartsWith("bhv", StringComparison.Ordinal) ? input[3..] : input);
    }
    public static SourceEditPlan Prepare(string root, string input, string model, string action, string interaction)
    {
        string name = Name(input), loop = name + "_loop";
        var ids = new ObjectParser().ParseModelIds(Path.Combine(root, "include", "model_ids.h"));
        if (model != "MODEL_NONE" && !ids.ContainsKey(model)) throw new InvalidDataException("select a model defined by this project");
        var plan = new SourceEditPlan();
        string script = Path.Combine(root, "data", "behavior_data.c"), header = Path.Combine(root, "include", "behavior_data.h"),
            actions = Path.Combine(root, "src", "game", "behavior_actions.c"), declarations = Path.Combine(root, "src", "game", "behavior_actions.h");
        foreach (string path in new[] { script, header, actions, declarations })
            if (!File.Exists(path)) throw new FileNotFoundException("required behavior integration file is missing", path);
        string original = plan.Read(script), originalHeader = plan.Read(header);
        if (Regex.IsMatch(CSourceCalls.MaskComments(original + "\n" + originalHeader), @"\b" + Regex.Escape(name) + @"\b")) throw new InvalidDataException("this behavior name already exists");
        string nativePath = Path.Combine(root, "src", "game", "behaviors", name + ".inc.c");
        if (File.Exists(nativePath)) throw new InvalidDataException("the native behavior file already exists");
        string movement = action switch
        {
            "patrol" => "    o->oForwardVel = 4.0f;\n    cur_obj_update_floor_and_walls();\n    cur_obj_move_standard(-78);\n    if (o->oMoveFlags & OBJ_MOVE_HIT_WALL) {\n        o->oMoveAngleYaw += 0x8000;\n    }",
            "chase" => "    if (gMarioObject != NULL) {\n        obj_turn_toward_object(o, gMarioObject, O_MOVE_ANGLE_YAW_INDEX, 0x400);\n    }\n    o->oForwardVel = 5.5f;\n    cur_obj_update_floor_and_walls();\n    cur_obj_move_standard(-78);",
            "jump" => "    cur_obj_update_floor_and_walls();\n    if (o->oMoveFlags & OBJ_MOVE_ON_GROUND) {\n        o->oVelY = 14.0f;\n    }\n    cur_obj_move_standard(-78);",
            "spin" => "    o->oFaceAngleYaw += 0x300;",
            "float" => "    o->oPosY = o->oHomeY + sins(o->oTimer * 0x350) * 120.0f;",
            "passive" => "    o->oForwardVel = 0.0f;",
            _ => throw new InvalidDataException("unknown movement template")
        };
        string type = interaction switch { "none" => "0", "solid" => "0", "damage" => "INTERACT_DAMAGE", "coin" => "INTERACT_COIN", _ => throw new InvalidDataException("select one supported interaction") };
        string collision = interaction is "none" or "solid" ? "" : $"    o->oInteractType = {type};\n    o->oDamageOrCoinValue = 1;\n    cur_obj_set_hitbox_radius_and_height(80.0f, 100.0f);\n";
        string push = interaction == "solid" ? "    if (gMarioObject != NULL) {\n        cur_obj_push_mario_away_from_cylinder(80.0f, 100.0f);\n    }\n" : "";
        string collect = interaction == "coin" ? "    if (o->oInteractStatus & INT_STATUS_INTERACTED) {\n        obj_mark_for_deletion(o);\n    }\n" : "";
        string native = "#include \"engine/math_util.h\"\n\n" + $"void {loop}(void) {{\n{collision}{movement}\n{push}{collect}    o->oInteractStatus = 0;\n}}\n";
        string behavior = $"\n/* Randm64 template: {action}; preferred model {model}. */\nconst BehaviorScript {name}[] = {{\n    BEGIN(OBJ_LIST_GENACTOR),\n    OR_INT(oFlags, OBJ_FLAG_UPDATE_GFX_POS_AND_ANGLE),\n    SET_MODEL({model}),\n    SET_HOME(),\n    SET_INT(oIntangibleTimer, 0),\n    SET_FLOAT(oGravity, -4),\n    BEGIN_LOOP(),\n        CALL_NATIVE({loop}),\n    END_LOOP(),\n}};\n";
        plan.Write(nativePath, native);
        plan.Write(script, original + behavior);
        plan.Write(header, Declaration(originalHeader, $"extern const BehaviorScript {name}[];"));
        plan.Write(declarations, Declaration(plan.Read(declarations), $"void {loop}(void);"));
        plan.Write(actions, plan.Read(actions) + $"\n#include \"behaviors/{name}.inc.c\"\n");
        return plan;
    }
    private static string Declaration(string source, string declaration)
    {
        int index = source.LastIndexOf("#endif", StringComparison.Ordinal);
        return index < 0 ? source + "\n" + declaration + "\n" : source.Insert(index, declaration + "\n");
    }
}
