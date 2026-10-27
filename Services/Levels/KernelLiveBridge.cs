using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Sm64DecompLevelViewer.Models;

namespace Sm64DecompLevelViewer.Services.Levels;

internal sealed class KernelLiveBridge
{
    private readonly string _rom;
    private readonly string _hash;
    private readonly Dictionary<string, int> _symbols = new();
    private readonly Dictionary<LevelObject, int[]> _original = new();
    private readonly Dictionary<string, int> _levels = new();
    public KernelLiveBridge(string root, string rom, Dictionary<LevelObject, int[]> originals)
    {
        _rom = rom;
        _hash = Hash();
        var types = File.ReadAllText(Path.Combine(root, "include", "types.h"));
        if (!types.Contains("/*0x25C*/ void *respawnInfo") || !types.Contains("/*0x1F6*/ s16 respawnInfoType"))
            throw new IOException("Unsupported decomp object layout");
        foreach (Match m in Regex.Matches(File.ReadAllText(Path.ChangeExtension(rom, ".map")), @"0x([0-9a-fA-F]{8,16})\s+(\w+)\s*$", RegexOptions.Multiline))
            _symbols[m.Groups[2].Value] = unchecked((int)Convert.ToUInt64(m.Groups[1].Value, 16));
        int id = 0;
        foreach (var line in File.ReadLines(Path.Combine(root, "levels", "level_defines.h")))
        {
            if (Regex.IsMatch(line, @"^\s*(DEFINE_LEVEL|STUB_LEVEL)\("))
            {
                id++;
                var m = Regex.Match(line, @"DEFINE_LEVEL\([^,]+,[^,]+,[^,]+,\s*(\w+)");
                if (m.Success) _levels[m.Groups[1].Value] = id;
            }
        }
        foreach (var entry in originals) _original[entry.Key] = entry.Value;
        _ = Symbol("gObjectPool"); _ = Symbol("gCurrLevelNum"); _ = Symbol("gCurrAreaIndex");
    }
    public static Dictionary<LevelObject, int[]> Capture(IEnumerable<LevelObject> objects) => objects.Where(o => !o.IsNew && o.SourceType == ObjectSourceType.Normal).ToDictionary(o => o, o => new[] { o.X, o.Y, o.Z, Angle(o.RX), Angle(o.RY), Angle(o.RZ), unchecked((int)o.Params) });
    private string Hash() { using var stream = File.OpenRead(_rom); return Convert.ToHexString(SHA256.HashData(stream)); }
    private int Symbol(string name) => _symbols.TryGetValue(name, out var value) ? value : throw new IOException("Missing build symbol: " + name);
    private static int Angle(int degrees) => (short)(degrees * 65536L / 360);
    public async Task SendAsync(LevelObject obj, int area)
    {
        if (!_original.TryGetValue(obj, out var original) || obj.IsDeleted || obj.IsNew)
            throw new IOException("Select an existing normal object; macro, special, and Mario objects are unsupported");
        if (Hash() != _hash) throw new IOException("Build changed; disconnect and select the rebuilt ROM");
        var source = (obj.SourceFile ?? "").Replace('\\', '/');
        var level = Regex.Match(source, @"/levels/(\w+)/");
        if (!level.Success || !_levels.TryGetValue(level.Groups[1].Value, out var levelId)) throw new IOException("Cannot resolve the source level");
        int[] request = new int[24];
        request[0] = Symbol("gObjectPool"); request[1] = Symbol("gCurrLevelNum"); request[2] = Symbol("gCurrAreaIndex");
        request[3] = levelId; request[4] = area; request[5] = Symbol(obj.Behavior); request[6] = original[6];
        Array.Copy(original, 0, request, 7, 6);
        request[13] = obj.X; request[14] = obj.Y; request[15] = obj.Z;
        request[16] = Angle(obj.RX); request[17] = Angle(obj.RY); request[18] = Angle(obj.RZ);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", 64640, timeout.Token);
        using var stream = client.GetStream();
        using var payload = new MemoryStream();
        void Word(int value) { Span<byte> bytes = stackalloc byte[4]; System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes, value); payload.Write(bytes); }
        Word(0x524D3634); Word(1);
        var hashBytes = Encoding.ASCII.GetBytes(_hash); payload.WriteByte(0); payload.WriteByte((byte)hashBytes.Length); payload.Write(hashBytes);
        foreach (int value in request) Word(value);
        await stream.WriteAsync(payload.ToArray(), timeout.Token);
        var prefix = new byte[2]; await stream.ReadExactlyAsync(prefix, timeout.Token);
        var reply = new byte[(prefix[0] << 8) | prefix[1]]; await stream.ReadExactlyAsync(reply, timeout.Token);
        var status = Encoding.UTF8.GetString(reply);
        if (status != "ok") throw new IOException(status);
    }
}
