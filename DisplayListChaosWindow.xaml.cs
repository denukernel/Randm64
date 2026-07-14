using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;

namespace Sm64DecompLevelViewer
{
    public partial class DisplayListChaosWindow : Window
    {
        private readonly string _projectRoot;
        private readonly string[] _exclDescriptions = new string[]
        {
            "Exclusions disabled. Standard game rendering mode.",
            "HUD elements (health meter, coins count, star count, lives count, timer) disappear.",
            "HUD disappears + Lakitu clouds and eyes disappear from cameraman and enemy models.",
            "HUD & Lakitu elements disappear + all non-Mario objects disappear, except Lakitu's shell.",
            "HUD & Lakitu elements disappear + most objects (Goombas, Koopas, Bob-ombs, etc.) disappear from rendering.",
            "HUD, Lakitu & objects disappear + parts of Mario (limbs, cap, gloves) disappear randomly.",
            "HUD, Lakitu & objects disappear + only Mario's red torso is rendered in-game.",
            "Mario becomes completely invisible, alongside all objects and HUD elements.",
            "Mario, objects, HUD, and parts of the level geometry disappear from rendering.",
            "Everything is invisible. Only the skybox remains."
        };

        public DisplayListChaosWindow(string projectRoot)
        {
            InitializeComponent();
            _projectRoot = projectRoot;
            UpdateDescription(0);
        }

        private void LevelSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (LevelValueText == null || DescriptionText == null) return;
            int val = (int)e.NewValue;
            LevelValueText.Text = $"Level {val} {(val == 0 ? "(Disabled)" : "")}";
            UpdateDescription(val);
        }

        private void UpdateDescription(int level)
        {
            if (level >= 0 && level < _exclDescriptions.Length)
            {
                DescriptionText.Text = _exclDescriptions[level];
            }
        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            int level = (int)LevelSlider.Value;
            int trigger = TriggerComboBox.SelectedIndex;

            if (string.IsNullOrEmpty(_projectRoot) || !Directory.Exists(_projectRoot))
            {
                MessageBox.Show("Invalid project root folder. Please load a valid project first.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                // 1. Back up and patch include/PR/gbi.h
                PatchSourceFile("include/PR/gbi.h", "#define\tgSPDisplayList(pkt,dl)\tgDma1p(pkt,G_DL,dl,0,G_DL_PUSH)",
                    @"#if defined(_LANGUAGE_C) || defined(_LANGUAGE_C_DIRECT)
#include ""chaos_config.h""
#ifdef CHAOS_DL_EXCLUSION_LEVEL
extern int should_exclude_display_list(const void *dl);
#define	gSPDisplayList(pkt,dl)	gDma1p(pkt, should_exclude_display_list((const void *)(dl)) ? G_NOOP : G_DL, dl, 0, G_DL_PUSH)
#else
#define	gSPDisplayList(pkt,dl)	gDma1p(pkt,G_DL,dl,0,G_DL_PUSH)
#endif
#else
#define	gSPDisplayList(pkt,dl)	gDma1p(pkt,G_DL,dl,0,G_DL_PUSH)
#endif");

                PatchSourceFile("include/PR/gbi.h", "#define\tgDPSetTextureImage(pkt, f, s, w, i)\tgSetImage(pkt, G_SETTIMG, f, s, w, i)",
                    @"#ifdef CHAOS_DL_EXCLUSION_LEVEL
extern void chaos_set_texture_image(Gfx *pkt, u32 fmt, u32 siz, u32 width, const void *image);
#define	gDPSetTextureImage(pkt, f, s, w, i)	chaos_set_texture_image((Gfx *)(pkt), f, s, w, i)
#else
#define	gDPSetTextureImage(pkt, f, s, w, i)	gSetImage(pkt, G_SETTIMG, f, s, w, i)
#endif");

                // 2. Back up and patch rendering_graph_node.c
                string oldGfx = "static void geo_append_display_list(void *displayList, s16 layer) {";
                string newGfx = @"#include ""chaos_config.h""

#ifdef CHAOS_DL_EXCLUSION_LEVEL
extern u32 gGlobalTimer;
extern struct Controller gControllers[];

#define CHAOS_U_JPAD 0x0800
#define CHAOS_D_JPAD 0x0400
#define CHAOS_L_JPAD 0x0200
#define CHAOS_R_JPAD 0x0100

static int sMarioDlCount = 0;
static int sActorDlCount = 0;

static int sExcludedDlIndex = 0;
static int sExcludedRange = 1;
static int sFrameDlCount = 0;

static int check_exclusion_trigger(void) {
    extern struct MarioState gMarioStates[];
#if CHAOS_DL_EXCLUSION_TRIGGER == 1
    if (gMarioStates[0].forwardVel <= 2.0f) return 0;
#elif CHAOS_DL_EXCLUSION_TRIGGER == 2
    if (gMarioStates[0].forwardVel > 2.0f) return 0;
#endif
    return 1;
}

static void update_exclusion_inputs(void) {
    if (gControllers[0].buttonPressed & CHAOS_L_JPAD) {
        sExcludedDlIndex--;
        if (sExcludedDlIndex < 0) sExcludedDlIndex = 0;
    }
    if (gControllers[0].buttonPressed & CHAOS_R_JPAD) {
        sExcludedDlIndex++;
    }
    if (gControllers[0].buttonPressed & CHAOS_U_JPAD) {
        sExcludedRange++;
    }
    if (gControllers[0].buttonPressed & CHAOS_D_JPAD) {
        sExcludedRange--;
        if (sExcludedRange < 1) sExcludedRange = 1;
    }
}

int should_exclude_display_list(const void *dl) {
    static u32 sLastFrame = 0;
    u32 segment;
    int level;

    if (gGlobalTimer != sLastFrame) {
        sLastFrame = gGlobalTimer;
        sFrameDlCount = 0;
        sMarioDlCount = 0;
        sActorDlCount = 0;
        update_exclusion_inputs();
    }
    sFrameDlCount++;

    segment = (uintptr_t)dl >> 24;
    level = CHAOS_DL_EXCLUSION_LEVEL;

    if (segment == 0x04) sMarioDlCount++;
    if (segment == 0x05 || segment == 0x06) sActorDlCount++;

    if (sExcludedDlIndex > 0 && sFrameDlCount >= sExcludedDlIndex && sFrameDlCount < sExcludedDlIndex + sExcludedRange) {
        return 1;
    }

    if (!check_exclusion_trigger()) return 0;

    // HUD, menus, text, and dynamically generated shadows (segments 0x00, 0x02, and 0x80)
    if (level >= 1) {
        if (segment == 0x80 || segment == 0x00 || segment == 0x02) {
            return 1;
        }
    }

    // Skybox (segment 0x0A) culling option
    #if CHAOS_DL_EXCLUDE_SKYBOX == 1
    if (segment == 0x0A) {
        return 1;
    }
    #endif

    if (level >= 9) {
        #if CHAOS_DL_EXCLUDE_SKYBOX == 1
        return 1;
        #else
        if (segment != 0x0A) {
            return 1;
        }
        #endif
    }

    if (level >= 8) {
        if (segment == 0x07) {
            return 1;
        }
    }

    if (level >= 7) {
        // Exclude Mario (0x04) and actors (0x05/0x06)
        if (segment == 0x04 || segment == 0x05 || segment == 0x06) {
            return 1;
        }
    }

    if (level >= 6) {
        if (segment == 0x04) {
            if (sMarioDlCount > 1) {
                return 1;
            }
        }
    }

    if (level >= 5) {
        if (segment == 0x04) {
            return (sMarioDlCount % 3 == 0);
        }
    }

    if (level >= 4) {
        if (segment == 0x05 || segment == 0x06) {
            return 1;
        }
    }

    if (level >= 3) {
        if (segment == 0x05 || segment == 0x06) {
            if (sActorDlCount < 8) {
                return 1;
            }
        } else if (segment != 0x04 && segment != 0x07 && segment != 0x02 && segment != 0x0A) {
            return 1;
        }
    }

    if (level >= 2) {
        if (segment == 0x05 || segment == 0x06) {
            if (sActorDlCount == 1 || sActorDlCount == 3) {
                return 1;
            }
        }
    }

    return 0;
}

int should_exclude_texture(const void *image) {
    u32 segment = (uintptr_t)image >> 24;
    int level = CHAOS_DL_EXCLUSION_LEVEL;

    if (!check_exclusion_trigger()) return 0;

    // Exclude level specific textures (0x09), level textures (0x08), and level geometry textures (0x07) at Level 6+
    if (level >= 6) {
        if (segment == 0x09 || segment == 0x08 || segment == 0x07) {
            return 1;
        }
    }

    return 0;
}

void chaos_set_texture_image(Gfx *pkt, u32 fmt, u32 siz, u32 width, const void *image) {
    if (should_exclude_texture(image)) {
        pkt->words.w0 = 0;
        pkt->words.w1 = 0;
    } else {
        pkt->words.w0 = ((u32)G_SETTIMG << 24) | ((fmt & 7) << 21) | ((siz & 3) << 19) | (((width) - 1) & 0xfff);
        pkt->words.w1 = (uintptr_t)image;
    }
}

void patch_display_list(Gfx *dl, int depth) {
    Gfx *virtual_dl;
    u32 segment;
    if (dl == NULL || depth > 16) return;

    segment = (uintptr_t)dl >> 24;
    if (segment != 0x07 && segment != 0x09 && segment != 0x02 && segment != 0x03 && segment != 0x04 && segment != 0x05 && segment != 0x06) {
        return;
    }

    virtual_dl = segmented_to_virtual(dl);
    if (virtual_dl == NULL) return;

    while (1) {
        u8 opcode = virtual_dl->words.w0 >> 24;

        if (opcode == 0xfd) { // G_SETTIMG
            const void *image = (const void *)virtual_dl->words.w1;
            if (should_exclude_texture(image)) {
                virtual_dl->words.w0 = 0;
                virtual_dl->words.w1 = 0;
            }
        }
        else if (opcode == 0xde) { // G_DL
            Gfx *child_dl = (Gfx *)virtual_dl->words.w1;
            patch_display_list(child_dl, depth + 1);
        }

        if (opcode == 0xdf) { // G_ENDDL
            break;
        }

        virtual_dl++;
    }
}
#endif

static void geo_append_display_list(void *displayList, s16 layer) {";

                PatchSourceFile("src/game/rendering_graph_node.c", oldGfx, newGfx);

                string oldLoop = @"            while (currList != NULL) {
                gSPMatrix(gDisplayListHead++, VIRTUAL_TO_PHYSICAL(currList->transform),
                          G_MTX_MODELVIEW | G_MTX_LOAD | G_MTX_NOPUSH);
                gSPDisplayList(gDisplayListHead++, currList->displayList);
                currList = currList->next;
            }";

                string newLoop = @"            while (currList != NULL) {
                gSPMatrix(gDisplayListHead++, VIRTUAL_TO_PHYSICAL(currList->transform),
                          G_MTX_MODELVIEW | G_MTX_LOAD | G_MTX_NOPUSH);
#ifdef CHAOS_DL_EXCLUSION_LEVEL
                patch_display_list(currList->displayList, 0);
#endif
                gSPDisplayList(gDisplayListHead++, currList->displayList);
                currList = currList->next;
            }";

                PatchSourceFile("src/game/rendering_graph_node.c", oldLoop, newLoop);

                string oldFuncHead = @"static void geo_process_master_list_sub(struct GraphNodeMasterList *node) {
    struct DisplayListNode *currList;";

                string newFuncHead = @"static void geo_process_master_list_sub(struct GraphNodeMasterList *node) {
#ifdef CHAOS_DL_EXCLUSION_LEVEL
    extern void patch_display_list(Gfx *dl, int depth);
#endif
    struct DisplayListNode *currList;";

                PatchSourceFile("src/game/rendering_graph_node.c", oldFuncHead, newFuncHead);

                // 3. Update chaos_config.h
                UpdateChaosConfig(level, trigger, ExcludeSkyboxCheck.IsChecked == true);

                // Force compilation by updating last write times of modified source/header files
                string gbiFullPath = Path.Combine(_projectRoot, "include", "PR", "gbi.h");
                string gfxFullPath = Path.Combine(_projectRoot, "src", "game", "rendering_graph_node.c");
                if (File.Exists(gbiFullPath)) File.SetLastWriteTime(gbiFullPath, DateTime.Now);
                if (File.Exists(gfxFullPath)) File.SetLastWriteTime(gfxFullPath, DateTime.Now);

                MessageBox.Show("Display List exclusion patches successfully applied to decomp source files!\nRecompile the ROM to run inside emulator.", "Exclusions Applied", MessageBoxButton.OK, MessageBoxImage.Information);
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error applying patches: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Restore_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_projectRoot) || !Directory.Exists(_projectRoot)) return;

            try
            {
                string[] files = { "src/game/rendering_graph_node.c", "include/PR/gbi.h" };
                foreach (var relPath in files)
                {
                    string fullPath = Path.Combine(_projectRoot, relPath.Replace('/', Path.DirectorySeparatorChar));
                    string backupPath = fullPath + ".bak";
                    if (File.Exists(backupPath))
                    {
                        File.Copy(backupPath, fullPath, true);
                    }
                }

                UpdateChaosConfig(0, 0, false);

                MessageBox.Show("Display List settings reverted to normal.", "Reverted", MessageBoxButton.OK, MessageBoxImage.Information);
                LevelSlider.Value = 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error reverting files: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateChaosConfig(int level, int trigger, bool excludeSkybox)
        {
            string configDir = Path.Combine(_projectRoot, "include");
            if (!Directory.Exists(configDir)) return;

            string configPath = Path.Combine(configDir, "chaos_config.h");
            List<string> lines = new List<string>();

            if (File.Exists(configPath))
            {
                var existingLines = File.ReadAllLines(configPath);
                foreach (var line in existingLines)
                {
                    if (!line.Contains("CHAOS_DL_EXCLUSION") && !line.Contains("CHAOS_DL_EXCLUDE"))
                    {
                        lines.Add(line);
                    }
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
            if (endifIndex != -1)
            {
                if (level > 0)
                {
                    lines.Insert(endifIndex, $"#define CHAOS_DL_EXCLUSION_LEVEL {level}");
                    lines.Insert(endifIndex + 1, $"#define CHAOS_DL_EXCLUSION_TRIGGER {trigger}");
                    lines.Insert(endifIndex + 2, $"#define CHAOS_DL_EXCLUDE_SKYBOX {(excludeSkybox ? 1 : 0)}");
                }
            }

            File.WriteAllText(configPath, string.Join("\n", lines).Replace("\r\n", "\n") + "\n");
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

            string content = File.ReadAllText(fullPath).Replace("\r\n", "\n");
            string normalizedOld = oldText.Replace("\r\n", "\n");
            string normalizedNew = newText.Replace("\r\n", "\n");

            if (content.Contains(normalizedOld) && !content.Contains(normalizedNew))
            {
                content = content.Replace(normalizedOld, normalizedNew);
                File.WriteAllText(fullPath, content);
                File.SetLastWriteTime(fullPath, DateTime.Now);
            }
        }
    }
}
