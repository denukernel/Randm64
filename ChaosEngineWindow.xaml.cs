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
            int m64Mode = MusicNotesModeComboBox.SelectedIndex;
            bool randomizeTextures = RandomizeTexturesCheck.IsChecked == true;
            int textureMode = TextureRandomizeModeComboBox.SelectedIndex;
            
            bool glitchAnimations = GlitchAnimationsCheck.IsChecked == true;
            int animMode = AnimationGlitcherModeComboBox.SelectedIndex;
            bool glitchHud = GlitchHudCheck.IsChecked == true;
            int hudMode = HudGlitcherModeComboBox.SelectedIndex;

            bool replaceSfx = ReplaceSfxCheck.IsChecked == true;

            bool jumpWeird = ChaosLogicJumpWeird.IsChecked == true;
            bool jumpDeath = ChaosLogicJumpDeath.IsChecked == true;
            bool limboMario = LimboMarioCheck.IsChecked == true;
            bool alienSound = AlienSoundCheatCheck.IsChecked == true;

            string selectedLevel = "All Levels";
            Dispatcher.Invoke(() =>
            {
                selectedLevel = (LevelSelectionComboBox.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? "All Levels";
            });

            if (!targetMusic && !targetSounds && !randomizeDl && !targetModels && !targetGoddard && !shuffleSounds &&
                !randomizeSkybox && !randomizeText && !jumpWeird && !jumpDeath && !limboMario && !alienSound && !randomizeTextures &&
                !glitchAnimations && !glitchHud && !replaceSfx && !randomizeMarioColors)
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

                                // Copy shuffled backups to active slots
                                for (int i = 0; i < aiffFiles.Length; i++)
                                {
                                    string sourceBackup = shuffledFiles[i] + ".bak";
                                    string targetFile = aiffFiles[i];
                                    File.Copy(sourceBackup, targetFile, true);
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

                    // Perform C source file patches based on selected logic cheats
                    int skyboxSeed = _random.Next(0, 9);
                    
                    // Create src/game/chaos_config.h
                    try
                    {
                        string configDir = Path.Combine(_projectRoot, "src", "game");
                        if (Directory.Exists(configDir))
                        {
                            string configPath = Path.Combine(configDir, "chaos_config.h");
                            StringBuilder configContent = new StringBuilder();
                            configContent.AppendLine("#ifndef CHAOS_CONFIG_H");
                            configContent.AppendLine("#define CHAOS_CONFIG_H");
                            configContent.AppendLine();
                            configContent.AppendLine("// Auto-generated by Chaos Engine");
                            
                            if (randomizeSkybox)
                            {
                                configContent.AppendLine("#define CHAOS_RANDOM_SKYBOX");
                                configContent.AppendLine($"#define CHAOS_SKYBOX_SEED {skyboxSeed}");
                            }
                            if (jumpWeird)
                            {
                                configContent.AppendLine("#define CHAOS_JUMP_WEIRD");
                            }
                            if (jumpDeath)
                            {
                                configContent.AppendLine("#define CHAOS_JUMP_DEATH");
                            }
                            if (limboMario)
                            {
                                configContent.AppendLine("#define CHAOS_LIMBO_MARIO");
                            }
                            if (alienSound)
                            {
                                configContent.AppendLine("#define CHAOS_ALIEN_SOUND");
                            }
                            
                            configContent.AppendLine();
                            configContent.AppendLine("#endif");
                            File.WriteAllText(configPath, configContent.ToString().Replace("\r\n", "\n"));
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
                        PatchSourceFile("src/game/mario_actions_airborne.c", "#include <PR/ultratypes.h>", "#include <PR/ultratypes.h>\n#include \"game/chaos_config.h\"");
                    }
                    
                    if (limboMario)
                    {
                        string oldLimbo = "        rotNode->rotation[0] = bodyState->torsoAngle[1];\n        rotNode->rotation[1] = bodyState->torsoAngle[2];\n        rotNode->rotation[2] = bodyState->torsoAngle[0];";
                        string newLimbo = "        rotNode->rotation[0] = bodyState->torsoAngle[1];\n        rotNode->rotation[1] = bodyState->torsoAngle[2];\n        rotNode->rotation[2] = bodyState->torsoAngle[0];\n#ifdef CHAOS_LIMBO_MARIO\n        rotNode->rotation[0] += 0x3800;\n#endif";
                        PatchSourceFile("src/game/mario_misc.c", oldLimbo, newLimbo);
                        PatchSourceFile("src/game/mario_misc.c", "#include <PR/ultratypes.h>", "#include <PR/ultratypes.h>\n#include \"game/chaos_config.h\"");
                    }
                    
                    if (alienSound)
                    {
                        string oldAlienLoop = "        {\n            u8 *_ptr_pc;\n            _ptr_pc = (*state).pc++;\n            cmd = *_ptr_pc;\n        }";
                        string newAlienLoop = "        {\n            u8 *_ptr_pc;\n            _ptr_pc = (*state).pc++;\n            cmd = *_ptr_pc;\n#ifdef CHAOS_ALIEN_SOUND\n            if (cmd > 0xc0) {\n                cmd = (cmd + 13) % 256;\n            }\n#endif\n        }";
                        PatchSourceFile("src/audio/copt/seq_channel_layer_process_script_copt.inc.c", oldAlienLoop, newAlienLoop);
                        PatchSourceFile("src/audio/seqplayer.c", "#include \"seqplayer.h\"", "#include \"seqplayer.h\"\n#include \"game/chaos_config.h\"");
                        string parentFile = Path.Combine(_projectRoot, "src", "audio", "seqplayer.c");
                        if (File.Exists(parentFile))
                        {
                            File.SetLastWriteTime(parentFile, DateTime.Now);
                        }
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

                    // 3. Display Lists / Models (.c, .inc.c)
                    if (randomizeDl || targetModels)
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
                                }
                            }
                            else
                            {
                                filesToCorrupt.AddRange(Directory.GetFiles(levelsDir, "*.c", SearchOption.AllDirectories));
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
                                }
                            }
                            else
                            {
                                filesToCorrupt.AddRange(Directory.GetFiles(editorLevelsDir, "*.c", SearchOption.AllDirectories));
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
                            // Create backup before modifying if it doesn't already exist
                            string backupPath = file + ".bak";
                            if (!File.Exists(backupPath))
                            {
                                File.Copy(file, backupPath);
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
                                CorruptTexturePixels(file, intensity, textureMode);
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

        private void CorruptM64SequenceFile(string filePath, double intensity, int m64Mode)
        {
            var service = new Services.M64Service();
            var tracks = service.LoadM64(filePath);
            if (tracks == null || tracks.Count == 0)
            {
                // Fall back to raw binary corruption if structured midi parsing fails
                CorruptBinaryFile(filePath, intensity, 0);
                return;
            }

            switch (m64Mode)
            {
                case 0: // 1. Transpose (Pitch Shift)
                    int pitchOffset = _random.Next(-6, 7); // Transpose up/down up to 6 semitones
                    foreach (var track in tracks)
                    {
                        foreach (var note in track.Notes)
                        {
                            if (_random.NextDouble() < (intensity / 100.0))
                            {
                                int newPitch = note.Pitch + pitchOffset;
                                note.Pitch = (byte)Math.Clamp(newPitch, 0, 127);
                            }
                        }
                    }
                    break;

                case 1: // 2. Tempo Shift (BPM Warp)
                    int tempoOffset = _random.Next(-30, 31);
                    int newTempo = service.Tempo + tempoOffset;
                    service.Tempo = (byte)Math.Clamp(newTempo, 30, 240);
                    break;

                case 2: // 3. Instrument Swap (Orchestration)
                    foreach (var track in tracks)
                    {
                        if (_random.NextDouble() < (intensity / 100.0))
                        {
                            byte newInst = (byte)_random.Next(0, 32);
                            track.Instrument = newInst;
                            foreach (var note in track.Notes)
                            {
                                note.Instrument = newInst;
                            }
                        }
                    }
                    break;

                case 3: // 4. Duration Glitcher
                    foreach (var track in tracks)
                    {
                        foreach (var note in track.Notes)
                        {
                            if (_random.NextDouble() < (intensity / 100.0))
                            {
                                note.DurationTicks = _random.NextDouble() < 0.5 ? _random.Next(10, 50) : _random.Next(500, 2000);
                            }
                        }
                    }
                    break;

                case 4: // 5. Velocity / Volume Randomizer
                    foreach (var track in tracks)
                    {
                        if (_random.NextDouble() < (intensity / 100.0))
                        {
                            track.Volume = (byte)_random.Next(10, 128);
                        }
                        foreach (var note in track.Notes)
                        {
                            if (_random.NextDouble() < (intensity / 100.0))
                            {
                                note.Velocity = (byte)_random.Next(10, 128);
                            }
                        }
                    }
                    break;

                case 5: // 6. Melody Scrambler (Shuffle pitches)
                    foreach (var track in tracks)
                    {
                        if (track.Notes.Count > 1)
                        {
                            int shuffleCount = (int)(track.Notes.Count * (intensity / 100.0) * 0.1);
                            if (shuffleCount <= 0) shuffleCount = 1;
                            for (int s = 0; s < shuffleCount; s++)
                            {
                                int idx1 = _random.Next(0, track.Notes.Count);
                                int idx2 = _random.Next(0, track.Notes.Count);
                                byte tempPitch = track.Notes[idx1].Pitch;
                                track.Notes[idx1].Pitch = track.Notes[idx2].Pitch;
                                track.Notes[idx2].Pitch = tempPitch;
                            }
                        }
                    }
                    break;
            }

            service.SaveM64(filePath, tracks);
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
                        chars[idx] = uppercase ? char.ToUpper(chars[idx]) : char.ToLower(chars[idx]);
                        uppercase = !uppercase;
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
                var regex = new Regex(@"\{\{\{\s*(-?\d+),\s*(-?\d+),\s*(-?\d+)\s*\},\s*(\d+|0x[0-9a-fA-F]+),\s*\{\s*(-?\d+),\s*(-?\d+)\s*\},\s*\{\s*(\d+|0x[0-9a-fA-F]+|-?\d+),\s*(\d+|0x[0-9a-fA-F]+|-?\d+),\s*(\d+|0x[0-9a-fA-F]+|-?\d+),\s*(\d+|0x[0-9a-fA-F]+|-?\d+)\s*\}\s*\}\}\}");

                string newContent = regex.Replace(content, m =>
                {
                    if (_random.NextDouble() >= rate * 0.5) return m.Value; // scale rate

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
                            u = (int)(u * (1.5 + _random.NextDouble() * 3.0));
                            v = (int)(v * (1.5 + _random.NextDouble() * 3.0));
                            break;

                        case 4: // Polygon Exploder
                            x += _random.Next(-400, 401);
                            y += _random.Next(-400, 401);
                            z += _random.Next(-400, 401);
                            break;

                        case 5: // Voxelizer Snap
                            x = (x / 200) * 200;
                            y = (y / 200) * 200;
                            z = (z / 200) * 200;
                            break;

                        case 6: // UV Swap
                            int tempU = u;
                            u = v;
                            v = tempU;
                            break;

                        case 7: // Scale Distortion
                            y = (int)(y * 0.15);
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

                File.WriteAllText(filePath, newContent);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error corrupting DL file {filePath}: {ex.Message}");
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

                string newContent = regex.Replace(content, m =>
                {
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
                            z = (int)(z * 0.1);
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
                            x = (x / 60) * 60;
                            y = (y / 60) * 60;
                            z = (z / 60) * 60;
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
                            x = 0;
                            y = 0;
                            z = 0;
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
                        PresetComboBox.Items.Add(Path.GetFileNameWithoutExtension(file));
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
                MusicNotesModeComboBox.SelectedIndex = 0;
                M64AllRadio.IsChecked = true;
                TargetSoundsCheck.IsChecked = true;
                ShuffleSoundsCheck.IsChecked = false;
                ExcludeInstrumentsShuffleCheck.IsChecked = false;
                ExcludeSfxShuffleCheck.IsChecked = false;
                ShuffleSfxOnlyCheck.IsChecked = false;
                ReplaceSfxCheck.IsChecked = false;
                RandomizeDlCheck.IsChecked = false;
                DlRandomizerModeComboBox.SelectedIndex = 0;
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
                MusicNotesModeComboBox.SelectedIndex = preset.MusicNotesMode;
                M64AllRadio.IsChecked = preset.M64All;
                M64SelectRadio.IsChecked = preset.M64Select;

                TargetSoundsCheck.IsChecked = preset.TargetSounds;
                ShuffleSoundsCheck.IsChecked = preset.ShuffleSounds;
                ExcludeInstrumentsShuffleCheck.IsChecked = preset.ExcludeInstrumentsShuffle;
                ExcludeSfxShuffleCheck.IsChecked = preset.ExcludeSfxShuffle;
                ShuffleSfxOnlyCheck.IsChecked = preset.ShuffleSfxOnly;

                ReplaceSfxCheck.IsChecked = preset.ReplaceSfx;

                RandomizeDlCheck.IsChecked = preset.RandomizeDl;
                DlRandomizerModeComboBox.SelectedIndex = preset.DlRandomizerMode;

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
                AlienSoundCheatCheck.IsChecked = preset.AlienSoundCheat;
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
                TargetGoddardCheck_Toggle(null, null);
                GlitchAnimationsCheck_Toggle(null, null);
                GlitchHudCheck_Toggle(null, null);
                RandomizeTexturesCheck_Toggle(null, null);
                TextureTarget_Changed(null, null);
                RandomizeTextCheck_Toggle(null, null);
                RandomizeMarioColorsCheck_Toggle(null, null);
                M64Radio_Checked(null, null);
            }
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

            var preset = new ChaosPreset
            {
                PresetName = name,
                Intensity = IntensifySlider.Value,
                TargetLevelIndex = LevelSelectionComboBox.SelectedIndex,

                TargetMusicNotes = TargetMusicNotesCheck.IsChecked == true,
                MusicNotesMode = MusicNotesModeComboBox.SelectedIndex,
                M64All = M64AllRadio.IsChecked == true,
                M64Select = M64SelectRadio.IsChecked == true,

                TargetSounds = TargetSoundsCheck.IsChecked == true,
                ShuffleSounds = ShuffleSoundsCheck.IsChecked == true,
                ExcludeInstrumentsShuffle = ExcludeInstrumentsShuffleCheck.IsChecked == true,
                ExcludeSfxShuffle = ExcludeSfxShuffleCheck.IsChecked == true,
                ShuffleSfxOnly = ShuffleSfxOnlyCheck.IsChecked == true,

                ReplaceSfx = ReplaceSfxCheck.IsChecked == true,

                RandomizeDl = RandomizeDlCheck.IsChecked == true,
                DlRandomizerMode = DlRandomizerModeComboBox.SelectedIndex,

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
                TextureRandomizeSelectedOnly = TextureRandomizeSelectedRadio.IsChecked == true,
                TextureRandomizeSelectedPaths = _selectedTextures.ToList(),

                ModeIndex = ModeComboBox.SelectedIndex,
                RandomizeSkybox = RandomizeSkyboxCheck.IsChecked == true,
                RandomizeText = RandomizeTextCheck.IsChecked == true,
                TextRandomizeMode = TextRandomizeModeComboBox.SelectedIndex,

                ChaosLogicJumpWeird = ChaosLogicJumpWeird.IsChecked == true,
                ChaosLogicJumpDeath = ChaosLogicJumpDeath.IsChecked == true,
                LimboMario = LimboMarioCheck.IsChecked == true,
                AlienSoundCheat = AlienSoundCheatCheck.IsChecked == true
            };

            try
            {
                string json = System.Text.Json.JsonSerializer.Serialize(preset, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                string filePath = Path.Combine(_presetsDir, name + ".json");
                File.WriteAllText(filePath, json);

                LoadPresetsList(name);
                MessageBox.Show($"Preset '{name}' saved successfully!", "Preset Saved", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save preset: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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
    }

    public class ChaosPreset
    {
        public string PresetName { get; set; } = string.Empty;
        public double Intensity { get; set; } = 10;
        public int TargetLevelIndex { get; set; } = 0;
        
        public bool TargetMusicNotes { get; set; } = true;
        public int MusicNotesMode { get; set; } = 0;
        public bool M64All { get; set; } = true;
        public bool M64Select { get; set; } = false;

        public bool TargetSounds { get; set; } = true;
        public bool ShuffleSounds { get; set; } = false;
        public bool ExcludeInstrumentsShuffle { get; set; } = false;
        public bool ExcludeSfxShuffle { get; set; } = false;
        public bool ShuffleSfxOnly { get; set; } = false;

        public bool ReplaceSfx { get; set; } = false;

        public bool RandomizeDl { get; set; } = false;
        public int DlRandomizerMode { get; set; } = 0;

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
        public bool TextureRandomizeSelectedOnly { get; set; } = false;
        public List<string> TextureRandomizeSelectedPaths { get; set; } = new();

        public int ModeIndex { get; set; } = 0;
        public bool RandomizeSkybox { get; set; } = false;
        public bool RandomizeText { get; set; } = false;
        public int TextRandomizeMode { get; set; } = 0;

        public bool ChaosLogicJumpWeird { get; set; } = false;
        public bool ChaosLogicJumpDeath { get; set; } = false;
        public bool LimboMario { get; set; } = false;
        public bool AlienSoundCheat { get; set; } = false;
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
