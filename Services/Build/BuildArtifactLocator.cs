using System.IO;

namespace Sm64DecompLevelViewer.Services.Build;

internal sealed class BuildArtifactLocator
{
    private readonly RepositoryBuildProfile _profile;
    private readonly AppSettings? _settings;
    public BuildArtifactLocator(string root, AppSettings? settings) : this(RepositoryBuildProfile.Load(root), settings) { }
    internal BuildArtifactLocator(RepositoryBuildProfile profile, AppSettings? settings) { _profile = profile; _settings = settings; }
    internal bool RomExists() => FindArtifactPath() != null;
    internal string? FindBuiltRomPath() => _settings?.BuildTargetPlatform == "N64" ? FindArtifactPath() : null;
    internal string? FindArtifactPath()
    {
        if (_settings == null || _profile.Validate(_settings) != null) return null;
        string path = _profile.ArtifactPath(_settings);
        if (!File.Exists(path)) return null;
        if (_settings.BuildTargetPlatform == "PC")
        {
            using var stream = File.OpenRead(path);
            Span<byte> magic = stackalloc byte[4];
            if (stream.Read(magic) != 4) return null;
            bool native = _settings.BuildEnvironment == "MSYS2" ? magic[0] == 'M' && magic[1] == 'Z'
                : magic.SequenceEqual(new byte[] { 0x7f, (byte)'E', (byte)'L', (byte)'F' });
            if (!native) return null;
        }
        return path;
    }
}
