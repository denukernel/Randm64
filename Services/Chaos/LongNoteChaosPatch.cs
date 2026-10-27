using System.IO;

namespace Sm64DecompLevelViewer.Services.Chaos;

internal static class LongNoteChaosPatch
{
    internal static void Apply(string root)
    {
        var patcher = new ChaosSourcePatcher(root);
        const string original = "        if (!layer->stopSomething && layer->delay <= layer->duration) {\n            seq_channel_layer_note_decay(layer);\n            layer->stopSomething = TRUE;\n        }";
        const string replacement = "#ifdef CHAOS_ALIEN_SOUND\n        {\n            /* duration is the silent tail, not the audible note length. */\n            s16 duration = layer->duration / 4;\n            if (!layer->stopSomething && layer->delay <= duration) {\n                seq_channel_layer_note_decay(layer);\n                layer->stopSomething = TRUE;\n            }\n        }\n#else\n" + original + "\n#endif";
        bool found = false;
        foreach (string file in new[] { "src/audio/copt/seq_channel_layer_process_script_copt.inc.c", "src/audio/seqplayer.c" })
        {
            string path = Path.Combine(root, file);
            if (!File.Exists(path)) continue;
            string text = File.ReadAllText(path).Replace("\r\n", "\n");
            foreach (int offset in new[] { 217, 221 })
            {
                string legacy = "            volatile int offset = " + offset + ";\n            s16 duration = *(s16 *)((u8 *)layer + offset);";
                if (text.Contains(legacy))
                {
                    if (!File.Exists(path + ".bak")) File.Copy(path, path + ".bak");
                    text = text.Replace(legacy, "            /* duration is the silent tail, not the audible note length. */\n            s16 duration = layer->duration / 4;");
                    File.WriteAllText(path, text);
                }
            }
            found |= patcher.PatchSourceFile(file, original, replacement);
        }
        if (!found) throw new InvalidDataException("long-note chaos does not match this audio source version");
        if (!patcher.PatchSourceFile("src/audio/seqplayer.c", "#include \"seqplayer.h\"", "#include \"seqplayer.h\"\n#include \"chaos_config.h\""))
            throw new InvalidDataException("long-note chaos could not install its configuration include");
    }
}
