using System.Diagnostics;
using System.IO;

namespace Sm64DecompLevelViewer.Services.Build;

internal static class BuildProcessFactory
{
    internal static (string? Distribution, string Path) WslLocation(string root)
    {
        string path = Path.GetFullPath(root);
        if (path.StartsWith(@"\\wsl.localhost\", StringComparison.OrdinalIgnoreCase) || path.StartsWith(@"\\wsl$\", StringComparison.OrdinalIgnoreCase))
        {
            var parts = path.Split('\\', StringSplitOptions.RemoveEmptyEntries);
            return (parts[1], "/" + string.Join("/", parts.Skip(2)));
        }
        if (path.Length >= 2 && path[1] == ':') path = "/mnt/" + char.ToLowerInvariant(path[0]) + path[2..].Replace('\\', '/');
        return (null, path);
    }

    public static ProcessStartInfo Create(string root, AppSettings settings, string command)
    {
        var info = new ProcessStartInfo
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8, StandardErrorEncoding = System.Text.Encoding.UTF8,
        };
        if (settings.BuildTargetPlatform == "PC" && settings.BuildEnvironment == "MSYS2")
        {
            info.FileName = Path.Combine(settings.MsysPath, "usr", "bin", "bash.exe");
            if (!File.Exists(info.FileName)) throw new FileNotFoundException("Check the MSYS2 path: bash.exe was not found.", info.FileName);
            string path = Path.GetFullPath(root).Replace('\\', '/');
            if (path.Length >= 2 && path[1] == ':') path = "/" + char.ToLowerInvariant(path[0]) + path[2..];
            info.Environment["MSYSTEM"] = "MINGW64";
            info.ArgumentList.Add("-l"); info.ArgumentList.Add("-c");
            info.ArgumentList.Add("cd " + BuildCommandFactory.Quote(path) + " && " + command);
        }
        else
        {
            info.FileName = "wsl.exe";
            var location = WslLocation(root);
            if (location.Distribution != null)
            {
                info.ArgumentList.Add("--distribution"); info.ArgumentList.Add(location.Distribution);
            }
            info.ArgumentList.Add("--cd"); info.ArgumentList.Add(location.Path);
            info.ArgumentList.Add("--exec"); info.ArgumentList.Add("sh"); info.ArgumentList.Add("-c"); info.ArgumentList.Add(command);
        }
        return info;
    }
}
