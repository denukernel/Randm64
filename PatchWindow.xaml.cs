using System;
using System.IO;
using System.Linq;
using System.Windows;
using Microsoft.Win32;

namespace Sm64DecompLevelViewer
{
    public partial class PatchWindow : Window
    {
        public class PatchFileItem
        {
            public string Name { get; set; } = string.Empty;
            public string FullPath { get; set; } = string.Empty;

            public override string ToString() => Name;
        }

        private readonly string _projectRoot;

        public PatchWindow(string projectRoot)
        {
            InitializeComponent();
            _projectRoot = projectRoot;

            ScanEnhancementsFolder();
        }

        private void ScanEnhancementsFolder()
        {
            AvailablePatchesList.Items.Clear();
            string enhancementsPath = Path.Combine(_projectRoot, "enhancements");
            
            if (Directory.Exists(enhancementsPath))
            {
                try
                {
                    var files = Directory.GetFiles(enhancementsPath, "*.*")
                        .Where(file => file.EndsWith(".patch", StringComparison.OrdinalIgnoreCase) || 
                                       file.EndsWith(".diff", StringComparison.OrdinalIgnoreCase))
                        .OrderBy(f => Path.GetFileName(f));

                    foreach (var file in files)
                    {
                        AvailablePatchesList.Items.Add(new PatchFileItem
                        {
                            Name = Path.GetFileName(file),
                            FullPath = file
                        });
                    }

                    LogOutputText.AppendText($"Scanned enhancements folder: found {AvailablePatchesList.Items.Count} patches.\n\n");
                }
                catch (Exception ex)
                {
                    LogOutputText.AppendText($"Error scanning enhancements: {ex.Message}\n\n");
                }
            }
            else
            {
                LogOutputText.AppendText($"Enhancements folder not found at: {enhancementsPath}\n\n");
            }
        }

        private void RescanButton_Click(object sender, RoutedEventArgs e)
        {
            ScanEnhancementsFolder();
        }

        private void AvailablePatchesList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (AvailablePatchesList.SelectedItem is PatchFileItem selected)
            {
                PatchPathText.Text = selected.FullPath;
            }
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "Patch Files (*.patch;*.diff;*.txt)|*.patch;*.diff;*.txt|All Files (*.*)|*.*",
                Title = "Select Decomp Patch File"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                PatchPathText.Text = openFileDialog.FileName;
                AvailablePatchesList.SelectedItem = null; // Clear list selection since a custom file was chosen
            }
        }

        private string ConvertToWslPath(string windowsPath)
        {
            if (string.IsNullOrEmpty(windowsPath)) return string.Empty;

            string path = windowsPath.Replace("\\", "/");

            // Handle UNC paths pointing to WSL, e.g. //wsl.localhost/Ubuntu-20.04/root/sm64/... -> /root/sm64/...
            // or //wsl$/Ubuntu-20.04/root/sm64/... -> /root/sm64/...
            if (path.StartsWith("//wsl.localhost/", StringComparison.OrdinalIgnoreCase))
            {
                int nextSlash = path.IndexOf('/', 16); // Find slash after wsl.localhost/distro_name
                if (nextSlash != -1)
                {
                    return path.Substring(nextSlash);
                }
            }
            else if (path.StartsWith("//wsl$/", StringComparison.OrdinalIgnoreCase))
            {
                int nextSlash = path.IndexOf('/', 7); // Find slash after wsl$/distro_name
                if (nextSlash != -1)
                {
                    return path.Substring(nextSlash);
                }
            }

            // Standard Windows drive paths (e.g. C:/...)
            if (path.Length >= 2 && path[1] == ':')
            {
                char drive = char.ToLower(path[0]);
                path = $"/mnt/{drive}{path.Substring(2)}";
            }
            return path;
        }

        private void ApplyGit_Click(object sender, RoutedEventArgs e)
        {
            string patchFile = PatchPathText.Text.Trim();
            if (string.IsNullOrEmpty(patchFile) || !File.Exists(patchFile))
            {
                MessageBox.Show("Please select a valid patch file first.", "Invalid File", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string wslPath = ConvertToWslPath(patchFile);
            string command = $"git -C /root/sm64 apply --ignore-whitespace --verbose \"{wslPath}\"";
            RunWslCommand(command);
        }

        private void ApplyPatchCmd_Click(object sender, RoutedEventArgs e)
        {
            string patchFile = PatchPathText.Text.Trim();
            if (string.IsNullOrEmpty(patchFile) || !File.Exists(patchFile))
            {
                MessageBox.Show("Please select a valid patch file first.", "Invalid File", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string wslPath = ConvertToWslPath(patchFile);
            string command = $"patch -p1 -d /root/sm64 -i \"{wslPath}\"";
            RunWslCommand(command);
        }

        private void RunWslCommand(string command)
        {
            LogOutputText.Clear();
            LogOutputText.AppendText($"> Executing: {command}\n\n");

            try
            {
                var processInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "wsl",
                    Arguments = $"sh -c \"{command.Replace("\"", "\\\"")}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var process = System.Diagnostics.Process.Start(processInfo))
                {
                    if (process == null)
                    {
                        LogOutputText.AppendText("Error: Failed to start WSL process.\n");
                        return;
                    }

                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();
                    process.WaitForExit();

                    if (!string.IsNullOrEmpty(output))
                    {
                        LogOutputText.AppendText(output + "\n");
                    }
                    if (!string.IsNullOrEmpty(error))
                    {
                        LogOutputText.AppendText("Stderr:\n" + error + "\n");
                    }

                    if (process.ExitCode == 0)
                    {
                        LogOutputText.AppendText("\n🎉 Success! The patch was applied successfully.\n");
                        MessageBox.Show("Patch applied successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        LogOutputText.AppendText($"\n❌ Failed with exit code {process.ExitCode}.\n");
                        MessageBox.Show("Failed to apply patch. Check console output logs for errors.", "Patch Failure", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                LogOutputText.AppendText($"Exception occurred: {ex.Message}\n");
            }
        }

        private void ApplyCPatches_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                LogOutputText.Text = "";
                LogOutputText.AppendText("Applying Randm64 C Patches...\n");

                bool skipGoddard = SkipGoddardCheck.IsChecked == true;
                bool skipTitle = SkipTitleCheck.IsChecked == true;
                bool skipLakitu = SkipLakituCheck.IsChecked == true;
                bool start99Lives = Start99LivesCheck.IsChecked == true;
                bool stageSelect = StageSelectCheck.IsChecked == true;
                bool audioProtection = AudioProtectionCheck.IsChecked == true;
                bool supportNewChannels = SupportNewChannelsCheck.IsChecked == true;

                if (!skipGoddard && !skipTitle && !skipLakitu && !start99Lives && !stageSelect && !audioProtection && !supportNewChannels)
                {
                    MessageBox.Show("Please check at least one patch to apply.", "No Patches Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // 1. Skip Goddard face (modify levels/intro/script.c)
                if (skipGoddard)
                {
                    string introScript = Path.Combine(_projectRoot, "levels", "intro", "script.c");
                    if (File.Exists(introScript))
                    {
                        string content = File.ReadAllText(introScript);
                        string backupPath = introScript + ".bak";
                        if (!File.Exists(backupPath))
                        {
                            File.Copy(introScript, backupPath);
                        }

                        string targetCall = "EXIT_AND_EXECUTE(/*seg*/ 0x14, _introSegmentRomStart, _introSegmentRomEnd, level_intro_mario_head_regular)";
                        if (content.Contains(targetCall))
                        {
                            content = content.Replace(targetCall, "EXIT_AND_EXECUTE(/*seg*/ 0x14, _menuSegmentRomStart, _menuSegmentRomEnd, level_main_menu_entry_1)");
                            File.WriteAllText(introScript, content);
                            LogOutputText.AppendText("- Bypassed Goddard face screen (transitions straight to Save Select).\n");
                        }
                        else
                        {
                            var callRegex = new System.Text.RegularExpressions.Regex(@"EXIT_AND_EXECUTE\s*\(\s*(/\*seg\*/\s*)?0x14\s*,\s*_introSegmentRomStart\s*,\s*_introSegmentRomEnd\s*,\s*level_intro_mario_head_regular\s*\)");
                            if (callRegex.IsMatch(content))
                            {
                                content = callRegex.Replace(content, "EXIT_AND_EXECUTE(/*seg*/ 0x14, _menuSegmentRomStart, _menuSegmentRomEnd, level_main_menu_entry_1)");
                                File.WriteAllText(introScript, content);
                                LogOutputText.AppendText("- Bypassed Goddard face screen (transitions straight to Save Select).\n");
                            }
                            else
                            {
                                LogOutputText.AppendText("Warning: Goddard head transition call not found in levels/intro/script.c.\n");
                            }
                        }
                    }
                    else
                    {
                        LogOutputText.AppendText("Warning: levels/intro/script.c not found. Skipping Goddard bypass.\n");
                    }
                }

                // 1b. Skip Title Screen (modify levels/entry.c or levels/entry/script.c)
                if (skipTitle)
                {
                    string entryFile = Path.Combine(_projectRoot, "levels", "entry.c");
                    if (!File.Exists(entryFile))
                    {
                        entryFile = Path.Combine(_projectRoot, "levels", "entry", "script.c");
                    }

                    if (File.Exists(entryFile))
                    {
                        string content = File.ReadAllText(entryFile);
                        string backupPath = entryFile + ".bak";
                        if (!File.Exists(backupPath))
                        {
                            File.Copy(entryFile, backupPath);
                        }

                        if (content.Contains("level_intro_splash_screen"))
                        {
                            // Add menu header if not present
                            if (!content.Contains("levels/menu/header.h"))
                            {
                                content = "#include \"levels/menu/header.h\"\n" + content;
                            }

                            content = content.Replace(
                                "EXECUTE(/*seg*/ 0x14, /*script*/ _introSegmentRomStart, /*scriptEnd*/ _introSegmentRomEnd, /*entry*/ level_intro_splash_screen)",
                                "EXECUTE(/*seg*/ 0x14, /*script*/ _menuSegmentRomStart, /*scriptEnd*/ _menuSegmentRomEnd, /*entry*/ level_main_menu_entry_1)"
                            );

                            // Fallback replaces
                            content = content.Replace("level_intro_splash_screen", "level_main_menu_entry_1");
                            content = content.Replace("_introSegment", "_menuSegment");

                            File.WriteAllText(entryFile, content);
                            LogOutputText.AppendText("- Bypassed Title Screen (boots straight to Save Select).\n");
                        }
                        else
                        {
                            LogOutputText.AppendText("Warning: level_intro_splash_screen not found in entry script.\n");
                        }
                    }
                    else
                    {
                        LogOutputText.AppendText("Warning: entry script file not found. Skipping Title Screen bypass.\n");
                    }
                }

                // 1c. Enable Stage Select (modify src/game/main.c)
                if (stageSelect)
                {
                    string mainFile = Path.Combine(_projectRoot, "src", "game", "main.c");
                    if (File.Exists(mainFile))
                    {
                        string content = File.ReadAllText(mainFile);
                        string backupPath = mainFile + ".bak";
                        if (!File.Exists(backupPath))
                        {
                            File.Copy(mainFile, backupPath);
                        }

                        if (content.Contains("gDebugLevelSelect = FALSE;"))
                        {
                            content = content.Replace("gDebugLevelSelect = FALSE;", "gDebugLevelSelect = TRUE;");
                            File.WriteAllText(mainFile, content);
                            LogOutputText.AppendText("- Enabled Developer Stage Select Menu.\n");
                        }
                        else if (content.Contains("gDebugLevelSelect = TRUE;"))
                        {
                            LogOutputText.AppendText("- Developer Stage Select Menu already enabled.\n");
                        }
                        else
                        {
                            LogOutputText.AppendText("Warning: gDebugLevelSelect definition not found in main.c.\n");
                        }
                    }
                    else
                    {
                        LogOutputText.AppendText("Warning: src/game/main.c not found. Skipping Stage Select patch.\n");
                    }
                }

                // 2. Skip Lakitu Intro Cutscene
                if (skipLakitu)
                {
                    string cutsceneFile = Path.Combine(_projectRoot, "src", "game", "mario_actions_cutscene.c");
                    if (File.Exists(cutsceneFile))
                    {
                        string content = File.ReadAllText(cutsceneFile);
                        string backupPath = cutsceneFile + ".bak";
                        if (!File.Exists(backupPath))
                        {
                            File.Copy(cutsceneFile, backupPath);
                        }

                        int startIndex = content.IndexOf("s32 act_intro_cutscene(");
                        if (startIndex == -1) startIndex = content.IndexOf("s32 act_intro_cutscene (");

                        if (startIndex != -1)
                        {
                            int openBraceIndex = content.IndexOf('{', startIndex);
                            if (openBraceIndex != -1)
                            {
                                int braceCount = 1;
                                int closeBraceIndex = -1;
                                for (int i = openBraceIndex + 1; i < content.Length; i++)
                                {
                                    if (content[i] == '{') braceCount++;
                                    else if (content[i] == '}')
                                    {
                                        braceCount--;
                                        if (braceCount == 0)
                                        {
                                            closeBraceIndex = i;
                                            break;
                                        }
                                    }
                                }

                                if (closeBraceIndex != -1)
                                {
                                    string before = content.Substring(0, startIndex);
                                    string after = content.Substring(closeBraceIndex + 1);
                                    content = before + "s32 act_intro_cutscene(struct MarioState *m) {\n    return set_mario_action(m, ACT_IDLE, 0);\n}" + after;
                                    File.WriteAllText(cutsceneFile, content);
                                    LogOutputText.AppendText("- Bypassed Lakitu intro pipe cutscene (Mario starts in idle stance).\n");
                                }
                                else
                                {
                                    LogOutputText.AppendText("Warning: act_intro_cutscene closing brace not found. Skipping Lakitu bypass.\n");
                                }
                            }
                            else
                            {
                                LogOutputText.AppendText("Warning: act_intro_cutscene opening brace not found. Skipping Lakitu bypass.\n");
                            }
                        }
                        else
                        {
                            LogOutputText.AppendText("Warning: act_intro_cutscene signature not found. Skipping Lakitu bypass.\n");
                        }
                    }
                    else
                    {
                        LogOutputText.AppendText("Warning: mario_actions_cutscene.c not found. Skipping Lakitu bypass.\n");
                    }
                }

                // 3. Start with 99 Lives
                if (start99Lives)
                {
                    string marioFile = Path.Combine(_projectRoot, "src", "game", "mario.c");
                    if (File.Exists(marioFile))
                    {
                        string content = File.ReadAllText(marioFile);
                        string backupPath = marioFile + ".bak";
                        if (!File.Exists(backupPath))
                        {
                            File.Copy(marioFile, backupPath);
                        }

                        if (content.Contains("m->numLives = 4;"))
                        {
                            content = content.Replace("m->numLives = 4;", "m->numLives = 99;");
                            File.WriteAllText(marioFile, content);
                            LogOutputText.AppendText("- Set starting life count to 99 lives.\n");
                        }
                        else if (content.Contains("m->numLives = 99;"))
                        {
                            LogOutputText.AppendText("- Starting life count already set to 99 lives.\n");
                        }
                        else
                        {
                            var lifeRegex = new System.Text.RegularExpressions.Regex(@"m\s*->\s*numLives\s*=\s*\d+\s*;");
                            if (lifeRegex.IsMatch(content))
                            {
                                content = lifeRegex.Replace(content, "m->numLives = 99;");
                                File.WriteAllText(marioFile, content);
                                LogOutputText.AppendText("- Set starting life count to 99 lives (regex fallback).\n");
                            }
                            else
                            {
                                LogOutputText.AppendText("Warning: Mario numLives assignment not found. Skipping starting lives patch.\n");
                            }
                        }
                    }
                    else
                    {
                        LogOutputText.AppendText("Warning: mario.c not found. Skipping starting lives patch.\n");
                    }
                }

                // 4. Audio Protection Guard
                if (audioProtection)
                {
                    string playbackFile = Path.Combine(_projectRoot, "src", "audio", "playback.c");
                    if (File.Exists(playbackFile))
                    {
                        string backupPath = playbackFile + ".bak";
                        if (File.Exists(backupPath))
                        {
                            File.Copy(backupPath, playbackFile, true);
                        }
                        else
                        {
                            File.Copy(playbackFile, backupPath);
                        }
                        string content = File.ReadAllText(playbackFile);

                        // 4.1. Patch get_instrument
                        string targetGetInst = "struct Instrument *get_instrument(s32 bankId, s32 instId) {\r\n    struct Instrument *inst;";
                        string targetGetInstLF = "struct Instrument *get_instrument(s32 bankId, s32 instId) {\n    struct Instrument *inst;";
                        string replacementGetInst = "struct Instrument *get_instrument(s32 bankId, s32 instId) {\n    struct Instrument *inst;\n    if (gCtlEntries == NULL || gAlCtlHeader == NULL || bankId < 0 || bankId >= gAlCtlHeader->seqCount) {\n        return NULL;\n    }";

                        if (content.Contains(targetGetInst))
                        {
                            content = content.Replace(targetGetInst, replacementGetInst);
                        }
                        else if (content.Contains(targetGetInstLF))
                        {
                            content = content.Replace(targetGetInstLF, replacementGetInst);
                        }

                        // 4.2. Patch get_instrument_inner
                        string targetGetInstInner = "struct Instrument *get_instrument_inner(s32 bankId, s32 instId) {\r\n    struct Instrument *inst;";
                        string targetGetInstInnerLF = "struct Instrument *get_instrument_inner(s32 bankId, s32 instId) {\n    struct Instrument *inst;";
                        string replacementGetInstInner = "struct Instrument *get_instrument_inner(s32 bankId, s32 instId) {\n    struct Instrument *inst;\n    if (gCtlEntries == NULL || gAlCtlHeader == NULL || bankId < 0 || bankId >= gAlCtlHeader->seqCount) {\n        return NULL;\n    }";

                        if (content.Contains(targetGetInstInner))
                        {
                            content = content.Replace(targetGetInstInner, replacementGetInstInner);
                        }
                        else if (content.Contains(targetGetInstInnerLF))
                        {
                            content = content.Replace(targetGetInstInnerLF, replacementGetInstInner);
                        }

                        // 4.3. Patch get_drum
                        string targetGetDrum = "struct Drum *get_drum(s32 bankId, s32 drumId) {\r\n    struct Drum *drum;";
                        string targetGetDrumLF = "struct Drum *get_drum(s32 bankId, s32 drumId) {\n    struct Drum *drum;";
                        string replacementGetDrum = "struct Drum *get_drum(s32 bankId, s32 drumId) {\n    struct Drum *drum;\n    if (gCtlEntries == NULL || gAlCtlHeader == NULL || bankId < 0 || bankId >= gAlCtlHeader->seqCount) {\n        return NULL;\n    }";

                        content = content.Replace(targetGetDrum, replacementGetDrum);
                        content = content.Replace(targetGetDrumLF, replacementGetDrum);

                        File.WriteAllText(playbackFile, content);
                        LogOutputText.AppendText("- Applied out-of-bounds guards to src/audio/playback.c.\n");
                    }
                    else
                    {
                        LogOutputText.AppendText("Warning: src/audio/playback.c not found. Skipping playback patch.\n");
                    }

                    string seqPlayerFile = Path.Combine(_projectRoot, "src", "audio", "seqplayer.c");
                    if (File.Exists(seqPlayerFile))
                    {
                        string backupPath = seqPlayerFile + ".bak";
                        if (File.Exists(backupPath))
                        {
                            File.Copy(backupPath, seqPlayerFile, true);
                        }
                        else
                        {
                            File.Copy(seqPlayerFile, backupPath);
                        }
                        string content = File.ReadAllText(seqPlayerFile);

                        // 4.4. Patch get_instrument in seqplayer.c (C89 compliant - after pad declaration/VERSION checks)
                        string targetSeqGetInstEU = "#if defined(VERSION_EU) || defined(VERSION_SH) || defined(VERSION_CN)\r\n    inst = get_instrument_inner(seqChannel->bankId, instId);";
                        string targetSeqGetInstEULF = "#if defined(VERSION_EU) || defined(VERSION_SH) || defined(VERSION_CN)\n    inst = get_instrument_inner(seqChannel->bankId, instId);";
                        string replacementSeqGetInstEU = "#if defined(VERSION_EU) || defined(VERSION_SH) || defined(VERSION_CN)\n    if (gCtlEntries == NULL || gAlCtlHeader == NULL || seqChannel->bankId >= gAlCtlHeader->seqCount) {\n        *instOut = NULL;\n        return 0;\n    }\n    inst = get_instrument_inner(seqChannel->bankId, instId);";

                        string targetSeqGetInstUS = "#else\r\n    UNUSED u32 pad;\r\n\r\n    if (instId >= gCtlEntries[seqChannel->bankId].numInstruments) {";
                        string targetSeqGetInstUSLF = "#else\n    UNUSED u32 pad;\n\n    if (instId >= gCtlEntries[seqChannel->bankId].numInstruments) {";
                        string replacementSeqGetInstUS = "#else\n    UNUSED u32 pad;\n    if (gCtlEntries == NULL || gAlCtlHeader == NULL || seqChannel->bankId >= gAlCtlHeader->seqCount) {\n        *instOut = NULL;\n        return 0;\n    }\n\n    if (instId >= gCtlEntries[seqChannel->bankId].numInstruments) {";

                        if (content.Contains(targetSeqGetInstEU))
                        {
                            content = content.Replace(targetSeqGetInstEU, replacementSeqGetInstEU);
                        }
                        else if (content.Contains(targetSeqGetInstEULF))
                        {
                            content = content.Replace(targetSeqGetInstEULF, replacementSeqGetInstEU);
                        }

                        if (content.Contains(targetSeqGetInstUS))
                        {
                            content = content.Replace(targetSeqGetInstUS, replacementSeqGetInstUS);
                        }
                        else if (content.Contains(targetSeqGetInstUSLF))
                        {
                            content = content.Replace(targetSeqGetInstUSLF, replacementSeqGetInstUS);
                        }

                        // 4.5. Patch drum processing bounds check in seqplayer.c
                        string targetDrumProcess = "#if defined(VERSION_EU)\r\n                drum = get_drum(seqChannel->bankId, cmd);\r\n#else\r\n                if (cmd >= gCtlEntries[seqChannel->bankId].numDrums) {";
                        string targetDrumProcessLF = "#if defined(VERSION_EU)\n                drum = get_drum(seqChannel->bankId, cmd);\n#else\n                if (cmd >= gCtlEntries[seqChannel->bankId].numDrums) {";
                        string replacementDrumProcess = "#if defined(VERSION_EU)\n                drum = get_drum(seqChannel->bankId, cmd);\n#else\n                if (gCtlEntries == NULL || gAlCtlHeader == NULL || seqChannel->bankId >= gAlCtlHeader->seqCount) {\n                    layer->stopSomething = TRUE;\n                    goto skip;\n                }\n                if (cmd >= gCtlEntries[seqChannel->bankId].numDrums) {";

                        if (content.Contains(targetDrumProcess))
                        {
                            content = content.Replace(targetDrumProcess, replacementDrumProcess);
                        }
                        else if (content.Contains(targetDrumProcessLF))
                        {
                            content = content.Replace(targetDrumProcessLF, replacementDrumProcess);
                        }

                        File.WriteAllText(seqPlayerFile, content);
                        LogOutputText.AppendText("- Applied out-of-bounds guards to src/audio/seqplayer.c.\n");
                    }
                    else
                    {
                        LogOutputText.AppendText("Warning: src/audio/seqplayer.c not found. Skipping seqplayer patch.\n");
                    }
                }

                // 5. Support New Channels (modify src/audio/external.c)
                if (supportNewChannels)
                {
                    string externalFile = Path.Combine(_projectRoot, "src", "audio", "external.c");
                    if (File.Exists(externalFile))
                    {
                        string backupPath = externalFile + ".bak";
                        if (File.Exists(backupPath))
                        {
                            File.Copy(backupPath, externalFile, true);
                        }
                        else
                        {
                            File.Copy(externalFile, backupPath);
                        }
                        string content = File.ReadAllText(externalFile);

                        // Find the start of process_level_music_dynamics and patch it
                        string targetDynamic = "void process_level_music_dynamics(void) {\r\n    u32 conditionBits;";
                        string targetDynamicLF = "void process_level_music_dynamics(void) {\n    u32 conditionBits;";
                        string replacementDynamic = "void process_level_music_dynamics(void) {\n    u32 conditionBits;\n    return; // Support New Channels Patch";

                        if (content.Contains(targetDynamic))
                        {
                            content = content.Replace(targetDynamic, replacementDynamic);
                            File.WriteAllText(externalFile, content);
                            LogOutputText.AppendText("- Applied Support New Channels patch to src/audio/external.c.\n");
                        }
                        else if (content.Contains(targetDynamicLF))
                        {
                            content = content.Replace(targetDynamicLF, replacementDynamic);
                            File.WriteAllText(externalFile, content);
                            LogOutputText.AppendText("- Applied Support New Channels patch to src/audio/external.c.\n");
                        }
                        else
                        {
                            LogOutputText.AppendText("Warning: process_level_music_dynamics signature not found in src/audio/external.c. Skipping channels patch.\n");
                        }
                    }
                    else
                    {
                        LogOutputText.AppendText("Warning: src/audio/external.c not found. Skipping Support New Channels patch.\n");
                    }
                }

                LogOutputText.AppendText("\n🎉 C patches applied successfully!\n");
                MessageBox.Show("Randm64 C Patches applied successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                LogOutputText.AppendText($"Exception occurred while applying C patches: {ex.Message}\n");
                MessageBox.Show($"Failed to apply C patches: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
