using System.IO;
using System.Text.RegularExpressions;

namespace Sm64DecompLevelViewer.Services.Build;

internal static class ColdToolBootstrap
{
    // Several SM64 makefiles build their tools while parsing the main makefile. An explicit
    // parallel bootstrap supplies a jobserver to recursive tool builds, including IDO setup.
    // Leave unrecognized tool layouts to the repository instead of guessing their targets.
    public static string Command(RepositoryBuildProfile profile, AppSettings settings, bool cleaned)
    {
        if (settings.BuildTargetPlatform == "PC" && settings.BuildEnvironment == "MSYS2") return "";
        string makefile = Path.Combine(profile.Root, "tools", "Makefile");
        if (!File.Exists(makefile)) return "";
        bool ido = settings.BuildTargetPlatform == "N64" && settings.BuildCompiler == "ido"
            && profile.Evaluate(settings).GetValueOrDefault("USE_QEMU_IRIX", "0") == "0";
        bool missing = !File.Exists(Path.Combine(profile.Root, "tools", "textconv"))
            || ido && Directory.Exists(Path.Combine(profile.Root, "tools", "ido-static-recomp"))
                && !File.Exists(Path.Combine(profile.Root, "tools", "ido-static-recomp", "build", "out", "cc"));
        if (!cleaned && !missing) return "";
        string source = File.ReadAllText(makefile);
        if (!source.Contains("textconv") || !Regex.IsMatch(source, @"(?m)^all\s*:")) return "";
        string target = !ido && Regex.IsMatch(source, @"(?m)^all-except-recomp\s*:") ? "all-except-recomp" : "all";
        var commands = new List<string> { $"make -j{BuildCommandFactory.Jobs(settings)} -C 'tools' {target}" };
        string secondary = Path.Combine(profile.Root, "tools", "sm64tools", "Makefile");
        if (File.Exists(secondary)) commands.Add($"make -j{BuildCommandFactory.Jobs(settings)} -C 'tools/sm64tools'");
        return string.Join(" && ", commands);
    }
}
