using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Text;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace Sm64DecompLevelViewer
{
    public partial class ChaosEngineWindow : Window
    {
        private readonly string _projectRoot;
        private readonly Random _random = new();
        private readonly List<SoundReplacerWindow.SoundReplacementRule> _sfxReplacementRules = new();
        private readonly List<TextureReplacerWindow.TextureReplacementRule> _textureReplacementRules = new();
        private readonly List<int> _activeM64Modes = new() { 0, 1, 2, 3, 4, 5 };

        private readonly string _presetsDir;
        private bool _isLoadingPreset = false;
        private List<string> _selectedTextures = new();

        public ChaosEngineWindow(string projectRoot)
        {
            InitializeComponent();
            _projectRoot = projectRoot;
            
            _presetsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Randm64", "Settings", "ChaosPresets");
            Directory.CreateDirectory(_presetsDir);

            LoadProjectLevelsAndM64s();
            LoadPresetsList();

            string defaultPath = Path.Combine(_presetsDir, "_default_settings.json");
            if (File.Exists(defaultPath))
            {
                LoadPresetFromFile(defaultPath);
            }
        }

        private async void ChaosButton_Click(object sender, RoutedEventArgs e)
        {
            double intensity = IntensifySlider.Value;
            bool targetMusic = TargetMusicNotesCheck.IsChecked == true;
            bool targetSounds = TargetSoundsCheck.IsChecked == true;
            bool shuffleSounds = ShuffleSoundsCheck.IsChecked == true;
            bool excludeInstruments = ExcludeInstrumentsShuffleCheck.IsChecked == true;
            bool excludeSfx = ExcludeSfxShuffleCheck.IsChecked == true;
            bool shuffleSfxOnly = ShuffleSfxOnlyCheck.IsChecked == true;
            bool randomizeDl = RandomizeDlCheck.IsChecked == true;
            int dlMode = DlRandomizerModeComboBox.SelectedIndex;
            bool randomizeCollision = RandomizeCollisionCheck.IsChecked == true;
            int collisionMode = CollisionRandomizerModeComboBox.SelectedIndex;
            bool targetModels = TargetModelsCheck.IsChecked == true;
            bool targetGoddard = TargetGoddardCheck.IsChecked == true;
            int goddardMode = GoddardModeComboBox.SelectedIndex;
            int mode = ModeComboBox.SelectedIndex;
            bool randomizeMarioColors = RandomizeMarioColorsCheck.IsChecked == true;
            int marioColorsArea = MarioColorsAreaComboBox.SelectedIndex;
            int marioColorsMode = MarioColorsModeComboBox.SelectedIndex;

            bool randomizeSkybox = RandomizeSkyboxCheck.IsChecked == true;
            bool randomizeText = RandomizeTextCheck.IsChecked == true;
            int textMode = TextRandomizeModeComboBox.SelectedIndex;
            int m64Mode = 0; // ignored, randomly selected from _activeM64Modes in CorruptM64SequenceFile
            bool randomizeTextures = RandomizeTexturesCheck.IsChecked == true;
            int textureMode = TextureRandomizeModeComboBox.SelectedIndex;
            
            bool glitchAnimations = GlitchAnimationsCheck.IsChecked == true;
            int animMode = AnimationGlitcherModeComboBox.SelectedIndex;
            bool glitchHud = GlitchHudCheck.IsChecked == true;
            int hudMode = HudGlitcherModeComboBox.SelectedIndex;

            bool replaceSfx = ReplaceSfxCheck.IsChecked == true;
            bool replaceTexturesCustom = ReplaceTexturesCustomCheck.IsChecked == true;

            bool jumpWeird = ChaosLogicJumpWeird.IsChecked == true;
            bool jumpDeath = ChaosLogicJumpDeath.IsChecked == true;
            bool limboMario = LimboMarioCheck.IsChecked == true;
            int limboMarioMode = LimboMarioModeComboBox.SelectedIndex;
            bool alienSound = AlienSoundCheatCheck.IsChecked == true;

            bool scrambleTitleScreen = ScrambleTitleScreenCheck.IsChecked == true;
            int titleScramblerMode = TitleScreenScramblerModeComboBox.SelectedIndex;
            bool randomizeCutsceneCamera = RandomizeCutsceneCameraCheck.IsChecked == true;
            int cutsceneCameraMode = CutsceneCameraModeComboBox.SelectedIndex;
            bool lakituCameraChaos = LakituCameraChaosCheck.IsChecked == true;
            int lakituCameraMode = LakituCameraModeComboBox.SelectedIndex;
            bool startLevelChaos = StartLevelChaosCheck.IsChecked == true;
            string startLevelConstant = "LEVEL_CASTLE_GROUNDS";
            string[] startLevelConstants = {
                "LEVEL_BOB", "LEVEL_WF", "LEVEL_JRB", "LEVEL_CCM", "LEVEL_BBH", "LEVEL_HMC",
                "LEVEL_LLL", "LEVEL_SSL", "LEVEL_DDD", "LEVEL_SL", "LEVEL_WDW", "LEVEL_TTM",
                "LEVEL_THI", "LEVEL_TTC", "LEVEL_RR", "LEVEL_SA", "LEVEL_COTMC", "LEVEL_TOTWC",
                "LEVEL_BITDW", "LEVEL_BITFS", "LEVEL_BITS"
            };
            int startLevelIndex = StartLevelComboBox.SelectedIndex;
            if (startLevelIndex >= 0 && startLevelIndex < startLevelConstants.Length)
            {
                startLevelConstant = startLevelConstants[startLevelIndex];
            }

            string selectedLevel = "All Levels";
            Dispatcher.Invoke(() =>
            {
                selectedLevel = (LevelSelectionComboBox.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? "All Levels";
            });

            if (!targetMusic && !targetSounds && !randomizeDl && !randomizeCollision && !targetModels && !targetGoddard && !shuffleSounds &&
                !randomizeSkybox && !randomizeText && !jumpWeird && !jumpDeath && !limboMario && !alienSound && !randomizeTextures &&
                !glitchAnimations && !glitchHud && !replaceSfx && !replaceTexturesCustom && !randomizeMarioColors && DlExclusionCheck.IsChecked != true &&
                !scrambleTitleScreen && !randomizeCutsceneCamera && !startLevelChaos && !lakituCameraChaos)
            {
                MessageBox.Show("Please select at least one target asset type or cheat to inflict.", "No Targets Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ChaosButton.IsEnabled = false;
            CorruptionProgressBar.Visibility = Visibility.Visible;
            CorruptionProgressBar.Value = 0;
            StatusTextBlock.Text = "Scanning workspace for files...";

            try
            {
                await Task.Run(() =>
                {
                    // Perform sound shuffling first if requested
                    if (shuffleSounds)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            StatusTextBlock.Text = "Randomizing and shuffling sound samples...";
                        });

                        string sampleDir = Path.Combine(_projectRoot, "sound", "samples");
                        if (Directory.Exists(sampleDir))
                        {
                            var aiffFilesList = Directory.GetFiles(sampleDir, "*.aiff", SearchOption.AllDirectories).ToList();
                            if (excludeInstruments)
                            {
                                aiffFilesList = aiffFilesList.Where(f => !f.Split(Path.DirectorySeparatorChar).Contains("instruments")).ToList();
                            }
                            if (excludeSfx)
                            {
                                aiffFilesList = aiffFilesList.Where(f => !f.Split(Path.DirectorySeparatorChar).Any(p => p.StartsWith("sfx", StringComparison.OrdinalIgnoreCase))).ToList();
                            }
                            if (shuffleSfxOnly)
                            {
                                aiffFilesList = aiffFilesList.Where(f => f.Split(Path.DirectorySeparatorChar).Any(p => p.StartsWith("sfx", StringComparison.OrdinalIgnoreCase))).ToList();
                            }
                            var aiffFiles = aiffFilesList.ToArray();
                            if (aiffFiles.Length > 0)
                            {
                                // Ensure all are backed up first
                                foreach (var file in aiffFiles)
                                {
                                    string backupPath = file + ".bak";
                                    if (!File.Exists(backupPath))
                                    {
                                        File.Copy(file, backupPath);
                                    }
                                }

                                int sfxMode = 0;
                                bool sfxIdentityShuffle = true;
                                bool sfxPitchVariation = false;
                                Dispatcher.Invoke(() =>
                                {
                                    sfxMode = SfxRandomizerModeComboBox.SelectedIndex;
                                    sfxIdentityShuffle = SfxIdentityShuffleCheck.IsChecked == true;
                                    sfxPitchVariation = SfxPitchVariationCheck.IsChecked == true;
                                });

                                // Define source backups to target mapping
                                string[] sourceBackups = new string[aiffFiles.Length];
                                if (sfxIdentityShuffle)
                                {
                                    // Shuffle
                                    var shuffledFiles = new List<string>(aiffFiles);
                                    int n = shuffledFiles.Count;
                                    while (n > 1)
                                    {
                                        n--;
                                        int k = _random.Next(n + 1);
                                        string temp = shuffledFiles[k];
                                        shuffledFiles[k] = shuffledFiles[n];
                                        shuffledFiles[n] = temp;
                                    }
                                    for (int i = 0; i < aiffFiles.Length; i++)
                                    {
                                        sourceBackups[i] = shuffledFiles[i] + ".bak";
                                    }
                                }
                                else
                                {
                                    for (int i = 0; i < aiffFiles.Length; i++)
                                    {
                                        sourceBackups[i] = aiffFiles[i] + ".bak";
                                    }
                                }

                                // Copy backups to active slots and apply modification
                                for (int i = 0; i < aiffFiles.Length; i++)
                                {
                                    string sourceBackup = sourceBackups[i];
                                    string targetFile = aiffFiles[i];
                                    File.Copy(sourceBackup, targetFile, true);

                                    // Apply our custom pitch / mode changes to the target file!
                                    if (sfxMode != 0 || sfxPitchVariation)
                                    {
                                        ModifyAiffFile(targetFile, sfxMode, sfxPitchVariation);
                                    }

                                    File.SetLastWriteTime(targetFile, DateTime.Now);
                                }
                            }
                        }
                    }

                    // Perform SFX replacement if requested
                    if (replaceSfx && _sfxReplacementRules.Count > 0)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            StatusTextBlock.Text = "Replacing sound files...";
                        });

                        foreach (var rule in _sfxReplacementRules)
                        {
                            try
                            {
                                // Make sure replacement is backed up
                                string replacementBackup = rule.ReplacementPath + ".bak";
                                if (!File.Exists(replacementBackup))
                                {
                                    File.Copy(rule.ReplacementPath, replacementBackup);
                                }

                                // Make sure target is backed up
                                string targetBackup = rule.TargetPath + ".bak";
                                if (!File.Exists(targetBackup))
                                {
                                    File.Copy(rule.TargetPath, targetBackup);
                                }

                                // Copy replacement backup to target file active slot
                                File.Copy(replacementBackup, rule.TargetPath, true);
                                File.SetLastWriteTime(rule.TargetPath, DateTime.Now);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Error replacing sound file {rule.TargetPath}: {ex.Message}");
                            }
                        }
                    }

                    // Perform Custom Texture replacement if requested
                    if (replaceTexturesCustom && _textureReplacementRules.Count > 0)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            StatusTextBlock.Text = "Applying custom texture replacements...";
                        });

                        foreach (var rule in _textureReplacementRules)
                        {
                            try
                            {
                                string targetPath = rule.TargetPath;
                                string repPath = rule.ReplacementPath;

                                if (File.Exists(repPath) && File.Exists(targetPath))
                                {
                                    // Make backup of target first
                                    string backupPath = targetPath + ".bak";
                                    if (!File.Exists(backupPath))
                                    {
                                        File.Copy(targetPath, backupPath);
                                    }

                                    // Read target dimensions
                                    int targetW = 64;
                                    int targetH = 64;
                                    double dpiX = 96;
                                    double dpiY = 96;
                                    using (var stream = new FileStream(targetPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                                    {
                                        var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                                        targetW = decoder.Frames[0].PixelWidth;
                                        targetH = decoder.Frames[0].PixelHeight;
                                        dpiX = decoder.Frames[0].DpiX;
                                        dpiY = decoder.Frames[0].DpiY;
                                    }

                                    // Get scaled pixels of replacement image
                                    byte[] pixels = GetTextureXPixels(repPath, targetW, targetH);

                                    // Write back as PNG to targetPath
                                    var bitmap = System.Windows.Media.Imaging.BitmapSource.Create(
                                        targetW, targetH, dpiX, dpiY,
                                        System.Windows.Media.PixelFormats.Bgra32, null, pixels, targetW * 4);

                                    var encoder = new PngBitmapEncoder();
                                    encoder.Frames.Add(BitmapFrame.Create(bitmap));

                                    using (var outStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None))
                                    {
                                        encoder.Save(outStream);
                                    }
                                    File.SetLastWriteTime(targetPath, DateTime.Now);
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Error replacing custom texture: {ex.Message}");
                            }
                        }
                    }

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
                                var existingLines = File.ReadAllLines(configPath);
                                foreach (var line in existingLines)
                                {
                                    string trimmed = line.Trim();
                                    if (trimmed.Contains("CHAOS_RANDOM_SKYBOX") ||
                                        trimmed.Contains("CHAOS_SKYBOX_SEED") ||
                                        trimmed.Contains("CHAOS_JUMP_WEIRD") ||
                                        trimmed.Contains("CHAOS_JUMP_DEATH") ||
                                        trimmed.Contains("CHAOS_LIMBO_MARIO") ||
                                        trimmed.Contains("CHAOS_LAKITU_CAMERA_MODE") ||
                                        trimmed.Contains("CHAOS_ALIEN_SOUND") ||
                                        trimmed.Contains("CHAOS_TITLE_SCRAMBLER_MODE") ||
                                        trimmed.Contains("CHAOS_CUTSCENE_CAMERA_MODE") ||
                                        trimmed.Contains("CHAOS_START_LEVEL") ||
                                        trimmed.Contains("CHAOS_FACELESS_V2"))
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

                            var newOptions = new List<string>();
                            if (randomizeSkybox)
                            {
                                newOptions.Add("#define CHAOS_RANDOM_SKYBOX");
                                newOptions.Add($"#define CHAOS_SKYBOX_SEED {skyboxSeed}");
                            }
                            if (jumpWeird)
                            {
                                newOptions.Add("#define CHAOS_JUMP_WEIRD");
                            }
                            if (jumpDeath)
                            {
                                newOptions.Add("#define CHAOS_JUMP_DEATH");
                            }
                            if (limboMario)
                            {
                                newOptions.Add("#define CHAOS_LIMBO_MARIO");
                                newOptions.Add($"#define CHAOS_LIMBO_MARIO_MODE {limboMarioMode}");
                            }
                            if (lakituCameraChaos)
                            {
                                newOptions.Add($"#define CHAOS_LAKITU_CAMERA_MODE {lakituCameraMode}");
                            }
                            if (alienSound)
                            {
                                newOptions.Add("#define CHAOS_ALIEN_SOUND");
                            }
                            if (scrambleTitleScreen)
                            {
                                newOptions.Add($"#define CHAOS_TITLE_SCRAMBLER_MODE {titleScramblerMode}");
                            }
                            if (randomizeCutsceneCamera)
                            {
                                newOptions.Add($"#define CHAOS_CUTSCENE_CAMERA_MODE {cutsceneCameraMode}");
                            }
                            if (startLevelChaos)
                            {
                                newOptions.Add($"#define CHAOS_START_LEVEL {startLevelConstant}");
                            }
                            if (randomizeDl && dlMode == 8)
                            {
                                newOptions.Add("#define CHAOS_FACELESS_V2");
                            }

                            lines.InsertRange(endifIndex, newOptions);

                            File.WriteAllText(configPath, string.Join("\n", lines).Replace("\r\n", "\n") + "\n");
                            File.SetLastWriteTime(configPath, DateTime.Now);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error writing chaos_config.h: {ex.Message}");
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
                            var levelDirs = Directory.GetDirectories(levelsDir);
                            foreach (var levelDir in levelDirs)
                            {
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
                                    var geoFiles = Directory.GetFiles(areasDir, "geo.inc.c", SearchOption.AllDirectories);
                                    foreach (var geoFile in geoFiles)
                                    {
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
                        
                        PatchSourceFile("src/game/mario_actions_airborne.c", "s32 act_jump(struct MarioState *m) {", "s32 act_jump(struct MarioState *m) {\n" + jumpHooksStr);
                        
                        string oldDouble = "s32 act_double_jump(struct MarioState *m) {\n" +
                                           "    s32 animation = (m->vel[1] >= 0.0f)\n" +
                                           "        ? MARIO_ANIM_DOUBLE_JUMP_RISE\n" +
                                           "        : MARIO_ANIM_DOUBLE_JUMP_FALL;";
                        string newDouble = "s32 act_double_jump(struct MarioState *m) {\n" +
                                           "    s32 animation = (m->vel[1] >= 0.0f)\n" +
                                           "        ? MARIO_ANIM_DOUBLE_JUMP_RISE\n" +
                                           "        : MARIO_ANIM_DOUBLE_JUMP_FALL;\n" +
                                           jumpHooksStr;
                        PatchSourceFile("src/game/mario_actions_airborne.c", oldDouble, newDouble);
                        
                        PatchSourceFile("src/game/mario_actions_airborne.c", "s32 act_triple_jump(struct MarioState *m) {", "s32 act_triple_jump(struct MarioState *m) {\n" + jumpHooksStr);
                        PatchSourceFile("src/game/mario_actions_airborne.c", "#include <PR/ultratypes.h>", "#include <PR/ultratypes.h>\n#include \"chaos_config.h\"");
                    }
                    
                    if (limboMario)
                    {
                        string oldLimbo = "        rotNode->rotation[0] = bodyState->torsoAngle[1];\n        rotNode->rotation[1] = bodyState->torsoAngle[2];\n        rotNode->rotation[2] = bodyState->torsoAngle[0];";
                        string newLimbo = "        rotNode->rotation[0] = bodyState->torsoAngle[1];\n        rotNode->rotation[1] = bodyState->torsoAngle[2];\n        rotNode->rotation[2] = bodyState->torsoAngle[0];\n#ifdef CHAOS_LIMBO_MARIO_MODE\n        {\n            extern u32 gGlobalTimer;\n            switch (CHAOS_LIMBO_MARIO_MODE) {\n                case 1:\n                    rotNode->rotation[0] += 0x3800;\n                    break;\n                case 2:\n                    rotNode->rotation[1] += gGlobalTimer * 0x800;\n                    break;\n                case 3:\n                    rotNode->rotation[0] -= 0x4000;\n                    break;\n                case 4:\n                    rotNode->rotation[0] += (gGlobalTimer % 2 == 0) ? 0x2400 : -0x2400;\n                    break;\n            }\n        }\n#endif";
                        PatchSourceFile("src/game/mario_misc.c", oldLimbo, newLimbo);
                        PatchSourceFile("src/game/mario_misc.c", "#include <PR/ultratypes.h>", "#include <PR/ultratypes.h>\n#include \"chaos_config.h\"");

                        // Patch rendering_graph_node.c for spaghetti stretch
                        string oldAnimPart = "    vec3s_copy(rotation, gVec3sZero);\n    vec3f_set(translation, node->translation[0], node->translation[1], node->translation[2]);";
                        string newAnimPart = "    vec3s_copy(rotation, gVec3sZero);\n    vec3f_set(translation, node->translation[0], node->translation[1], node->translation[2]);\n#ifdef CHAOS_LIMBO_MARIO_MODE\n    if (CHAOS_LIMBO_MARIO_MODE == 6) {\n        translation[0] *= 2.5f;\n        translation[1] *= 2.5f;\n        translation[2] *= 2.5f;\n    }\n#endif";
                        PatchSourceFile("src/game/rendering_graph_node.c", oldAnimPart, newAnimPart);
                        PatchSourceFile("src/game/rendering_graph_node.c", "#include \"sm64.h\"", "#include \"sm64.h\"\n#include \"chaos_config.h\"");

                        // Patch src/game/mario.c to sink Mario in quicksand/ground when Mode 5 is active
                        string oldSink = "void sink_mario_in_quicksand(struct MarioState *m) {\n    struct Object *o = m->marioObj;\n\n    if (o->header.gfx.throwMatrix) {\n        (*o->header.gfx.throwMatrix)[3][1] -= m->quicksandDepth;\n    }\n\n    o->header.gfx.pos[1] -= m->quicksandDepth;\n}";
                        string newSink = "void sink_mario_in_quicksand(struct MarioState *m) {\n    struct Object *o = m->marioObj;\n    f32 depth = m->quicksandDepth;\n#ifdef CHAOS_LIMBO_MARIO_MODE\n    if (CHAOS_LIMBO_MARIO_MODE == 5) {\n        depth += 40.0f;\n    }\n#endif\n\n    if (o->header.gfx.throwMatrix) {\n        (*o->header.gfx.throwMatrix)[3][1] -= depth;\n    }\n\n    o->header.gfx.pos[1] -= depth;\n}";
                        PatchSourceFile("src/game/mario.c", oldSink, newSink);
                        PatchSourceFile("src/game/mario.c", "#include \"sm64.h\"", "#include \"sm64.h\"\n#include \"chaos_config.h\"");
                    }
                    
                    if (alienSound)
                    {
                        string oldAlienLoop = "        {\n            u8 *_ptr_pc;\n            _ptr_pc = (*state).pc++;\n            cmd = *_ptr_pc;\n        }";
                        string newAlienLoop = "        {\n            u8 *_ptr_pc;\n            _ptr_pc = (*state).pc++;\n            cmd = *_ptr_pc;\n#ifdef CHAOS_ALIEN_SOUND\n            if (cmd > 0xc0) {\n                cmd = (cmd + 13) % 256;\n            }\n#endif\n        }";
                        PatchSourceFile("src/audio/copt/seq_channel_layer_process_script_copt.inc.c", oldAlienLoop, newAlienLoop);
                        PatchSourceFile("src/audio/seqplayer.c", "#include \"seqplayer.h\"", "#include \"seqplayer.h\"\n#include \"chaos_config.h\"");
                        string parentFile = Path.Combine(_projectRoot, "src", "audio", "seqplayer.c");
                        if (File.Exists(parentFile))
                        {
                            File.SetLastWriteTime(parentFile, DateTime.Now);
                        }
                    }

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

                        PatchSourceFile("src/menu/intro_geo.c", oldLogoScale, newLogoScale);
                        PatchSourceFile("src/menu/intro_geo.c", "#include <PR/ultratypes.h>", "#include <PR/ultratypes.h>\n#include \"chaos_config.h\"\n#include \"engine/math_util.h\"\nextern u32 gGlobalTimer;");
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
                        PatchSourceFile("src/game/camera.c", oldIntroMove, newIntroMove);

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
                        PatchSourceFile("src/game/camera.c", oldIntroFollow, newIntroFollow);

                        string cameraHelper = @"#ifdef CHAOS_CUTSCENE_CAMERA_MODE
void apply_chaos_intro_camera(struct Camera *c) {
    extern u32 gGlobalTimer;
    u32 seed = gGlobalTimer * 1664525 + 1013904223;
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
                        PatchSourceFile("src/game/camera.c", "s32 intro_peach_move_camera_start_to_pipe(struct Camera *c, struct CutsceneSplinePoint positionSpline[],", cameraHelper + "s32 intro_peach_move_camera_start_to_pipe(struct Camera *c, struct CutsceneSplinePoint positionSpline[],");
                        PatchSourceFile("src/game/camera.c", "#include <ultra64.h>", "#include <ultra64.h>\n#include \"chaos_config.h\"");
                    }

                    if (lakituCameraChaos)
                    {
                        string oldCameraHelper = "    clamp_pitch(gLakituState.pos, gLakituState.focus, 0x3E00, -0x3E00);\n    gLakituState.mode = c->mode;\n    gLakituState.defMode = c->defMode;\n}";
                        string newCameraHelper = "    clamp_pitch(gLakituState.pos, gLakituState.focus, 0x3E00, -0x3E00);\n    gLakituState.mode = c->mode;\n    gLakituState.defMode = c->defMode;\n}\n\n#ifdef CHAOS_LAKITU_CAMERA_MODE\nvoid apply_chaos_lakitu_camera(struct Camera *c) {\n    extern struct MarioState gMarioStates[];\n    struct MarioState *m = &gMarioStates[0];\n    extern u32 gGlobalTimer;\n    u32 seed = gGlobalTimer * 1664525 + 1013904223;\n    f32 dist;\n    int activeMode = CHAOS_LAKITU_CAMERA_MODE;\n    if (activeMode == 10) {\n        activeMode = (gGlobalTimer / 90) % 9 + 1;\n    }\n\n    switch (activeMode) {\n        case 1:\n            gLakituState.pos[0] += (seed % 60 - 30) * 1.5f;\n            gLakituState.pos[1] += ((seed >> 4) % 60 - 30) * 1.5f;\n            gLakituState.pos[2] += ((seed >> 8) % 60 - 30) * 1.5f;\n            gLakituState.focus[0] += ((seed >> 12) % 40 - 20) * 1.0f;\n            gLakituState.focus[1] += ((seed >> 16) % 40 - 20) * 1.0f;\n            gLakituState.focus[2] += ((seed >> 20) % 40 - 20) * 1.0f;\n            break;\n        case 2:\n            gLakituState.pos[0] = m->pos[0];\n            gLakituState.pos[1] = m->pos[1] + 1800.f;\n            gLakituState.pos[2] = m->pos[2] + 1.f;\n            vec3f_copy(gLakituState.focus, m->pos);\n            break;\n        case 3:\n            gLakituState.pos[0] = m->pos[0] + 500.f * sins(m->faceAngle[1]);\n            gLakituState.pos[1] = m->pos[1] + 120.f;\n            gLakituState.pos[2] = m->pos[2] + 500.f * coss(m->faceAngle[1]);\n            vec3f_copy(gLakituState.focus, m->pos);\n            break;\n        case 4:\n            dist = 800.f;\n            gLakituState.pos[0] = m->pos[0] + dist * sins(gGlobalTimer * 200);\n            gLakituState.pos[1] = m->pos[1] + 250.f;\n            gLakituState.pos[2] = m->pos[2] + dist * coss(gGlobalTimer * 200);\n            vec3f_copy(gLakituState.focus, m->pos);\n            break;\n        case 5:\n            gLakituState.pos[0] += 300.f * sins(gGlobalTimer * 250);\n            gLakituState.pos[1] += 150.f * coss(gGlobalTimer * 180);\n            break;\n        case 6:\n            dist = 700.f + 500.f * sins(gGlobalTimer * 150);\n            gLakituState.pos[0] = m->pos[0] + dist * sins(c->yaw);\n            gLakituState.pos[2] = m->pos[2] + dist * coss(c->yaw);\n            break;\n        case 7:\n            gLakituState.pos[0] = m->pos[0];\n            gLakituState.pos[1] = m->pos[1] + 200.f;\n            gLakituState.pos[2] = m->pos[2] + 900.f;\n            vec3f_copy(gLakituState.focus, m->pos);\n            break;\n        case 8:\n            gLakituState.pos[0] = m->pos[0] - (m->pos[0] - gLakituState.pos[0]) * 0.999f;\n            gLakituState.pos[1] = m->pos[1] + 600.f;\n            gLakituState.pos[2] = m->pos[2] - (m->pos[2] - gLakituState.pos[2]) * 0.999f;\n            vec3f_copy(gLakituState.focus, m->pos);\n            break;\n        case 9:\n            gLakituState.pos[1] = m->pos[1] - (gLakituState.pos[1] - m->pos[1]);\n            break;\n    }\n    vec3f_copy(c->pos, gLakituState.pos);\n    vec3f_copy(c->focus, gLakituState.focus);\n}\n#endif";
                        PatchSourceFile("src/game/camera.c", oldCameraHelper, newCameraHelper);

                        string oldCameraLakitu = "    update_lakitu(c);\n\n    gLakituState.lastFrameAction = sMarioCamState->action;";
                        string newCameraLakitu = "    update_lakitu(c);\n#ifdef CHAOS_LAKITU_CAMERA_MODE\n    {\n        void apply_chaos_lakitu_camera(struct Camera *c);\n        apply_chaos_lakitu_camera(c);\n    }\n#endif\n\n    gLakituState.lastFrameAction = sMarioCamState->action;";
                        PatchSourceFile("src/game/camera.c", oldCameraLakitu, newCameraLakitu);
                        PatchSourceFile("src/game/camera.c", "#include <ultra64.h>", "#include <ultra64.h>\n#include \"chaos_config.h\"");
                    }

                    if (startLevelChaos)
                    {
                        string oldInitSaveFile = "    gNeverEnteredCastle = !save_file_exists(gCurrSaveFileNum - 1);\n\n    gCurrLevelNum = levelNum;";
                        string newInitSaveFile = "    gNeverEnteredCastle = !save_file_exists(gCurrSaveFileNum - 1);\n\n#ifdef CHAOS_START_LEVEL\n    levelNum = CHAOS_START_LEVEL;\n#endif\n\n    gCurrLevelNum = levelNum;";
                        PatchSourceFile("src/game/level_update.c", oldInitSaveFile, newInitSaveFile);

                        string oldCutscene = "            } else if (!gDebugLevelSelect) {\n                if (gMarioState->action != ACT_UNINITIALIZED) {\n                    if (save_file_exists(gCurrSaveFileNum - 1)) {\n                        set_mario_action(gMarioState, ACT_IDLE, 0);\n                    } else {\n                        set_mario_action(gMarioState, ACT_INTRO_CUTSCENE, 0);\n                        val4 = TRUE;\n                    }\n                }\n            }";
                        string newCutscene = "            } else if (!gDebugLevelSelect) {\n                if (gMarioState->action != ACT_UNINITIALIZED) {\n                    if (save_file_exists(gCurrSaveFileNum - 1)) {\n                        set_mario_action(gMarioState, ACT_IDLE, 0);\n                    } else {\n#ifdef CHAOS_START_LEVEL\n                        if (gCurrLevelNum != LEVEL_CASTLE_GROUNDS) {\n                            set_mario_action(gMarioState, ACT_IDLE, 0);\n                        } else {\n                            set_mario_action(gMarioState, ACT_INTRO_CUTSCENE, 0);\n                            val4 = TRUE;\n                        }\n#else\n                        set_mario_action(gMarioState, ACT_INTRO_CUTSCENE, 0);\n                        val4 = TRUE;\n#endif\n                    }\n                }\n            }";
                        PatchSourceFile("src/game/level_update.c", oldCutscene, newCutscene);

                        PatchSourceFile("src/game/level_update.c", "#include <ultra64.h>", "#include <ultra64.h>\n#include \"chaos_config.h\"");
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
                        PatchSourceFile("src/game/rendering_graph_node.c", oldRenderList, newRenderList);
                        PatchSourceFile("src/game/rendering_graph_node.c", "#include \"sm64.h\"", "#include \"sm64.h\"\n#include \"chaos_config.h\"");

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
                        PatchSourceFile("src/game/geo_misc.c", oldMakeVertex, newMakeVertex);
                        PatchSourceFile("src/game/geo_misc.c", "#include \"sm64.h\"", "#include \"sm64.h\"\n#include \"chaos_config.h\"\n#ifdef CHAOS_FACELESS_V2\nextern u32 gGlobalTimer;\nextern struct GraphNodeCamera *gCurGraphNodeCamera;\n#endif");
                    }

                    // Apply game text randomization if requested
                    if (randomizeText)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            StatusTextBlock.Text = "Randomizing game text and dialogs...";
                        });

                        string textDir = Path.Combine(_projectRoot, "text");
                        if (Directory.Exists(textDir))
                        {
                            var textFiles = Directory.GetFiles(textDir, "*.h", SearchOption.AllDirectories)
                                .Concat(Directory.GetFiles(textDir, "*.inc.c", SearchOption.AllDirectories));
                            foreach (var file in textFiles)
                            {
                                try
                                {
                                    string backupPath = file + ".bak";
                                    if (!File.Exists(backupPath))
                                    {
                                        File.Copy(file, backupPath);
                                    }
                                    string content = File.ReadAllText(backupPath);
                                    string mutated = RandomizeGameText(content, textMode);
                                    File.WriteAllText(file, mutated);
                                    File.SetLastWriteTime(file, DateTime.Now);
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"Error randomizing text file {file}: {ex.Message}");
                                }
                            }
                        }
                    }

                    List<string> filesToCorrupt = new();

                    // 1. Music Notes (.m64)
                    if (targetMusic)
                    {
                        bool selectSpecificM64 = false;
                        var selectedM64Files = new List<string>();
                        Dispatcher.Invoke(() =>
                        {
                            selectSpecificM64 = M64SelectRadio.IsChecked == true;
                            if (selectSpecificM64)
                            {
                                foreach (System.Windows.Controls.ListBoxItem item in M64SelectionListBox.SelectedItems)
                                {
                                    if (item.Tag is string path)
                                    {
                                        selectedM64Files.Add(path);
                                    }
                                }
                            }
                        });

                        if (selectSpecificM64)
                        {
                            foreach (var file in selectedM64Files)
                            {
                                filesToCorrupt.Add(file);
                            }
                        }
                        else
                        {
                            string seqDir = Path.Combine(_projectRoot, "sound", "sequences");
                            if (Directory.Exists(seqDir))
                            {
                                var m64Files = Directory.GetFiles(seqDir, "*.m64", SearchOption.AllDirectories);
                                foreach (var file in m64Files)
                                {
                                    string fileName = Path.GetFileName(file).ToLower();
                                    if (fileName.Contains("00_sound_player") || fileName.Contains("sfx"))
                                    {
                                        continue;
                                    }
                                    filesToCorrupt.Add(file);
                                }
                            }
                        }
                    }

                    // 2. Sounds (.aiff)
                    if (targetSounds)
                    {
                        string sampleDir = Path.Combine(_projectRoot, "sound", "samples");
                        if (Directory.Exists(sampleDir))
                        {
                            filesToCorrupt.AddRange(Directory.GetFiles(sampleDir, "*.aiff", SearchOption.AllDirectories));
                        }
                    }

                    // 3. Display Lists / Models / Collisions (.c, .inc.c)
                    if (randomizeDl || targetModels || randomizeCollision)
                    {
                        string levelsDir = Path.Combine(_projectRoot, "levels");
                        if (Directory.Exists(levelsDir))
                        {
                            if (selectedLevel != "All Levels")
                            {
                                string targetDir = Path.Combine(levelsDir, selectedLevel);
                                if (Directory.Exists(targetDir))
                                {
                                    filesToCorrupt.AddRange(Directory.GetFiles(targetDir, "*.c", SearchOption.AllDirectories));
                                    filesToCorrupt.AddRange(Directory.GetFiles(targetDir, "*.inc.c", SearchOption.AllDirectories));
                                }
                            }
                            else
                            {
                                filesToCorrupt.AddRange(Directory.GetFiles(levelsDir, "*.c", SearchOption.AllDirectories));
                                filesToCorrupt.AddRange(Directory.GetFiles(levelsDir, "*.inc.c", SearchOption.AllDirectories));
                            }
                        }

                        // Actors only get scanned if "All Levels" is selected (since they are globally shared across all levels)
                        if (selectedLevel == "All Levels")
                        {
                            string actorsDir = Path.Combine(_projectRoot, "actors");
                            if (Directory.Exists(actorsDir))
                            {
                                filesToCorrupt.AddRange(Directory.GetFiles(actorsDir, "*.c", SearchOption.AllDirectories));
                                filesToCorrupt.AddRange(Directory.GetFiles(actorsDir, "*.inc.c", SearchOption.AllDirectories));
                            }
                        }

                        string editorLevelsDir = Path.Combine(_projectRoot, "leveleditor", "levels");
                        if (Directory.Exists(editorLevelsDir))
                        {
                            if (selectedLevel != "All Levels")
                            {
                                string targetDir = Path.Combine(editorLevelsDir, selectedLevel);
                                if (Directory.Exists(targetDir))
                                {
                                    filesToCorrupt.AddRange(Directory.GetFiles(targetDir, "*.c", SearchOption.AllDirectories));
                                    filesToCorrupt.AddRange(Directory.GetFiles(targetDir, "*.inc.c", SearchOption.AllDirectories));
                                }
                            }
                            else
                            {
                                filesToCorrupt.AddRange(Directory.GetFiles(editorLevelsDir, "*.c", SearchOption.AllDirectories));
                                filesToCorrupt.AddRange(Directory.GetFiles(editorLevelsDir, "*.inc.c", SearchOption.AllDirectories));
                            }
                        }
                    }

                    // 3b. Mario Clothing Colors
                    if (randomizeMarioColors)
                    {
                        string marioDir = Path.Combine(_projectRoot, "actors", "mario");
                        if (Directory.Exists(marioDir))
                        {
                            filesToCorrupt.AddRange(Directory.GetFiles(marioDir, "*.c", SearchOption.AllDirectories));
                            filesToCorrupt.AddRange(Directory.GetFiles(marioDir, "*.inc.c", SearchOption.AllDirectories));
                        }
                    }

                    // 4. Goddard (dynlist .c files)
                    if (targetGoddard)
                    {
                        string goddardDynDir = Path.Combine(_projectRoot, "src", "goddard", "dynlists");
                        if (Directory.Exists(goddardDynDir))
                        {
                            var files = Directory.GetFiles(goddardDynDir, "*.c", SearchOption.TopDirectoryOnly);
                            foreach (var f in files)
                            {
                                if (!f.EndsWith("dynlist_mario_master.c", StringComparison.OrdinalIgnoreCase))
                                {
                                    filesToCorrupt.Add(f);
                                }
                            }
                        }
                    }

                    // 5. Textures (.png)
                    if (randomizeTextures)
                    {
                        string texDir = Path.Combine(_projectRoot, "textures");
                        string actorsDir = Path.Combine(_projectRoot, "actors");

                        var pngFilesList = new List<string>();
                        if (Directory.Exists(texDir))
                        {
                            pngFilesList.AddRange(Directory.GetFiles(texDir, "*.png", SearchOption.AllDirectories));
                        }
                        if (Directory.Exists(actorsDir))
                        {
                            pngFilesList.AddRange(Directory.GetFiles(actorsDir, "*.png", SearchOption.AllDirectories));
                        }

                        bool randomizeSelectedOnly = false;
                        Dispatcher.Invoke(() => {
                            randomizeSelectedOnly = TextureRandomizeSelectedRadio.IsChecked == true;
                        });

                        if (randomizeSelectedOnly)
                        {
                            var selectedSet = new HashSet<string>(_selectedTextures.Select(p => Path.Combine(_projectRoot, p.Replace('/', Path.DirectorySeparatorChar))), StringComparer.OrdinalIgnoreCase);
                            pngFilesList = pngFilesList.Where(f => selectedSet.Contains(f)).ToList();
                        }
                        var pngFiles = pngFilesList.ToArray();

                            if (textureMode == 0) // Texture Swapping (Shuffle)
                            {
                                Dispatcher.Invoke(() =>
                                {
                                    StatusTextBlock.Text = "Grouping and swapping textures of matching sizes...";
                                });

                                // Group images by size
                                var sizeGroups = new Dictionary<string, List<string>>();
                                foreach (var file in pngFiles)
                                {
                                    try
                                    {
                                        // Create backup first
                                        string backupPath = file + ".bak";
                                        if (!File.Exists(backupPath))
                                        {
                                            File.Copy(file, backupPath);
                                        }

                                        // Read dimensions quickly
                                        using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
                                        {
                                            var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                                            int w = decoder.Frames[0].PixelWidth;
                                            int h = decoder.Frames[0].PixelHeight;
                                            string sizeKey = $"{w}_{h}";
                                            if (!sizeGroups.ContainsKey(sizeKey))
                                            {
                                                sizeGroups[sizeKey] = new List<string>();
                                            }
                                            sizeGroups[sizeKey].Add(file);
                                        }
                                    }
                                    catch {}
                                }

                                // Shuffle matching size files
                                foreach (var group in sizeGroups.Values)
                                {
                                    if (group.Count < 2) continue;

                                    var shuffled = new List<string>(group);
                                    int n = shuffled.Count;
                                    while (n > 1)
                                    {
                                        n--;
                                        int k = _random.Next(n + 1);
                                        string temp = shuffled[k];
                                        shuffled[k] = shuffled[n];
                                        shuffled[n] = temp;
                                    }

                                    // Swap content using backup files
                                    for (int i = 0; i < group.Count; i++)
                                    {
                                        if (_random.NextDouble() < (intensity / 100.0))
                                        {
                                            string sourceBackup = shuffled[i] + ".bak";
                                            string targetFile = group[i];
                                            File.Copy(sourceBackup, targetFile, true);
                                            File.SetLastWriteTime(targetFile, DateTime.Now);
                                        }
                                    }
                                }
                            }
                            else
                            {
                                // Add to list for sequential pixel corruption
                                foreach (var file in pngFiles)
                                {
                                    filesToCorrupt.Add(file);
                                }
                            }
                    }

                    // 6. HUD drawing
                    if (glitchHud)
                    {
                        string hudFile = Path.Combine(_projectRoot, "src", "game", "hud.c");
                        if (File.Exists(hudFile))
                        {
                            filesToCorrupt.Add(hudFile);
                        }
                    }

                    // 7. Mario Animations
                    if (glitchAnimations)
                    {
                        string animDir = Path.Combine(_projectRoot, "assets", "anims");
                        if (Directory.Exists(animDir))
                        {
                            filesToCorrupt.AddRange(Directory.GetFiles(animDir, "*.inc.c", SearchOption.AllDirectories));
                        }
                    }

                    int totalFiles = filesToCorrupt.Count;
                    if (totalFiles == 0)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            StatusTextBlock.Text = "No files found for selected targets.";
                        });
                        return;
                    }

                    int processed = 0;

                    foreach (var file in filesToCorrupt)
                    {
                        try
                        {
                            // Create backup before modifying if it doesn't already exist; restore original before corrupting
                            string backupPath = file + ".bak";
                            if (!File.Exists(backupPath))
                            {
                                File.Copy(file, backupPath);
                            }
                            else
                            {
                                File.Copy(backupPath, file, true);
                            }

                            string ext = Path.GetExtension(file).ToLower();

                            if (ext == ".m64")
                            {
                                CorruptM64SequenceFile(file, intensity, m64Mode);
                            }
                            else if (ext == ".aiff")
                            {
                                // If this sound is targeted for custom replacement, skip corrupting it so it stays clean and replaced
                                if (replaceSfx && _sfxReplacementRules.Any(r => r.TargetPath == file))
                                {
                                    // Skip
                                }
                                else
                                {
                                    CorruptBinaryFile(file, intensity, mode);
                                }
                            }
                            else if (ext == ".png")
                            {
                                if (replaceTexturesCustom && _textureReplacementRules.Any(r => r.TargetPath == file))
                                {
                                    // Skip standard corruption, keep replaced texture clean
                                }
                                else
                                {
                                    CorruptTexturePixels(file, intensity, textureMode);
                                }
                            }
                            else if (file.EndsWith("hud.c", StringComparison.OrdinalIgnoreCase))
                            {
                                CorruptHudFile(file, intensity, hudMode);
                            }
                            else if (file.Contains("anims") && file.EndsWith(".inc.c", StringComparison.OrdinalIgnoreCase))
                            {
                                CorruptAnimationFile(file, intensity, animMode);
                            }
                            else if (file.Contains("goddard"))
                            {
                                CorruptGoddardFile(file, intensity, goddardMode);
                            }
                            else if (file.EndsWith(".inc.c", StringComparison.OrdinalIgnoreCase) || ext == ".c" || ext == ".h")
                            {
                                if (randomizeMarioColors && file.Contains("mario"))
                                {
                                    CorruptMarioColorsFile(file, marioColorsArea, marioColorsMode);
                                }
                                if (randomizeDl)
                                {
                                    CorruptDisplayListFile(file, intensity, dlMode);
                                }
                                if (randomizeCollision && file.Contains("collision"))
                                {
                                    CorruptCollisionFile(file, intensity, collisionMode);
                                }
                                if (targetModels)
                                {
                                    CorruptTextSourceFile(file, intensity, mode);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error corrupting file {file}: {ex.Message}");
                        }

                        processed++;
                        int progressVal = (int)((processed / (double)totalFiles) * 100);
                        Dispatcher.Invoke(() =>
                        {
                            CorruptionProgressBar.Value = progressVal;
                            StatusTextBlock.Text = $"Corrupted {processed}/{totalFiles} files...";
                        });
                    }
                });

                StatusTextBlock.Text = "Chaos successfully inflicted! Rebuild project to see effects.";
                MessageBox.Show("Chaos successfully inflicted! Build the ROM via WSL to experience the glitched results.\n\nYou can revert all corruptions anytime using the standard Git checkout option.", "Chaos Inflicted", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = "Error running Chaos Engine.";
                MessageBox.Show($"Error running Chaos Engine: {ex.Message}", "Chaos Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ChaosButton.IsEnabled = true;
                CorruptionProgressBar.Visibility = Visibility.Collapsed;
            }
        }

        private void IntensifySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (IntensifyValueText == null) return;

            double val = e.NewValue;
            IntensifyValueText.Text = $"{val:F0}%";

            if (val <= 100)
            {
                IntensifyValueText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(76, 175, 80)); // Green (#4CAF50)
                if (IntensifyWarningText != null) IntensifyWarningText.Visibility = Visibility.Collapsed;
            }
            else if (val <= 250)
            {
                IntensifyValueText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 193, 7)); // Yellow/Amber (#FFC107)
                if (IntensifyWarningText != null) IntensifyWarningText.Visibility = Visibility.Collapsed;
            }
            else if (val <= 400)
            {
                IntensifyValueText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(244, 67, 54)); // Red (#F44336)
                if (IntensifyWarningText != null) IntensifyWarningText.Visibility = Visibility.Collapsed;
            }
            else
            {
                IntensifyValueText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(183, 28, 28)); // Dark Red (#B71C1C)
                if (IntensifyWarningText != null) IntensifyWarningText.Visibility = Visibility.Visible;
            }
        }

        private void ShuffleSoundsCheck_Toggle(object sender, RoutedEventArgs e)
        {
            if (ShuffleSoundsOptionsPanel != null)
            {
                ShuffleSoundsOptionsPanel.Visibility = ShuffleSoundsCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void RandomizeMarioColorsCheck_Toggle(object sender, RoutedEventArgs e)
        {
            if (MarioColorsOptionsPanel != null)
            {
                MarioColorsOptionsPanel.Visibility = RandomizeMarioColorsCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void TargetMusicNotesCheck_Toggle(object sender, RoutedEventArgs e)
        {
            if (MusicNotesOptionsPanel != null)
            {
                MusicNotesOptionsPanel.Visibility = TargetMusicNotesCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void RandomizeTextCheck_Toggle(object sender, RoutedEventArgs e)
        {
            if (TextRandomizeOptionsPanel != null)
            {
                TextRandomizeOptionsPanel.Visibility = RandomizeTextCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void RandomizeTexturesCheck_Toggle(object sender, RoutedEventArgs e)
        {
            if (TextureRandomizeOptionsPanel != null)
            {
                TextureRandomizeOptionsPanel.Visibility = RandomizeTexturesCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void ScrambleTitleScreenCheck_Toggle(object sender, RoutedEventArgs e)
        {
            if (TitleScreenScramblerPanel != null)
            {
                TitleScreenScramblerPanel.Visibility = ScrambleTitleScreenCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void RandomizeCutsceneCameraCheck_Toggle(object sender, RoutedEventArgs e)
        {
            if (CutsceneCameraPanel != null)
            {
                CutsceneCameraPanel.Visibility = RandomizeCutsceneCameraCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void LakituCameraChaosCheck_Toggle(object sender, RoutedEventArgs e)
        {
            if (LakituCameraChaosPanel != null)
            {
                LakituCameraChaosPanel.Visibility = LakituCameraChaosCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void LimboMarioCheck_Toggle(object sender, RoutedEventArgs e)
        {
            if (LimboMarioPanel != null)
            {
                LimboMarioPanel.Visibility = LimboMarioCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void StartLevelChaosCheck_Toggle(object sender, RoutedEventArgs e)
        {
            if (StartLevelChaosPanel != null)
            {
                StartLevelChaosPanel.Visibility = StartLevelChaosCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void TextureTarget_Changed(object sender, RoutedEventArgs e)
        {
            if (SelectTexturesButton != null && TextureRandomizeSelectedRadio != null)
            {
                SelectTexturesButton.Visibility = TextureRandomizeSelectedRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void SelectTexturesButton_Click(object sender, RoutedEventArgs e)
        {
            var window = new TextureSelectionWindow(_projectRoot, _selectedTextures) { Owner = this };
            if (window.ShowDialog() == true)
            {
                _selectedTextures = window.SelectedRelativePaths;
            }
        }

        private void ReplaceTexturesCustomCheck_Toggle(object sender, RoutedEventArgs e)
        {
            if (ReplaceTexturesCustomPanel != null)
            {
                ReplaceTexturesCustomPanel.Visibility = ReplaceTexturesCustomCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void ConfigureTextureReplacer_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new TextureReplacerWindow(_projectRoot, _textureReplacementRules) { Owner = this };
            if (dialog.ShowDialog() == true)
            {
                _textureReplacementRules.Clear();
                _textureReplacementRules.AddRange(dialog.ActiveRules);
                ActiveTextureReplacementsSummaryText.Text = $"Active replacement rules: {_textureReplacementRules.Count}";
            }
        }

        private void LoadProjectLevelsAndM64s()
        {
            try
            {
                // Load levels
                string levelsDir = Path.Combine(_projectRoot, "levels");
                if (!Directory.Exists(levelsDir)) levelsDir = Path.Combine(_projectRoot, "howtomake", "levels");

                if (Directory.Exists(levelsDir))
                {
                    var dirs = Directory.GetDirectories(levelsDir);
                    foreach (var dir in dirs)
                    {
                        string name = Path.GetFileName(dir);
                        if (name.StartsWith(".") || name == "src" || name == "include" || name == "bin") continue;

                        var item = new System.Windows.Controls.ComboBoxItem { Content = name };
                        LevelSelectionComboBox.Items.Add(item);
                    }
                }

                // Load M64 files
                string seqDir = Path.Combine(_projectRoot, "sound", "sequences");
                if (Directory.Exists(seqDir))
                {
                    var m64Files = Directory.GetFiles(seqDir, "*.m64", SearchOption.AllDirectories);
                    foreach (var file in m64Files)
                    {
                        string name = Path.GetFileName(file);
                        if (name.Contains("00_sound_player") || name.Contains("sfx")) continue;

                        var item = new System.Windows.Controls.ListBoxItem { Content = name, Tag = file };
                        M64SelectionListBox.Items.Add(item);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error scanning project files on initialization: {ex.Message}");
            }
        }

        private void M64Radio_Checked(object sender, RoutedEventArgs e)
        {
            if (M64SelectionListBox != null)
            {
                M64SelectionListBox.Visibility = M64SelectRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void ReplaceSfxCheck_Toggle(object sender, RoutedEventArgs e)
        {
            if (ReplaceSfxPanel != null)
            {
                ReplaceSfxPanel.Visibility = ReplaceSfxCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void ConfigureSoundReplacer_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SoundReplacerWindow(_projectRoot, _sfxReplacementRules) { Owner = this };
            if (dialog.ShowDialog() == true)
            {
                _sfxReplacementRules.Clear();
                _sfxReplacementRules.AddRange(dialog.ActiveRules);
                ActiveReplacementsSummaryText.Text = $"Active replacement rules: {_sfxReplacementRules.Count}";
            }
        }

        private void ConfigureM64Modes_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new M64CorruptionWindow(_activeM64Modes) { Owner = this };
            if (dialog.ShowDialog() == true)
            {
                _activeM64Modes.Clear();
                _activeM64Modes.AddRange(dialog.SelectedModeIds);
            }
        }

        private void GlitchAnimationsCheck_Toggle(object sender, RoutedEventArgs e)
        {
            if (AnimationGlitcherPanel != null)
            {
                AnimationGlitcherPanel.Visibility = GlitchAnimationsCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void GlitchHudCheck_Toggle(object sender, RoutedEventArgs e)
        {
            if (HudGlitcherPanel != null)
            {
                HudGlitcherPanel.Visibility = GlitchHudCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void RandomizeDlCheck_Toggle(object sender, RoutedEventArgs e)
        {
            if (DlRandomizerPanel != null)
            {
                DlRandomizerPanel.Visibility = RandomizeDlCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void DlExclusionCheck_Toggle(object sender, RoutedEventArgs e)
        {
            if (DlExclusionPanel != null)
            {
                DlExclusionPanel.Visibility = DlExclusionCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void RandomizeCollisionCheck_Toggle(object sender, RoutedEventArgs e)
        {
            if (CollisionRandomizerPanel != null)
            {
                CollisionRandomizerPanel.Visibility = RandomizeCollisionCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void TargetGoddardCheck_Toggle(object sender, RoutedEventArgs e)
        {
            if (GoddardOptionsPanel != null)
            {
                GoddardOptionsPanel.Visibility = TargetGoddardCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void CorruptTexturePixels(string filePath, double intensity, int textureMode)
        {
            try
            {
                byte[] pixels;
                int width, height, stride;
                double DpiX, DpiY;

                using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    var frame = decoder.Frames[0];
                    width = frame.PixelWidth;
                    height = frame.PixelHeight;
                    DpiX = frame.DpiX;
                    DpiY = frame.DpiY;

                    // Convert to Bgra32 for uniform byte manipulation
                    var formatted = new System.Windows.Media.Imaging.FormatConvertedBitmap(frame, System.Windows.Media.PixelFormats.Bgra32, null, 0);
                    stride = width * 4;
                    pixels = new byte[height * stride];
                    formatted.CopyPixels(pixels, stride, 0);
                }

                double rate = (intensity / 500.0); // scale rate to 0.0 - 1.0 based on new 500% limit

                switch (textureMode)
                {
                    case 1: // Color Inversion (Negative)
                        for (int i = 0; i < pixels.Length; i += 4)
                        {
                            if (_random.NextDouble() < rate)
                            {
                                pixels[i] = (byte)(255 - pixels[i]);       // Blue
                                pixels[i + 1] = (byte)(255 - pixels[i + 1]); // Green
                                pixels[i + 2] = (byte)(255 - pixels[i + 2]); // Red
                            }
                        }
                        break;

                    case 2: // Hue Shift (Color Cycle)
                        for (int i = 0; i < pixels.Length; i += 4)
                        {
                            if (_random.NextDouble() < rate)
                            {
                                byte b = pixels[i];
                                byte g = pixels[i + 1];
                                byte r = pixels[i + 2];
                                pixels[i] = g;
                                pixels[i + 1] = r;
                                pixels[i + 2] = b;
                            }
                        }
                        break;

                    case 3: // Grayscale & Desaturate
                        for (int i = 0; i < pixels.Length; i += 4)
                        {
                            if (_random.NextDouble() < rate)
                            {
                                byte b = pixels[i];
                                byte g = pixels[i + 1];
                                byte r = pixels[i + 2];
                                byte gray = (byte)(0.299 * r + 0.587 * g + 0.114 * b);
                                pixels[i] = gray;
                                pixels[i + 1] = gray;
                                pixels[i + 2] = gray;
                            }
                        }
                        break;

                    case 4: // Noise Injection
                        int noiseRange = (int)(rate * 100);
                        if (noiseRange < 5) noiseRange = 5;
                        for (int i = 0; i < pixels.Length; i += 4)
                        {
                            if (_random.NextDouble() < rate)
                            {
                                int val = _random.Next(-noiseRange, noiseRange + 1);
                                pixels[i] = (byte)Math.Clamp(pixels[i] + val, 0, 255);
                                pixels[i + 1] = (byte)Math.Clamp(pixels[i + 1] + val, 0, 255);
                                pixels[i + 2] = (byte)Math.Clamp(pixels[i + 2] + val, 0, 255);
                            }
                        }
                        break;

                    case 5: // Pixelate / Mosaic
                        int blockSize = (int)(rate * 16);
                        if (blockSize < 2) blockSize = 2;
                        for (int y = 0; y < height; y += blockSize)
                        {
                            for (int x = 0; x < width; x += blockSize)
                            {
                                int cx = Math.Min(x + blockSize / 2, width - 1);
                                int cy = Math.Min(y + blockSize / 2, height - 1);
                                int centerIdx = (cy * width + cx) * 4;
                                byte b = pixels[centerIdx];
                                byte g = pixels[centerIdx + 1];
                                byte r = pixels[centerIdx + 2];
                                byte a = pixels[centerIdx + 3];

                                for (int by = 0; by < blockSize && y + by < height; by++)
                                {
                                    for (int bx = 0; bx < blockSize && x + bx < width; bx++)
                                    {
                                        int idx = ((y + by) * width + (x + bx)) * 4;
                                        pixels[idx] = b;
                                        pixels[idx + 1] = g;
                                        pixels[idx + 2] = r;
                                        pixels[idx + 3] = a;
                                    }
                                }
                            }
                        }
                        break;


                }

                // Write back as PNG
                var bitmap = System.Windows.Media.Imaging.BitmapSource.Create(
                    width, height, DpiX, DpiY,
                    System.Windows.Media.PixelFormats.Bgra32, null, pixels, stride);

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));

                using (var outStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    encoder.Save(outStream);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error corrupting texture {filePath}: {ex.Message}");
            }
        }

        private byte[] GetTextureXPixels(string customPath, int targetWidth, int targetHeight)
        {
            byte[] targetPixels = new byte[targetWidth * targetHeight * 4];
            System.Windows.Media.Imaging.BitmapSource src = null;

            if (!string.IsNullOrEmpty(customPath) && File.Exists(customPath))
            {
                try
                {
                    using (var stream = new FileStream(customPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                        src = decoder.Frames[0];
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error reading custom texture file: {ex.Message}");
                }
            }

            if (src == null)
            {
                // Generate default "X" image of size targetWidth x targetHeight
                for (int y = 0; y < targetHeight; y++)
                {
                    for (int x = 0; x < targetWidth; x++)
                    {
                        int idx = (y * targetWidth + x) * 4;
                        double normX = (double)x / targetWidth;
                        double normY = (double)y / targetHeight;
                        bool onDiagonal1 = Math.Abs(normX - normY) < 0.05;
                        bool onDiagonal2 = Math.Abs(normX - (1.0 - normY)) < 0.05;
                        if (onDiagonal1 || onDiagonal2)
                        {
                            targetPixels[idx] = 0;
                            targetPixels[idx + 1] = 0;
                            targetPixels[idx + 2] = 255;
                            targetPixels[idx + 3] = 255;
                        }
                        else
                        {
                            targetPixels[idx] = 0;
                            targetPixels[idx + 1] = 0;
                            targetPixels[idx + 2] = 0;
                            targetPixels[idx + 3] = 255;
                        }
                    }
                }
                return targetPixels;
            }

            double scaleX = (double)targetWidth / src.PixelWidth;
            double scaleY = (double)targetHeight / src.PixelHeight;

            var scaleTransform = new System.Windows.Media.ScaleTransform(scaleX, scaleY);
            var resized = new System.Windows.Media.Imaging.TransformedBitmap(src, scaleTransform);
            var formatted = new System.Windows.Media.Imaging.FormatConvertedBitmap(resized, System.Windows.Media.PixelFormats.Bgra32, null, 0);
            
            formatted.CopyPixels(targetPixels, targetWidth * 4, 0);
            return targetPixels;
        }

        private void ModifyAiffFile(string filePath, int mode, bool applyPitchVariation)
        {
            try
            {
                byte[] data = File.ReadAllBytes(filePath);
                if (data.Length < 12) return;

                if (data[0] != 0x46 || data[1] != 0x4F || data[2] != 0x52 || data[3] != 0x4D) return; // 'FORM'
                if (data[8] != 0x41 || data[9] != 0x49 || data[10] != 0x46 || data[11] != 0x46) return; // 'AIFF'

                int pos = 12;
                int commPos = -1;
                int ssndPos = -1;
                int ssndSize = -1;

                while (pos + 8 <= data.Length)
                {
                    string chunkName = System.Text.Encoding.ASCII.GetString(data, pos, 4);
                    int chunkSize = (data[pos + 4] << 24) | (data[pos + 5] << 16) | (data[pos + 6] << 8) | data[pos + 7];
                    
                    if (chunkName == "COMM")
                    {
                        commPos = pos;
                    }
                    else if (chunkName == "SSND")
                    {
                        ssndPos = pos;
                        ssndSize = chunkSize;
                    }
                    
                    pos += 8 + ((chunkSize + 1) & ~1);
                }

                if (commPos == -1) return;

                int numChannels = (data[commPos + 8] << 8) | data[commPos + 9];
                int numSampleFrames = (data[commPos + 10] << 24) | (data[commPos + 11] << 16) | (data[commPos + 12] << 8) | data[commPos + 13];
                int sampleSize = (data[commPos + 14] << 8) | data[commPos + 15];
                
                int exp = ((data[commPos + 16] & 0x7F) << 8) | data[commPos + 17];
                uint hiMant = ((uint)data[commPos + 18] << 24) | ((uint)data[commPos + 19] << 16) | ((uint)data[commPos + 20] << 8) | data[commPos + 21];
                double originalRate = hiMant * Math.Pow(2, exp - 16383 - 31);
                if (double.IsNaN(originalRate) || double.IsInfinity(originalRate) || originalRate <= 0)
                {
                    originalRate = 32000;
                }

                double rateMultiplier = 1.0;
                
                if (mode == 1)
                {
                    rateMultiplier = 1.3 + _random.NextDouble() * 0.9;
                }
                else if (mode == 2)
                {
                    rateMultiplier = 0.45 + _random.NextDouble() * 0.3;
                }
                else if (mode == 3)
                {
                    rateMultiplier = 0.35 + _random.NextDouble() * 2.15;
                }
                
                if (applyPitchVariation)
                {
                    rateMultiplier *= (0.7 + _random.NextDouble() * 0.7);
                }

                if (rateMultiplier != 1.0)
                {
                    double newRate = originalRate * rateMultiplier;
                    byte[] rateBytes = Services.AiffWavTranscoder.EncodeDoubleTo80Bit(newRate);
                    Array.Copy(rateBytes, 0, data, commPos + 16, 10);
                }

                if (mode == 5)
                {
                    double lenMultiplier = 0.1 + _random.NextDouble() * 0.4;
                    int newSampleFrames = (int)(numSampleFrames * lenMultiplier);
                    if (newSampleFrames < 1) newSampleFrames = 1;
                    
                    data[commPos + 10] = (byte)(newSampleFrames >> 24);
                    data[commPos + 11] = (byte)(newSampleFrames >> 16);
                    data[commPos + 12] = (byte)(newSampleFrames >> 8);
                    data[commPos + 13] = (byte)newSampleFrames;
                }

                if (mode == 4 && ssndPos != -1 && ssndSize > 8)
                {
                    int dataOffset = ssndPos + 16;
                    int dataSize = ssndSize - 8;
                    if (dataOffset + dataSize <= data.Length)
                    {
                        if (sampleSize == 16)
                        {
                            int sampleCount = dataSize / 2;
                            for (int i = 0; i < sampleCount / 2; i++)
                            {
                                int idx1 = dataOffset + i * 2;
                                int idx2 = dataOffset + (sampleCount - 1 - i) * 2;
                                
                                byte b0 = data[idx1];
                                byte b1 = data[idx1 + 1];
                                
                                data[idx1] = data[idx2];
                                data[idx1 + 1] = data[idx2 + 1];
                                
                                data[idx2] = b0;
                                data[idx2 + 1] = b1;
                            }
                        }
                        else
                        {
                            for (int i = 0; i < dataSize / 2; i++)
                            {
                                int idx1 = dataOffset + i;
                                int idx2 = dataOffset + (dataSize - 1 - i);
                                
                                byte temp = data[idx1];
                                data[idx1] = data[idx2];
                                data[idx2] = temp;
                            }
                        }
                    }
                }

                if ((mode == 6 || mode == 7) && ssndPos != -1 && ssndSize > 8)
                {
                    int dataOffset = ssndPos + 16;
                    int dataSize = ssndSize - 8;
                    if (dataOffset + dataSize <= data.Length)
                    {
                        if (mode == 6) // Amplify Volume (Make Louder)
                        {
                            if (sampleSize == 16)
                            {
                                int sampleCount = dataSize / 2;
                                for (int i = 0; i < sampleCount; i++)
                                {
                                    int idx = dataOffset + i * 2;
                                    if (idx + 1 < data.Length)
                                    {
                                        int sample = (data[idx] << 8) | data[idx + 1];
                                        if (sample >= 0x8000) sample -= 0x10000; // Sign-extend to 32-bit signed int
                                        
                                        int amplified = (int)(sample * 2.0); // Boost by 2.0x
                                        int clamped = Math.Clamp(amplified, -32768, 32767);
                                        
                                        ushort unsigned = (ushort)(clamped < 0 ? clamped + 0x10000 : clamped);
                                        data[idx] = (byte)(unsigned >> 8);
                                        data[idx + 1] = (byte)(unsigned & 0xFF);
                                    }
                                }
                            }
                            else
                            {
                                for (int i = 0; i < dataSize; i++)
                                {
                                    int idx = dataOffset + i;
                                    if (idx < data.Length)
                                    {
                                        int sample = data[idx];
                                        if (sample >= 0x80) sample -= 256; // Sign-extend 8-bit to 32-bit signed int
                                        
                                        int amplified = (int)(sample * 2.0); // Boost by 2.0x
                                        int clamped = Math.Clamp(amplified, -128, 127);
                                        
                                        data[idx] = (byte)(clamped < 0 ? clamped + 256 : clamped);
                                    }
                                }
                            }
                        }
                        else if (mode == 7) // Mute (Silence)
                        {
                            for (int i = 0; i < dataSize; i++)
                            {
                                int idx = dataOffset + i;
                                if (idx < data.Length)
                                {
                                    data[idx] = 0;
                                }
                            }
                        }
                    }
                }

                File.WriteAllBytes(filePath, data);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error modifying AIFF file {filePath}: {ex.Message}");
            }
        }

        private void CorruptM64SequenceFile(string filePath, double intensity, int ignoredMode)
        {
            byte[] data;
            try
            {
                data = File.ReadAllBytes(filePath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading M64: {ex.Message}");
                return;
            }

            var tempoOffsets = new List<int>();
            var instrumentOffsets = new List<int>();
            var volumeOffsets = new List<int>();
            var pitchOffsets = new List<int>();
            var velocityOffsets = new List<int>();
            var panOffsets = new List<int>();
            var reverbOffsets = new List<int>();
            var pitchBendOffsets = new List<int>();

            var visited = new HashSet<int>();
            var channelOffsets = new HashSet<int>();
            var layerLargeNotes = new Dictionary<int, bool>();

            int seqStartOffset = 0;
            if (data.Length >= 6 && data[0] == 0x00 && data[1] == 0x02)
            {
                seqStartOffset = (data[4] << 8) | data[5];
            }

            try
            {
                // Parse sequence headers starting at seqStartOffset
                ParseSeqCommandsForOffsets(data, seqStartOffset, visited, channelOffsets, tempoOffsets, volumeOffsets);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing seq headers of {filePath}: {ex.Message}");
            }

            try
            {
                // Parse channels found
                visited.Clear();
                foreach (int chanPos in channelOffsets)
                {
                    ParseChanCommandsForOffsets(data, chanPos, visited, layerLargeNotes, instrumentOffsets, volumeOffsets, panOffsets, reverbOffsets, pitchBendOffsets, false);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing channels of {filePath}: {ex.Message}");
            }

            try
            {
                // Parse layers found
                visited.Clear();
                foreach (var kvp in layerLargeNotes)
                {
                    int layerPos = kvp.Key;
                    bool largeNotes = kvp.Value;
                    ParseLayerCommandsForOffsets(data, layerPos, visited, pitchOffsets, velocityOffsets, instrumentOffsets, largeNotes);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing layers of {filePath}: {ex.Message}");
            }

            // Shuffle the active modes list so that no single mode always runs first and hogs all the offsets
            var shuffledModes = new List<int>(_activeM64Modes);
            int n = shuffledModes.Count;
            while (n > 1)
            {
                n--;
                int k = _random.Next(n + 1);
                int temp = shuffledModes[k];
                shuffledModes[k] = shuffledModes[n];
                shuffledModes[n] = temp;
            }

            foreach (int m64Mode in shuffledModes)
            {
                // Reset category tracking for each mode so that checked corruption modes combine and stack instead of blocking each other
                var modifiedPitches = new HashSet<int>();
                var modifiedVelocities = new HashSet<int>();
                var modifiedDurations = new HashSet<int>();
                var modifiedTempos = new HashSet<int>();
                var modifiedInstruments = new HashSet<int>();
                var modifiedPans = new HashSet<int>();
                var modifiedReverbs = new HashSet<int>();
                var modifiedPitchBends = new HashSet<int>();
                if (m64Mode == 0) // Transpose (Pitch Shift)
                {
                    if (pitchOffsets.Count > 0)
                    {
                        int pitchOffset = _random.Next(-6, 7);
                        foreach (int offset in pitchOffsets)
                        {
                            if (offset >= 0 && offset < data.Length && !modifiedPitches.Contains(offset) && _random.NextDouble() < (intensity / 100.0))
                            {
                                byte cmd = data[offset];
                                int pitch = cmd & 0x3f;
                                int newPitch = Math.Clamp(pitch + pitchOffset, 0, 127);
                                data[offset] = (byte)((cmd & 0xc0) | (newPitch & 0x3f));
                                modifiedPitches.Add(offset);
                            }
                        }
                    }
                }
                else if (m64Mode == 1) // Tempo Shift (BPM Warp)
                {
                    foreach (int offset in tempoOffsets)
                    {
                        if (offset >= 0 && offset < data.Length && !modifiedTempos.Contains(offset) && _random.NextDouble() < (intensity / 100.0))
                        {
                            int tempoOffset = _random.Next(-30, 31);
                            int tempo = data[offset];
                            data[offset] = (byte)Math.Clamp(tempo + tempoOffset, 48, 240);
                            modifiedTempos.Add(offset);
                        }
                    }
                }
                else if (m64Mode == 2) // Instrument Swap (Orchestration)
                {
                    foreach (int offset in instrumentOffsets)
                    {
                        if (offset >= 0 && offset < data.Length && !modifiedInstruments.Contains(offset) && _random.NextDouble() < (intensity / 100.0))
                        {
                            data[offset] = (byte)_random.Next(0, 32);
                            modifiedInstruments.Add(offset);
                        }
                    }
                }
                else if (m64Mode == 3) // Duration Glitcher
                {
                    foreach (int offset in pitchOffsets)
                    {
                        if (offset >= 0 && offset + 2 < data.Length)
                        {
                            byte cmd = data[offset];
                            if ((cmd & 0xc0) == 0x80) // note2
                            {
                                int durOffset = offset + 2;
                                if (!modifiedDurations.Contains(durOffset) && _random.NextDouble() < (intensity / 100.0))
                                {
                                    data[durOffset] = (byte)_random.Next(10, 255);
                                    modifiedDurations.Add(durOffset);
                                }
                            }
                        }
                    }
                }
                else if (m64Mode == 4) // Velocity Randomizer
                {
                    foreach (int offset in volumeOffsets)
                    {
                        if (offset >= 0 && offset < data.Length && !modifiedVelocities.Contains(offset) && _random.NextDouble() < (intensity / 100.0))
                        {
                            data[offset] = (byte)_random.Next(10, 128);
                            modifiedVelocities.Add(offset);
                        }
                    }
                    foreach (int offset in velocityOffsets)
                    {
                        if (offset >= 0 && offset < data.Length && !modifiedVelocities.Contains(offset) && _random.NextDouble() < (intensity / 100.0))
                        {
                            data[offset] = (byte)_random.Next(10, 128);
                            modifiedVelocities.Add(offset);
                        }
                    }
                }
                else if (m64Mode == 5) // Melody Scrambler (Shuffle)
                {
                    if (pitchOffsets.Count > 0)
                    {
                        int shuffleCount = (int)(pitchOffsets.Count * (intensity / 100.0) * 0.1);
                        if (shuffleCount <= 0) shuffleCount = 1;
                        for (int s = 0; s < shuffleCount; s++)
                        {
                            int idx1 = pitchOffsets[_random.Next(0, pitchOffsets.Count)];
                            int idx2 = pitchOffsets[_random.Next(0, pitchOffsets.Count)];
                            if (idx1 >= 0 && idx1 < data.Length && idx2 >= 0 && idx2 < data.Length)
                            {
                                if (!modifiedPitches.Contains(idx1) && !modifiedPitches.Contains(idx2))
                                {
                                    byte cmd1 = data[idx1];
                                    byte cmd2 = data[idx2];
                                    byte newCmd1 = (byte)((cmd1 & 0xc0) | (cmd2 & 0x3f));
                                    byte newCmd2 = (byte)((cmd2 & 0xc0) | (cmd1 & 0x3f));
                                    data[idx1] = newCmd1;
                                    data[idx2] = newCmd2;
                                    modifiedPitches.Add(idx1);
                                    modifiedPitches.Add(idx2);
                                }
                            }
                        }
                    }
                }
                else if (m64Mode == 6) // Octave Jumper (Shift +12 or -12 semitones)
                {
                    foreach (int offset in pitchOffsets)
                    {
                        if (offset >= 0 && offset < data.Length && !modifiedPitches.Contains(offset) && _random.NextDouble() < (intensity / 100.0))
                        {
                            byte cmd = data[offset];
                            int pitch = cmd & 0x3f;
                            int shift = _random.Next(0, 2) == 0 ? 12 : -12;
                            int newPitch = Math.Clamp(pitch + shift, 0, 127);
                            data[offset] = (byte)((cmd & 0xc0) | (newPitch & 0x3f));
                            modifiedPitches.Add(offset);
                        }
                    }
                }
                else if (m64Mode == 7) // Channel Muter
                {
                    foreach (int offset in volumeOffsets)
                    {
                        if (offset >= 0 && offset < data.Length && !modifiedVelocities.Contains(offset) && _random.NextDouble() < (intensity / 100.0) * 0.4)
                        {
                            data[offset] = 0; // Mute channel
                            modifiedVelocities.Add(offset);
                        }
                    }
                }
                else if (m64Mode == 8) // Pan Scrambler (0, 64, 127)
                {
                    foreach (int offset in panOffsets)
                    {
                        if (offset >= 0 && offset < data.Length && !modifiedPans.Contains(offset) && _random.NextDouble() < (intensity / 100.0))
                        {
                            int r = _random.Next(0, 3);
                            data[offset] = (byte)(r == 0 ? 0 : (r == 1 ? 64 : 127));
                            modifiedPans.Add(offset);
                        }
                    }
                }
                else if (m64Mode == 9) // Reverb Maxer
                {
                    foreach (int offset in reverbOffsets)
                    {
                        if (offset >= 0 && offset < data.Length && !modifiedReverbs.Contains(offset) && _random.NextDouble() < (intensity / 100.0))
                        {
                            data[offset] = 127; // Max reverb
                            modifiedReverbs.Add(offset);
                        }
                    }
                }
                else if (m64Mode == 10) // Pitch Bend Chaos
                {
                    foreach (int offset in pitchBendOffsets)
                    {
                        if (offset >= 0 && offset < data.Length && !modifiedPitchBends.Contains(offset) && _random.NextDouble() < (intensity / 100.0))
                        {
                            data[offset] = (byte)_random.Next(0, 255); // Scramble pitch bend value (using 0 to 254 for index safety)
                            modifiedPitchBends.Add(offset);
                        }
                    }
                }
                else if (m64Mode == 11) // Note Lengthener (Sustain)
                {
                    foreach (int offset in pitchOffsets)
                    {
                        if (offset >= 0 && offset + 2 < data.Length)
                        {
                            byte cmd = data[offset];
                            if ((cmd & 0xc0) == 0x80) // note2 has duration parameter at offset+2
                            {
                                int durOffset = offset + 2;
                                if (!modifiedDurations.Contains(durOffset) && _random.NextDouble() < (intensity / 100.0))
                                {
                                    int val = data[durOffset];
                                    data[durOffset] = (byte)Math.Clamp(val * 4, 10, 255);
                                    modifiedDurations.Add(durOffset);
                                }
                            }
                        }
                    }
                }
                else if (m64Mode == 12) // Note Shortener (Staccato)
                {
                    foreach (int offset in pitchOffsets)
                    {
                        if (offset >= 0 && offset + 2 < data.Length)
                        {
                            byte cmd = data[offset];
                            if ((cmd & 0xc0) == 0x80)
                            {
                                int durOffset = offset + 2;
                                if (!modifiedDurations.Contains(durOffset) && _random.NextDouble() < (intensity / 100.0))
                                {
                                    data[durOffset] = (byte)_random.Next(1, 8); // extremely short duration
                                    modifiedDurations.Add(durOffset);
                                }
                            }
                        }
                    }
                }
                else if (m64Mode == 13) // Major Scale Lock
                {
                    int[] majorScale = { 0, 2, 4, 5, 7, 9, 11 };
                    foreach (int offset in pitchOffsets)
                    {
                        if (offset >= 0 && offset < data.Length && !modifiedPitches.Contains(offset) && _random.NextDouble() < (intensity / 100.0))
                        {
                            byte cmd = data[offset];
                            int pitch = cmd & 0x3f;
                            int noteNum = pitch % 12;
                            int octave = pitch / 12;
                            int closest = majorScale.OrderBy(n => Math.Abs(n - noteNum)).First();
                            int newPitch = Math.Clamp(octave * 12 + closest, 0, 127);
                            data[offset] = (byte)((cmd & 0xc0) | (newPitch & 0x3f));
                            modifiedPitches.Add(offset);
                        }
                    }
                }
                else if (m64Mode == 14) // Minor Scale Lock
                {
                    int[] minorScale = { 0, 2, 3, 5, 7, 8, 10 };
                    foreach (int offset in pitchOffsets)
                    {
                        if (offset >= 0 && offset < data.Length && !modifiedPitches.Contains(offset) && _random.NextDouble() < (intensity / 100.0))
                        {
                            byte cmd = data[offset];
                            int pitch = cmd & 0x3f;
                            int noteNum = pitch % 12;
                            int octave = pitch / 12;
                            int closest = minorScale.OrderBy(n => Math.Abs(n - noteNum)).First();
                            int newPitch = Math.Clamp(octave * 12 + closest, 0, 127);
                            data[offset] = (byte)((cmd & 0xc0) | (newPitch & 0x3f));
                            modifiedPitches.Add(offset);
                        }
                    }
                }
                else if (m64Mode == 15) // Atonal Glitcher
                {
                    foreach (int offset in pitchOffsets)
                    {
                        if (offset >= 0 && offset < data.Length && !modifiedPitches.Contains(offset) && _random.NextDouble() < (intensity / 100.0))
                        {
                            byte cmd = data[offset];
                            int newPitch = _random.Next(0, 128);
                            data[offset] = (byte)((cmd & 0xc0) | (newPitch & 0x3f));
                            modifiedPitches.Add(offset);
                        }
                    }
                }
                else if (m64Mode == 16) // Velocity Inverter
                {
                    foreach (int offset in velocityOffsets)
                    {
                        if (offset >= 0 && offset < data.Length && !modifiedVelocities.Contains(offset) && _random.NextDouble() < (intensity / 100.0))
                        {
                            int val = data[offset];
                            data[offset] = (byte)Math.Clamp(127 - val, 10, 127);
                            modifiedVelocities.Add(offset);
                        }
                    }
                }
                else if (m64Mode == 17) // Dynamic Tremolo
                {
                    foreach (int offset in volumeOffsets)
                    {
                        if (offset >= 0 && offset < data.Length && !modifiedVelocities.Contains(offset) && _random.NextDouble() < (intensity / 100.0))
                        {
                            int val = data[offset];
                            int mod = _random.Next(0, 2) == 0 ? 30 : -30;
                            data[offset] = (byte)Math.Clamp(val + mod, 10, 127);
                            modifiedVelocities.Add(offset);
                        }
                    }
                }
                else if (m64Mode == 18) // Transpose Per Channel
                {
                    int[] channelTransposes = new int[16];
                    for (int c = 0; c < 16; c++)
                    {
                        channelTransposes[c] = _random.Next(-12, 13);
                    }
                    
                    for (int i = 0; i < pitchOffsets.Count; i++)
                    {
                        int offset = pitchOffsets[i];
                        if (offset >= 0 && offset < data.Length && !modifiedPitches.Contains(offset) && _random.NextDouble() < (intensity / 100.0))
                        {
                            byte cmd = data[offset];
                            int pitch = cmd & 0x3f;
                            int ch = i % 16;
                            int newPitch = Math.Clamp(pitch + channelTransposes[ch], 0, 127);
                            data[offset] = (byte)((cmd & 0xc0) | (newPitch & 0x3f));
                            modifiedPitches.Add(offset);
                        }
                    }
                }
                else if (m64Mode == 19) // Harmonizer (Octave Duplicator)
                {
                    for (int i = 0; i < pitchOffsets.Count - 1; i += 2)
                    {
                        int offset2 = pitchOffsets[i + 1];
                        if (offset2 >= 0 && offset2 < data.Length && !modifiedPitches.Contains(offset2) && _random.NextDouble() < (intensity / 100.0) * 0.5)
                        {
                            int offset1 = pitchOffsets[i];
                            if (offset1 >= 0 && offset1 < data.Length)
                            {
                                byte cmd1 = data[offset1];
                                int pitch1 = cmd1 & 0x3f;
                                int newPitch = Math.Clamp(pitch1 + 12, 0, 127);
                                data[offset2] = (byte)((data[offset2] & 0xc0) | (newPitch & 0x3f));
                                modifiedPitches.Add(offset2);
                            }
                        }
                    }
                }
                else if (m64Mode == 20) // Drum Roll Glitcher
                {
                    foreach (int offset in velocityOffsets)
                    {
                        if (offset >= 0 && offset < data.Length && !modifiedVelocities.Contains(offset) && _random.NextDouble() < (intensity / 100.0) * 0.2)
                        {
                            data[offset] = 127;
                            modifiedVelocities.Add(offset);
                        }
                    }
                }
                else if (m64Mode == 21) // Tempo Drifter
                {
                    int currentTempo = 120;
                    foreach (int offset in tempoOffsets)
                    {
                        if (offset >= 0 && offset < data.Length && !modifiedTempos.Contains(offset) && _random.NextDouble() < (intensity / 100.0))
                        {
                            currentTempo = data[offset];
                            int mod = _random.Next(-15, 16);
                            data[offset] = (byte)Math.Clamp(currentTempo + mod, 40, 220);
                            modifiedTempos.Add(offset);
                        }
                    }
                }
                else if (m64Mode == 22) // Max all sequence volumes
                {
                    foreach (int offset in volumeOffsets)
                    {
                        if (offset >= 0 && offset < data.Length && !modifiedVelocities.Contains(offset) && _random.NextDouble() < (intensity / 100.0))
                        {
                            data[offset] = 127;
                            modifiedVelocities.Add(offset);
                        }
                    }
                }
                else if (m64Mode == 23) // Note Eraser and Lengthener
                {
                    for (int i = 0; i < pitchOffsets.Count; i++)
                    {
                        int offset = pitchOffsets[i];
                        int velOffset = velocityOffsets[i];
                        if (offset >= 0 && offset < data.Length && velOffset >= 0 && velOffset < data.Length)
                        {
                            byte cmd = data[offset];
                            int durOffset = -1;
                            bool isType2 = (cmd & 0xc0) == 0x80;
                            if (isType2)
                            {
                                durOffset = velOffset + 1;
                            }
                            else if ((cmd & 0xc0) == 0x00 || (cmd & 0xc0) == 0x40)
                            {
                                durOffset = velOffset - 1;
                            }

                            // 50% chance to erase (mute)
                            if (_random.NextDouble() < 0.50)
                            {
                                if (!modifiedVelocities.Contains(velOffset))
                                {
                                    data[velOffset] = 0;
                                    modifiedVelocities.Add(velOffset);
                                }
                            }
                            // Otherwise, make some notes longer based on intensity
                            else if (_random.NextDouble() < (intensity / 100.0))
                            {
                                if (durOffset >= 0 && durOffset < data.Length && !modifiedDurations.Contains(durOffset))
                                {
                                    int val = data[durOffset];
                                    int clampMax = isType2 ? 255 : 127;
                                    data[durOffset] = (byte)Math.Clamp(val * 4, 10, clampMax);
                                    modifiedDurations.Add(durOffset);
                                }
                            }
                        }
                    }
                }
                else if (m64Mode == 24) // Melody Reverser
                {
                    if (pitchOffsets.Count > 0)
                    {
                        List<int> targetOffsets = new List<int>();
                        foreach (int offset in pitchOffsets)
                        {
                            if (offset >= 0 && offset < data.Length && !modifiedPitches.Contains(offset) && _random.NextDouble() < (intensity / 100.0))
                            {
                                targetOffsets.Add(offset);
                            }
                        }
                        
                        List<byte> pitches = new List<byte>();
                        foreach (int offset in targetOffsets)
                        {
                            pitches.Add((byte)(data[offset] & 0x3f));
                        }
                        pitches.Reverse();
                        
                        for (int i = 0; i < targetOffsets.Count; i++)
                        {
                            int offset = targetOffsets[i];
                            byte cmd = data[offset];
                            byte newPitch = pitches[i];
                            data[offset] = (byte)((cmd & 0xc0) | (newPitch & 0x3f));
                            modifiedPitches.Add(offset);
                        }
                    }
                }
                else if (m64Mode == 25) // Double Note (+3 Pitch)
                {
                    for (int i = 0; i < pitchOffsets.Count - 1; i += 2)
                    {
                        int offset2 = pitchOffsets[i + 1];
                        if (offset2 >= 0 && offset2 < data.Length && !modifiedPitches.Contains(offset2) && _random.NextDouble() < (intensity / 100.0) * 0.5)
                        {
                            int offset1 = pitchOffsets[i];
                            if (offset1 >= 0 && offset1 < data.Length)
                            {
                                byte cmd1 = data[offset1];
                                int pitch1 = cmd1 & 0x3f;
                                int newPitch = Math.Clamp(pitch1 + 3, 0, 127);
                                data[offset2] = (byte)((data[offset2] & 0xc0) | (newPitch & 0x3f));
                                modifiedPitches.Add(offset2);
                            }
                        }
                    }
                }
                else if (m64Mode == 26) // Pitch Flatten (Monotone)
                {
                    // For each channel, find its pitch offsets and flatten them to a single random pitch
                    foreach (int chanPos in channelOffsets)
                    {
                        var chanVisited = new HashSet<int>();
                        var chanLayerLargeNotes = new Dictionary<int, bool>();
                        var chanInstrumentOffsets = new List<int>();
                        var chanVolumeOffsets = new List<int>();
                        var chanPanOffsets = new List<int>();
                        var chanReverbOffsets = new List<int>();
                        var chanPitchBendOffsets = new List<int>();

                        ParseChanCommandsForOffsets(data, chanPos, chanVisited, chanLayerLargeNotes, chanInstrumentOffsets, chanVolumeOffsets, chanPanOffsets, chanReverbOffsets, chanPitchBendOffsets, false);

                        var chanPitchOffsets = new List<int>();
                        var chanVelocityOffsets = new List<int>();
                        var chanVisitedLayers = new HashSet<int>();
                        foreach (var kvp in chanLayerLargeNotes)
                        {
                            ParseLayerCommandsForOffsets(data, kvp.Key, chanVisitedLayers, chanPitchOffsets, chanVelocityOffsets, chanInstrumentOffsets, kvp.Value);
                        }

                        if (chanPitchOffsets.Count > 0)
                        {
                            // Choose a single target pitch for this entire channel (C2 to C5 / MIDI 36 to 72, or based on the first note pitch)
                            int targetPitch = _random.Next(36, 73);
                            int firstOffset = chanPitchOffsets[0];
                            if (firstOffset >= 0 && firstOffset < data.Length)
                            {
                                targetPitch = data[firstOffset] & 0x3f;
                            }

                            foreach (int offset in chanPitchOffsets)
                            {
                                if (offset >= 0 && offset < data.Length && !modifiedPitches.Contains(offset) && _random.NextDouble() < (intensity / 100.0))
                                {
                                    byte cmd = data[offset];
                                    data[offset] = (byte)((cmd & 0xc0) | (targetPitch & 0x3f));
                                    modifiedPitches.Add(offset);
                                }
                            }
                        }
                    }
                }
            }

            try
            {
                File.WriteAllBytes(filePath, data);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error writing mutated M64: {ex.Message}");
            }
        }

        private int ReadVarIntForOffsets(byte[] data, ref int pos)
        {
            int val = 0;
            byte b;
            do
            {
                if (pos < 0 || pos >= data.Length) break;
                b = data[pos++];
                val = (val << 7) | (b & 0x7f);
            } while ((b & 0x80) != 0);
            return val;
        }

        private int GetSeqCmdSizeForOffsets(byte cmd, byte[] data, ref int pos)
        {
            // 0 parameter bytes
            if (cmd == 0xff || cmd == 0xfe || cmd == 0xf7 || cmd == 0xf1 || 
                cmd == 0xd4 || cmd == 0xc6)
            {
                return 0;
            }

            // Variable length integer (m64_read_compressed_u16)
            if (cmd == 0xfd)
            {
                ReadVarIntForOffsets(data, ref pos);
                return 0;
            }

            // 1 parameter byte
            if (cmd == 0xf8 || cmd == 0xf2 || cmd == 0xdc || cmd == 0xda || 
                cmd == 0xd5 || cmd == 0xdf || cmd == 0xde || cmd == 0xdd || 
                cmd == 0xdb || cmd == 0xd3 || cmd == 0xd0 || cmd == 0xcc || 
                cmd == 0xc9 || cmd == 0xc8 || cmd == 0xd9)
            {
                return 1;
            }

            // 2 parameter bytes
            if (cmd == 0xfc || cmd == 0xfb || cmd == 0xfa || cmd == 0xf9 || 
                cmd == 0xf5 || cmd == 0xd7 || cmd == 0xd6 || cmd == 0xd2 || 
                cmd == 0xd1)
            {
                return 2;
            }

            // 3 parameter bytes
            if (cmd == 0xc7)
            {
                return 3;
            }

            // Default for sub-commands or channel starts
            if ((cmd & 0xF0) == 0x90) return 2;
            if ((cmd & 0xF0) == 0x00) return 0;
            if ((cmd & 0xF0) == 0x50) return 0;
            if ((cmd & 0xF0) == 0x70) return 0;
            if ((cmd & 0xF0) == 0x80) return 0;

            return 0;
        }

        private int GetChanCmdSizeForOffsets(byte cmd, byte[] data, ref int pos)
        {
            // 0 parameter bytes
            if (cmd == 0xff || cmd == 0xfe || cmd == 0xf7 || cmd == 0xf6 || 
                cmd == 0xf1 || cmd == 0xc5 || cmd == 0xc3 || cmd == 0xc4 || 
                cmd == 0xe4 || cmd == 0xea || cmd == 0xec)
            {
                return 0;
            }

            // Variable length integer (m64_read_compressed_u16)
            if (cmd == 0xfd)
            {
                ReadVarIntForOffsets(data, ref pos);
                return 0;
            }

            // 1 parameter byte
            if (cmd == 0xf8 || cmd == 0xf4 || cmd == 0xf3 || cmd == 0xf2 || 
                cmd == 0xc6 || cmd == 0xc1 || cmd == 0xdf || cmd == 0xe0 || 
                cmd == 0xdd || cmd == 0xdc || cmd == 0xdb || cmd == 0xd9 || 
                cmd == 0xd8 || cmd == 0xd7 || cmd == 0xd6 || cmd == 0xd4 || 
                cmd == 0xd3 || cmd == 0xd2 || cmd == 0xd1 || cmd == 0xe3 || 
                cmd == 0xe5 || cmd == 0xe6 || cmd == 0xeb)
            {
                return 1;
            }

            // 2 parameter bytes
            if (cmd == 0xfc || cmd == 0xfb || cmd == 0xfa || cmd == 0xf9 || 
                cmd == 0xf5 || cmd == 0xc2 || cmd == 0xda || cmd == 0xe7)
            {
                return 2;
            }

            // 3 parameter bytes
            if (cmd == 0xe2 || cmd == 0xe1 || cmd == 0xc7)
            {
                return 3;
            }

            // 8 parameter bytes
            if (cmd == 0xe8)
            {
                return 8;
            }

            // Default for any sub-commands or note events that fall into channel parsing
            if ((cmd & 0xF0) == 0x90) return 2;
            if ((cmd & 0xF0) == 0x10) return 2;
            if ((cmd & 0xF0) == 0x20) return 0;
            if ((cmd & 0xF0) == 0x30) return 1;
            if ((cmd & 0xF0) == 0x40) return 1;
            if ((cmd & 0xF0) == 0x50) return 0;
            if ((cmd & 0xF0) == 0x60) return 0;
            if ((cmd & 0xF0) == 0x70) return 0;
            if ((cmd & 0xF0) == 0x80) return 0;

            return 0;
        }

        private int GetLayerCmdSizeForOffsets(byte cmd, byte[] data, ref int pos)
        {
            // 0 parameter bytes
            if (cmd == 0xff || cmd == 0xf7 || cmd == 0xc4 || cmd == 0xc5 || 
                cmd == 0xc8 || cmd == 0xcc)
            {
                return 0;
            }

            // Rest command (0xc0) takes a variable-length integer delay parameter
            // Set short note default play percentage (0xc3) takes a variable-length integer parameter
            if (cmd == 0xc0 || cmd == 0xc3)
            {
                ReadVarIntForOffsets(data, ref pos);
                return 0;
            }

            // 1 parameter byte
            if (cmd == 0xf8 || cmd == 0xf4 || cmd == 0xc1 || cmd == 0xca || 
                cmd == 0xc2 || cmd == 0xc9 || cmd == 0xc6)
            {
                return 1;
            }

            // 2 parameter bytes
            if (cmd == 0xfc || cmd == 0xfb)
            {
                return 2;
            }

            // 3 parameter bytes (e.g. 0xcb reads s16 + u8 = 3 bytes)
            if (cmd == 0xcb)
            {
                return 3;
            }

            // Portamento command (0xc7) has special dynamic parameter size
            if (cmd == 0xc7)
            {
                if (pos < data.Length)
                {
                    byte mode = data[pos++]; // reads mode (1 byte)
                    pos++; // Skip targetNote (1 byte)
                    if ((mode & 0x80) != 0)
                    {
                        pos++; // Special mode: reads 1 byte for time
                    }
                    else
                    {
                        ReadVarIntForOffsets(data, ref pos); // Non-special mode: reads compressed u16 for time
                    }
                }
                return 0;
            }

            // 0 parameter bytes for note table selectors (0xd0..0xef) and other commands
            return 0;
        }

        private void ParseSeqCommandsForOffsets(byte[] data, int pos, HashSet<int> visited, HashSet<int> channelOffsets, List<int> tempoOffsets, List<int> volumeOffsets)
        {
            if (pos < 0 || pos >= data.Length || visited.Contains(pos)) return;
            visited.Add(pos);

            while (pos < data.Length)
            {
                byte cmd = data[pos++];
                if (cmd == 0xff) break;

                if ((cmd & 0xf0) == 0x90)
                {
                    int chanPos = (data[pos++] << 8) | data[pos++];
                    if (chanPos > 0 && chanPos < data.Length)
                    {
                        channelOffsets.Add(chanPos);
                    }
                }
                else if (cmd == 0xfb)
                {
                    int jumpOffset = (data[pos++] << 8) | data[pos++];
                    if (jumpOffset > 0 && jumpOffset < data.Length)
                    {
                        ParseSeqCommandsForOffsets(data, jumpOffset, visited, channelOffsets, tempoOffsets, volumeOffsets);
                    }
                    break;
                }
                else if (cmd == 0xfd)
                {
                    ReadVarIntForOffsets(data, ref pos);
                }
                else if (cmd == 0xfe)
                {
                }
                else if (cmd == 0xf2)
                {
                    volumeOffsets.Add(pos);
                    pos++;
                }
                else if (cmd == 0xdd)
                {
                    tempoOffsets.Add(pos);
                    pos++;
                }
                else
                {
                    int argSize = GetSeqCmdSizeForOffsets(cmd, data, ref pos);
                    pos += argSize;
                }
            }
        }

        private void ParseChanCommandsForOffsets(byte[] data, int pos, HashSet<int> visited, Dictionary<int, bool> layerLargeNotes, List<int> instrumentOffsets, List<int> volumeOffsets, List<int> panOffsets, List<int> reverbOffsets, List<int> pitchBendOffsets, bool largeNotesState = false)
        {
            if (pos < 0 || pos >= data.Length || visited.Contains(pos)) return;
            visited.Add(pos);

            while (pos < data.Length)
            {
                byte cmd = data[pos++];
                if (cmd == 0xff) break;

                if (cmd == 0xfb)
                {
                    int jumpOffset = (data[pos++] << 8) | data[pos++];
                    if (jumpOffset > 0 && jumpOffset < data.Length)
                    {
                        ParseChanCommandsForOffsets(data, jumpOffset, visited, layerLargeNotes, instrumentOffsets, volumeOffsets, panOffsets, reverbOffsets, pitchBendOffsets, largeNotesState);
                    }
                    break;
                }
                else if (cmd == 0xfd)
                {
                    ReadVarIntForOffsets(data, ref pos);
                }
                else if (cmd == 0xc1)
                {
                    instrumentOffsets.Add(pos);
                    pos++;
                }
                else if (cmd == 0xc3) // chan_largenotesoff
                {
                    largeNotesState = false;
                }
                else if (cmd == 0xc4) // chan_largenoteson
                {
                    largeNotesState = true;
                }
                else if (cmd == 0xdf)
                {
                    volumeOffsets.Add(pos);
                    pos++;
                }
                else if (cmd == 0xdd) // set pan
                {
                    panOffsets.Add(pos);
                    pos++;
                }
                else if (cmd == 0xd4) // set reverb
                {
                    reverbOffsets.Add(pos);
                    pos++;
                }
                else if (cmd == 0xd3) // set pitch bend
                {
                    pitchBendOffsets.Add(pos);
                    pos++;
                }
                else if ((cmd & 0xf0) == 0x90)
                {
                    int layerPos = (data[pos++] << 8) | data[pos++];
                    if (layerPos > 0 && layerPos < data.Length)
                    {
                        layerLargeNotes[layerPos] = largeNotesState;
                    }
                }
                else
                {
                    int argSize = GetChanCmdSizeForOffsets(cmd, data, ref pos);
                    pos += argSize;
                }
            }
        }

        private void ParseLayerCommandsForOffsets(byte[] data, int pos, HashSet<int> visited, List<int> pitchOffsets, List<int> velocityOffsets, List<int> instrumentOffsets, bool largeNotes)
        {
            if (pos < 0 || pos >= data.Length || visited.Contains(pos)) return;
            visited.Add(pos);

            while (pos < data.Length)
            {
                byte cmd = data[pos++];
                if (cmd == 0xff) break;

                if (cmd == 0xfb)
                {
                    int jumpOffset = (data[pos++] << 8) | data[pos++];
                    if (jumpOffset > 0 && jumpOffset < data.Length)
                    {
                        ParseLayerCommandsForOffsets(data, jumpOffset, visited, pitchOffsets, velocityOffsets, instrumentOffsets, largeNotes);
                    }
                    break;
                }
                else if (cmd == 0xfc)
                {
                    int callOffset = (data[pos++] << 8) | data[pos++];
                    if (callOffset > 0 && callOffset < data.Length)
                    {
                        ParseLayerCommandsForOffsets(data, callOffset, visited, pitchOffsets, velocityOffsets, instrumentOffsets, largeNotes);
                    }
                }
                else if (cmd == 0xfd)
                {
                    ReadVarIntForOffsets(data, ref pos);
                }
                else if (cmd == 0xc6)
                {
                    instrumentOffsets.Add(pos);
                    pos++;
                }
                else if (cmd >= 0x00 && cmd <= 0x3f) // Type 0 note (large)
                {
                    pitchOffsets.Add(pos - 1);
                    ReadVarIntForOffsets(data, ref pos);
                    velocityOffsets.Add(pos);
                    pos += 2; // Skip velocity (1 byte) and gate (1 byte)
                }
                else if (cmd >= 0x40 && cmd <= 0x7f) // Type 1 note (medium)
                {
                    pitchOffsets.Add(pos - 1);
                    ReadVarIntForOffsets(data, ref pos);
                    velocityOffsets.Add(pos);
                    pos++; // Skip velocity (1 byte)
                }
                else if (cmd >= 0x80 && cmd <= 0xbf) // Type 2 note (small)
                {
                    pitchOffsets.Add(pos - 1);
                    velocityOffsets.Add(pos);
                    pos += 2; // Skip velocity (1 byte) and duration/gate (1 byte)
                }
                else
                {
                    int argSize = GetLayerCmdSizeForOffsets(cmd, data, ref pos);
                    pos += argSize;
                }
            }
        }

        private void CorruptBinaryFile(string filePath, double intensity, int mode)
        {
            byte[] bytes = File.ReadAllBytes(filePath);
            if (bytes.Length == 0) return;

            // Intensity determines how many bytes are affected (e.g. up to 10% for full slider value)
            double rate = (intensity / 100.0) * 0.05; 
            int changesCount = (int)(bytes.Length * rate);
            if (changesCount <= 0) changesCount = 1;

            int startOffset = 0;
            bool isAiff = filePath.EndsWith(".aiff", StringComparison.OrdinalIgnoreCase);
            bool isM64 = filePath.EndsWith(".m64", StringComparison.OrdinalIgnoreCase);

            if (isAiff)
            {
                startOffset = 512;
            }
            else if (isM64)
            {
                startOffset = 32;
            }
            if (bytes.Length <= startOffset) return;

            // For .m64 files, pre-calculate forbidden offsets to prevent driver crashes
            bool[] forbidden = null;
            if (isM64)
            {
                forbidden = new bool[bytes.Length];
                for (int j = 0; j < Math.Min(bytes.Length, startOffset); j++)
                {
                    forbidden[j] = true;
                }
                for (int j = startOffset; j < bytes.Length; j++)
                {
                    if (bytes[j] >= 0x80)
                    {
                        forbidden[j] = true;
                        if (j + 1 < bytes.Length) forbidden[j + 1] = true;
                        if (j + 2 < bytes.Length) forbidden[j + 2] = true;
                        if (j + 3 < bytes.Length) forbidden[j + 3] = true;
                    }
                }
            }

            for (int i = 0; i < changesCount; i++)
            {
                int pos = _random.Next(startOffset, bytes.Length);

                if (isM64)
                {
                    // Find a non-forbidden offset to corrupt
                    int retries = 100;
                    while (forbidden[pos] && retries-- > 0)
                    {
                        pos = _random.Next(startOffset, bytes.Length);
                    }
                    if (forbidden[pos]) continue;
                }

                switch (mode)
                {
                    case 0: // Byte Swapping
                        if (pos < bytes.Length - 1)
                        {
                            if (isM64 && (forbidden[pos] || forbidden[pos + 1]))
                            {
                                break;
                            }
                            byte temp = bytes[pos];
                            bytes[pos] = bytes[pos + 1];
                            bytes[pos + 1] = temp;
                        }
                        break;
                    case 1: // Random Noise
                        if (isM64)
                            bytes[pos] = (byte)_random.Next(0, 128);
                        else
                            bytes[pos] = (byte)_random.Next(256);
                        break;
                    case 2: // Bitwise Logical (XOR)
                        if (isM64)
                            bytes[pos] = (byte)((bytes[pos] ^ (byte)_random.Next(128)) & 0x7F);
                        else
                            bytes[pos] ^= (byte)_random.Next(256);
                        break;
                    case 3: // Shift Corrupt
                        if (isM64)
                            bytes[pos] = (byte)((bytes[pos] << 1) & 0x7F);
                        else
                            bytes[pos] = (byte)(bytes[pos] << 1 | bytes[pos] >> 7);
                        break;
                    case 4: // Zeroing
                        bytes[pos] = 0;
                        break;
                    case 5: // Arithmetic Addition (Pitch Transpose / Value Offset)
                        if (isM64)
                        {
                            int offset = _random.Next(-12, 13); // Shift by up to an octave
                            int newVal = bytes[pos] + offset;
                            if (newVal < 0) newVal = 0;
                            if (newVal > 127) newVal = 127;
                            bytes[pos] = (byte)newVal;
                        }
                        else
                        {
                            bytes[pos] = (byte)(bytes[pos] + _random.Next(-16, 17));
                        }
                        break;
                }
            }

            File.WriteAllBytes(filePath, bytes);
        }

        private void CorruptTextSourceFile(string filePath, double intensity, int mode)
        {
            string content = File.ReadAllText(filePath);
            
            // Regex to find only bracketed coordinate groups like { x, y, z } or { x, y }
            var coordRegex = new Regex(@"\{[ \t]*(-?[0-9]+)[ \t]*,[ \t]*(-?[0-9]+)(?:[ \t]*,[ \t]*(-?[0-9]+))?[ \t]*\}");

            string newContent = coordRegex.Replace(content, match =>
            {
                // Corrupt with probability proportional to intensity
                if (_random.NextDouble() * 100.0 > intensity * 0.2)
                {
                    return match.Value;
                }

                string g1 = match.Groups[1].Value;
                string g2 = match.Groups[2].Value;
                bool hasG3 = match.Groups[3].Success;
                string g3 = hasG3 ? match.Groups[3].Value : "";

                if (int.TryParse(g1, out int x) && int.TryParse(g2, out int y))
                {
                    int z = 0;
                    if (hasG3) int.TryParse(g3, out z);

                    // Apply corruption logic
                    switch (mode)
                    {
                        case 0: // Swap / Negate sign
                            x = -x;
                            y = -y;
                            if (hasG3) z = -z;
                            break;
                        case 1: // Add random offset (vertex stretching!)
                            int range = (int)(intensity * 4);
                            x += _random.Next(-range, range + 1);
                            y += _random.Next(-range, range + 1);
                            if (hasG3) z += _random.Next(-range, range + 1);
                            break;
                        case 2: // XOR mask
                            int mask = _random.Next(255);
                            x ^= mask;
                            y ^= mask;
                            if (hasG3) z ^= mask;
                            break;
                        case 3: // Scale / Shift
                            double scale = 0.5 + (_random.NextDouble() * 1.5);
                            x = (int)(x * scale);
                            y = (int)(y * scale);
                            if (hasG3) z = (int)(z * scale);
                            break;
                        case 4: // Zero out
                            x = 0;
                            y = 0;
                            if (hasG3) z = 0;
                            break;
                        case 5: // Arithmetic Addition / Uniform Offset
                            int tx = _random.Next(-500, 501);
                            int ty = _random.Next(-500, 501);
                            int tz = _random.Next(-500, 501);
                            x += tx;
                            y += ty;
                            if (hasG3) z += tz;
                            break;
                    }

                    if (hasG3)
                    {
                        return $"{{ {x}, {y}, {z} }}";
                    }
                    else
                    {
                        return $"{{ {x}, {y} }}";
                    }
                }

                return match.Value;
            });

            // Also search and corrupt hex literals that look like color codes/flags inside curly brackets
            // e.g. {0x1b, 0x89, 0xe1, 0xff}
            var hexRegex = new Regex(@"\{[ \t]*(0x[0-9a-fA-F]+)[ \t]*,[ \t]*(0x[0-9a-fA-F]+)[ \t]*,[ \t]*(0x[0-9a-fA-F]+)[ \t]*,[ \t]*(0x[0-9a-fA-F]+)[ \t]*\}");
            
            newContent = hexRegex.Replace(newContent, match =>
            {
                if (_random.NextDouble() * 100.0 > intensity * 0.2)
                {
                    return match.Value;
                }

                try
                {
                    uint c1 = Convert.ToUInt32(match.Groups[1].Value.Substring(2), 16);
                    uint c2 = Convert.ToUInt32(match.Groups[2].Value.Substring(2), 16);
                    uint c3 = Convert.ToUInt32(match.Groups[3].Value.Substring(2), 16);
                    uint c4 = Convert.ToUInt32(match.Groups[4].Value.Substring(2), 16);

                    c1 = (uint)((c1 + _random.Next(50)) % 256);
                    c2 = (uint)((c2 + _random.Next(50)) % 256);
                    c3 = (uint)((c3 + _random.Next(50)) % 256);

                    return $"{{0x{c1:X2}, 0x{c2:X2}, 0x{c3:X2}, 0x{c4:X2}}}";
                }
                catch { return match.Value; }
            });

            File.WriteAllText(filePath, newContent);
        }

        private void PatchSourceFile(string relativePath, string oldText, string newText)
        {
            string fullPath = Path.Combine(_projectRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath)) return;

            string backupPath = fullPath + ".bak";
            if (!File.Exists(backupPath))
            {
                File.Copy(fullPath, backupPath);
            }

            // Read the current file content to allow consecutive edits to accumulate
            string content = File.ReadAllText(fullPath);
            content = content.Replace("\r\n", "\n");
            
            string normalizedOld = oldText.Replace("\r\n", "\n");
            string normalizedNew = newText.Replace("\r\n", "\n");

            if (content.Contains(normalizedOld) && !content.Contains(normalizedNew))
            {
                content = content.Replace(normalizedOld, normalizedNew);
                File.WriteAllText(fullPath, content);
                File.SetLastWriteTime(fullPath, DateTime.Now);
            }
        }

        private string RandomizeGameText(string content, int textMode)
        {
            int index = 0;
            StringBuilder sb = new StringBuilder();

            while (index < content.Length)
            {
                if (index < content.Length - 3 && content[index] == '_' && content[index + 1] == '(' && content[index + 2] == '"')
                {
                    // Found start of _("
                    sb.Append("_(\"");
                    index += 3;

                    bool inString = true;
                    StringBuilder strSegment = new StringBuilder();
                    
                    while (index < content.Length)
                    {
                        if (inString && content[index] == '"' && index + 1 < content.Length && content[index + 1] == ')')
                        {
                            // Found end of _("...")
                            // Corrupt the last collected string segment
                            sb.Append(CorruptTextString(strSegment.ToString(), textMode));
                            sb.Append("\")");
                            index += 2;
                            break;
                        }
                        
                        if (content[index] == '"')
                        {
                            // Check if it is escaped (e.g. \")
                            bool escaped = false;
                            int backslashCount = 0;
                            int k = index - 1;
                            while (k >= 0 && content[k] == '\\')
                            {
                                backslashCount++;
                                k--;
                            }
                            if (backslashCount % 2 != 0)
                            {
                                escaped = true;
                            }
                            
                            if (!escaped)
                            {
                                // Toggle inString
                                if (inString)
                                {
                                    // We were inside, now we go outside a string segment
                                    sb.Append(CorruptTextString(strSegment.ToString(), textMode));
                                    strSegment.Clear();
                                }
                                inString = !inString;
                                sb.Append('"');
                                index++;
                                continue;
                            }
                        }
                        
                        if (inString)
                        {
                            strSegment.Append(content[index]);
                        }
                        else
                        {
                            sb.Append(content[index]);
                        }
                        index++;
                    }
                }
                else
                {
                    sb.Append(content[index]);
                    index++;
                }
            }

            return sb.ToString();
        }

        private static readonly string[] EscapeKeywords = { "wait", "this", "world", "You", "Power", "I", "keep", "all", "col" };

        private int GetEscapeLength(string str, int index)
        {
            if (str[index] != '\\') return 0;
            if (index + 1 >= str.Length) return 1;

            char next = str[index + 1];
            if (next == 'n' || next == '"' || next == '\\')
            {
                return 2;
            }

            foreach (var kw in EscapeKeywords)
            {
                string target = kw + "\\";
                if (index + 1 + target.Length <= str.Length && str.Substring(index + 1, target.Length) == target)
                {
                    return 1 + target.Length;
                }
            }

            return 2;
        }

        private string CorruptTextString(string str, int textMode)
        {
            // Build a list of characters that are safe to corrupt
            // Safe characters are letters and numbers.
            // We preserve punctuation, spaces, newlines, backslashes, and [B]/[A] button prompts.
            List<int> safeIndices = new List<int>();
            List<char> safeChars = new List<char>();

            for (int i = 0; i < str.Length; i++)
            {
                char c = str[i];
                
                // Skip [ ... ] blocks completely (these are button tokens like [B], [A])
                if (c == '[')
                {
                    while (i < str.Length && str[i] != ']')
                    {
                        i++;
                    }
                    continue;
                }

                // Skip backslash escape sequences
                if (c == '\\')
                {
                    int len = GetEscapeLength(str, i);
                    i += len - 1;
                    continue;
                }

                // Check if it's a letter or digit
                if (char.IsLetterOrDigit(c))
                {
                    safeIndices.Add(i);
                    safeChars.Add(c);
                }
            }

            if (safeIndices.Count == 0) return str;

            char[] chars = str.ToCharArray();

            switch (textMode)
            {
                case 0: // Scramble Characters
                    int n = safeChars.Count;
                    while (n > 1)
                    {
                        n--;
                        int k = _random.Next(n + 1);
                        char temp = safeChars[k];
                        safeChars[k] = safeChars[n];
                        safeChars[n] = temp;
                    }
                    for (int i = 0; i < safeIndices.Count; i++)
                    {
                        chars[safeIndices[i]] = safeChars[i];
                    }
                    break;

                case 1: // Word Shuffle
                    var words = new List<string>();
                    int start = -1;
                    for (int i = 0; i < str.Length; i++)
                    {
                        if (str[i] == '[')
                        {
                            start = -1;
                            while (i < str.Length && str[i] != ']')
                            {
                                i++;
                            }
                            continue;
                        }

                        if (str[i] == '\\')
                        {
                            start = -1;
                            int len = GetEscapeLength(str, i);
                            i += len - 1;
                            continue;
                        }

                        if (char.IsLetterOrDigit(str[i]))
                        {
                            if (start == -1) start = i;
                        }
                        else
                        {
                            if (start != -1)
                            {
                                words.Add(str.Substring(start, i - start));
                                start = -1;
                            }
                        }
                    }
                    if (start != -1)
                    {
                        words.Add(str.Substring(start, str.Length - start));
                    }

                    if (words.Count > 1)
                    {
                        int wN = words.Count;
                        while (wN > 1)
                        {
                            wN--;
                            int k = _random.Next(wN + 1);
                            string temp = words[k];
                            words[k] = words[wN];
                            words[wN] = temp;
                        }
                    }

                    StringBuilder wSb = new StringBuilder();
                    int wCount = 0;
                    start = -1;
                    for (int i = 0; i < str.Length; i++)
                    {
                        if (str[i] == '[')
                        {
                            start = -1;
                            while (i < str.Length && str[i] != ']')
                            {
                                wSb.Append(str[i]);
                                i++;
                            }
                            if (i < str.Length) wSb.Append(str[i]);
                            continue;
                        }

                        if (str[i] == '\\')
                        {
                            start = -1;
                            int len = GetEscapeLength(str, i);
                            for (int k = 0; k < len; k++)
                            {
                                wSb.Append(str[i + k]);
                            }
                            i += len - 1;
                            continue;
                        }

                        if (char.IsLetterOrDigit(str[i]))
                        {
                            if (start == -1)
                            {
                                start = i;
                                if (wCount < words.Count)
                                {
                                    wSb.Append(words[wCount++]);
                                }
                            }
                        }
                        else
                        {
                            start = -1;
                            wSb.Append(str[i]);
                        }
                    }
                    return wSb.ToString();

                case 2: // Leet Speak
                    for (int i = 0; i < safeIndices.Count; i++)
                    {
                        int idx = safeIndices[i];
                        char orig = char.ToUpper(chars[idx]);
                        switch (orig)
                        {
                            case 'E': chars[idx] = '3'; break;
                            case 'A': chars[idx] = '4'; break;
                            case 'T': chars[idx] = '7'; break;
                            case 'O': chars[idx] = '0'; break;
                            case 'I': chars[idx] = '1'; break;
                            case 'S': chars[idx] = '5'; break;
                            case 'G': chars[idx] = '9'; break;
                            case 'B': chars[idx] = '8'; break;
                        }
                    }
                    break;

                case 3: // Alien/Glitch Symbols
                    // Restricted to compile-safe characters supported in standard charmap.txt
                    char[] glitchSymbols = new char[] { '?', '!', '.', ',', '-', '\'', '(', ')', '~', 'A', 'B', '0', '1', '2' };
                    for (int i = 0; i < safeIndices.Count; i++)
                    {
                        int idx = safeIndices[i];
                        chars[idx] = glitchSymbols[_random.Next(glitchSymbols.Length)];
                    }
                    break;

                case 4: // Mocking Case
                    bool uppercase = true;
                    for (int i = 0; i < safeIndices.Count; i++)
                    {
                        int idx = safeIndices[i];
                        char orig = chars[idx];
                        // Keep full-width letters uppercase to avoid compilation errors due to unmapped lowercase full-width letters in SM64 charmap
                        if (orig >= 0xFF21 && orig <= 0xFF3A)
                        {
                            chars[idx] = orig;
                        }
                        else
                        {
                            chars[idx] = uppercase ? char.ToUpper(orig) : char.ToLower(orig);
                            uppercase = !uppercase;
                        }
                    }
                    break;
            }

            return new string(chars);
        }

        private void CorruptAnimationFile(string filePath, double intensity, int animMode)
        {
            try
            {
                // Create backup first
                string backupPath = filePath + ".bak";
                if (!File.Exists(backupPath))
                {
                    File.Copy(filePath, backupPath);
                }

                string content = File.ReadAllText(filePath);
                
                // Matches the values array definition: static const s16 anim_xxxx_values[] = { ... };
                var regex = new Regex(@"(static const s16 anim_[a-zA-Z0-9_]+_values\[\]\s*=\s*\{)([\s\S]*?)(\};)");
                var match = regex.Match(content);
                if (!match.Success) return;

                string header = match.Groups[1].Value;
                string valuesText = match.Groups[2].Value;
                string footer = match.Groups[3].Value;

                // Split values by comma
                string[] tokens = valuesText.Split(',');
                var parsedValues = new List<short>();

                foreach (var token in tokens)
                {
                    string trimmed = token.Trim();
                    if (string.IsNullOrEmpty(trimmed)) continue;

                    short val = 0;
                    try
                    {
                        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                        {
                            val = (short)Convert.ToInt32(trimmed, 16);
                        }
                        else if (trimmed.StartsWith("-0x", StringComparison.OrdinalIgnoreCase))
                        {
                            val = (short)(-1 * Convert.ToInt32(trimmed.Substring(1), 16));
                        }
                        else
                        {
                            val = short.Parse(trimmed);
                        }
                    }
                    catch
                    {
                        continue;
                    }

                    parsedValues.Add(val);
                }

                if (parsedValues.Count == 0) return;

                // Apply corruption logic
                double rate = (intensity / 500.0); // max rate 1.0 at 500%
                int changesCount = (int)(parsedValues.Count * rate * 0.1); // subtle change per animation to keep it recognizable but glitched
                if (changesCount <= 0) changesCount = 1;

                switch (animMode)
                {
                    case 0: // 1. Joint Jitter
                        for (int i = 0; i < changesCount; i++)
                        {
                            int idx = _random.Next(0, parsedValues.Count);
                            parsedValues[idx] = (short)(parsedValues[idx] + _random.Next(-300, 301));
                        }
                        break;

                    case 1: // 2. Joint Stretch
                        double scale = 1.5 + (_random.NextDouble() * 3.0);
                        for (int i = 0; i < changesCount; i++)
                        {
                            int idx = _random.Next(0, parsedValues.Count);
                            parsedValues[idx] = (short)Math.Clamp(parsedValues[idx] * scale, short.MinValue, short.MaxValue);
                        }
                        break;

                    case 2: // 3. Angle Invert
                        for (int i = 0; i < changesCount; i++)
                        {
                            int idx = _random.Next(0, parsedValues.Count);
                            parsedValues[idx] = (short)(-parsedValues[idx]);
                        }
                        break;

                    case 3: // 4. Stutter Step
                        int stutterLen = _random.Next(2, 6);
                        for (int i = 0; i < changesCount; i++)
                        {
                            int startIdx = _random.Next(0, Math.Max(1, parsedValues.Count - stutterLen));
                            short valToRepeat = parsedValues[startIdx];
                            for (int k = 0; k < stutterLen && startIdx + k < parsedValues.Count; k++)
                            {
                                parsedValues[startIdx + k] = valToRepeat;
                            }
                        }
                        break;

                    case 4: // 5. Pose Freeze (T-Pose)
                        for (int i = 0; i < changesCount; i++)
                        {
                            int idx = _random.Next(0, parsedValues.Count);
                            parsedValues[idx] = 0;
                        }
                        break;

                    case 5: // 6. Extreme Twist
                        for (int i = 0; i < changesCount; i++)
                        {
                            int idx = _random.Next(0, parsedValues.Count);
                            parsedValues[idx] = (short)(_random.NextDouble() < 0.5 ? short.MinValue : short.MaxValue);
                        }
                        break;

                    case 6: // 7. Frame Scrambler (Shuffle indices/values)
                        for (int i = 0; i < changesCount; i++)
                        {
                            int idx1 = _random.Next(0, parsedValues.Count);
                            int idx2 = _random.Next(0, parsedValues.Count);
                            short temp = parsedValues[idx1];
                            parsedValues[idx1] = parsedValues[idx2];
                            parsedValues[idx2] = temp;
                        }
                        break;

                    case 7: // 8. Offset Detach (Limbs)
                        for (int i = 0; i < changesCount; i++)
                        {
                            int idx = _random.Next(0, parsedValues.Count);
                            if (Math.Abs(parsedValues[idx]) > 1000)
                            {
                                parsedValues[idx] = (short)Math.Clamp(parsedValues[idx] + _random.Next(-4000, 4001), short.MinValue, short.MaxValue);
                            }
                        }
                        break;
                }

                // Format values back into C arrays
                var sb = new StringBuilder();
                sb.AppendLine();
                for (int i = 0; i < parsedValues.Count; i++)
                {
                    if (i % 8 == 0) sb.Append("    ");
                    
                    ushort uval = (ushort)parsedValues[i];
                    sb.Append($"0x{uval:X4}");

                    if (i < parsedValues.Count - 1)
                    {
                        sb.Append(", ");
                    }
                    
                    if (i % 8 == 7 && i < parsedValues.Count - 1)
                    {
                        sb.AppendLine();
                    }
                }
                sb.AppendLine();
                sb.Append("    ");

                string newContent = regex.Replace(content, match.Result($"$1{sb.ToString()}$3"));
                File.WriteAllText(filePath, newContent);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error corrupting animation {filePath}: {ex.Message}");
            }
        }

        private void CorruptHudFile(string filePath, double intensity, int hudMode)
        {
            try
            {
                // Create backup first
                string backupPath = filePath + ".bak";
                if (!File.Exists(backupPath))
                {
                    File.Copy(filePath, backupPath);
                }

                string content = File.ReadAllText(filePath);
                double rate = (intensity / 500.0); // max rate 1.0 at 500%

                switch (hudMode)
                {
                    case 0: // 1. Position Scramble
                        int newTopY = _random.Next(30, 220);
                        content = Regex.Replace(content, @"#define HUD_TOP_Y \d+", $"#define HUD_TOP_Y {newTopY}");
                        
                        content = Regex.Replace(content, @"print_text\(([^,]+),[ \t]*([^,]+),[ \t]*(""[^""]+"")\)", m => {
                            if (_random.NextDouble() < rate)
                            {
                                int dx = _random.Next(-60, 61);
                                int dy = _random.Next(-40, 41);
                                return $"print_text(({m.Groups[1].Value}) + {dx}, ({m.Groups[2].Value}) + {dy}, {m.Groups[3].Value})";
                            }
                            return m.Value;
                        });
                        break;

                    case 1: // 2. Glyph Corruption
                        string[] glyphs = { ",", "+", "*", "-", "$", "/" };
                        content = Regex.Replace(content, @"print_text\(([^,]+),[ \t]*([^,]+),[ \t]*""([^""]+)""\)", m => {
                            if (_random.NextDouble() < rate)
                            {
                                string textVal = m.Groups[3].Value;
                                var sb = new StringBuilder();
                                foreach (char c in textVal)
                                {
                                    if (glyphs.Contains(c.ToString()))
                                    {
                                        sb.Append(glyphs[_random.Next(0, glyphs.Length)]);
                                    }
                                    else
                                    {
                                        sb.Append(c);
                                    }
                                }
                                return $"print_text({m.Groups[1].Value}, {m.Groups[2].Value}, \"{sb.ToString()}\")";
                            }
                            return m.Value;
                        });
                        break;

                    case 2: // 3. Int Format Break
                        content = Regex.Replace(content, @"""%d""", m => {
                            if (_random.NextDouble() < rate)
                            {
                                string[] formats = { "\"%x\"", "\"%o\"", "\"CRASH%d\"", "\"%d?\"", "\"???\"" };
                                return formats[_random.Next(0, formats.Length)];
                            }
                            return m.Value;
                        });
                        break;

                    case 3: // 4. Health Meter Warp
                        content = Regex.Replace(content, @"guTranslate\(mtx,[ \t]*\(f32\) sPowerMeterHUD\.x,[ \t]*\(f32\) sPowerMeterHUD\.y,[ \t]*0\)", m => {
                            int dx = _random.Next(-80, 81);
                            int dy = _random.Next(-60, 61);
                            return $"guTranslate(mtx, (f32) sPowerMeterHUD.x + {dx}, (f32) sPowerMeterHUD.y + {dy}, 0)";
                        });
                        
                        content = Regex.Replace(content, @"\(\*healthLUT\)\[numHealthWedges - 1\]", m => {
                            int offset = _random.Next(0, 8);
                            return $"(*healthLUT)[(numHealthWedges - 1 + {offset}) % 8]";
                        });
                        break;

                    case 4: // 5. Strobe Stutter
                        content = Regex.Replace(content, @"void render_hud\(([^)]*)\)[ \t]*\{", m => {
                            return m.Value + "\n    if (gGlobalTimer & 4) return;";
                        });
                        content = Regex.Replace(content, @"void render_hud_mario_lives\(void\)[ \t]*\{", m => {
                            return m.Value + "\n    if (gGlobalTimer & 8) return;";
                        });
                        content = Regex.Replace(content, @"void render_hud_coins\(void\)[ \t]*\{", m => {
                            return m.Value + "\n    if (gGlobalTimer & 8) return;";
                        });
                        break;
                }

                File.WriteAllText(filePath, content);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error corrupting HUD file {filePath}: {ex.Message}");
            }
        }

        private int ParseIntHexSafe(string val)
        {
            val = val.Trim();
            if (val.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(val.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out int hex))
                    return hex;
            }
            if (int.TryParse(val, out int res))
                return res;
            return 0;
        }

        private void CorruptDisplayListFile(string filePath, double intensity, int dlMode)
        {
            try
            {
                // Create backup first
                string backupPath = filePath + ".bak";
                if (!File.Exists(backupPath))
                {
                    File.Copy(filePath, backupPath);
                }

                string content = File.ReadAllText(filePath);
                double rate = (intensity / 500.0); // max rate 1.0 at 500%

                // Matches N64 Vtx structures: {{{x, y, z}, flag, {u, v}, {r, g, b, a}}}
                var regex = new Regex(@"\{\{\{\s*(-?\d+),\s*(-?\d+),\s*(-?\d+)\s*\},\s*(\d+|0x[0-9a-fA-F]+),\s*\{\s*(-?\d+),\s*(-?\d+)\s*\},\s*\{\s*(\d+|0x[0-9a-fA-F]+|-?\d+),\s*(\d+|0x[0-9a-fA-F]+|-?\d+),\s*(\d+|0x[0-9a-fA-F]+|-?\d+),\s*(\d+|0x[0-9a-fA-F]+|-?\d+)\s*\}\s*\}\}");

                string newContent = regex.Replace(content, m =>
                {
                    double effectiveRate = (dlMode == 7) ? 1.0 : (rate * 0.5);
                    if (_random.NextDouble() >= effectiveRate) return m.Value; // scale rate

                    int x = int.Parse(m.Groups[1].Value);
                    int y = int.Parse(m.Groups[2].Value);
                    int z = int.Parse(m.Groups[3].Value);
                    string flag = m.Groups[4].Value;
                    int u = int.Parse(m.Groups[5].Value);
                    int v = int.Parse(m.Groups[6].Value);
                    string r = m.Groups[7].Value;
                    string g = m.Groups[8].Value;
                    string b = m.Groups[9].Value;
                    string a = m.Groups[10].Value;

                    switch (dlMode)
                    {
                        case 0: // Rainbow World (Vertex Colors)
                            // Spatial rainbow gradient based on vertex coordinates
                            double hue = (x + y + z) / 3000.0;
                            hue = hue - Math.Floor(hue);

                            // HSV/Hue to RGB conversion
                            double r_h = Math.Abs(hue * 6.0 - 3.0) - 1.0;
                            double g_h = 2.0 - Math.Abs(hue * 6.0 - 2.0);
                            double b_h = 2.0 - Math.Abs(hue * 6.0 - 4.0);

                            byte rv = (byte)(Math.Clamp(r_h, 0.0, 1.0) * 255.0);
                            byte gv = (byte)(Math.Clamp(g_h, 0.0, 1.0) * 255.0);
                            byte bv = (byte)(Math.Clamp(b_h, 0.0, 1.0) * 255.0);

                            r = $"0x{rv:X2}";
                            g = $"0x{gv:X2}";
                            b = $"0x{bv:X2}";
                            a = "0xFF";
                            break;

                        case 1: // Faceless Mario (No UVs)
                        case 8: // Faceless Mario V2 (Castle Glow & Crystal Water)
                            u = 0;
                            v = 0;
                            break;

                        case 2: // Normal Inversion
                            int nr = ParseIntHexSafe(r);
                            int ng = ParseIntHexSafe(g);
                            int nb = ParseIntHexSafe(b);
                            r = $"0x{(byte)(-nr):X2}";
                            g = $"0x{(byte)(-ng):X2}";
                            b = $"0x{(byte)(-nb):X2}";
                            break;

                        case 3: // Texture Scrambler (UV Size)
                            double scaleUV = 1.5 + _random.NextDouble() * (intensity / 100.0) * 5.0;
                            u = (int)(u * scaleUV);
                            v = (int)(v * scaleUV);
                            break;

                        case 4: // Polygon Exploder
                            double explScale = 1.0 + (intensity / 50.0);
                            x += (int)(_random.Next(-400, 401) * explScale);
                            y += (int)(_random.Next(-400, 401) * explScale);
                            z += (int)(_random.Next(-400, 401) * explScale);
                            break;

                        case 5: // Voxelizer Snap
                            int snapUnit = (int)(50 + (intensity * 3.0));
                            if (snapUnit < 1) snapUnit = 1;
                            x = (x / snapUnit) * snapUnit;
                            y = (y / snapUnit) * snapUnit;
                            z = (z / snapUnit) * snapUnit;
                            break;

                        case 6: // UV Swap
                            int tempU = u;
                            u = v;
                            v = tempU;
                            u += (int)((_random.NextDouble() - 0.5) * intensity * 10.0);
                            v += (int)((_random.NextDouble() - 0.5) * intensity * 10.0);
                            break;

                        case 7: // Scale Distortion (Funhouse Wave)
                            double waveScale = (intensity / 100.0) * 1.5;
                            x += (int)(Math.Sin(y / 150.0) * 80.0 * waveScale);
                            y += (int)(Math.Cos(x / 150.0) * 80.0 * waveScale);
                            z += (int)(Math.Sin(x / 150.0) * 80.0 * waveScale);
                            break;
                    }

                    return $"{{{{{{{x}, {y}, {z}}}, {flag}, {{{u}, {v}}}, {{{r}, {g}, {b}, {a}}}}}}}";
                });

                if (dlMode == 0)
                {
                    // Inject clear lighting geometry mode inside Display Lists to achieve full Bowser boss level bright color glow
                    newContent = Regex.Replace(newContent, @"(static const Gfx [a-zA-Z0-9_]+\[\]\s*=\s*\{)", "$1\n    gsSPClearGeometryMode(G_LIGHTING),");
                }
                else if (dlMode == 1) // Faceless Mario (No UVs)
                {
                    // Enable Environment Mapping (Texture Coordinate Generation) to strip static UVs and force shifting textures/crystal reflections
                    newContent = Regex.Replace(newContent, @"(static const Gfx [a-zA-Z0-9_]+\[\]\s*=\s*\{)", "$1\n    gsSPSetGeometryMode(G_TEXTURE_GEN | G_TEXTURE_GEN_LINEAR),");
                }
                else if (dlMode == 8) // Faceless Mario V2 (Castle Glow & Crystal Water)
                {
                    // Enable Environment Mapping (Texture Coordinate Generation) to strip static UVs and force shifting textures/crystal reflections
                    newContent = Regex.Replace(newContent, @"(static const Gfx [a-zA-Z0-9_]+\[\]\s*=\s*\{)", "$1\n    gsSPSetGeometryMode(G_TEXTURE_GEN | G_TEXTURE_GEN_LINEAR),");
                    newContent = Regex.Replace(newContent, @"\bG_CC_MODULATERGB\b", "G_CC_FADEA");
                    newContent = Regex.Replace(newContent, @"\bG_CC_MODULATERGBA\b", "G_CC_FADEA");
                }
                else if (dlMode == 2) // Normal Inversion
                {
                    // Invert culling modes to draw the model inside-out
                    newContent = Regex.Replace(newContent, @"(static const Gfx [a-zA-Z0-9_]+\[\]\s*=\s*\{)", "$1\n    gsSPClearGeometryMode(G_CULL_BACK),\n    gsSPSetGeometryMode(G_CULL_FRONT),");
                }
                else if (dlMode == 3) // Texture Scrambler (UV Size)
                {
                    // Force a micro texture scale mapping to pixelate/tile textures extensively
                    newContent = Regex.Replace(newContent, @"(static const Gfx [a-zA-Z0-9_]+\[\]\s*=\s*\{)", "$1\n    gsSPTexture(0x0001, 0x0001, 0, G_TX_RENDERTILE, G_ON),");
                }
                else if (dlMode == 4) // Polygon Exploder
                {
                    // Clear both front and back culling so exploded faces are visible from all sides
                    newContent = Regex.Replace(newContent, @"(static const Gfx [a-zA-Z0-9_]+\[\]\s*=\s*\{)", "$1\n    gsSPClearGeometryMode(G_CULL_BOTH),");
                }
                else if (dlMode == 5) // Voxelizer Snap
                {
                    // Disable lighting and shading to enhance blocky retro voxel aesthetic
                    newContent = Regex.Replace(newContent, @"(static const Gfx [a-zA-Z0-9_]+\[\]\s*=\s*\{)", "$1\n    gsSPClearGeometryMode(G_LIGHTING),");
                }
                else if (dlMode == 6) // UV Swap
                {
                    // Force a fixed skewed texture coordinate translation scale
                    newContent = Regex.Replace(newContent, @"(static const Gfx [a-zA-Z0-9_]+\[\]\s*=\s*\{)", "$1\n    gsSPTexture(0x4000, 0x4000, 0, G_TX_RENDERTILE, G_ON),");
                }

                File.WriteAllText(filePath, newContent);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error corrupting DL file {filePath}: {ex.Message}");
            }
        }

        private void CorruptCollisionFile(string filePath, double intensity, int mode)
        {
            try
            {
                // Create backup first
                string backupPath = filePath + ".bak";
                if (!File.Exists(backupPath))
                {
                    File.Copy(filePath, backupPath);
                }

                string content = File.ReadAllText(filePath);
                double rate = (intensity / 100.0); // max rate at 100%

                if (mode == 7 || mode == 8)
                {
                    // Vertex warping mode
                    var regexVertex = new Regex(@"COL_VERTEX\(\s*(-?\d+)\s*,\s*(-?\d+)\s*,\s*(-?\d+)\s*\)");
                    content = regexVertex.Replace(content, m =>
                    {
                        if (_random.NextDouble() >= rate) return m.Value;

                        int x = int.Parse(m.Groups[1].Value);
                        int y = int.Parse(m.Groups[2].Value);
                        int z = int.Parse(m.Groups[3].Value);

                        if (mode == 7) // Surface Noise / Jitter
                        {
                            x += _random.Next(-150, 151);
                            y += _random.Next(-150, 151);
                            z += _random.Next(-150, 151);
                        }
                        else if (mode == 8) // Giant Slopes
                        {
                            y = (int)(y * 2.5);
                        }

                        // Clamp coordinates to fit in N64 signed 16-bit short limits [-32768, 32767]
                        // preventing IDO compiler integer out-of-bounds/overflow warnings or errors.
                        x = Math.Clamp(x, -32500, 32500);
                        y = Math.Clamp(y, -32500, 32500);
                        z = Math.Clamp(z, -32500, 32500);

                        return $"COL_VERTEX({x}, {y}, {z})";
                    });
                }
                else
                {
                    // Surface type customization modes
                    var regexTriInit = new Regex(@"COL_TRI_INIT\(\s*([A-Za-z0-9_]+)\s*,\s*(\d+)\s*\)");
                    string[] chaosPool = {
                        "SURFACE_DEFAULT", "SURFACE_BURNING", "SURFACE_VERY_SLIPPERY",
                        "SURFACE_NOT_SLIPPERY", "SURFACE_DEATH_PLANE", "SURFACE_VERTICAL_WIND",
                        "SURFACE_ICE", "SURFACE_SHALLOW_QUICKSAND"
                    };

                    bool IsSpecialSurface(string name)
                    {
                        return name == "SURFACE_0004" ||
                               name == "SURFACE_FLOWING_WATER" ||
                               name == "SURFACE_DEEP_MOVING_QUICKSAND" ||
                               name == "SURFACE_SHALLOW_MOVING_QUICKSAND" ||
                               name == "SURFACE_MOVING_QUICKSAND" ||
                               name == "SURFACE_HORIZONTAL_WIND" ||
                               name == "SURFACE_INSTANT_MOVING_QUICKSAND";
                    }

                    content = regexTriInit.Replace(content, m =>
                    {
                        if (_random.NextDouble() >= rate) return m.Value;

                        string originalSurface = m.Groups[1].Value;
                        string count = m.Groups[2].Value;

                        // CRITICAL: Special force surfaces expect 4 parameters per triangle instead of 3.
                        // We must NEVER swap between special and non-special surfaces, as that causes
                        // loader offset shifting and instant game crashes.
                        if (IsSpecialSurface(originalSurface))
                        {
                            string[] specialPool = {
                                "SURFACE_FLOWING_WATER", "SURFACE_DEEP_MOVING_QUICKSAND",
                                "SURFACE_SHALLOW_MOVING_QUICKSAND", "SURFACE_MOVING_QUICKSAND",
                                "SURFACE_HORIZONTAL_WIND", "SURFACE_INSTANT_MOVING_QUICKSAND"
                            };
                            string targetSpecial = specialPool[_random.Next(specialPool.Length)];
                            return $"COL_TRI_INIT({targetSpecial}, {count})";
                        }

                        string targetSurface = originalSurface;
                        switch (mode)
                        {
                            case 0: targetSurface = "SURFACE_VERY_SLIPPERY"; break;
                            case 1: targetSurface = "SURFACE_BURNING"; break;
                            case 2: targetSurface = "SURFACE_DEATH_PLANE"; break;
                            case 3: targetSurface = "SURFACE_DEEP_QUICKSAND"; break;
                            case 4: targetSurface = "SURFACE_NOT_SLIPPERY"; break;
                            case 5: targetSurface = "SURFACE_VERTICAL_WIND"; break;
                            case 6: targetSurface = "SURFACE_QUICKSAND"; break;
                            case 9: targetSurface = chaosPool[_random.Next(chaosPool.Length)]; break;
                        }

                        return $"COL_TRI_INIT({targetSurface}, {count})";
                    });
                }

                File.WriteAllText(filePath, content);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error corrupting collision file {filePath}: {ex.Message}");
            }
        }

        private void CorruptGoddardFile(string filePath, double intensity, int goddardMode)
        {
            try
            {
                // Create backup first
                string backupPath = filePath + ".bak";
                if (!File.Exists(backupPath))
                {
                    File.Copy(filePath, backupPath);
                }

                string content = File.ReadAllText(filePath);
                double rate = (intensity / 500.0);

                // Matches 3D vertex coordinate arrays: { x, y, z }
                var regex = new Regex(@"\{\s*(-?\d+),\s*(-?\d+),\s*(-?\d+)\s*\}");

                int vtxCounter = 0;
                string newContent = regex.Replace(content, m =>
                {
                    vtxCounter++;
                    if (_random.NextDouble() >= rate) return m.Value;

                     int x = int.Parse(m.Groups[1].Value);
                     int y = int.Parse(m.Groups[2].Value);
                     int z = int.Parse(m.Groups[3].Value);

                     switch (goddardMode)
                     {
                         case 0: // 1. Face Jitter (Vibration)
                             x += _random.Next(-40, 41);
                             y += _random.Next(-40, 41);
                             z += _random.Next(-40, 41);
                             break;

                         case 1: // 2. Face Stretch (Meltdown)
                             y = (int)(y * (1.5 + _random.NextDouble() * 3.0));
                             break;

                         case 2: // 3. Face Squash
                             // Add a unique offset to prevent Z-plane coordinate collapse
                             z = (int)(z * 0.1) + (vtxCounter % 7 - 3);
                             break;

                         case 3: // 4. Vertex Exploder
                             x += _random.Next(-400, 401);
                             y += _random.Next(-400, 401);
                             z += _random.Next(-400, 401);
                             break;

                         case 4: // 5. Mirror Face (Invert X)
                             x = -x;
                             break;

                         case 5: // 6. Low Poly Voxelizer
                             // Add 3D grid sub-voxel offsets so nearby vertices never share coordinates
                             x = (x / 60) * 60 + (vtxCounter % 7 - 3);
                             y = (y / 60) * 60 + ((vtxCounter / 7) % 7 - 3);
                             z = (z / 60) * 60 + ((vtxCounter / 49) % 7 - 3);
                             break;

                         case 6: // 7. Static Noise
                             x = _random.Next(-1000, 1001);
                             y = _random.Next(-1000, 1001);
                             z = _random.Next(-1000, 1001);
                             break;

                         case 7: // 8. Facial Tremor
                             if (Math.Abs(z) > 500)
                             {
                                 z += _random.Next(-300, 301);
                             }
                             break;

                         case 8: // 9. Mouth/Eye Warp
                             if (Math.Abs(x) > 150)
                             {
                                 x = (int)(x * 2.2);
                             }
                             if (Math.Abs(y) > 150)
                             {
                                 y = (int)(y * 2.2);
                             }
                             break;

                         case 9: // 10. Melt Face (Zero Coords)
                             // Distribute vertices within a tiny grid near zero so they don't overlap
                             x = (vtxCounter % 11 - 5) * 2;
                             y = ((vtxCounter / 11) % 11 - 5) * 2;
                             z = ((vtxCounter / 121) % 11 - 5) * 2;
                             break;
                     }

                     return $"{{ {x}, {y}, {z} }}";
                 });

                File.WriteAllText(filePath, newContent);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error corrupting Goddard file {filePath}: {ex.Message}");
            }
        }

        private void LoadPresetsList(string selectedPresetName = "")
        {
            _isLoadingPreset = true;
            try
            {
                PresetComboBox.Items.Clear();
                PresetComboBox.Items.Add("Default Settings");

                if (Directory.Exists(_presetsDir))
                {
                    var files = Directory.GetFiles(_presetsDir, "*.json");
                    foreach (var file in files)
                    {
                        string name = Path.GetFileNameWithoutExtension(file);
                        if (!name.StartsWith("_"))
                        {
                            PresetComboBox.Items.Add(name);
                        }
                    }
                }

                if (!string.IsNullOrEmpty(selectedPresetName))
                {
                    PresetComboBox.SelectedItem = selectedPresetName;
                }
                else
                {
                    PresetComboBox.SelectedIndex = 0;
                }
            }
            catch { }
            finally
            {
                _isLoadingPreset = false;
            }
        }

        private void PresetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingPreset) return;
            if (IntensifySlider == null) return; // Guard for XAML init phase

            if (PresetComboBox.SelectedIndex <= 0)
            {
                // Reset to default settings
                IntensifySlider.Value = 10;
                LevelSelectionComboBox.SelectedIndex = 0;
                TargetMusicNotesCheck.IsChecked = true;
                _activeM64Modes.Clear();
                _activeM64Modes.AddRange(new[] { 0, 1, 2, 3, 4, 5 });
                M64AllRadio.IsChecked = true;
                TargetSoundsCheck.IsChecked = true;
                ShuffleSoundsCheck.IsChecked = false;
                ExcludeInstrumentsShuffleCheck.IsChecked = false;
                ExcludeSfxShuffleCheck.IsChecked = false;
                ShuffleSfxOnlyCheck.IsChecked = false;
                SfxIdentityShuffleCheck.IsChecked = true;
                SfxPitchVariationCheck.IsChecked = false;
                SfxRandomizerModeComboBox.SelectedIndex = 0;
                ReplaceSfxCheck.IsChecked = false;
                RandomizeDlCheck.IsChecked = false;
                DlRandomizerModeComboBox.SelectedIndex = 0;
                DlExclusionCheck.IsChecked = false;
                TargetModelsCheck.IsChecked = true;
                TargetGoddardCheck.IsChecked = true;
                GoddardModeComboBox.SelectedIndex = 0;
                RandomizeMarioColorsCheck.IsChecked = false;
                MarioColorsAreaComboBox.SelectedIndex = 0;
                MarioColorsModeComboBox.SelectedIndex = 0;
                
                GlitchAnimationsCheck.IsChecked = false;
                AnimationGlitcherModeComboBox.SelectedIndex = 0;
                GlitchHudCheck.IsChecked = false;
                HudGlitcherModeComboBox.SelectedIndex = 0;
                RandomizeTexturesCheck.IsChecked = false;
                TextureRandomizeModeComboBox.SelectedIndex = 0;
                ReplaceTexturesCustomCheck.IsChecked = false;
                _textureReplacementRules.Clear();
                ActiveTextureReplacementsSummaryText.Text = "Active replacement rules: 0";
                TextureRandomizeAllRadio.IsChecked = true;
                _selectedTextures.Clear();
                ModeComboBox.SelectedIndex = 0;
                RandomizeSkyboxCheck.IsChecked = false;
                RandomizeTextCheck.IsChecked = false;
                TextRandomizeModeComboBox.SelectedIndex = 0;
                
                ChaosLogicJumpWeird.IsChecked = false;
                ChaosLogicJumpDeath.IsChecked = false;
                LimboMarioCheck.IsChecked = false;
                AlienSoundCheatCheck.IsChecked = false;
                return;
            }

            string presetName = PresetComboBox.SelectedItem.ToString();
            string filePath = Path.Combine(_presetsDir, presetName + ".json");
            LoadPresetFromFile(filePath);
        }

        private void LoadPresetFromFile(string filePath)
        {
            if (!File.Exists(filePath)) return;

            try
            {
                string json = File.ReadAllText(filePath);
                var preset = System.Text.Json.JsonSerializer.Deserialize<ChaosPreset>(json);
                if (preset == null) return;

                _isLoadingPreset = true;

                IntensifySlider.Value = preset.Intensity;
                if (preset.TargetLevelIndex < LevelSelectionComboBox.Items.Count)
                    LevelSelectionComboBox.SelectedIndex = preset.TargetLevelIndex;

                TargetMusicNotesCheck.IsChecked = preset.TargetMusicNotes;
                _activeM64Modes.Clear();
                if (preset.ActiveM64Modes != null && preset.ActiveM64Modes.Count > 0)
                {
                    _activeM64Modes.AddRange(preset.ActiveM64Modes);
                }
                else
                {
                    _activeM64Modes.Add(preset.MusicNotesMode);
                }
                M64AllRadio.IsChecked = preset.M64All;
                M64SelectRadio.IsChecked = preset.M64Select;

                TargetSoundsCheck.IsChecked = preset.TargetSounds;
                ShuffleSoundsCheck.IsChecked = preset.ShuffleSounds;
                ExcludeInstrumentsShuffleCheck.IsChecked = preset.ExcludeInstrumentsShuffle;
                ExcludeSfxShuffleCheck.IsChecked = preset.ExcludeSfxShuffle;
                ShuffleSfxOnlyCheck.IsChecked = preset.ShuffleSfxOnly;
                SfxIdentityShuffleCheck.IsChecked = preset.SfxIdentityShuffle;
                SfxPitchVariationCheck.IsChecked = preset.SfxPitchVariation;
                SfxRandomizerModeComboBox.SelectedIndex = preset.SfxRandomizerMode;

                ReplaceSfxCheck.IsChecked = preset.ReplaceSfx;

                RandomizeDlCheck.IsChecked = preset.RandomizeDl;
                DlRandomizerModeComboBox.SelectedIndex = preset.DlRandomizerMode;
                DlExclusionCheck.IsChecked = preset.DlExclusion;

                TargetModelsCheck.IsChecked = preset.TargetModels;
                TargetGoddardCheck.IsChecked = preset.TargetGoddard;
                GoddardModeComboBox.SelectedIndex = preset.GoddardMode;
                RandomizeMarioColorsCheck.IsChecked = preset.RandomizeMarioColors;
                MarioColorsAreaComboBox.SelectedIndex = preset.MarioColorsArea;
                MarioColorsModeComboBox.SelectedIndex = preset.MarioColorsMode;

                GlitchAnimationsCheck.IsChecked = preset.GlitchAnimations;
                AnimationGlitcherModeComboBox.SelectedIndex = preset.AnimationGlitcherMode;

                GlitchHudCheck.IsChecked = preset.GlitchHud;
                HudGlitcherModeComboBox.SelectedIndex = preset.HudGlitcherMode;

                RandomizeTexturesCheck.IsChecked = preset.RandomizeTextures;
                TextureRandomizeModeComboBox.SelectedIndex = preset.TextureRandomizeMode;
                ReplaceTexturesCustomCheck.IsChecked = preset.ReplaceTexturesCustom;
                _textureReplacementRules.Clear();
                if (preset.ReplaceTexturesRules != null)
                {
                    _textureReplacementRules.AddRange(preset.ReplaceTexturesRules);
                }
                ActiveTextureReplacementsSummaryText.Text = $"Active replacement rules: {_textureReplacementRules.Count}";
                if (preset.TextureRandomizeSelectedOnly)
                {
                    TextureRandomizeSelectedRadio.IsChecked = true;
                }
                else
                {
                    TextureRandomizeAllRadio.IsChecked = true;
                }
                _selectedTextures = preset.TextureRandomizeSelectedPaths ?? new List<string>();

                ModeComboBox.SelectedIndex = preset.ModeIndex;
                RandomizeSkyboxCheck.IsChecked = preset.RandomizeSkybox;
                RandomizeTextCheck.IsChecked = preset.RandomizeText;
                TextRandomizeModeComboBox.SelectedIndex = preset.TextRandomizeMode;

                ChaosLogicJumpWeird.IsChecked = preset.ChaosLogicJumpWeird;
                ChaosLogicJumpDeath.IsChecked = preset.ChaosLogicJumpDeath;
                LimboMarioCheck.IsChecked = preset.LimboMario;
                LimboMarioModeComboBox.SelectedIndex = preset.LimboMarioMode;
                AlienSoundCheatCheck.IsChecked = preset.AlienSoundCheat;

                ScrambleTitleScreenCheck.IsChecked = preset.ScrambleTitleScreen;
                TitleScreenScramblerModeComboBox.SelectedIndex = preset.TitleScreenScramblerMode;
                RandomizeCutsceneCameraCheck.IsChecked = preset.RandomizeCutsceneCamera;
                CutsceneCameraModeComboBox.SelectedIndex = preset.CutsceneCameraMode;
                LakituCameraChaosCheck.IsChecked = preset.LakituCameraChaos;
                LakituCameraModeComboBox.SelectedIndex = preset.LakituCameraMode;
                StartLevelChaosCheck.IsChecked = preset.StartLevelChaos;
                StartLevelComboBox.SelectedIndex = preset.StartLevelIndex;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load preset: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _isLoadingPreset = false;
                
                // Trigger visibility updates manually
                TargetMusicNotesCheck_Toggle(null, null);
                ShuffleSoundsCheck_Toggle(null, null);
                ReplaceSfxCheck_Toggle(null, null);
                RandomizeDlCheck_Toggle(null, null);
                DlExclusionCheck_Toggle(null, null);
                TargetGoddardCheck_Toggle(null, null);
                GlitchAnimationsCheck_Toggle(null, null);
                GlitchHudCheck_Toggle(null, null);
                RandomizeTexturesCheck_Toggle(null, null);
                ReplaceTexturesCustomCheck_Toggle(null, null);
                TextureTarget_Changed(null, null);
                RandomizeTextCheck_Toggle(null, null);
                RandomizeMarioColorsCheck_Toggle(null, null);
                M64Radio_Checked(null, null);
                ScrambleTitleScreenCheck_Toggle(null, null);
                RandomizeCutsceneCameraCheck_Toggle(null, null);
                LakituCameraChaosCheck_Toggle(null, null);
                LimboMarioCheck_Toggle(null, null);
                StartLevelChaosCheck_Toggle(null, null);
            }
        }

        private void SavePresetToFile(string filePath)
        {
            var preset = new ChaosPreset
            {
                PresetName = Path.GetFileNameWithoutExtension(filePath),
                Intensity = IntensifySlider.Value,
                TargetLevelIndex = LevelSelectionComboBox.SelectedIndex,

                TargetMusicNotes = TargetMusicNotesCheck.IsChecked == true,
                MusicNotesMode = _activeM64Modes.FirstOrDefault(),
                ActiveM64Modes = new List<int>(_activeM64Modes),
                M64All = M64AllRadio.IsChecked == true,
                M64Select = M64SelectRadio.IsChecked == true,

                TargetSounds = TargetSoundsCheck.IsChecked == true,
                ShuffleSounds = ShuffleSoundsCheck.IsChecked == true,
                ExcludeInstrumentsShuffle = ExcludeInstrumentsShuffleCheck.IsChecked == true,
                ExcludeSfxShuffle = ExcludeSfxShuffleCheck.IsChecked == true,
                ShuffleSfxOnly = ShuffleSfxOnlyCheck.IsChecked == true,
                SfxIdentityShuffle = SfxIdentityShuffleCheck.IsChecked == true,
                SfxPitchVariation = SfxPitchVariationCheck.IsChecked == true,
                SfxRandomizerMode = SfxRandomizerModeComboBox.SelectedIndex,

                ReplaceSfx = ReplaceSfxCheck.IsChecked == true,

                RandomizeDl = RandomizeDlCheck.IsChecked == true,
                DlRandomizerMode = DlRandomizerModeComboBox.SelectedIndex,
                DlExclusion = DlExclusionCheck.IsChecked == true,

                TargetModels = TargetModelsCheck.IsChecked == true,
                TargetGoddard = TargetGoddardCheck.IsChecked == true,
                GoddardMode = GoddardModeComboBox.SelectedIndex,
                RandomizeMarioColors = RandomizeMarioColorsCheck.IsChecked == true,
                MarioColorsArea = MarioColorsAreaComboBox.SelectedIndex,
                MarioColorsMode = MarioColorsModeComboBox.SelectedIndex,

                GlitchAnimations = GlitchAnimationsCheck.IsChecked == true,
                AnimationGlitcherMode = AnimationGlitcherModeComboBox.SelectedIndex,

                GlitchHud = GlitchHudCheck.IsChecked == true,
                HudGlitcherMode = HudGlitcherModeComboBox.SelectedIndex,

                RandomizeTextures = RandomizeTexturesCheck.IsChecked == true,
                TextureRandomizeMode = TextureRandomizeModeComboBox.SelectedIndex,
                ReplaceTexturesCustom = ReplaceTexturesCustomCheck.IsChecked == true,
                ReplaceTexturesRules = _textureReplacementRules.ToList(),
                TextureRandomizeSelectedOnly = TextureRandomizeSelectedRadio.IsChecked == true,
                TextureRandomizeSelectedPaths = _selectedTextures.ToList(),

                ModeIndex = ModeComboBox.SelectedIndex,
                RandomizeSkybox = RandomizeSkyboxCheck.IsChecked == true,
                RandomizeText = RandomizeTextCheck.IsChecked == true,
                TextRandomizeMode = TextRandomizeModeComboBox.SelectedIndex,

                ChaosLogicJumpWeird = ChaosLogicJumpWeird.IsChecked == true,
                ChaosLogicJumpDeath = ChaosLogicJumpDeath.IsChecked == true,
                LimboMario = LimboMarioCheck.IsChecked == true,
                LimboMarioMode = LimboMarioModeComboBox.SelectedIndex,
                AlienSoundCheat = AlienSoundCheatCheck.IsChecked == true,

                ScrambleTitleScreen = ScrambleTitleScreenCheck.IsChecked == true,
                TitleScreenScramblerMode = TitleScreenScramblerModeComboBox.SelectedIndex,
                RandomizeCutsceneCamera = RandomizeCutsceneCameraCheck.IsChecked == true,
                CutsceneCameraMode = CutsceneCameraModeComboBox.SelectedIndex,
                LakituCameraChaos = LakituCameraChaosCheck.IsChecked == true,
                LakituCameraMode = LakituCameraModeComboBox.SelectedIndex,
                StartLevelChaos = StartLevelChaosCheck.IsChecked == true,
                StartLevelIndex = StartLevelComboBox.SelectedIndex
            };

            try
            {
                string json = System.Text.Json.JsonSerializer.Serialize(preset, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(filePath, json);
            }
            catch { }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            base.OnClosing(e);
            SavePresetToFile(Path.Combine(_presetsDir, "_default_settings.json"));
        }

        private void SavePreset_Click(object sender, RoutedEventArgs e)
        {
            string name = InputBox.Show("Enter a name for the chaos preset:", "Save Chaos Preset", "MyChaosPreset");
            if (string.IsNullOrEmpty(name)) return;

            // Remove invalid file characters
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }

            string filePath = Path.Combine(_presetsDir, name + ".json");
            SavePresetToFile(filePath);
            LoadPresetsList(name);
            MessageBox.Show($"Preset '{name}' saved successfully!", "Preset Saved", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void DeletePreset_Click(object sender, RoutedEventArgs e)
        {
            if (PresetComboBox.SelectedIndex <= 0)
            {
                MessageBox.Show("Cannot delete the default settings template.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string presetName = PresetComboBox.SelectedItem.ToString();
            string filePath = Path.Combine(_presetsDir, presetName + ".json");
            if (!File.Exists(filePath)) return;

            if (MessageBox.Show($"Are you sure you want to delete preset '{presetName}'?", "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                try
                {
                    File.Delete(filePath);
                    LoadPresetsList();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to delete preset: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void CorruptMarioColorsFile(string filePath, int targetArea, int marioColorsMode)
        {
            try
            {
                // Create backup first
                string backupPath = filePath + ".bak";
                if (!File.Exists(backupPath))
                {
                    File.Copy(filePath, backupPath);
                }

                string content = File.ReadAllText(filePath);
                bool modified = false;

                // Pattern 1: gdSPDefLights1 macro
                var regexGdSP = new Regex(@"(static\s+const\s+)?(Lights1)\s+(\w+)\s*=\s*gdSPDefLights1\s*\(\s*([0-9a-fA-FxX\s,\-\+]+)\s*\)\s*;", RegexOptions.Singleline);
                
                // Pattern 2: Light_t / Lights1 brace structs
                var regexStruct = new Regex(@"(static\s+const\s+)?(Light_t|Lights1)\s+(\w+)\s*=\s*\{([^{}]*\{[^{}]*\}[^{}]*)*\};", RegexOptions.Singleline);

                string newContent = regexGdSP.Replace(content, m =>
                {
                    string structName = m.Groups[3].Value.ToLower();
                    string argsStr = m.Groups[4].Value;
                    string[] args = argsStr.Split(',').Select(a => a.Trim()).ToArray();

                    if (args.Length < 6) return m.Value;

                    bool shouldRandomize = false;
                    switch (targetArea)
                    {
                        case 0: shouldRandomize = true; break;
                        case 1: if (structName.Contains("red")) shouldRandomize = true; break;
                        case 2: if (structName.Contains("blue")) shouldRandomize = true; break;
                        case 3: if (structName.Contains("white")) shouldRandomize = true; break;
                        case 4: if (structName.Contains("brown")) shouldRandomize = true; break;
                        case 5: if (structName.Contains("skin")) shouldRandomize = true; break;
                        case 6: if (structName.Contains("yellow")) shouldRandomize = true; break;
                    }

                    if (!shouldRandomize) return m.Value;

                    modified = true;

                    int avR = 0, avG = 0, avB = 0;
                    int dvR = 0, dvG = 0, dvB = 0;

                    if (marioColorsMode == 1) // Invert Original Colors
                    {
                        avR = 255 - ParseIntHexSafe(args[0]);
                        avG = 255 - ParseIntHexSafe(args[1]);
                        avB = 255 - ParseIntHexSafe(args[2]);
                        dvR = 255 - ParseIntHexSafe(args[3]);
                        dvG = 255 - ParseIntHexSafe(args[4]);
                        dvB = 255 - ParseIntHexSafe(args[5]);
                    }
                    else if (marioColorsMode == 2) // Channel Swap (Hue Shift)
                    {
                        avR = ParseIntHexSafe(args[1]);
                        avG = ParseIntHexSafe(args[2]);
                        avB = ParseIntHexSafe(args[0]);
                        dvR = ParseIntHexSafe(args[4]);
                        dvG = ParseIntHexSafe(args[5]);
                        dvB = ParseIntHexSafe(args[3]);
                    }
                    else if (marioColorsMode == 3) // Psychedelic Neon Glow
                    {
                        dvR = _random.Next(0, 2) * 255;
                        dvG = _random.Next(0, 2) * 255;
                        dvB = _random.Next(0, 2) * 255;
                        if (dvR == 0 && dvG == 0 && dvB == 0) dvR = 255;

                        avR = dvG;
                        avG = dvB;
                        avB = dvR;
                    }
                    else // Full Shaded Random (Mode 0)
                    {
                        dvR = _random.Next(0, 256);
                        dvG = _random.Next(0, 256);
                        dvB = _random.Next(0, 256);

                        avR = dvR / 2;
                        avG = dvG / 2;
                        avB = dvB / 2;
                    }

                    string remaining = string.Join(", ", args.Skip(6));
                    if (!string.IsNullOrEmpty(remaining)) remaining = ", " + remaining;

                    return $"{m.Groups[1].Value}{m.Groups[2].Value} {m.Groups[3].Value} = gdSPDefLights1(\n    0x{avR:X2}, 0x{avG:X2}, 0x{avB:X2},\n    0x{dvR:X2}, 0x{dvG:X2}, 0x{dvB:X2}{remaining}\n);";
                });

                newContent = regexStruct.Replace(newContent, m =>
                {
                    string structName = m.Groups[3].Value.ToLower();
                    string structBody = m.Value;

                    bool shouldRandomize = false;
                    switch (targetArea)
                    {
                        case 0: shouldRandomize = true; break;
                        case 1: if (structName.Contains("red")) shouldRandomize = true; break;
                        case 2: if (structName.Contains("blue")) shouldRandomize = true; break;
                        case 3: if (structName.Contains("white")) shouldRandomize = true; break;
                        case 4: if (structName.Contains("brown")) shouldRandomize = true; break;
                        case 5: if (structName.Contains("skin")) shouldRandomize = true; break;
                        case 6: if (structName.Contains("yellow")) shouldRandomize = true; break;
                    }

                    if (!shouldRandomize) return m.Value;

                    modified = true;

                    byte rv = 0, gv = 0, bv = 0;
                    byte ambR = 0, ambG = 0, ambB = 0;

                    if (marioColorsMode == 1) // Invert Original Colors
                    {
                        var colorRegex = new Regex(@"\{\s*(0x[0-9a-fA-F]+|\d+)\s*,\s*(0x[0-9a-fA-F]+|\d+)\s*,\s*(0x[0-9a-fA-F]+|\d+)\s*\}");
                        var matches = colorRegex.Matches(structBody);
                        if (matches.Count >= 1)
                        {
                            int origR = ParseIntHexSafe(matches[0].Groups[1].Value);
                            int origG = ParseIntHexSafe(matches[0].Groups[2].Value);
                            int origB = ParseIntHexSafe(matches[0].Groups[3].Value);

                            rv = (byte)(255 - origR);
                            gv = (byte)(255 - origG);
                            bv = (byte)(255 - origB);

                            ambR = (byte)(rv / 4);
                            ambG = (byte)(gv / 4);
                            ambB = (byte)(bv / 4);
                        }
                        else return m.Value;
                    }
                    else if (marioColorsMode == 2) // Channel Swap (Hue Shift)
                    {
                        var colorRegex = new Regex(@"\{\s*(0x[0-9a-fA-F]+|\d+)\s*,\s*(0x[0-9a-fA-F]+|\d+)\s*,\s*(0x[0-9a-fA-F]+|\d+)\s*\}");
                        var matches = colorRegex.Matches(structBody);
                        if (matches.Count >= 1)
                        {
                            int origR = ParseIntHexSafe(matches[0].Groups[1].Value);
                            int origG = ParseIntHexSafe(matches[0].Groups[2].Value);
                            int origB = ParseIntHexSafe(matches[0].Groups[3].Value);

                            rv = (byte)origG;
                            gv = (byte)origB;
                            bv = (byte)origR;

                            ambR = (byte)(rv / 4);
                            ambG = (byte)(gv / 4);
                            ambB = (byte)(bv / 4);
                        }
                        else return m.Value;
                    }
                    else if (marioColorsMode == 3) // Psychedelic Neon Glow
                    {
                        rv = (byte)(_random.Next(0, 2) * 255);
                        gv = (byte)(_random.Next(0, 2) * 255);
                        bv = (byte)(_random.Next(0, 2) * 255);
                        if (rv == 0 && gv == 0 && bv == 0) rv = 255;

                        ambR = gv;
                        ambG = bv;
                        ambB = rv;
                    }
                    else // Full Shaded Random (Mode 0)
                    {
                        rv = (byte)_random.Next(0, 256);
                        gv = (byte)_random.Next(0, 256);
                        bv = (byte)_random.Next(0, 256);

                        ambR = (byte)(rv / 4);
                        ambG = (byte)(gv / 4);
                        ambB = (byte)(bv / 4);
                    }

                    var repColorRegex = new Regex(@"\{\s*(0x[0-9a-fA-F]+|\d+)\s*,\s*(0x[0-9a-fA-F]+|\d+)\s*,\s*(0x[0-9a-fA-F]+|\d+)\s*\}");
                    int matchCount = 0;
                    return repColorRegex.Replace(structBody, cm =>
                    {
                        matchCount++;
                        if (matchCount <= 2)
                        {
                            return $"{{ 0x{rv:X2}, 0x{gv:X2}, 0x{bv:X2} }}";
                        }
                        else
                        {
                            return $"{{ 0x{ambR:X2}, 0x{ambG:X2}, 0x{ambB:X2} }}";
                        }
                    });
                });

                if (modified)
                {
                    File.WriteAllText(filePath, newContent);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error corrupting Mario colors in {filePath}: {ex.Message}");
            }
        }

        private void OpenDlExclusionWindow_Click(object sender, RoutedEventArgs e)
        {
            var window = new DisplayListChaosWindow(_projectRoot)
            {
                Owner = this
            };
            window.ShowDialog();
        }
    }

    public class ChaosPreset
    {
        public string PresetName { get; set; } = string.Empty;
        public double Intensity { get; set; } = 10;
        public int TargetLevelIndex { get; set; } = 0;
        
        public bool TargetMusicNotes { get; set; } = true;
        public int MusicNotesMode { get; set; } = 0;
        public List<int> ActiveM64Modes { get; set; } = new() { 0, 1, 2, 3, 4, 5 };
        public bool M64All { get; set; } = true;
        public bool M64Select { get; set; } = false;

        public bool TargetSounds { get; set; } = true;
        public bool ShuffleSounds { get; set; } = false;
        public bool ExcludeInstrumentsShuffle { get; set; } = false;
        public bool ExcludeSfxShuffle { get; set; } = false;
        public bool ShuffleSfxOnly { get; set; } = false;
        public bool SfxIdentityShuffle { get; set; } = true;
        public bool SfxPitchVariation { get; set; } = false;
        public int SfxRandomizerMode { get; set; } = 0;

        public bool ReplaceSfx { get; set; } = false;

        public bool RandomizeDl { get; set; } = false;
        public int DlRandomizerMode { get; set; } = 0;
        public bool DlExclusion { get; set; } = false;

        public bool TargetModels { get; set; } = true;
        public bool TargetGoddard { get; set; } = true;
        public int GoddardMode { get; set; } = 0;
        public bool RandomizeMarioColors { get; set; } = false;
        public int MarioColorsArea { get; set; } = 0;
        public int MarioColorsMode { get; set; } = 0;

        public bool GlitchAnimations { get; set; } = false;
        public int AnimationGlitcherMode { get; set; } = 0;

        public bool GlitchHud { get; set; } = false;
        public int HudGlitcherMode { get; set; } = 0;

        public bool RandomizeTextures { get; set; } = false;
        public int TextureRandomizeMode { get; set; } = 0;
        public bool ReplaceTexturesCustom { get; set; } = false;
        public List<TextureReplacerWindow.TextureReplacementRule> ReplaceTexturesRules { get; set; } = new();
        public bool TextureRandomizeSelectedOnly { get; set; } = false;
        public List<string> TextureRandomizeSelectedPaths { get; set; } = new();

        public int ModeIndex { get; set; } = 0;
        public bool RandomizeSkybox { get; set; } = false;
        public bool RandomizeText { get; set; } = false;
        public int TextRandomizeMode { get; set; } = 0;

        public bool ChaosLogicJumpWeird { get; set; } = false;
        public bool ChaosLogicJumpDeath { get; set; } = false;
        public bool LimboMario { get; set; } = false;
        public int LimboMarioMode { get; set; } = 0;
        public bool AlienSoundCheat { get; set; } = false;

        public bool ScrambleTitleScreen { get; set; } = false;
        public int TitleScreenScramblerMode { get; set; } = 0;
        public bool RandomizeCutsceneCamera { get; set; } = false;
        public int CutsceneCameraMode { get; set; } = 0;
        public bool LakituCameraChaos { get; set; } = false;
        public int LakituCameraMode { get; set; } = 0;
        public bool StartLevelChaos { get; set; } = false;
        public int StartLevelIndex { get; set; } = 0;
    }

    public static class InputBox
    {
        public static string Show(string prompt, string title, string defaultValue = "")
        {
            Window window = new Window
            {
                Title = title,
                Width = 350,
                Height = 150,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 30, 30)),
                Foreground = System.Windows.Media.Brushes.White,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false
            };

            StackPanel sp = new StackPanel { Margin = new Thickness(15) };
            TextBlock tb = new TextBlock { Text = prompt, Margin = new Thickness(0, 0, 0, 10), Foreground = System.Windows.Media.Brushes.White };
            TextBox txt = new TextBox { Text = defaultValue, Height = 26, Margin = new Thickness(0, 0, 0, 15), Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(50, 50, 50)), Foreground = System.Windows.Media.Brushes.White, BorderBrush = System.Windows.Media.Brushes.Gray, Padding = new Thickness(3) };
            
            StackPanel btnSp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            Button btnOk = new Button { Content = "OK", Width = 80, Height = 28, IsDefault = true, Margin = new Thickness(0, 0, 10, 0), Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 122, 204)), Foreground = System.Windows.Media.Brushes.White, BorderThickness = new Thickness(0) };
            Button btnCancel = new Button { Content = "Cancel", Width = 80, Height = 28, IsCancel = true, Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(60, 60, 60)), Foreground = System.Windows.Media.Brushes.White, BorderThickness = new Thickness(0) };

            btnSp.Children.Add(btnOk);
            btnSp.Children.Add(btnCancel);

            sp.Children.Add(tb);
            sp.Children.Add(txt);
            sp.Children.Add(btnSp);

            window.Content = sp;

            string result = "";
            btnOk.Click += (s, e) => { result = txt.Text.Trim(); window.DialogResult = true; window.Close(); };
            btnCancel.Click += (s, e) => { window.DialogResult = false; window.Close(); };

            if (window.ShowDialog() == true)
            {
                return result;
            }
            return "";
        }
    }
}
