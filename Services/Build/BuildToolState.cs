using System.IO;

namespace Sm64DecompLevelViewer.Services.Build;

internal static class BuildToolState
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> InvalidatedHosts = new(StringComparer.OrdinalIgnoreCase);
    public static void Invalidate(string root) => InvalidatedHosts[BuildConfigurationState.RepositoryKey(root)] = 0;
    public static void Remember(string root) => InvalidatedHosts.TryRemove(BuildConfigurationState.RepositoryKey(root), out _);
    private static readonly string[] Probes =
    {
        "tools/armips", "tools/textconv", "tools/aiff_extract_codebook", "tools/vadpcm_enc",
        "tools/mio0", "tools/n64graphics", "tools/sm64tools/mio0", "tools/sm64tools/n64graphics",
        "tools/ido-static-recomp/build/out/cc", "tools/ido-static-recomp/build/libc_impl.o",
        "tools/audiofile/audiofile.o", "tools/sm64tools/libmio0.o",
    };

    public static bool NeedsClean(string root, AppSettings settings)
    {
        string marker = Path.Combine(root, ".randm64-tools-host");
        string? previous = File.Exists(marker) ? File.ReadAllText(marker).Trim() : null;
        string environment = BuildConfigurationState.EnvironmentKey(settings);
        if (previous == environment && !InvalidatedHosts.ContainsKey(BuildConfigurationState.RepositoryKey(root))) return false;
        if (!string.IsNullOrEmpty(previous) && previous != "pending" && previous != environment) return true;

        // No marker is not evidence of incompatibility. Inspect existing binaries, including
        // partial builds left by the old "pending" marker, and let make rebuild missing tools.
        bool windows = environment.StartsWith("MSYS2:", StringComparison.Ordinal);
        foreach (string relative in Probes)
            {
                string stem = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
                bool executable = !relative.EndsWith(".o", StringComparison.Ordinal);
                string preferred = windows && executable ? stem + ".exe" : stem;
                string alternate = preferred == stem ? stem + ".exe" : stem;
                string file = File.Exists(preferred) ? preferred : alternate;
                if (!File.Exists(file)) continue;
                using var stream = File.OpenRead(file);
                Span<byte> header = stackalloc byte[4];
                if (stream.Read(header) < 4) continue;
                bool elf = header[0] == 0x7f && header[1] == 'E' && header[2] == 'L' && header[3] == 'F';
                bool pe = header[0] == 'M' && header[1] == 'Z';
                bool coff = Path.GetExtension(file) == ".o" && (header[0] == 0x64 && header[1] == 0x86 || header[0] == 0x4c && header[1] == 0x01);
                if (windows ? elf : pe || coff) return true;
            }
        return false;
    }

    public static string PreparationCommand(string root, AppSettings settings, bool clean)
    {
        string marker = Path.Combine(root, ".randm64-tools-host");
        string environment = BuildConfigurationState.EnvironmentKey(settings);
        if (!clean && File.Exists(marker) && File.ReadAllText(marker).Trim() == environment) return ":";
        var commands = new List<string>();
        if (clean)
            foreach (string directory in new[] { "tools", "tools/sm64tools" })
                if (File.Exists(Path.Combine(root, directory, "Makefile")))
                    commands.Add("make -C " + BuildCommandFactory.Quote(directory) + " clean");
        // Record the host after cleanup succeeds and before target compilation. A later ROM
        // compile failure must not clean already rebuilt host tools on the next attempt.
        commands.Add("printf '%s' " + BuildCommandFactory.Quote(environment)
            + " > '.randm64-tools-host'");
        return string.Join(" && ", commands);
    }
}
