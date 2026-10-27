using System.Diagnostics;

namespace Sm64DecompLevelViewer.Services;

public static class SourceRevertCommand
{
    public static ProcessStartInfo Create(string projectRoot)
    {
        var project = WslPatchCommand.Resolve(projectRoot);
        var info = new ProcessStartInfo("wsl.exe")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        if (project.Distribution != null)
        {
            info.ArgumentList.Add("--distribution");
            info.ArgumentList.Add(project.Distribution);
        }
        info.ArgumentList.Add("--exec");
        info.ArgumentList.Add("sh");
        info.ArgumentList.Add("-c");
        // Pass the path as an argument, never interpolate it into shell code. Verify that
        // the selected directory is the repository root before any destructive command.
        info.ArgumentList.Add("set -eu; cd -- \"$1\"; " +
            "test \"$(git rev-parse --show-toplevel)\" = \"$(pwd -P)\" || { echo \"select the repository root to revert source\" >&2; exit 1; }; " +
            "git checkout -f; git clean -fd -e 'baserom.*'; " +
            "echo 'source reset complete; extracting US assets'; python3 extract_assets.py us");
        info.ArgumentList.Add("randm64-revert");
        info.ArgumentList.Add(project.LinuxPath);
        return info;
    }
}
