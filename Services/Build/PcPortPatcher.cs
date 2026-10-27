using System.Diagnostics;
using System.IO;
namespace Sm64DecompLevelViewer.Services.Build;
internal sealed class PcPortPatcher
{
    private readonly string _projectRoot; private readonly Action<string> ReportLog;
    public PcPortPatcher(string root, Action<string> log) { _projectRoot = root; ReportLog = log; }
    internal static async Task WriteIfChangedAsync(string path, string content)
    {
        if (await File.ReadAllTextAsync(path) != content) await File.WriteAllTextAsync(path, content);
    }
        internal async Task PatchPcPortFilesAsync()
        {
            try
            {
                // 1. Patch src/pc/audio/audio_pulse.c
                string pulsePath = Path.Combine(_projectRoot, "src", "pc", "audio", "audio_pulse.c");
                if (File.Exists(pulsePath))
                {
                    string content = await Task.Run(() => File.ReadAllText(pulsePath));
                    
                    // Add null check return if not already present
                    string targetStr = "if (info == NULL) {\r\n        printf(\"pa_stream_get_timing_info failed, state is %d\\n\", pa_stream_get_state(pas.stream));\r\n    }";
                    string targetStrLf = "if (info == NULL) {\n        printf(\"pa_stream_get_timing_info failed, state is %d\\n\", pa_stream_get_state(pas.stream));\n    }";
                    
                    string replacement = "if (info == NULL) {\n        printf(\"pa_stream_get_timing_info failed, state is %d\\n\", pa_stream_get_state(pas.stream));\n        return 0;\n    }";
                    
                    bool patched = false;
                    if (content.Contains(targetStr))
                    {
                        content = content.Replace(targetStr, replacement);
                        patched = true;
                    }
                    else if (content.Contains(targetStrLf))
                    {
                        content = content.Replace(targetStrLf, replacement);
                        patched = true;
                    }

                    if (patched)
                    {
                        await WriteIfChangedAsync(pulsePath, content);
                        ReportLog("> Patched src/pc/audio/audio_pulse.c to prevent PulseAudio null timing crash under WSL.\n");
                    }
                }

                // 2. Patch src/pc/gfx/gfx_glx.c
                string glxPath = Path.Combine(_projectRoot, "src", "pc", "gfx", "gfx_glx.c");
                if (File.Exists(glxPath))
                {
                    string content = await Task.Run(() => File.ReadAllText(glxPath));
                    bool patched = false;

                    if (!content.Contains("static bool is_wsl(void)"))
                    {
                        string wslHelper = @"static bool is_wsl(void) {
    static int memo = -1;
    if (memo != -1) return (bool)memo;
    FILE *f = fopen(""/proc/version"", ""r"");
    if (f == NULL) {
        memo = 0;
        return false;
    }
    char buf[1024];
    if (fgets(buf, sizeof(buf), f) != NULL) {
        if (strstr(buf, ""Microsoft"") != NULL || strstr(buf, ""microsoft"") != NULL) {
            memo = 1;
            fclose(f);
            return true;
        }
    }
    fclose(f);
    memo = 0;
    return false;
}

static int64_t get_time(void) {";

                        if (content.Contains("static int64_t get_time(void) {"))
                        {
                            content = content.Replace("static int64_t get_time(void) {", wslHelper);
                            patched = true;
                        }
                    }

                    string omlTarget = "if (glx.glXGetSyncValuesOML != NULL && glx.glXGetSyncValuesOML(glx.dpy, glx.win, &ust, &msc, &sbc))";
                    string omlReplacement = "if (!is_wsl() && glx.glXGetSyncValuesOML != NULL && glx.glXGetSyncValuesOML(glx.dpy, glx.win, &ust, &msc, &sbc))";
                    if (content.Contains(omlTarget) && !content.Contains(omlReplacement))
                    {
                        content = content.Replace(omlTarget, omlReplacement);
                        patched = true;
                    }

                    string sgiTarget = "if (glx.glXGetVideoSyncSGI != NULL)";
                    string sgiReplacement = "if (!is_wsl() && glx.glXGetVideoSyncSGI != NULL)";
                    if (content.Contains(sgiTarget) && !content.Contains(sgiReplacement))
                    {
                        content = content.Replace(sgiTarget, sgiReplacement);
                        patched = true;
                    }

                    string swapTarget = "static void gfx_glx_swap_buffers_begin(void) {\r\n    glx.wanted_ust += FRAME_INTERVAL_US_NUMERATOR;";
                    string swapTargetLf = "static void gfx_glx_swap_buffers_begin(void) {\n    glx.wanted_ust += FRAME_INTERVAL_US_NUMERATOR;";
                    string swapReplacement = "static void gfx_glx_swap_buffers_begin(void) {\n    extern bool gFastForward;\n    uint64_t interval = FRAME_INTERVAL_US_NUMERATOR;\n    if (gFastForward) {\n        interval /= 4;\n    }\n    glx.wanted_ust += interval;";

                    if (content.Contains(swapTarget))
                    {
                        content = content.Replace(swapTarget, swapReplacement);
                        patched = true;
                    }
                    else if (content.Contains(swapTargetLf))
                    {
                        content = content.Replace(swapTargetLf, swapReplacement);
                        patched = true;
                    }

                    if (patched)
                    {
                        await WriteIfChangedAsync(glxPath, content);
                        ReportLog("> Patched src/pc/gfx/gfx_glx.c to bypass sync control extension frame drops under WSL.\n");
                    }
                }

                // 3. Patch src/pc/pc_main.c for fast-forward
                string pcMainPath = Path.Combine(_projectRoot, "src", "pc", "pc_main.c");
                if (File.Exists(pcMainPath))
                {
                    string content = await Task.Run(() => File.ReadAllText(pcMainPath));
                    bool patched = false;

                    if (!content.Contains("bool gFastForward = false;"))
                    {
                        string target = "s8 gShowDebugText;\r\n\r\nstatic struct AudioAPI *audio_api;";
                        string targetLf = "s8 gShowDebugText;\n\nstatic struct AudioAPI *audio_api;";
                        string replacement = "s8 gShowDebugText;\n\nbool gFastForward = false;\n\nstatic struct AudioAPI *audio_api;";
                        
                        if (content.Contains(target))
                        {
                            content = content.Replace(target, replacement);
                            patched = true;
                        }
                        else if (content.Contains(targetLf))
                        {
                            content = content.Replace(targetLf, replacement);
                            patched = true;
                        }
                        else
                        {
                            string fallbackTarget = "static struct AudioAPI *audio_api;";
                            string fallbackReplacement = "bool gFastForward = false;\nstatic struct AudioAPI *audio_api;";
                            if (content.Contains(fallbackTarget))
                            {
                                content = content.Replace(fallbackTarget, fallbackReplacement);
                                patched = true;
                            }
                        }
                    }

                    // Clean up loops iteration if present from previous patch
                    if (content.Contains("int loops = gFastForward ? 4 : 1;"))
                    {
                        string loopsTarget = "int loops = gFastForward ? 4 : 1;\r\n    for (int i = 0; i < loops; i++) {\r\n        game_loop_one_iteration();\r\n    }";
                        string loopsTargetLf = "int loops = gFastForward ? 4 : 1;\n    for (int i = 0; i < loops; i++) {\n        game_loop_one_iteration();\n    }";
                        if (content.Contains(loopsTarget))
                        {
                            content = content.Replace(loopsTarget, "game_loop_one_iteration();");
                            patched = true;
                        }
                        else if (content.Contains(loopsTargetLf))
                        {
                            content = content.Replace(loopsTargetLf, "game_loop_one_iteration();");
                            patched = true;
                        }
                    }

                    // Patch audio playback block to skip on fast-forward
                    if (!content.Contains("if (!gFastForward) {"))
                    {
                        string playTarget = "audio_api->play((u8 *)audio_buffer, 2 * num_audio_samples * 4);";
                        string playReplacement = "if (!gFastForward) {\n        audio_api->play((u8 *)audio_buffer, 2 * num_audio_samples * 4);\n    }";
                        if (content.Contains(playTarget))
                        {
                            content = content.Replace(playTarget, playReplacement);
                            patched = true;
                        }
                    }

                    if (patched)
                    {
                        await WriteIfChangedAsync(pcMainPath, content);
                        ReportLog("> Patched src/pc/pc_main.c to support 4x fast-forward mode.\n");
                    }
                }

                // 4. Patch src/pc/controller/controller_keyboard.c for F4 input detection
                string kbPath = Path.Combine(_projectRoot, "src", "pc", "controller", "controller_keyboard.c");
                if (File.Exists(kbPath))
                {
                    string content = await Task.Run(() => File.ReadAllText(kbPath));
                    bool patched = false;

                    if (!content.Contains("extern bool gFastForward;"))
                    {
                        string target = "bool keyboard_on_key_down(int scancode) {";
                        string replacement = "extern bool gFastForward;\n\nbool keyboard_on_key_down(int scancode) {\n    if (scancode == 0x3e) { // F4\n        gFastForward = !gFastForward;\n        return true;\n    }";
                        if (content.Contains(target))
                        {
                            content = content.Replace(target, replacement);
                            patched = true;
                        }
                    }

                    if (patched)
                    {
                        await WriteIfChangedAsync(kbPath, content);
                        ReportLog("> Patched src/pc/controller/controller_keyboard.c to toggle fast-forward on F4.\n");
                    }
                }

                // 5. Patch src/pc/gfx/gfx_dxgi.cpp for DirectX fast-forward frame limiter
                string dxgiPath = Path.Combine(_projectRoot, "src", "pc", "gfx", "gfx_dxgi.cpp");
                if (File.Exists(dxgiPath))
                {
                    string content = await Task.Run(() => File.ReadAllText(dxgiPath));
                    bool patched = false;

                    // Clean up bad patch if present
                    string badPatch = "extern \"C\" bool gFastForward;\n    uint64_t interval = FRAME_INTERVAL_US_NUMERATOR;\n    if (gFastForward) {\n        interval /= 4;\n    }\n    dxgi.frame_timestamp += interval;";
                    string badPatchLf = "extern \"C\" bool gFastForward;\n    uint64_t interval = FRAME_INTERVAL_US_NUMERATOR;\n    if (gFastForward) {\n        interval /= 4;\n    }\n    dxgi.frame_timestamp += interval;";
                    string badPatchCrLf = "extern \"C\" bool gFastForward;\r\n    uint64_t interval = FRAME_INTERVAL_US_NUMERATOR;\r\n    if (gFastForward) {\r\n        interval /= 4;\r\n    }\r\n    dxgi.frame_timestamp += interval;";

                    if (content.Contains(badPatch) || content.Contains(badPatchLf) || content.Contains(badPatchCrLf))
                    {
                        content = content.Replace(badPatch, "dxgi.frame_timestamp += FRAME_INTERVAL_US_NUMERATOR;");
                        content = content.Replace(badPatchLf, "dxgi.frame_timestamp += FRAME_INTERVAL_US_NUMERATOR;");
                        content = content.Replace(badPatchCrLf, "dxgi.frame_timestamp += FRAME_INTERVAL_US_NUMERATOR;");
                        patched = true;
                    }

                    // Add global extern if missing
                    if (!content.Contains("using namespace Microsoft::WRL; // For ComPtr\n\nextern \"C\" bool gFastForward;") &&
                        !content.Contains("using namespace Microsoft::WRL; // For ComPtr\r\n\r\nextern \"C\" bool gFastForward;"))
                    {
                        // Remove any other global declarations if present to avoid duplicates
                        content = content.Replace("\nextern \"C\" bool gFastForward;", "");
                        content = content.Replace("\r\nextern \"C\" bool gFastForward;", "");

                        string usingNamespace = "using namespace Microsoft::WRL; // For ComPtr";
                        if (content.Contains(usingNamespace))
                        {
                            content = content.Replace(usingNamespace, "using namespace Microsoft::WRL; // For ComPtr\n\nextern \"C\" bool gFastForward;");
                            patched = true;
                        }
                    }

                    // Apply correct function patch
                    if (!content.Contains("uint64_t interval = FRAME_INTERVAL_US_NUMERATOR;"))
                    {
                        string target = "dxgi.frame_timestamp += FRAME_INTERVAL_US_NUMERATOR;";
                        string replacement = "uint64_t interval = FRAME_INTERVAL_US_NUMERATOR;\n    if (gFastForward) {\n        interval /= 4;\n    }\n    dxgi.frame_timestamp += interval;";
                        if (content.Contains(target))
                        {
                            content = content.Replace(target, replacement);
                            patched = true;
                        }
                    }

                    if (patched)
                    {
                        await WriteIfChangedAsync(dxgiPath, content);
                        ReportLog("> Patched src/pc/gfx/gfx_dxgi.cpp to support DirectX fast-forward frame limits.\n");
                    }
                }

                // 6. Patch src/pc/gfx/gfx_sdl2.c for SDL2 fast-forward frame limits
                string sdl2Path = Path.Combine(_projectRoot, "src", "pc", "gfx", "gfx_sdl2.c");
                if (File.Exists(sdl2Path))
                {
                    string content = await Task.Run(() => File.ReadAllText(sdl2Path));
                    bool patched = false;

                    if (!content.Contains("extern bool gFastForward;"))
                    {
                        string target = "static void sync_framerate_with_timer(void) {\r\n    // Number of milliseconds a frame should take (30 fps)\r\n    const Uint32 FRAME_TIME = 1000 / 30;\r\n    static Uint32 last_time;\r\n    Uint32 elapsed = SDL_GetTicks() - last_time;\r\n\r\n    if (elapsed < FRAME_TIME)\r\n        SDL_Delay(FRAME_TIME - elapsed);\r\n    last_time += FRAME_TIME;\r\n}";
                        string targetLf = "static void sync_framerate_with_timer(void) {\n    // Number of milliseconds a frame should take (30 fps)\n    const Uint32 FRAME_TIME = 1000 / 30;\n    static Uint32 last_time;\n    Uint32 elapsed = SDL_GetTicks() - last_time;\n\n    if (elapsed < FRAME_TIME)\n        SDL_Delay(FRAME_TIME - elapsed);\n    last_time += FRAME_TIME;\n}";

                        string replacement = "static void sync_framerate_with_timer(void) {\n    extern bool gFastForward;\n    Uint32 frame_time = 1000 / 30;\n    if (gFastForward) {\n        frame_time /= 4;\n    }\n    static Uint32 last_time;\n    Uint32 elapsed = SDL_GetTicks() - last_time;\n\n    if (elapsed < frame_time)\n        SDL_Delay(frame_time - elapsed);\n    last_time += frame_time;\n}";

                        if (content.Contains(target))
                        {
                            content = content.Replace(target, replacement);
                            patched = true;
                        }
                        else if (content.Contains(targetLf))
                        {
                            content = content.Replace(targetLf, replacement);
                            patched = true;
                        }
                    }

                    if (patched)
                    {
                        await WriteIfChangedAsync(sdl2Path, content);
                        ReportLog("> Patched src/pc/gfx/gfx_sdl2.c to support SDL2 fast-forward frame limits.\n");
                    }
                }

                // 7. Patch tools/armips.cpp to include standard integer headers on MSYS2
                string armipsPath = Path.Combine(_projectRoot, "tools", "armips.cpp");
                if (File.Exists(armipsPath))
                {
                    string content = await Task.Run(() => File.ReadAllText(armipsPath));
                    bool patched = false;

                    if (!content.Contains("#include <stdint.h>"))
                    {
                        string target = "#define _CRT_SECURE_NO_WARNINGS";
                        string replacement = "#define _CRT_SECURE_NO_WARNINGS\n#include <stdint.h>\n#include <cstdint>\n#include <inttypes.h>";
                        if (content.Contains(target))
                        {
                            content = content.Replace(target, replacement);
                            patched = true;
                        }
                    }

                    if (patched)
                    {
                        await WriteIfChangedAsync(armipsPath, content);
                        ReportLog("> Patched tools/armips.cpp to include stdint headers under MSYS2.\n");
                    }
                }
            }
            catch (Exception ex)
            {
                ReportLog($"> Warning: Failed to automatically patch PC Port files: {ex.Message}\n");
            }
        }
}

