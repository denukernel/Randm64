using System.IO;
using System.Text;
using System.Text.RegularExpressions;
namespace Sm64DecompLevelViewer.Services.Chaos;
internal sealed class ChaosSourcePatchService
{
    private readonly string _projectRoot;
    private readonly Random _random;
    private readonly ChaosQueueState _queue;
    public ChaosSourcePatchService(string root, Random random, ChaosQueueState queue) { _projectRoot = root; _random = random; _queue = queue; }
    public void Apply(ChaosOptions options, CancellationToken token = default)
    {
        var sourcePatcher = new ChaosSourcePatcher(_projectRoot);
            var randomizeDl = options.RandomizeDl;
            var dlMode = options.DlMode;
            var mode = options.Mode;
            var randomizeSkybox = options.RandomizeSkybox;
            var jumpWeird = options.JumpWeird;
            var jumpDeath = options.JumpDeath;
            var limboMario = options.LimboMario;
            var limboMarioMode = options.LimboMarioMode;
            var alienSound = options.AlienSound;
            var wackierSounds = options.WackierSounds;
            var wackyInstruments = options.WackyInstruments;
            var adsrScrambler = options.AdsrScrambler;
            var vibratoPitch = options.VibratoPitch;
            var dmaBitcrusher = options.DmaBitcrusher;
            var gameLogicRandomizer = options.GameLogicRandomizer;
            var scrambleTitleScreen = options.ScrambleTitleScreen;
            var titleScramblerMode = options.TitleScramblerMode;
            var randomizeCutsceneCamera = options.RandomizeCutsceneCamera;
            var cutsceneCameraMode = options.CutsceneCameraMode;
            var lakituCameraChaos = options.LakituCameraChaos;
            var lakituCameraMode = options.LakituCameraMode;
            var startLevelChaos = options.StartLevelChaos;
            var startLevelConstant = options.StartLevelConstant;
            var activeGameLogicModes = options.ActiveGameLogicModes;
            var gameLogicSimultaneous = options.GameLogicSimultaneous;
                    // Perform C source file patches based on selected logic cheats
                    int skyboxSeed = _random.Next(0, 9);
                    
                    // Create include/chaos_config.h
                    try
                    {
                        string configDir = Path.Combine(_projectRoot, "include");
                        if (Directory.Exists(configDir))
                        {
                            string configPath = Path.Combine(configDir, "chaos_config.h");
                            List<string> lines = new List<string>();
                            if (File.Exists(configPath))
                            {
                                if (!File.Exists(configPath + ".bak")) File.Copy(configPath, configPath + ".bak");
                                var existingLines = File.ReadAllLines(configPath);
                                foreach (var line in existingLines)
                                {
                                    token.ThrowIfCancellationRequested();
                                    string trimmed = line.Trim();
                                    if (trimmed == "#define CHAOS_CONFIG_H")
                                    {
                                        if (!lines.Any(l => l.Trim() == "#define CHAOS_CONFIG_H")) lines.Add(line);
                                        continue;
                                    }
                                    if (trimmed.StartsWith("#define CHAOS_"))
                                    {
                                        var parts = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                                        if (parts.Length >= 2)
                                        {
                                            string macroName = parts[1];
                                            if (!_queue.Macros.ContainsKey(macroName))
                                            {
                                                _queue.Macros[macroName] = trimmed;
                                            }
                                            if (macroName == "CHAOS_ACTIVE_LOGIC_MODES_LIST")
                                            {
                                                int idx = trimmed.IndexOf("CHAOS_ACTIVE_LOGIC_MODES_LIST");
                                                if (idx != -1)
                                                {
                                                    string listStr = trimmed.Substring(idx + "CHAOS_ACTIVE_LOGIC_MODES_LIST".Length).Trim();
                                                    var modes = listStr.Split(',');
                                                    foreach (var m in modes)
                                                    {
                                                        token.ThrowIfCancellationRequested();
                                                        if (int.TryParse(m.Trim(), out int val))
                                                        {
                                                            _queue.GameLogicModes.Add(val);
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }

                                    if (trimmed.StartsWith("#define CHAOS_", StringComparison.Ordinal)) continue;

                                    if (trimmed.Contains("CHAOS_RANDOM_SKYBOX") ||
                                        trimmed.Contains("CHAOS_SKYBOX_SEED") ||
                                        trimmed.Contains("CHAOS_JUMP_WEIRD") ||
                                        trimmed.Contains("CHAOS_JUMP_DEATH") ||
                                        trimmed.Contains("CHAOS_LIMBO_MARIO") ||
                                        trimmed.Contains("CHAOS_LAKITU_CAMERA_MODE") ||
                                        trimmed.Contains("CHAOS_ALIEN_SOUND") ||
                                         trimmed.Contains("CHAOS_LONG_NOTES_DISTORTION") ||
                                          trimmed.Contains("CHAOS_WACKIER_SOUNDS") ||
                                          trimmed.Contains("CHAOS_WACKY_INSTRUMENTS") ||
                                          trimmed.Contains("CHAOS_ADSR_ENVELOPE_SCRAMBLER") ||
                                          trimmed.Contains("CHAOS_VIBRATO_PITCH_BEND_CHAOS") ||
                                          trimmed.Contains("CHAOS_DMA_BITCRUSHER") ||
                                         trimmed.Contains("CHAOS_TITLE_SCRAMBLER_MODE") ||
                                        trimmed.Contains("CHAOS_CUTSCENE_CAMERA_MODE") ||
                                        trimmed.Contains("CHAOS_START_LEVEL") ||
                                        trimmed.Contains("CHAOS_FACELESS_V2") ||
                                        trimmed.Contains("CHAOS_ACTIVE_LOGIC_MODES_COUNT") ||
                                        trimmed.Contains("CHAOS_ACTIVE_LOGIC_MODES_LIST") ||
                                        trimmed.Contains("CHAOS_GAME_LOGIC_SIMULTANEOUS") ||
                                        trimmed.Contains("CHAOS_SEED"))
                                    {
                                        continue;
                                    }
                                    lines.Add(line);
                                }
                            }
                            else
                            {
                                lines.Add("#ifndef CHAOS_CONFIG_H");
                                lines.Add("#define CHAOS_CONFIG_H");
                                lines.Add("");
                                lines.Add("// Auto-generated by Chaos Engine");
                                lines.Add("");
                                lines.Add("#endif");
                            }

                            int endifIndex = lines.FindLastIndex(l => l.Trim() == "#endif");
                            if (endifIndex == -1)
                            {
                                lines.Add("#endif");
                                endifIndex = lines.Count - 1;
                            }

                            int gameSeed = _random.Next(1, 1000000);
                            _queue.Macros["CHAOS_SEED"] = $"#define CHAOS_SEED {gameSeed}";

                            if (randomizeSkybox)
                            {
                                _queue.Macros["CHAOS_RANDOM_SKYBOX"] = "#define CHAOS_RANDOM_SKYBOX";
                                _queue.Macros["CHAOS_SKYBOX_SEED"] = $"#define CHAOS_SKYBOX_SEED {skyboxSeed}";
                            }
                            if (jumpWeird)
                            {
                                if (_queue.Macros.ContainsKey("CHAOS_JUMP_WEIRD"))
                                {
                                    throw new InvalidOperationException("Weird jump mode is already applied. Operation refused to prevent redundancy.");
                                }
                                _queue.Macros["CHAOS_JUMP_WEIRD"] = "#define CHAOS_JUMP_WEIRD";
                            }
                            if (jumpDeath)
                            {
                                if (_queue.Macros.ContainsKey("CHAOS_JUMP_DEATH"))
                                {
                                    throw new InvalidOperationException("Death jump mode is already applied. Operation refused to prevent redundancy.");
                                }
                                _queue.Macros["CHAOS_JUMP_DEATH"] = "#define CHAOS_JUMP_DEATH";
                            }
                            if (limboMario)
                            {
                                string expectedLimboMacro = $"#define CHAOS_LIMBO_MARIO_MODE {limboMarioMode}";
                                if (_queue.Macros.TryGetValue("CHAOS_LIMBO_MARIO_MODE", out var existingLimbo) && existingLimbo == expectedLimboMacro)
                                {
                                    throw new InvalidOperationException($"Limbo Mario mode {limboMarioMode} is already applied. Operation refused to prevent redundancy.");
                                }
                                _queue.Macros["CHAOS_LIMBO_MARIO"] = "#define CHAOS_LIMBO_MARIO";
                                _queue.Macros["CHAOS_LIMBO_MARIO_MODE"] = expectedLimboMacro;
                            }
                            if (lakituCameraChaos)
                            {
                                string expectedCameraMacro = $"#define CHAOS_LAKITU_CAMERA_MODE {lakituCameraMode}";
                                if (_queue.Macros.TryGetValue("CHAOS_LAKITU_CAMERA_MODE", out var existingCam) && existingCam == expectedCameraMacro)
                                {
                                    throw new InvalidOperationException($"Lakitu camera mode {lakituCameraMode} is already applied. Operation refused to prevent redundancy.");
                                }
                                _queue.Macros["CHAOS_LAKITU_CAMERA_MODE"] = expectedCameraMacro;
                            }
                            if (alienSound)
                            {
                                _queue.Macros["CHAOS_ALIEN_SOUND"] = "#define CHAOS_ALIEN_SOUND";
                                _queue.Macros["CHAOS_LONG_NOTES_DISTORTION"] = "#define CHAOS_LONG_NOTES_DISTORTION";
                            }
                            if (wackierSounds)
                            {
                                _queue.Macros["CHAOS_WACKIER_SOUNDS"] = "#define CHAOS_WACKIER_SOUNDS";
                            }
                            if (wackyInstruments)
                            {
                                _queue.Macros["CHAOS_WACKY_INSTRUMENTS"] = "#define CHAOS_WACKY_INSTRUMENTS";
                            }
                            if (adsrScrambler)
                            {
                                _queue.Macros["CHAOS_ADSR_ENVELOPE_SCRAMBLER"] = "#define CHAOS_ADSR_ENVELOPE_SCRAMBLER";
                            }
                            if (vibratoPitch)
                            {
                                _queue.Macros["CHAOS_VIBRATO_PITCH_BEND_CHAOS"] = "#define CHAOS_VIBRATO_PITCH_BEND_CHAOS";
                            }
                            if (dmaBitcrusher)
                            {
                                _queue.Macros["CHAOS_DMA_BITCRUSHER"] = "#define CHAOS_DMA_BITCRUSHER";
                            }
                            if (scrambleTitleScreen)
                            {
                                string expectedTitleMacro = $"#define CHAOS_TITLE_SCRAMBLER_MODE {titleScramblerMode}";
                                if (_queue.Macros.TryGetValue("CHAOS_TITLE_SCRAMBLER_MODE", out var existingTitle) && existingTitle == expectedTitleMacro)
                                {
                                    throw new InvalidOperationException($"Title screen scrambler mode {titleScramblerMode} is already applied. Operation refused to prevent redundancy.");
                                }
                                _queue.Macros["CHAOS_TITLE_SCRAMBLER_MODE"] = expectedTitleMacro;
                            }
                            if (randomizeCutsceneCamera)
                            {
                                string expectedCutsceneMacro = $"#define CHAOS_CUTSCENE_CAMERA_MODE {cutsceneCameraMode}";
                                if (_queue.Macros.TryGetValue("CHAOS_CUTSCENE_CAMERA_MODE", out var existingCutscene) && existingCutscene == expectedCutsceneMacro)
                                {
                                    throw new InvalidOperationException($"Cutscene camera mode {cutsceneCameraMode} is already applied. Operation refused to prevent redundancy.");
                                }
                                _queue.Macros["CHAOS_CUTSCENE_CAMERA_MODE"] = expectedCutsceneMacro;
                            }
                            if (startLevelChaos)
                            {
                                string expectedStartLevelMacro = $"#define CHAOS_START_LEVEL {startLevelConstant}";
                                if (_queue.Macros.TryGetValue("CHAOS_START_LEVEL", out var existingStartLevel) && existingStartLevel == expectedStartLevelMacro)
                                {
                                    throw new InvalidOperationException($"Start level {startLevelConstant} is already applied. Operation refused to prevent redundancy.");
                                }
                                _queue.Macros["CHAOS_START_LEVEL"] = expectedStartLevelMacro;
                            }
                            if (randomizeDl && dlMode == 8)
                            {
                                if (_queue.Macros.ContainsKey("CHAOS_FACELESS_V2"))
                                {
                                    throw new InvalidOperationException("Faceless V2 mode (dlMode 8) is already applied. Operation refused to prevent redundancy.");
                                }
                                _queue.Macros["CHAOS_FACELESS_V2"] = "#define CHAOS_FACELESS_V2";
                            }
                            if (gameLogicRandomizer && activeGameLogicModes.Count > 0)
                            {
                                if (!_queue.IsRunning || !_queue.IsAccumulating) _queue.GameLogicModes.Clear();
                                foreach (var glMode in activeGameLogicModes)
                                {
                                    token.ThrowIfCancellationRequested();
                                    _queue.GameLogicModes.Add(glMode);
                                }
                                _queue.Macros["CHAOS_ACTIVE_LOGIC_MODES_COUNT"] = $"#define CHAOS_ACTIVE_LOGIC_MODES_COUNT {_queue.GameLogicModes.Count}";
                                _queue.Macros["CHAOS_ACTIVE_LOGIC_MODES_LIST"] = $"#define CHAOS_ACTIVE_LOGIC_MODES_LIST {string.Join(", ", _queue.GameLogicModes)}";
                                if (gameLogicSimultaneous)
                                {
                                    _queue.Macros["CHAOS_GAME_LOGIC_SIMULTANEOUS"] = "#define CHAOS_GAME_LOGIC_SIMULTANEOUS";
                                }
                            }

                            _queue.Macros["CHAOS_GAME_LOGIC_CYCLE_FRAMES"] = $"#define CHAOS_GAME_LOGIC_CYCLE_FRAMES {options.GameLogicCycleFrames}";
                            if (!gameLogicSimultaneous) _queue.Macros.Remove("CHAOS_GAME_LOGIC_SIMULTANEOUS");
                            _queue.Macros.Remove("CHAOS_CONFIG_H");
                            var newOptions = _queue.Macros.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Value).ToList();
                            lines.InsertRange(endifIndex, newOptions);

                            File.WriteAllText(configPath, string.Join("\n", lines).Replace("\r\n", "\n") + "\n");
                            File.SetLastWriteTime(configPath, DateTime.Now);
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                            catch (Exception ex)
                    {
                        throw new IOException($"Error writing chaos_config.h: {ex.Message}", ex);
                    }

                    // Apply source patches
                    if (randomizeSkybox)
                    {
                        string[] skyboxNames = { "water", "bitfs", "wdw", "cloud_floor", "ccm", "ssl", "bbh", "bidw", "clouds", "bits" };
                        string[] skyboxConstants = {
                            "BACKGROUND_OCEAN_SKY",
                            "BACKGROUND_FLAMING_SKY",
                            "BACKGROUND_UNDERWATER_CITY",
                            "BACKGROUND_BELOW_CLOUDS",
                            "BACKGROUND_SNOW_MOUNTAINS",
                            "BACKGROUND_DESERT",
                            "BACKGROUND_HAUNTED",
                            "BACKGROUND_GREEN_SKY",
                            "BACKGROUND_ABOVE_CLOUDS",
                            "BACKGROUND_PURPLE_SKY"
                        };

                        string levelsDir = Path.Combine(_projectRoot, "levels");
                        if (Directory.Exists(levelsDir))
                        {
                            var levelDirs = Directory.GetDirectories(levelsDir).Order(StringComparer.Ordinal).ToArray();
                            foreach (var levelDir in levelDirs)
                            {
                                token.ThrowIfCancellationRequested();
                                int skyboxIndex = _random.Next(0, 10);
                                string randomName = skyboxNames[skyboxIndex];
                                string randomConst = skyboxConstants[skyboxIndex];

                                // 1. Patch levels/<level_name>/script.c to load the randomized skybox MIO0 binary
                                string scriptPath = Path.Combine(levelDir, "script.c");
                                if (File.Exists(scriptPath))
                                {
                                    string backupPath = scriptPath + ".bak";
                                    if (!File.Exists(backupPath))
                                    {
                                        File.Copy(scriptPath, backupPath);
                                    }

                                    string content = File.ReadAllText(scriptPath);
                                    content = content.Replace("\r\n", "\n");

                                    var startRegex = new Regex(@"_([a-zA-Z0-9_]+)_skybox_mio0SegmentRomStart");
                                    var endRegex = new Regex(@"_([a-zA-Z0-9_]+)_skybox_mio0SegmentRomEnd");

                                    content = startRegex.Replace(content, $"_{randomName}_skybox_mio0SegmentRomStart");
                                    content = endRegex.Replace(content, $"_{randomName}_skybox_mio0SegmentRomEnd");

                                    File.WriteAllText(scriptPath, content);
                                    File.SetLastWriteTime(scriptPath, DateTime.Now);
                                }

                                // 2. Patch levels/<level_name>/areas/*/geo.inc.c to match the background layout constant
                                string areasDir = Path.Combine(levelDir, "areas");
                                if (Directory.Exists(areasDir))
                                {
                                    var geoFiles = Directory.GetFiles(areasDir, "geo.inc.c", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray();
                                    foreach (var geoFile in geoFiles)
                                    {
                                        token.ThrowIfCancellationRequested();
                                        string backupPath = geoFile + ".bak";
                                        if (!File.Exists(backupPath))
                                        {
                                            File.Copy(geoFile, backupPath);
                                        }

                                        string content = File.ReadAllText(geoFile);
                                        content = content.Replace("\r\n", "\n");

                                        var bgRegex = new Regex(@"GEO_BACKGROUND\([ \t]*(BACKGROUND_[A-Z_]+|[0-9]+)[ \t]*,[ \t]*geo_skybox_main[ \t]*\)");
                                        if (bgRegex.IsMatch(content))
                                        {
                                            content = bgRegex.Replace(content, $"GEO_BACKGROUND({randomConst}, geo_skybox_main)");
                                            File.WriteAllText(geoFile, content);
                                            File.SetLastWriteTime(geoFile, DateTime.Now);
                                        }
                                    }
                                }
                            }
                        }
                    }
                    
                    if (jumpWeird || jumpDeath)
                    {
                        StringBuilder jumpHooks = new StringBuilder();
                        if (jumpDeath)
                        {
                            jumpHooks.AppendLine("    m->health = 0xFF;");
                        }
                        if (jumpWeird)
                        {
                            jumpHooks.AppendLine("    {");
                            jumpHooks.AppendLine("        extern u16 random_u16(void);");
                            jumpHooks.AppendLine("        m->vel[1] += ((float)(random_u16() % 100) - 50.0f) * 0.15f;");
                            jumpHooks.AppendLine("        m->vel[0] += ((float)(random_u16() % 100) - 50.0f) * 0.1f;");
                            jumpHooks.AppendLine("        m->vel[2] += ((float)(random_u16() % 100) - 50.0f) * 0.1f;");
                            jumpHooks.AppendLine("    }");
                        }
                        string jumpHooksStr = jumpHooks.ToString();
                        
                        sourcePatcher.PatchSourceFile("src/game/mario_actions_airborne.c", "s32 act_jump(struct MarioState *m) {", "s32 act_jump(struct MarioState *m) {\n" + jumpHooksStr);
                        
                        string oldDouble = "s32 act_double_jump(struct MarioState *m) {\n" +
                                           "    s32 animation = (m->vel[1] >= 0.0f)\n" +
                                           "        ? MARIO_ANIM_DOUBLE_JUMP_RISE\n" +
                                           "        : MARIO_ANIM_DOUBLE_JUMP_FALL;";
                        string newDouble = "s32 act_double_jump(struct MarioState *m) {\n" +
                                           "    s32 animation = (m->vel[1] >= 0.0f)\n" +
                                           "        ? MARIO_ANIM_DOUBLE_JUMP_RISE\n" +
                                           "        : MARIO_ANIM_DOUBLE_JUMP_FALL;\n" +
                                           jumpHooksStr;
                        sourcePatcher.PatchSourceFile("src/game/mario_actions_airborne.c", oldDouble, newDouble);
                        
                        sourcePatcher.PatchSourceFile("src/game/mario_actions_airborne.c", "s32 act_triple_jump(struct MarioState *m) {", "s32 act_triple_jump(struct MarioState *m) {\n" + jumpHooksStr);
                        sourcePatcher.PatchSourceFile("src/game/mario_actions_airborne.c", "#include <PR/ultratypes.h>", "#include <PR/ultratypes.h>\n#include \"chaos_config.h\"");
                    }
                    
                    if (limboMario)
                    {
                        string oldLimbo = "        rotNode->rotation[0] = bodyState->torsoAngle[1];\n        rotNode->rotation[1] = bodyState->torsoAngle[2];\n        rotNode->rotation[2] = bodyState->torsoAngle[0];";
                        string newLimbo = "        rotNode->rotation[0] = bodyState->torsoAngle[1];\n        rotNode->rotation[1] = bodyState->torsoAngle[2];\n        rotNode->rotation[2] = bodyState->torsoAngle[0];\n#ifdef CHAOS_LIMBO_MARIO_MODE\n        {\n            extern u32 gGlobalTimer;\n            switch (CHAOS_LIMBO_MARIO_MODE) {\n                case 1:\n                    rotNode->rotation[0] += 0x3800;\n                    break;\n                case 2:\n                    rotNode->rotation[1] += gGlobalTimer * 0x800;\n                    break;\n                case 3:\n                    rotNode->rotation[0] -= 0x4000;\n                    break;\n                case 4:\n                    rotNode->rotation[0] += (gGlobalTimer % 2 == 0) ? 0x2400 : -0x2400;\n                    break;\n            }\n        }\n#endif";
                        sourcePatcher.PatchSourceFile("src/game/mario_misc.c", oldLimbo, newLimbo);
                        sourcePatcher.PatchSourceFile("src/game/mario_misc.c", "#include <PR/ultratypes.h>", "#include <PR/ultratypes.h>\n#include \"chaos_config.h\"");

                        // Patch rendering_graph_node.c for spaghetti stretch
                        string oldAnimPart = "    vec3s_copy(rotation, gVec3sZero);\n    vec3f_set(translation, node->translation[0], node->translation[1], node->translation[2]);";
                        string newAnimPart = "    vec3s_copy(rotation, gVec3sZero);\n    vec3f_set(translation, node->translation[0], node->translation[1], node->translation[2]);\n#ifdef CHAOS_LIMBO_MARIO_MODE\n    if (CHAOS_LIMBO_MARIO_MODE == 6) {\n        translation[0] *= 2.5f;\n        translation[1] *= 2.5f;\n        translation[2] *= 2.5f;\n    }\n#endif";
                        sourcePatcher.PatchSourceFile("src/game/rendering_graph_node.c", oldAnimPart, newAnimPart);
                        sourcePatcher.PatchSourceFile("src/game/rendering_graph_node.c", "#include \"sm64.h\"", "#include \"sm64.h\"\n#include \"chaos_config.h\"");

                        // Patch src/game/mario.c to sink Mario in quicksand/ground when Mode 5 is active
                        string oldSink = "void sink_mario_in_quicksand(struct MarioState *m) {\n    struct Object *o = m->marioObj;\n\n    if (o->header.gfx.throwMatrix) {\n        (*o->header.gfx.throwMatrix)[3][1] -= m->quicksandDepth;\n    }\n\n    o->header.gfx.pos[1] -= m->quicksandDepth;\n}";
                        string newSink = "void sink_mario_in_quicksand(struct MarioState *m) {\n    struct Object *o = m->marioObj;\n    f32 depth = m->quicksandDepth;\n#ifdef CHAOS_LIMBO_MARIO_MODE\n    if (CHAOS_LIMBO_MARIO_MODE == 5) {\n        depth += 40.0f;\n    }\n#endif\n\n    if (o->header.gfx.throwMatrix) {\n        (*o->header.gfx.throwMatrix)[3][1] -= depth;\n    }\n\n    o->header.gfx.pos[1] -= depth;\n}";
                        sourcePatcher.PatchSourceFile("src/game/mario.c", oldSink, newSink);
                        sourcePatcher.PatchSourceFile("src/game/mario.c", "#include \"sm64.h\"", "#include \"sm64.h\"\n#include \"chaos_config.h\"");
                    }
                    
                    if (gameLogicRandomizer)
                    {
                        sourcePatcher.PatchSourceFile("src/game/mario.c", "gGlobalTimer - sChaosModeTimer >= 300", "gGlobalTimer - sChaosModeTimer >= CHAOS_GAME_LOGIC_CYCLE_FRAMES");
                        string oldFunc = "s32 execute_mario_action(UNUSED struct Object *o) {";
                        string newFunc = @"#ifdef CHAOS_ACTIVE_LOGIC_MODES_COUNT
static s32 sActiveModes[] = { CHAOS_ACTIVE_LOGIC_MODES_LIST };
static s32 sChaosCurrentMode = -1;
static u32 sChaosModeTimer = 0;

void handle_game_logic_randomizer_pre(struct MarioState *m) {
    static u32 sLastWarpTimer = 0;
    extern u32 gGlobalTimer;
    extern u16 random_u16(void);
    s32 i;
    s32 runMode;

    #ifdef CHAOS_GAME_LOGIC_SIMULTANEOUS
    for (i = 0; i < CHAOS_ACTIVE_LOGIC_MODES_COUNT; i++) {
        runMode = sActiveModes[i];
    #else
    if (sChaosCurrentMode == -1 || gGlobalTimer - sChaosModeTimer >= CHAOS_GAME_LOGIC_CYCLE_FRAMES) {
        sChaosCurrentMode = sActiveModes[random_u16() % CHAOS_ACTIVE_LOGIC_MODES_COUNT];
        sChaosModeTimer = gGlobalTimer;
    }
    runMode = sChaosCurrentMode;
    #endif

    if (runMode == 1) {
        if ((gGlobalTimer % 240) < 90) {
            m->action = ACT_COUGHING;
            m->vel[0] = 0.0f;
            m->vel[1] = 0.0f;
            m->vel[2] = 0.0f;
            m->forwardVel = 0.0f;
        }
    }
    if (runMode == 3) {
        if (m->forwardVel > 0.0f && m->forwardVel < 30.0f) {
            m->forwardVel = 30.0f;
        }
    }
    if (runMode == 4) {
        if (m->vel[1] < 0.0f) {
            m->vel[1] += 2.0f;
        }
    }
    if (runMode == 5) {
        if (m->vel[1] < 0.0f) {
            m->vel[1] -= 3.0f;
        }
    }
    if (runMode == 6) {
        m->controller->stickX = -m->controller->stickX;
        m->controller->stickY = -m->controller->stickY;
    }
    if (runMode == 7) {
        m->forwardVel *= 2.5f;
        m->vel[0] *= 2.5f;
        m->vel[2] *= 2.5f;
    }
    if (runMode == 11) {
        extern struct Camera *gCamera;
        if (gCamera) {
            gCamera->areaCenY += (random_u16() % 10 - 5);
        }
    }
    if (runMode == 12) {
        if (m->action & ACT_FLAG_AIR) {
            m->action = ACT_TWIRLING;
        }
    }
    if (runMode == 13) {
        if (gGlobalTimer - sLastWarpTimer >= 120) {
            m->pos[1] += 400.0f;
            sLastWarpTimer = gGlobalTimer;
        }
    }
    if (runMode == 14) {
        if (m->pos[1] <= m->floorHeight + 2.0f && m->vel[1] <= 0.0f) {
            m->vel[1] = 50.0f;
            m->action = ACT_DOUBLE_JUMP;
        }
    }
    if (runMode == 15) {
        if (gGlobalTimer % 2 == 0) {
            m->particleFlags |= PARTICLE_FIRE;
        }
    }
    if (runMode == 16) {
        u32 capTick = (gGlobalTimer / 90) % 3;
        if (capTick == 0) {
            m->flags |= MARIO_WING_CAP;
            m->flags &= ~(MARIO_METAL_CAP | MARIO_VANISH_CAP);
        } else if (capTick == 1) {
            m->flags |= MARIO_METAL_CAP;
            m->flags &= ~(MARIO_WING_CAP | MARIO_VANISH_CAP);
        } else {
            m->flags |= MARIO_VANISH_CAP;
            m->flags &= ~(MARIO_WING_CAP | MARIO_METAL_CAP);
        }
    }
    if (runMode == 17) {
        if (gGlobalTimer % 15 == 0 && m->health > 0x100) {
            m->health -= 0x20;
        }
    }
    if (runMode == 19) {
        if ((m->action & ACT_FLAG_MOVING) && !(m->action & ACT_FLAG_AIR)) {
            m->action = ACT_BUTT_SLIDE;
        }
    }
    if (runMode == 20) {
        if (gGlobalTimer - sLastWarpTimer >= 150) {
            m->pos[0] += sins(m->faceAngle[1]) * 400.0f;
            m->pos[2] += coss(m->faceAngle[1]) * 400.0f;
            sLastWarpTimer = gGlobalTimer;
        }
    }

    #ifdef CHAOS_GAME_LOGIC_SIMULTANEOUS
    }
    #endif
}

void handle_game_logic_randomizer_post(struct MarioState *m) {
    static Vec3f sFrozenGfxPos;
    static u8 sGfxPosFrozen = FALSE;
    s32 i;
    s32 runMode;

    m->marioObj->header.gfx.scale[0] = 1.0f;
    m->marioObj->header.gfx.scale[1] = 1.0f;
    m->marioObj->header.gfx.scale[2] = 1.0f;

    #ifdef CHAOS_GAME_LOGIC_SIMULTANEOUS
    for (i = 0; i < CHAOS_ACTIVE_LOGIC_MODES_COUNT; i++) {
        runMode = sActiveModes[i];
    #else
        runMode = sChaosCurrentMode;
    #endif

    if (runMode == 2) {
        if (m->action & ACT_FLAG_STATIONARY) {
            sGfxPosFrozen = FALSE;
        } else {
            if (!sGfxPosFrozen) {
                vec3f_copy(sFrozenGfxPos, m->marioObj->header.gfx.pos);
                sGfxPosFrozen = TRUE;
            }
            vec3f_copy(m->marioObj->header.gfx.pos, sFrozenGfxPos);
        }
    }
    if (runMode == 8) {
        m->marioObj->header.gfx.scale[0] = 3.0f;
        m->marioObj->header.gfx.scale[1] = 3.0f;
        m->marioObj->header.gfx.scale[2] = 3.0f;
    }
    if (runMode == 9) {
        m->marioObj->header.gfx.scale[0] = 0.3f;
        m->marioObj->header.gfx.scale[1] = 0.3f;
        m->marioObj->header.gfx.scale[2] = 0.3f;
    }
    if (runMode == 10) {
        m->marioObj->header.gfx.scale[0] = 0.1f;
    }
    if (runMode == 18) {
        m->marioObj->header.gfx.node.flags |= GRAPH_RENDER_INVISIBLE;
    }

    #ifdef CHAOS_GAME_LOGIC_SIMULTANEOUS
    }
    #endif

    #ifdef CHAOS_GAME_LOGIC_SIMULTANEOUS
    {
        u8 mode2Found = FALSE;
        for (i = 0; i < CHAOS_ACTIVE_LOGIC_MODES_COUNT; i++) {
            if (sActiveModes[i] == 2) {
                mode2Found = TRUE;
                break;
            }
        }
        if (!mode2Found) {
            sGfxPosFrozen = FALSE;
        }
    }
    #else
    if (sChaosCurrentMode != 2) {
        sGfxPosFrozen = FALSE;
    }
    #endif
}
#endif

s32 execute_mario_action(UNUSED struct Object *o) {";

                        if (!sourcePatcher.PatchSourceFile("src/game/mario.c", oldFunc, newFunc))
                            throw new InvalidDataException("gameplay chaos hook does not match this source version");

                        string oldPre = "        mario_reset_bodystate(gMarioState);\n        update_mario_inputs(gMarioState);";
                        string newPre = "        mario_reset_bodystate(gMarioState);\n        update_mario_inputs(gMarioState);\n#ifdef CHAOS_ACTIVE_LOGIC_MODES_COUNT\n        handle_game_logic_randomizer_pre(gMarioState);\n#endif";
                        sourcePatcher.PatchSourceFile("src/game/mario.c", oldPre, newPre);

                        string oldPost = "        return gMarioState->particleFlags;";
                        string newPost = "#ifdef CHAOS_ACTIVE_LOGIC_MODES_COUNT\n        handle_game_logic_randomizer_post(gMarioState);\n#endif\n        return gMarioState->particleFlags;";
                        sourcePatcher.PatchSourceFile("src/game/mario.c", oldPost, newPost);

                        sourcePatcher.PatchSourceFile("src/game/mario.c", "#include \"sm64.h\"", "#include \"sm64.h\"\n#include \"chaos_config.h\"");
                    }
                    
                    if (alienSound) LongNoteChaosPatch.Apply(_projectRoot);

                    if (scrambleTitleScreen)
                    {
                        string oldLogoScale = "        guScale(scaleMat, scaleX, scaleY, scaleZ);\n\n        gSPMatrix(dlIter++, scaleMat, G_MTX_MODELVIEW | G_MTX_MUL | G_MTX_PUSH);\n        gSPDisplayList(dlIter++, &intro_seg7_dl_logo);  // draw model\n        gSPPopMatrix(dlIter++, G_MTX_MODELVIEW);";
                        string newLogoScale = @"#ifdef CHAOS_TITLE_SCRAMBLER_MODE
        {
            Mtx *rotMat = alloc_display_list(sizeof(*rotMat));
            Mtx *transMat = alloc_display_list(sizeof(*transMat));
            u32 seed = gGlobalTimer * 1664525 + 1013904223;
            guTranslate(transMat, 0.0f, 0.0f, 0.0f);
            guRotate(rotMat, 0.0f, 0.0f, 0.0f, 0.0f);
            
            switch (CHAOS_TITLE_SCRAMBLER_MODE) {
                case 1: // Glitchy Scale Jitter
                    scaleX *= (1.0f + 0.3f * sins(gGlobalTimer * 4000));
                    scaleY *= (1.0f + 0.3f * coss(gGlobalTimer * 5000));
                    scaleZ *= (1.0f + 0.3f * sins(gGlobalTimer * 6000));
                    break;
                case 2: // Vinesauce Spin
                    guRotate(rotMat, gGlobalTimer * 8.0f, 0.0f, 1.0f, 1.0f);
                    break;
                case 3: // Melt / Vertical Stretch
                    scaleY *= 4.0f + 3.0f * sins(gGlobalTimer * 800);
                    scaleX *= 0.3f;
                    break;
                case 4: // Disco Jump
                    guTranslate(transMat, 40.0f * sins(gGlobalTimer * 1500), 30.0f * coss(gGlobalTimer * 2000), 0.0f);
                    break;
                case 5: // Upside Down / Mirror
                    scaleY *= -1.0f;
                    scaleX *= -1.0f;
                    break;
                case 6: // Random Warp Jitter
                    scaleX *= (0.5f + 1.0f * (seed % 100) / 100.0f);
                    guRotate(rotMat, (seed % 360), 1.0f, 1.0f, 1.0f);
                    break;
                case 7: // Shrinking Void
                    {
                        f32 s = 0.5f + 0.5f * sins(gGlobalTimer * 3000);
                        scaleX *= s; scaleY *= s; scaleZ *= s;
                    }
                    break;
                case 8: // Chaotic Skewed
                    guRotate(rotMat, 45.0f + 30.0f * sins(gGlobalTimer * 1000), 1.0f, 0.0f, 0.0f);
                    guTranslate(transMat, 0.0f, 0.0f, 50.0f * coss(gGlobalTimer * 1000));
                    break;
                case 9: // Completely Random Glitch
                    scaleX *= ((seed % 200) - 100) / 50.0f;
                    scaleY *= (((seed >> 4) % 200) - 100) / 50.0f;
                    guTranslate(transMat, (seed % 100) - 50, ((seed >> 4) % 100) - 50, ((seed >> 8) % 100) - 50);
                    break;
            }
            guScale(scaleMat, scaleX, scaleY, scaleZ);
            gSPMatrix(dlIter++, scaleMat, G_MTX_MODELVIEW | G_MTX_MUL | G_MTX_PUSH);
            gSPMatrix(dlIter++, rotMat, G_MTX_MODELVIEW | G_MTX_MUL | G_MTX_NOPUSH);
            gSPMatrix(dlIter++, transMat, G_MTX_MODELVIEW | G_MTX_MUL | G_MTX_NOPUSH);
        }
#else
        guScale(scaleMat, scaleX, scaleY, scaleZ);
        gSPMatrix(dlIter++, scaleMat, G_MTX_MODELVIEW | G_MTX_MUL | G_MTX_PUSH);
#endif
        gSPDisplayList(dlIter++, &intro_seg7_dl_logo);  // draw model
        gSPPopMatrix(dlIter++, G_MTX_MODELVIEW);";

                        sourcePatcher.PatchSourceFile("src/menu/intro_geo.c", oldLogoScale, newLogoScale);
                        sourcePatcher.PatchSourceFile("src/menu/intro_geo.c", "#include <PR/ultratypes.h>", "#include <PR/ultratypes.h>\n#include \"chaos_config.h\"\n#include \"engine/math_util.h\"\nextern u32 gGlobalTimer;");
                    }

                    if (randomizeCutsceneCamera)
                    {
                        string oldIntroMove = "    posReturn += focusReturn; // Unused\n    return focusReturn;\n}";
                        string newIntroMove = @"    posReturn += focusReturn; // Unused
#ifdef CHAOS_CUTSCENE_CAMERA_MODE
    {
        void apply_chaos_intro_camera(struct Camera *c);
        apply_chaos_intro_camera(c);
    }
#endif
    return focusReturn;
}";
                        sourcePatcher.PatchSourceFile("src/game/camera.c", oldIntroMove, newIntroMove);

                        string oldIntroFollow = "BAD_RETURN(s32) cutscene_intro_peach_follow_pipe_spline(struct Camera *c) {\n    move_point_along_spline(c->pos, sIntroPipeToDialogPosition, &sCutsceneSplineSegment, &sCutsceneSplineSegmentProgress);\n    move_point_along_spline(c->focus, sIntroPipeToDialogFocus, &sCutsceneSplineSegment, &sCutsceneSplineSegmentProgress);\n}";
                        string newIntroFollow = @"BAD_RETURN(s32) cutscene_intro_peach_follow_pipe_spline(struct Camera *c) {
    move_point_along_spline(c->pos, sIntroPipeToDialogPosition, &sCutsceneSplineSegment, &sCutsceneSplineSegmentProgress);
    move_point_along_spline(c->focus, sIntroPipeToDialogFocus, &sCutsceneSplineSegment, &sCutsceneSplineSegmentProgress);
#ifdef CHAOS_CUTSCENE_CAMERA_MODE
    {
        void apply_chaos_intro_camera(struct Camera *c);
        apply_chaos_intro_camera(c);
    }
#endif
}";
                        sourcePatcher.PatchSourceFile("src/game/camera.c", oldIntroFollow, newIntroFollow);

                        string cameraHelper = @"#ifdef CHAOS_CUTSCENE_CAMERA_MODE
void apply_chaos_intro_camera(struct Camera *c) {
    extern u32 gGlobalTimer;
    #ifdef CHAOS_SEED
    u32 seed = (gGlobalTimer + CHAOS_SEED) * 1664525 + 1013904223;
    #else
    u32 seed = gGlobalTimer * 1664525 + 1013904223;
    #endif
    f32 rx, rz;
    
    switch (CHAOS_CUTSCENE_CAMERA_MODE) {
        case 1: // Random Static Angles
            c->pos[0] += 2000.f * sins(gGlobalTimer / 50 * 50);
            c->pos[1] += 500.f;
            c->pos[2] += 2000.f * coss(gGlobalTimer / 50 * 50);
            break;
        case 2: // Jittery Handheld Camera
            c->pos[0] += (seed % 100 - 50) * 8.0f;
            c->pos[1] += ((seed >> 4) % 100 - 50) * 8.0f;
            c->pos[2] += ((seed >> 8) % 100 - 50) * 8.0f;
            c->focus[0] += ((seed >> 12) % 100 - 50) * 4.0f;
            c->focus[1] += ((seed >> 16) % 100 - 50) * 4.0f;
            c->focus[2] += ((seed >> 20) % 100 - 50) * 4.0f;
            break;
        case 3: // Drunken / Swaying Camera
            c->pos[0] += 800.f * sins(gGlobalTimer * 200);
            c->pos[1] += 400.f * coss(gGlobalTimer * 150);
            c->pos[2] += 800.f * coss(gGlobalTimer * 200);
            break;
        case 4: // Spinning Vortex
            rx = c->pos[0] - c->focus[0];
            rz = c->pos[2] - c->focus[2];
            c->pos[0] = c->focus[0] + rx * coss(gGlobalTimer * 800) - rz * sins(gGlobalTimer * 800);
            c->pos[2] = c->focus[2] + rx * sins(gGlobalTimer * 800) + rz * coss(gGlobalTimer * 800);
            break;
        case 5: // Close-up Focus
            c->pos[0] = c->focus[0] + (c->pos[0] - c->focus[0]) * 0.1f;
            c->pos[1] = c->focus[1] + (c->pos[1] - c->focus[1]) * 0.1f;
            c->pos[2] = c->focus[2] + (c->pos[2] - c->focus[2]) * 0.1f;
            break;
        case 6: // Birds-Eye View
            c->pos[0] = c->focus[0] + 10.0f;
            c->pos[1] = c->focus[1] + 3000.0f;
            c->pos[2] = c->focus[2] + 10.0f;
            break;
        case 7: // First Person view
            c->pos[0] = c->focus[0] - 300.0f;
            c->pos[1] = c->focus[1] + 100.0f;
            c->pos[2] = c->focus[2];
            break;
        case 8: // Warped Perspective
            c->pos[0] += 500.f * sins(gGlobalTimer * 400);
            c->focus[1] += 300.f * coss(gGlobalTimer * 500);
            break;
        case 9: // Completely Chaotic
            seed = (gGlobalTimer / 30) * 1664525 + 1013904223;
            c->pos[0] += (seed % 2000) - 1000;
            c->pos[1] += ((seed >> 4) % 1000) - 500;
            c->focus[2] += ((seed >> 8) % 1000) - 500;
            break;
    }
}
#endif
";
                        sourcePatcher.PatchSourceFile("src/game/camera.c", "s32 intro_peach_move_camera_start_to_pipe(struct Camera *c, struct CutsceneSplinePoint positionSpline[],", cameraHelper + "s32 intro_peach_move_camera_start_to_pipe(struct Camera *c, struct CutsceneSplinePoint positionSpline[],");
                        sourcePatcher.PatchSourceFile("src/game/camera.c", "#include <ultra64.h>", "#include <ultra64.h>\n#include \"chaos_config.h\"");
                    }

                    if (lakituCameraChaos)
                    {
                        string oldCameraHelper = "    clamp_pitch(gLakituState.pos, gLakituState.focus, 0x3E00, -0x3E00);\n    gLakituState.mode = c->mode;\n    gLakituState.defMode = c->defMode;\n}";
                        string newCameraHelper = "    clamp_pitch(gLakituState.pos, gLakituState.focus, 0x3E00, -0x3E00);\n    gLakituState.mode = c->mode;\n    gLakituState.defMode = c->defMode;\n}\n\n#ifdef CHAOS_LAKITU_CAMERA_MODE\nvoid apply_chaos_lakitu_camera(struct Camera *c) {\n    extern struct MarioState gMarioStates[];\n    struct MarioState *m = &gMarioStates[0];\n    extern u32 gGlobalTimer;\n#ifdef CHAOS_SEED\n    u32 seed = (gGlobalTimer + CHAOS_SEED) * 1664525 + 1013904223;\n#else\n    u32 seed = gGlobalTimer * 1664525 + 1013904223;\n#endif\n    f32 dist;\n    int activeMode = CHAOS_LAKITU_CAMERA_MODE;\n    if (activeMode == 10) {\n        activeMode = (gGlobalTimer / 90) % 9 + 1;\n    }\n\n    switch (activeMode) {\n        case 1:\n            gLakituState.pos[0] += (seed % 60 - 30) * 1.5f;\n            gLakituState.pos[1] += ((seed >> 4) % 60 - 30) * 1.5f;\n            gLakituState.pos[2] += ((seed >> 8) % 60 - 30) * 1.5f;\n            gLakituState.focus[0] += ((seed >> 12) % 40 - 20) * 1.0f;\n            gLakituState.focus[1] += ((seed >> 16) % 40 - 20) * 1.0f;\n            gLakituState.focus[2] += ((seed >> 20) % 40 - 20) * 1.0f;\n            break;\n        case 2:\n            gLakituState.pos[0] = m->pos[0];\n            gLakituState.pos[1] = m->pos[1] + 1800.f;\n            gLakituState.pos[2] = m->pos[2] + 1.f;\n            vec3f_copy(gLakituState.focus, m->pos);\n            break;\n        case 3:\n            gLakituState.pos[0] = m->pos[0] + 500.f * sins(m->faceAngle[1]);\n            gLakituState.pos[1] = m->pos[1] + 120.f;\n            gLakituState.pos[2] = m->pos[2] + 500.f * coss(m->faceAngle[1]);\n            vec3f_copy(gLakituState.focus, m->pos);\n            break;\n        case 4:\n            dist = 800.f;\n            gLakituState.pos[0] = m->pos[0] + dist * sins(gGlobalTimer * 200);\n            gLakituState.pos[1] = m->pos[1] + 250.f;\n            gLakituState.pos[2] = m->pos[2] + dist * coss(gGlobalTimer * 200);\n            vec3f_copy(gLakituState.focus, m->pos);\n            break;\n        case 5:\n            gLakituState.pos[0] += 300.f * sins(gGlobalTimer * 250);\n            gLakituState.pos[1] += 150.f * coss(gGlobalTimer * 180);\n            break;\n        case 6:\n            dist = 700.f + 500.f * sins(gGlobalTimer * 150);\n            gLakituState.pos[0] = m->pos[0] + dist * sins(c->yaw);\n            gLakituState.pos[2] = m->pos[2] + dist * coss(c->yaw);\n            break;\n        case 7:\n            gLakituState.pos[0] = m->pos[0];\n            gLakituState.pos[1] = m->pos[1] + 200.f;\n            gLakituState.pos[2] = m->pos[2] + 900.f;\n            vec3f_copy(gLakituState.focus, m->pos);\n            break;\n        case 8:\n            gLakituState.pos[0] = m->pos[0] - (m->pos[0] - gLakituState.pos[0]) * 0.999f;\n            gLakituState.pos[1] = m->pos[1] + 600.f;\n            gLakituState.pos[2] = m->pos[2] - (m->pos[2] - gLakituState.pos[2]) * 0.999f;\n            vec3f_copy(gLakituState.focus, m->pos);\n            break;\n        case 9:\n            gLakituState.pos[1] = m->pos[1] - (gLakituState.pos[1] - m->pos[1]);\n            break;\n    }\n    vec3f_copy(c->pos, gLakituState.pos);\n    vec3f_copy(c->focus, gLakituState.focus);\n}\n#endif";
                        sourcePatcher.PatchSourceFile("src/game/camera.c", oldCameraHelper, newCameraHelper);

                        string oldCameraLakitu = "    update_lakitu(c);\n\n    gLakituState.lastFrameAction = sMarioCamState->action;";
                        string newCameraLakitu = "    update_lakitu(c);\n#ifdef CHAOS_LAKITU_CAMERA_MODE\n    {\n        void apply_chaos_lakitu_camera(struct Camera *c);\n        apply_chaos_lakitu_camera(c);\n    }\n#endif\n\n    gLakituState.lastFrameAction = sMarioCamState->action;";
                        sourcePatcher.PatchSourceFile("src/game/camera.c", oldCameraLakitu, newCameraLakitu);
                        sourcePatcher.PatchSourceFile("src/game/camera.c", "#include <ultra64.h>", "#include <ultra64.h>\n#include \"chaos_config.h\"");
                    }

                    if (startLevelChaos)
                    {
                        string oldInitSaveFile = "    gNeverEnteredCastle = !save_file_exists(gCurrSaveFileNum - 1);\n\n    gCurrLevelNum = levelNum;";
                        string newInitSaveFile = "    gNeverEnteredCastle = !save_file_exists(gCurrSaveFileNum - 1);\n\n#ifdef CHAOS_START_LEVEL\n    levelNum = CHAOS_START_LEVEL;\n#endif\n\n    gCurrLevelNum = levelNum;";
                        sourcePatcher.PatchSourceFile("src/game/level_update.c", oldInitSaveFile, newInitSaveFile);

                        string oldCutscene = "            } else if (!gDebugLevelSelect) {\n                if (gMarioState->action != ACT_UNINITIALIZED) {\n                    if (save_file_exists(gCurrSaveFileNum - 1)) {\n                        set_mario_action(gMarioState, ACT_IDLE, 0);\n                    } else {\n                        set_mario_action(gMarioState, ACT_INTRO_CUTSCENE, 0);\n                        val4 = TRUE;\n                    }\n                }\n            }";
                        string newCutscene = "            } else if (!gDebugLevelSelect) {\n                if (gMarioState->action != ACT_UNINITIALIZED) {\n                    if (save_file_exists(gCurrSaveFileNum - 1)) {\n                        set_mario_action(gMarioState, ACT_IDLE, 0);\n                    } else {\n#ifdef CHAOS_START_LEVEL\n                        if (gCurrLevelNum != LEVEL_CASTLE_GROUNDS) {\n                            set_mario_action(gMarioState, ACT_IDLE, 0);\n                        } else {\n                            set_mario_action(gMarioState, ACT_INTRO_CUTSCENE, 0);\n                            val4 = TRUE;\n                        }\n#else\n                        set_mario_action(gMarioState, ACT_INTRO_CUTSCENE, 0);\n                        val4 = TRUE;\n#endif\n                    }\n                }\n            }";
                        sourcePatcher.PatchSourceFile("src/game/level_update.c", oldCutscene, newCutscene);

                        sourcePatcher.PatchSourceFile("src/game/level_update.c", "#include <ultra64.h>", "#include <ultra64.h>\n#include \"chaos_config.h\"");
                    }

                    if (randomizeDl && dlMode == 8)
                    {
                        // Patch src/game/rendering_graph_node.c to flash castle grounds colors and enable texture coordinate gen mapping
                        string oldRenderList = "                gSPDisplayList(gDisplayListHead++, currList->displayList);";
                        string newRenderList = "#ifdef CHAOS_FACELESS_V2\n" +
                                               "                {\n" +
                                               "                    s32 isCastleGrounds = (gCurrLevelNum == LEVEL_CASTLE_GROUNDS);\n" +
                                               "                    if (isCastleGrounds) {\n" +
                                               "                        extern struct GraphNodeCamera *gCurGraphNodeCamera;\n" +
                                               "                        u8 r = 0x30, g = 0x30, b = 0x30;\n" +
                                               "                        if (gCurGraphNodeCamera != NULL) {\n" +
                                               "                            f32 sum = gCurGraphNodeCamera->pos[0] + gCurGraphNodeCamera->pos[1] + gCurGraphNodeCamera->pos[2];\n" +
                                               "                            s32 hash = (s32)(sum / 80.0f);\n" +
                                               "                            if (hash & 1) {\n" +
                                               "                                r = 0xD0; g = 0x10; b = 0x10;\n" +
                                               "                            }\n" +
                                               "                        }\n" +
                                               "                        gDPSetEnvColor(gDisplayListHead++, r, g, b, 0xFF);\n" +
                                               "                        gSPSetGeometryMode(gDisplayListHead++, G_TEXTURE_GEN | G_TEXTURE_GEN_LINEAR);\n" +
                                               "                    }\n" +
                                               "#endif\n" +
                                               "                gSPDisplayList(gDisplayListHead++, currList->displayList);\n" +
                                               "#ifdef CHAOS_FACELESS_V2\n" +
                                               "                    if (isCastleGrounds) {\n" +
                                               "                        gSPClearGeometryMode(gDisplayListHead++, G_TEXTURE_GEN | G_TEXTURE_GEN_LINEAR);\n" +
                                               "                        gDPSetEnvColor(gDisplayListHead++, 255, 255, 255, 255);\n" +
                                               "                    }\n" +
                                               "                }\n" +
                                               "#endif";
                        sourcePatcher.PatchSourceFile("src/game/rendering_graph_node.c", oldRenderList, newRenderList);
                        sourcePatcher.PatchSourceFile("src/game/rendering_graph_node.c", "#include \"sm64.h\"", "#include \"sm64.h\"\n#include \"chaos_config.h\"");

                        // Patch src/game/geo_misc.c to flash water colors blue like crystal and strip texture UV mapping
                        string oldMakeVertex = "void make_vertex(Vtx *vtx, s32 n, s16 x, s16 y, s16 z, s16 tx, s16 ty, u8 r, u8 g, u8 b, u8 a)\n" +
                                               "#else\n" +
                                               "void make_vertex(Vtx *vtx, s32 n, f32 x, f32 y, f32 z, s16 tx, s16 ty, u8 r, u8 g, u8 b, u8 a)\n" +
                                               "#endif\n" +
                                               "{\n" +
                                               "    vtx[n].v.ob[0] = x;";
                        string newMakeVertex = "void make_vertex(Vtx *vtx, s32 n, s16 x, s16 y, s16 z, s16 tx, s16 ty, u8 r, u8 g, u8 b, u8 a)\n" +
                                               "#else\n" +
                                               "void make_vertex(Vtx *vtx, s32 n, f32 x, f32 y, f32 z, s16 tx, s16 ty, u8 r, u8 g, u8 b, u8 a)\n" +
                                               "#endif\n" +
                                               "{\n" +
                                               "#ifdef CHAOS_FACELESS_V2\n" +
                                               "    if (gCurrLevelNum == LEVEL_CASTLE_GROUNDS) {\n" +
                                               "        if (a < 255 || (b > r && b > g)) {\n" +
                                               "            extern struct GraphNodeCamera *gCurGraphNodeCamera;\n" +
                                               "            r = 0x00; g = 0x90; b = 0xFF; a = 0xA0;\n" +
                                               "            if (gCurGraphNodeCamera != NULL) {\n" +
                                               "                f32 sum = gCurGraphNodeCamera->pos[0] + gCurGraphNodeCamera->pos[1] + gCurGraphNodeCamera->pos[2];\n" +
                                               "                s32 hash = (s32)(sum / 50.0f);\n" +
                                               "                if (hash & 1) {\n" +
                                               "                    r = 0x7F; g = 0xD0; b = 0xFF; a = 0xE0;\n" +
                                               "                }\n" +
                                               "            }\n" +
                                               "            tx = 0;\n" +
                                               "            ty = 0;\n" +
                                               "        }\n" +
                                               "    }\n" +
                                               "#endif\n" +
                                               "    vtx[n].v.ob[0] = x;";
                        sourcePatcher.PatchSourceFile("src/game/geo_misc.c", oldMakeVertex, newMakeVertex);
                        sourcePatcher.PatchSourceFile("src/game/geo_misc.c", "#include \"sm64.h\"", "#include \"sm64.h\"\n#include \"chaos_config.h\"\n#ifdef CHAOS_FACELESS_V2\nextern u32 gGlobalTimer;\nextern struct GraphNodeCamera *gCurGraphNodeCamera;\n#endif");
                    }


    }
}
