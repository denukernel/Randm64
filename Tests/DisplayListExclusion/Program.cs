using System.Text;
using System.Text.RegularExpressions;
using Sm64DecompLevelViewer.Services.Chaos;

static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
static void Reject(Action action, string message)
{
    try { action(); } catch (Exception ex) when (ex is InvalidDataException or AggregateException) { return; }
    throw new Exception(message);
}

string source = """
// gsSPDisplayList(comment_only)
/* gsSPBranchList(comment_tail) */
const char *text = "gsSPDisplayList(string_only)";
  #define OMITTED gsSPDisplayList(macro_body)
Gfx example[] = {
    gsSPDisplayList( a ),
    gsSPDisplayList(a /* same target */),
    gsSPDisplayList((Gfx *) (b + 2)),
    gsSPBranchList(a),
    gsSPEndDisplayList(),
};
""".Replace("\n", "\r\n");
uint seed = 0;
while (DisplayListExclusionService.Transform(source, 9, seed, true).Excluded != 4) seed++;
var result = DisplayListExclusionService.Transform(source, 9, seed, true);
Check(result.Calls == 4, "Comments, literals or directives treated as commands");
Check(result.Source.Contains("*/ gsSPEndDisplayList()"), "Excluded tail did not terminate");
Check(Regex.Matches(result.Source, @"\*/ gsDPNoOp\(\)").Count == 3, "Calls did not emit no-ops");
Check(DisplayListExclusionService.Restore(result.Source) == source, "Restore was not exact");
Check(DisplayListExclusionService.Transform(result.Source, 9, seed, true).Source == result.Source, "Reapply stacked exclusions");
Check(DisplayListExclusionService.Transform(result.Source, 0, seed, true).Source == source, "Level zero failed to remove exclusions");
Check(DisplayListExclusionService.Transform(source, 9, seed, false).Source.Contains("gsSPBranchList(a)"), "Branch opt-out ignored");
Check(DisplayListExclusionService.Transform(source, 9, seed, true).Source == result.Source, "Seed is not deterministic");
Reject(() => DisplayListExclusionService.Restore(result.Source.Replace("*/ gsDPNoOp()", "*/ gsSPDisplayList(external_edit)")), "Edited marker overwritten");
Reject(() => DisplayListExclusionService.Transform("gsSPDisplayList((broken)", 9, seed, true), "Unbalanced call accepted");
Reject(() => DisplayListExclusionService.Transform("/* broken", 9, seed, true), "Unterminated comment accepted");

string population = string.Join("\n", Enumerable.Range(0, 1000).Select(i => $"gsSPDisplayList(target_{i}),"));
int previous = 0;
for (int level = 0; level <= 9; level++)
{
    var sample = DisplayListExclusionService.Transform(population, level, 1234, true);
    Check(sample.Excluded >= previous, "Density is not monotonic"); previous = sample.Excluded;
}
Check(previous is > 820 and < 960, "Density distribution is implausible");
Check(DisplayListExclusionService.Transform(population, 5, 1, true).Source != DisplayListExclusionService.Transform(population, 5, 2, true).Source, "Changing seed did not change pattern");

bool ioOnly = args.Length == 2 && args[0] == "--io-root";
string testBase = ioOnly ? args[1] : AppContext.BaseDirectory;
string root = Path.Combine(testBase, "dl-exclusion-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(Path.Combine(root, "include", "PR"));
Directory.CreateDirectory(Path.Combine(root, "actors"));
Directory.CreateDirectory(Path.Combine(root, "src", "game"));
string gbi = Path.Combine(root, "include", "PR", "gbi.h");
string graph = Path.Combine(root, "src", "game", "rendering_graph_node.c");
string actor = Path.Combine(root, "actors", "a.inc.c");
string other = Path.Combine(root, "actors", "b.inc.c");
string header = "#define gsSPDisplayList(dl) CALL(dl)\n#define gsSPBranchList(dl) BRANCH(dl)\n#define gsDPNoOp() NOP\n#define gsSPEndDisplayList() END\n";
try
{
    File.WriteAllText(gbi, header);
    File.WriteAllText(graph, "/* unrelated renderer edits */\n");
    File.WriteAllText(actor, source, new UTF8Encoding(true));
    File.WriteAllText(other, source);
    byte[] baseline = File.ReadAllBytes(actor);
    var applied = DisplayListExclusionService.Apply(root, 9, seed, true);
    Check(applied.Excluded == 8 && applied.Files == 2, "Incorrect apply coverage");
    Check(File.ReadAllBytes(actor + ".bak").SequenceEqual(baseline), "Original backup not preserved");
    File.AppendAllText(actor, "\r\n/* later external edit */");
    DisplayListExclusionService.Apply(root, 0, 0, true);
    Check(File.ReadAllText(actor) == source + "\r\n/* later external edit */", "Restore discarded unrelated edits");
    Check(File.ReadAllBytes(actor).Take(3).SequenceEqual(new byte[] { 239, 187, 191 }), "UTF-8 BOM lost");
    Check(File.ReadAllText(graph) == "/* unrelated renderer edits */\n", "Renderer overwritten");
    // Fail the second replacement after the first has committed; verify rollback.
    byte[] beforeActor = File.ReadAllBytes(actor), beforeOther = File.ReadAllBytes(other);
    using (var locked = new FileStream(other, FileMode.Open, FileAccess.Read, FileShare.Read))
        Reject(() => DisplayListExclusionService.Apply(root, 9, seed, true), "Locked destination unexpectedly replaced");
    Check(File.ReadAllBytes(actor).SequenceEqual(beforeActor) && File.ReadAllBytes(other).SequenceEqual(beforeOther), "Failed batch did not roll back");
    Check(!Directory.EnumerateFiles(root, "*.tmp", SearchOption.AllDirectories).Any(), "Staged files leaked");
    Check(File.ReadAllBytes(actor + ".bak").SequenceEqual(baseline), "Reapply replaced first backup");

    const string legacy = "#if defined(_LANGUAGE_C) || defined(_LANGUAGE_C_DIRECT)\n#include \"chaos_config.h\"\n#ifdef CHAOS_DL_EXCLUSION_LEVEL\nextern int should_exclude_display_list(const void *dl);\n#define gSPDisplayList(pkt,dl) gDma1p(pkt, should_exclude_display_list((const void *)(dl)) ? G_NOOP : G_DL, dl, 0, G_DL_PUSH)\n#else\n#define gSPDisplayList(pkt,dl) gDma1p(pkt,G_DL,dl,0,G_DL_PUSH)\n#endif\n#else\n#define gSPDisplayList(pkt,dl) gDma1p(pkt,G_DL,dl,0,G_DL_PUSH)\n#endif\n";
    Check(!DisplayListExclusionService.RemoveLegacyHeader(legacy).Contains("should_exclude"), "Legacy GBI hook retained");
    Reject(() => DisplayListExclusionService.RemoveLegacyHeader(legacy.Replace("extern int", "extern signed int")), "Unknown modified hook overwritten");
    const string legacyGraph = "/* keep */\n#include \"chaos_config.h\"\n\n#ifdef CHAOS_DL_EXCLUSION_LEVEL\nextern u32 gGlobalTimer;\nint should_exclude_display_list(const void *dl) { return 0; }\n#endif\n\nstatic void geo_append_display_list(void *displayList, s16 layer) {\n/* keep body */\n}\n";
    string cleaned = DisplayListExclusionService.RemoveLegacyGraph(legacyGraph);
    Check(!cleaned.Contains("should_exclude") && cleaned.Contains("/* keep */") && cleaned.Contains("/* keep body */"), "Legacy cleanup removed unrelated source");
}
finally
{
    if (Path.GetFullPath(root).StartsWith(Path.GetFullPath(testBase).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) Directory.Delete(root, true);
}

if (args.Length > 0 && !ioOnly)
{
    int files = 0, calls = 0, excluded = 0;
    foreach (string folder in new[] { "actors", "levels" })
        foreach (string path in Directory.EnumerateFiles(Path.Combine(args[0], folder), "*.c", SearchOption.AllDirectories))
        {
            string input = File.ReadAllText(path);
            var transformed = DisplayListExclusionService.Transform(input, 5, 1234, true);
            Check(DisplayListExclusionService.Restore(transformed.Source) == input, "Real-source restore mismatch: " + path);
            calls += transformed.Calls; excluded += transformed.Excluded; files++;
        }
    Check(calls > 100 && excluded > 0 && excluded < calls, "Real display-list coverage missing");
    Console.WriteLine($"Read-only SM64 check: {files} source files, {calls} call/branch sites, {excluded} excluded at 50% target-hash density.");
}

string cFixture = "#include <ultra64.h>\nGfx target[] = { gsSPEndDisplayList() };\nGfx caller[] = { gsSPDisplayList(target), gsSPBranchList(target), gsSPEndDisplayList() };\n";
uint cSeed = 0;
while (DisplayListExclusionService.Transform(cFixture, 9, cSeed, true).Excluded != 2) cSeed++;
File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "excluded_gbi_fixture.c"), DisplayListExclusionService.Transform(cFixture, 9, cSeed, true).Source);
Console.WriteLine("PASS: actual call/branch replacement, density/seed, comments/literals/directives, exact restore, external edits, staged rollback and legacy cleanup.");
