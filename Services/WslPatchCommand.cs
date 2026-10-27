using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace Sm64DecompLevelViewer.Services;

public static class WslPatchCommand
{
    public static (string? Distribution, string LinuxPath) Resolve(string path)
    {
        string normalized = path.Replace('\\', '/');
        if (normalized.StartsWith("//wsl.localhost/", StringComparison.OrdinalIgnoreCase) || normalized.StartsWith("//wsl$/", StringComparison.OrdinalIgnoreCase))
        {
            int start = normalized.IndexOf('/', 2) + 1, end = normalized.IndexOf('/', start);
            if (end < 0) throw new ArgumentException("select a project directory inside the wsl distribution");
            return (normalized[start..end], normalized[end..]);
        }
        if (normalized.Length >= 3 && char.IsLetter(normalized[0]) && normalized[1] == ':' && normalized[2] == '/')
            return (null, "/mnt/" + char.ToLowerInvariant(normalized[0]) + normalized[2..]);
        throw new ArgumentException("unsupported project or patch path: " + path);
    }
    public static ProcessStartInfo Create(string projectRoot, string patchPath, bool git, bool check)
    {
        var project = Resolve(projectRoot); var patch = Resolve(patchPath);
        if (project.Distribution != null && patch.Distribution != null && !string.Equals(project.Distribution, patch.Distribution, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("the patch and project must be accessible from the same wsl distribution");
        var info = new ProcessStartInfo("wsl.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        string? distribution = project.Distribution ?? patch.Distribution;
        if (distribution != null) { info.ArgumentList.Add("--distribution"); info.ArgumentList.Add(distribution); }
        info.ArgumentList.Add("--exec");
        string[] arguments = git
            ? new[] { "git", "-C", project.LinuxPath, "apply", check ? "--check" : "--verbose", "--", patch.LinuxPath }
            : new[] { "patch", "--batch", "--forward", "-p1", "-d", project.LinuxPath, "-i", patch.LinuxPath };
        foreach (string argument in arguments) info.ArgumentList.Add(argument);
        if (!git && check) info.ArgumentList.Add("--dry-run");
        return info;
    }
}
