using System.IO;
using System.Text.Json;

namespace Sm64DecompLevelViewer.Services.Build;

internal static class BuildConfigurationState
{
    public static string RepositoryKey(string root) => Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant();
    public static string EnvironmentKey(AppSettings settings) => settings.BuildTargetPlatform == "PC" && settings.BuildEnvironment == "MSYS2"
        ? "MSYS2:" + Path.GetFullPath(settings.MsysPath).ToUpperInvariant() : "WSL";
    public static string Signature(RepositoryBuildProfile profile, AppSettings settings) => string.Join("|",
        profile.Fingerprint, EnvironmentKey(settings), settings.BuildTargetPlatform, settings.BuildVersion,
        settings.BuildCompiler, settings.BuildGrucode, settings.BuildNonMatching);

    // A failed attempt can leave objects with different flags from the last successful build.
    private static string DirectoryFor(RepositoryBuildProfile profile, AppSettings settings)
    {
        string directory = Path.GetDirectoryName(profile.ArtifactPath(settings))!;
        string declared = profile.Evaluate(settings).GetValueOrDefault("BUILD_DIR", "");
        string buildDirectory = Path.GetFullPath(Path.Combine(profile.Root, declared.Replace('/', Path.DirectorySeparatorChar)));
        if (declared.Length == 0 || declared.Contains('$') || !directory.StartsWith(buildDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && directory != buildDirectory)
            throw new InvalidOperationException("Cannot safely resolve the repository build directory.");
        if (!buildDirectory.StartsWith(profile.Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Build outputs must be in a repository subdirectory.");
        for (string? path = buildDirectory; path != null && path != profile.Root; path = Path.GetDirectoryName(path))
            if (Directory.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Cannot prepare a build directory containing a symbolic link or junction.");
        return buildDirectory;
    }

    public static bool Prepare(RepositoryBuildProfile profile, AppSettings settings)
    {
        string buildDirectory = DirectoryFor(profile, settings);
        string marker = Path.Combine(buildDirectory, ".randm64-config.json");
        string signature = Signature(profile, settings);
        string? old = File.Exists(marker) ? JsonSerializer.Deserialize<string>(File.ReadAllText(marker)) : null;
        bool incompatible = old != signature && Directory.Exists(buildDirectory) && Directory.EnumerateFileSystemEntries(buildDirectory).Any();
        if (incompatible)
        {
            string buildRoot = Path.Combine(profile.Root, "build") + Path.DirectorySeparatorChar;
            if (!buildDirectory.StartsWith(buildRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Build settings changed for a custom output directory. Clean that directory with the repository build instructions, then retry.");
            // Inspect each level before descending; never traverse a junction during validation.
            var pending = new Stack<string>();
            pending.Push(buildDirectory);
            while (pending.Count > 0)
                foreach (string entry in Directory.EnumerateFileSystemEntries(pending.Pop()))
                {
                    var attributes = File.GetAttributes(entry);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                        throw new InvalidOperationException("Cannot clean a build directory containing a symbolic link or junction.");
                    if ((attributes & FileAttributes.Directory) != 0) pending.Push(entry);
                }
            Directory.Delete(buildDirectory, true);
        }
        Directory.CreateDirectory(buildDirectory);
        if (old != signature || incompatible) File.WriteAllText(marker, JsonSerializer.Serialize(signature));
        return incompatible;
    }

    public static async Task<bool> PrepareWslAsync(RepositoryBuildProfile profile, AppSettings settings, Action<string> log)
    {
        string directory = DirectoryFor(profile, settings);
        string marker = Path.Combine(directory, ".randm64-config.json");
        string signature = Signature(profile, settings);
        if (File.Exists(marker) && JsonSerializer.Deserialize<string>(File.ReadAllText(marker)) == signature) return false;
        string expected = JsonSerializer.Serialize(signature);
        string root = BuildProcessFactory.WslLocation(profile.Root).Path;
        string target = BuildProcessFactory.WslLocation(directory).Path;
        string command = "python3 - " + BuildCommandFactory.Quote(root) + " " + BuildCommandFactory.Quote(target) + " "
            + BuildCommandFactory.Quote(expected) + " <<'RANDM64_PREPARE_PY'\n" + PreparePython + "\nRANDM64_PREPARE_PY";
        bool cleaned = false;
        var result = await WslBuildSessions.RunAsync(profile.Root, command, line =>
        {
            if (line.Trim() == "RANDM64_CONFIG_CLEANED") cleaned = true;
            else if (!string.IsNullOrWhiteSpace(line)) log(line);
        });
        if (result.ExitCode != 0) throw new InvalidOperationException("Could not safely prepare the Linux build directory. See the preparation error above.");
        return cleaned;
    }

    private const string PreparePython = """
        import json, os, shutil, sys
        root, target = os.path.abspath(sys.argv[1]), os.path.abspath(sys.argv[2])
        if target == root or os.path.commonpath([root, target]) != root:
            raise RuntimeError('Build output must be inside the selected repository')
        current = target
        while current != root:
            if os.path.islink(current):
                raise RuntimeError('Cannot prepare a build directory containing a symbolic link')
            current = os.path.dirname(current)
        marker = os.path.join(target, '.randm64-config.json')
        expected = json.loads(sys.argv[3])
        old = None
        if os.path.isfile(marker):
            with open(marker) as stream:
                old = json.load(stream)
        incompatible = old != expected and os.path.isdir(target) and bool(os.listdir(target))
        if incompatible:
            base = os.path.join(root, 'build')
            if target == base or os.path.commonpath([base, target]) != base:
                raise RuntimeError('Clean the custom output directory using the repository instructions, then retry')
            for parent, directories, files in os.walk(target, followlinks=False):
                if any(os.path.islink(os.path.join(parent, entry)) for entry in directories + files):
                    raise RuntimeError('Cannot clean a build directory containing a symbolic link')
            shutil.rmtree(target)
            print('RANDM64_CONFIG_CLEANED')
        os.makedirs(target, exist_ok=True)
        if old != expected or incompatible:
            with open(marker, 'w') as stream:
                json.dump(expected, stream)
        """;

    public static void RecordSuccess(RepositoryBuildProfile profile, AppSettings settings)
    {
        settings.RepositoryBuildRecords ??= new();
        settings.RepositoryBuildRecords[RepositoryKey(profile.Root)] = new RepositoryBuildRecord
        {
            Configuration = Signature(profile, settings), Environment = EnvironmentKey(settings),
            Artifact = profile.ArtifactPath(settings), CompletedUtc = DateTime.UtcNow,
        };
    }
}

public sealed class RepositoryBuildRecord
{
    public string Configuration { get; set; } = "";
    public string Environment { get; set; } = "";
    public string Artifact { get; set; } = "";
    public DateTime CompletedUtc { get; set; }
}
