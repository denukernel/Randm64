using System.IO;

namespace Sm64DecompLevelViewer.Services.Build;

internal static class BaseRomSelection
{
    public static string Destination(string root, string version)
    {
        if (version.Length == 0 || version.Any(c => !char.IsAsciiLetterOrDigit(c)))
            throw new InvalidOperationException("Invalid ROM region.");
        return Path.Combine(root, $"baserom.{version}.z64");
    }

    // Stage and validate before touching an existing base ROM. Never modify the source.
    public static void Import(string source, string root, string version, bool replace = false)
    {
        string destination = Destination(root, version);
        string staged = Path.Combine(root, ".randm64-rom-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.Copy(source, staged);
            using (var stream = File.OpenRead(staged))
            {
                Span<byte> header = stackalloc byte[64];
                if (stream.Read(header) != header.Length || header[0] != 0x80 || header[1] != 0x37
                    || header[2] != 0x12 || header[3] != 0x40)
                    throw new InvalidDataException("Select a big-endian Nintendo 64 ROM in .z64 format.");
                byte country = version switch { "us" => (byte)'E', "jp" => (byte)'J', "eu" => (byte)'P', _ => 0 };
                if (country != 0 && header[0x3E] != country)
                    throw new InvalidDataException($"This ROM does not match the selected {version} region. Change Version or select the matching ROM.");
            }
            if (Path.GetFullPath(source).Equals(Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
                return;
            File.Move(staged, destination, replace);
        }
        finally
        {
            if (File.Exists(staged)) File.Delete(staged);
        }
    }
}
