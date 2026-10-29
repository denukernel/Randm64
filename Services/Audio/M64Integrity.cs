using System.IO;

namespace Sm64DecompLevelViewer.Services.Audio;

public static class M64Integrity
{
    public static void RequireNonZero(string path, byte[] data)
    {
        if (data.Length == 0 || !data.Any(value => value != 0))
            throw new InvalidDataException($"The music sequence is empty or entirely zero bytes: {path}. Restore a valid backup or re-extract it from the matching base ROM.");
    }

    public static void ValidateBuildInputs(string root, string version)
    {
        string sequences = Path.Combine(root, "sound", "sequences");
        if (!Directory.Exists(sequences)) return;
        string regional = Path.Combine(sequences, version.Equals("cn", StringComparison.OrdinalIgnoreCase) ? "sh" : version);
        var files = Directory.EnumerateFiles(sequences, "*.m64", SearchOption.TopDirectoryOnly);
        if (Directory.Exists(regional)) files = files.Concat(Directory.EnumerateFiles(regional, "*.m64", SearchOption.TopDirectoryOnly));
        foreach (string path in files) RequireNonZero(path, File.ReadAllBytes(path));
    }
}
