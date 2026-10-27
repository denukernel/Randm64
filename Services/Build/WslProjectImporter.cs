using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace Sm64DecompLevelViewer.Services.Build;

internal static class WslProjectImporter
{
    internal static bool ShouldOffer(string root, AppSettings settings)
    {
        if (settings.BuildTargetPlatform == "PC" && settings.BuildEnvironment == "MSYS2") return false;
        string path = Path.GetFullPath(root);
        return path.Length > 2 && path[1] == ':' && File.Exists(Path.Combine(path, "Makefile"))
            && Directory.Exists(Path.Combine(path, "levels"));
    }

    public static async Task<string> ImportAsync(string source)
    {
        var info = new ProcessStartInfo("wsl.exe")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
        };
        // ArgumentList passes paths and the script literally, without a shell interpreting them.
        foreach (string argument in new[] { "--cd", "/", "--exec", "python3", "-c", ImportPython,
            BuildProcessFactory.WslLocation(source).Path }) info.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = info };
        process.Start();
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> errors = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        string result = await output;
        string error = await errors;
        if (process.ExitCode != 0)
            throw new IOException("could not import the project into wsl. the windows folder is unchanged.\n" + error);
        using var json = JsonDocument.Parse(result);
        string distribution = json.RootElement.GetProperty("distribution").GetString()!;
        string path = json.RootElement.GetProperty("path").GetString()!;
        if (string.IsNullOrWhiteSpace(distribution) || distribution.IndexOfAny(new[] { '/', '\\' }) >= 0
            || !path.StartsWith('/') || path.StartsWith("/mnt/", StringComparison.Ordinal))
            throw new IOException("wsl returned an invalid project location. the windows folder is unchanged.");
        string target = @"\\wsl.localhost\" + distribution + path.Replace('/', '\\');
        if (!File.Exists(Path.Combine(target, "Makefile")))
            throw new IOException("the imported project could not be opened from windows: " + target);
        return target;
    }

    // Copy every file, including hidden files, backups, ROMs and version control metadata.
    // A unique staging directory prevents overwrites. Only a complete copy is published.
    // Never delete or alter the selected source, including when copying fails.
    internal const string ImportPython = """
        import json, os, pathlib, shutil, sys, tempfile, uuid
        source = pathlib.Path(sys.argv[1]).resolve(strict=True)
        if not (source / 'Makefile').is_file() or not (source / 'levels').is_dir():
            raise RuntimeError('select an sm64 repository containing Makefile and levels')
        distribution = os.environ.get('WSL_DISTRO_NAME')
        if not distribution:
            raise RuntimeError('could not identify the wsl distribution')
        parent = (pathlib.Path.home() / 'randm64' / 'projects').resolve()
        if str(parent).startswith('/mnt/'):
            raise RuntimeError('the wsl home directory must be on the linux filesystem')
        parent.mkdir(parents=True, exist_ok=True)
        if source == parent or source in parent.parents:
            raise RuntimeError('the import destination must be outside the source repository')
        stage = pathlib.Path(tempfile.mkdtemp(prefix='.import-', dir=parent))
        target = parent / (source.name + '-' + uuid.uuid4().hex)
        try:
            shutil.copytree(source, stage / 'repository', symlinks=True, copy_function=shutil.copy2)
            os.rename(stage / 'repository', target)
        except BaseException:
            # stage is owned by this invocation; symlinks inside it are not followed.
            shutil.rmtree(stage, ignore_errors=True)
            raise
        stage.rmdir()
        print(json.dumps({'distribution': distribution, 'path': str(target)}))
        """;
}
