using System.ComponentModel;

namespace Sm64DecompLevelViewer.Services;

public static class BuildFailureDiagnostics
{
    public static bool ShouldSuggestWslInstall(Exception error)
    {
        if (error is not Win32Exception native || native.NativeErrorCode is not (2 or 3)) return false;
        return new[] { "'wsl'", "'wsl.exe'", "\"wsl\"", "\"wsl.exe\"" }
            .Any(name => native.Message.Contains(name, StringComparison.OrdinalIgnoreCase));
    }
}
