using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Sm64DecompLevelViewer.Services.Chaos;

internal sealed record DisplayListExclusionResult(int Calls, int Excluded, int Files);

/// <summary>Source-level call exclusion using the target's own GBI macros, without changing opcodes or addresses.</summary>
internal static class DisplayListExclusionService
{
    private const string Marker = "RANDM64_DL_EXCLUDED";
    private static readonly Regex SavedCall = new(@"/\* RANDM64_DL_EXCLUDED ([A-Za-z0-9+/=]+) \*/ (gsDPNoOp\(\)|gsSPEndDisplayList\(\))", RegexOptions.CultureInvariant);
    private sealed record Change(string Path, byte[] Before, byte[] After);

    internal static DisplayListExclusionResult Apply(string root, int level, uint seed, bool includeBranches)
    {
        if (level is < 0 or > 9) throw new ArgumentOutOfRangeException(nameof(level));
        string gbi = Path.Combine(root, "include", "PR", "gbi.h");
        if (!File.Exists(gbi)) throw new InvalidDataException("SM64 include/PR/gbi.h was not found.");
        string header = File.ReadAllText(gbi);
        foreach (string macro in new[] { "gsSPDisplayList", "gsDPNoOp", "gsSPEndDisplayList", "gsSPBranchList" })
            if (!Regex.IsMatch(header, @"#\s*define\s+" + macro + @"\s*\("))
                throw new InvalidDataException($"The target GBI does not define {macro}; no files were changed.");

        var changes = new List<Change>();
        int calls = 0, excluded = 0;
        foreach (string directory in new[] { "actors", "levels" })
        {
            string folder = Path.Combine(root, directory);
            if (!Directory.Exists(folder)) continue;
            foreach (string path in Directory.EnumerateFiles(folder, "*.c", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal))
            {
                byte[] before = File.ReadAllBytes(path);
                string original = Decode(before), clean = Restore(original);
                var result = Transform(clean, level, seed, includeBranches);
                calls += result.Calls; excluded += result.Excluded;
                Add(path, before, result.Source);
            }
        }

        // Remove the old global hook only where its exact known scaffolding is recognized.
        Add(gbi, File.ReadAllBytes(gbi), RemoveLegacyHeader(header));
        string graph = Path.Combine(root, "src", "game", "rendering_graph_node.c");
        if (File.Exists(graph))
        {
            byte[] before = File.ReadAllBytes(graph);
            Add(graph, before, RemoveLegacyGraph(Decode(before)));
        }
        string config = Path.Combine(root, "include", "chaos_config.h");
        if (File.Exists(config))
        {
            byte[] before = File.ReadAllBytes(config);
            string updated = Regex.Replace(Decode(before), @"(?m)^[ \t]*#define[ \t]+CHAOS_DL_(?:EXCLUSION_LEVEL|EXCLUSION_TRIGGER|EXCLUDE_SKYBOX)\b[^\r\n]*(?:\r?\n|$)", "");
            Add(config, before, updated);
        }
        if (level > 0 && calls == 0)
            throw new InvalidDataException("No actor/level display-list calls were found; no files were changed.");
        Commit(changes);
        return new(calls, excluded, changes.Count);

        void Add(string path, byte[] before, string text)
        {
            byte[] body = Encoding.UTF8.GetBytes(text);
            byte[] after = HasBom(before) ? new byte[] { 239, 187, 191 }.Concat(body).ToArray() : body;
            if (!before.SequenceEqual(after)) changes.Add(new(path, before, after));
        }
    }

    internal static (string Source, int Calls, int Excluded) Transform(string source, int level, uint seed, bool includeBranches)
    {
        if (level is < 0 or > 9) throw new ArgumentOutOfRangeException(nameof(level));
        source = Restore(source);
        string code = CodeMask(source);
        var output = new StringBuilder();
        int last = 0, calls = 0, excluded = 0;
        foreach (Match match in Regex.Matches(code, @"\b(gsSPDisplayList|gsSPBranchList)\s*\("))
        {
            bool branch = match.Groups[1].Value == "gsSPBranchList";
            int open = code.IndexOf('(', match.Index), end = open + 1, depth = 1;
            for (; end < code.Length && depth > 0; end++)
            {
                if (code[end] == '(') depth++;
                else if (code[end] == ')') depth--;
            }
            if (depth != 0) throw new InvalidDataException("Unbalanced display-list call; no output generated.");
            string target = Regex.Replace(code[(open + 1)..(end - 1)], @"\s+", "");
            if (target.Length == 0) throw new InvalidDataException("Display-list call has no target.");
            calls++;
            if (level == 0 || (branch && !includeBranches) || Hash(target, seed) % 100 >= level * 10) continue;
            string original = source[match.Index..end];
            output.Append(source, last, match.Index - last);
            output.Append("/* " + Marker + " " + Convert.ToBase64String(Encoding.UTF8.GetBytes(original)) + " */ ");
            // A skipped tail branch must return. A no-op there could execute past the array.
            output.Append(branch ? "gsSPEndDisplayList()" : "gsDPNoOp()");
            last = end; excluded++;
        }
        output.Append(source, last, source.Length - last);
        return (output.ToString(), calls, excluded);
    }

    internal static string Restore(string source)
    {
        string restored = SavedCall.Replace(source, match =>
        {
            string original;
            try { original = Encoding.UTF8.GetString(Convert.FromBase64String(match.Groups[1].Value)); }
            catch (FormatException ex) { throw new InvalidDataException("Invalid exclusion restore marker.", ex); }
            bool branch = original.StartsWith("gsSPBranchList", StringComparison.Ordinal);
            if ((!branch && !original.StartsWith("gsSPDisplayList", StringComparison.Ordinal))
                || match.Groups[2].Value != (branch ? "gsSPEndDisplayList()" : "gsDPNoOp()"))
                throw new InvalidDataException("Edited exclusion marker; restore refused to preserve external changes.");
            return original;
        });
        if (restored.Contains(Marker, StringComparison.Ordinal))
            throw new InvalidDataException("Unrecognized or edited exclusion marker; no files were changed.");
        return restored;
    }

    internal static string RemoveLegacyHeader(string source)
    {
        var pattern = new Regex(@"(?m)^#if defined\(_LANGUAGE_C\) \|\| defined\(_LANGUAGE_C_DIRECT\)\r?\n#include ""chaos_config.h""\r?\n#ifdef CHAOS_DL_EXCLUSION_LEVEL\r?\nextern int should_exclude_display_list\(const void \*dl\);\r?\n#define[^\r\n]*should_exclude_display_list[^\r\n]*\r?\n#else\r?\n#define[^\r\n]*gSPDisplayList[^\r\n]*\r?\n#endif\r?\n#else\r?\n#define[^\r\n]*gSPDisplayList[^\r\n]*\r?\n#endif");
        string result = pattern.Replace(source, "#define\tgSPDisplayList(pkt,dl)\tgDma1p(pkt,G_DL,dl,0,G_DL_PUSH)");
        if (result.Contains("should_exclude_display_list", StringComparison.Ordinal))
            throw new InvalidDataException("Unrecognized legacy GBI hook; refusing to overwrite it. Restore that hook before applying exclusions.");
        return result;
    }

    internal static string RemoveLegacyGraph(string source)
    {
        string normalized = source.Replace("\r\n", "\n");
        const string start = "#include \"chaos_config.h\"\n\n#ifdef CHAOS_DL_EXCLUSION_LEVEL\nextern u32 gGlobalTimer;";
        const string end = "#endif\n\nstatic void geo_append_display_list(void *displayList, s16 layer) {";
        int first = normalized.IndexOf(start, StringComparison.Ordinal);
        if (first >= 0)
        {
            int last = normalized.IndexOf(end, first, StringComparison.Ordinal);
            if (last < 0) throw new InvalidDataException("Incomplete legacy exclusion renderer patch.");
            normalized = normalized.Remove(first, last + "#endif\n\n".Length - first);
        }
        if (normalized.Contains("should_exclude_display_list", StringComparison.Ordinal))
            throw new InvalidDataException("Unrecognized legacy renderer hook; no files were changed.");
        // Avoid normalizing unrelated files when there was no legacy patch.
        return first < 0 ? source : source.Contains("\r\n") ? normalized.Replace("\n", "\r\n") : normalized;
    }

    private static uint Hash(string target, uint seed)
    {
        uint value = 2166136261U ^ seed;
        foreach (byte b in Encoding.UTF8.GetBytes(target)) value = unchecked((value ^ b) * 16777619U);
        return value;
    }

    // Keep positions intact while masking comments, strings, characters and preprocessor directives.
    private static string CodeMask(string source)
    {
        char[] code = source.ToCharArray();
        for (int i = 0; i < code.Length; i++)
        {
            int start = i;
            if (source[i] == '/' && i + 1 < source.Length && source[i + 1] == '*')
            {
                int end = source.IndexOf("*/", i + 2, StringComparison.Ordinal);
                if (end < 0) throw new InvalidDataException("Unterminated C comment.");
                i = end + 1;
            }
            else if (source[i] == '/' && i + 1 < source.Length && source[i + 1] == '/')
            {
                while (i + 1 < source.Length && source[i + 1] != '\n') i++;
            }
            else if (source[i] is '"' or '\'')
            {
                char quote = source[i++];
                for (; i < source.Length && source[i] != quote; i++) if (source[i] == '\\') i++;
                if (i >= source.Length) throw new InvalidDataException("Unterminated C literal.");
            }
            else if (source[i] == '#' && IsDirectiveStart(source, i))
            {
                while (i + 1 < source.Length)
                {
                    if (source[i + 1] == '\n' && source[i] != '\\' && !(source[i] == '\r' && i > 0 && source[i - 1] == '\\')) break;
                    i++;
                }
            }
            else continue;
            for (int j = start; j <= i; j++) if (code[j] is not ('\r' or '\n')) code[j] = ' ';
        }
        return new string(code);
    }

    private static bool IsDirectiveStart(string source, int position)
    {
        for (int i = position - 1; i >= 0 && source[i] != '\n'; i--)
            if (source[i] is not (' ' or '\t' or '\r')) return false;
        return true;
    }

    private static bool HasBom(byte[] data) => data.Length >= 3 && data[0] == 239 && data[1] == 187 && data[2] == 191;
    private static string Decode(byte[] data) => new UTF8Encoding(false, true).GetString(data, HasBom(data) ? 3 : 0, data.Length - (HasBom(data) ? 3 : 0));

    private static void Commit(List<Change> changes)
    {
        var staged = new Dictionary<string, string>();
        var written = new List<Change>();
        try
        {
            foreach (var change in changes)
            {
                string path = change.Path + ".randm64-" + Guid.NewGuid().ToString("N") + ".tmp";
                staged.Add(change.Path, path);
                File.WriteAllBytes(path, change.After);
            }
            foreach (var change in changes)
                if (!File.ReadAllBytes(change.Path).SequenceEqual(change.Before))
                    throw new IOException("A source file changed while exclusions were staged; retry after saving your edits.");
            foreach (var change in changes)
            {
                if (!File.ReadAllBytes(change.Path).SequenceEqual(change.Before))
                    throw new IOException("External edits prevented replacement of " + change.Path);
                // Backups are retained for recovery; reapply/restore use markers, never stale .bak contents.
                if (!File.Exists(change.Path + ".bak")) File.Copy(change.Path, change.Path + ".bak");
                // ReplaceFile is unsupported by WSL's UNC provider (ERROR_NOT_SUPPORTED).
                // Keep staging in the same directory and rename over the destination.
                File.Move(staged[change.Path], change.Path, overwrite: true);
                written.Add(change);
            }
        }
        catch (Exception error)
        {
            var failures = new List<Exception> { error };
            foreach (var change in written.AsEnumerable().Reverse())
            {
                try
                {
                    if (!File.ReadAllBytes(change.Path).SequenceEqual(change.After))
                        throw new IOException("External edits prevented rollback of " + change.Path);
                    File.WriteAllBytes(staged[change.Path], change.Before);
                    File.Move(staged[change.Path], change.Path, overwrite: true);
                }
                catch (Exception rollback) { failures.Add(rollback); }
            }
            throw new AggregateException("Display-list exclusion failed; " + (failures.Count == 1 ? "committed source edits were rolled back." : "some files could not be rolled back."), failures);
        }
        finally { foreach (string path in staged.Values) if (File.Exists(path)) File.Delete(path); }
    }
}
