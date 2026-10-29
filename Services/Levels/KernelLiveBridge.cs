using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Sm64DecompLevelViewer.Models;

namespace Sm64DecompLevelViewer.Services.Levels;

internal sealed class LiveBridgeUnavailableException : IOException
{
    public string Status { get; }
    public LiveBridgeUnavailableException(Exception cause, string? status = null) : base(status ?? "KernelN64 is not accepting live editing connections at 127.0.0.1:64640. Open the updated KernelN64 build and keep it running. Live editing will retry automatically.", cause) { Status = status ?? "Waiting for KernelN64. Open the updated emulator build. Retrying automatically..."; }
}
internal sealed record LiveObjectSnapshot(int[] Transform, ObjectSourceType SourceType, string Behavior, string Preset, string? RawParams, string Model = "");
internal sealed class KernelLiveBridge
{
    private readonly int _port;
    private readonly string _rom;
    private readonly string _hash;
    private readonly Dictionary<string, int> _symbols = new();
    private readonly Dictionary<LevelObject, LiveObjectSnapshot> _original = new();
    private readonly Dictionary<LevelObject, int[]> _specials = new();
    private readonly Dictionary<LevelObject, int[]> _lastSpecialTransforms = new();
    private readonly Dictionary<LevelObject, int[]> _macros = new();
    private readonly Dictionary<LevelObject, string> _macroErrors = new();
    private readonly Dictionary<string, int> _levels = new();
    public KernelLiveBridge(string root, string rom, Dictionary<LevelObject, LiveObjectSnapshot> originals, int port = 64640)
    {
        if (port < 1 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        _port = port;
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
        foreach (var entry in originals)
        {
            _original[entry.Key] = entry.Value;
            if (entry.Value.SourceType == ObjectSourceType.Macro)
            {
                try { _macros[entry.Key] = MacroIdentity(root,entry.Value); }
                catch (IOException ex) { _macroErrors[entry.Key] = ex.Message; }
            }
        }
        foreach (var entry in originals.Where(e=>e.Value.SourceType==ObjectSourceType.Special))
        {
            try { _specials[entry.Key]=SpecialIdentity(root,entry.Value); }
            catch(IOException ex) { _macroErrors[entry.Key]=ex.Message; }
        }
        _ = Symbol("gObjectPool"); _ = Symbol("gCurrLevelNum"); _ = Symbol("gCurrAreaIndex");
    }
    public static Dictionary<LevelObject, LiveObjectSnapshot> Capture(IEnumerable<LevelObject> objects) => objects
        .Where(o => !o.IsNew && o.SourceType is ObjectSourceType.Normal or ObjectSourceType.Macro or ObjectSourceType.Special)
        .ToDictionary(o => o, o => new LiveObjectSnapshot(new[] { o.X, o.Y, o.Z, Angle(o.RX), Angle(o.RY), Angle(o.RZ), unchecked((int)o.Params), o.RY }, o.SourceType,o.Behavior,o.PresetName,o.RawParams,o.ModelName));
    internal static int MacroYaw(int degrees) => (short)((((int)(degrees * 16L / 45)) & 127) << 9);
    private static int[] MacroIdentity(string root,LiveObjectSnapshot snapshot)
    {
        var constants = new Dictionary<string,int>();
        foreach (string file in new[] { "macro_presets.h", "dialog_ids.h" })
        {
            string path=Path.Combine(root,"include",file);
            if(!File.Exists(path)) continue;
            string source=Regex.Replace(File.ReadAllText(path),@"/\*[\s\S]*?\*/|//[^\r\n]*","");
            foreach(Match body in Regex.Matches(source,@"enum\s+\w+\s*\{([^}]+)\}"))
            {
                int next=0;
                foreach(string item in body.Groups[1].Value.Split(','))
                {
                    if(string.IsNullOrWhiteSpace(item))continue;
                    var declaration=Regex.Match(item.Trim(),@"^(\w+)(?:\s*=\s*(-?(?:0x[0-9a-fA-F]+|\d+)))?$");
                    if(!declaration.Success) throw new IOException("Unsupported macro or dialog enum declaration");
                    if(declaration.Groups[2].Success) next=Literal(declaration.Groups[2].Value);
                    constants[declaration.Groups[1].Value]=next++;
                }
            }
        }
        int Value(string expression)
        {
            expression=expression.Trim();
            if(constants.TryGetValue(expression,out int value))return value;
            if(Regex.IsMatch(expression,@"^-?(?:0x[0-9a-fA-F]+|\d+)$"))return Literal(expression);
            throw new IOException("Cannot safely resolve macro parameter: "+expression);
        }
        string presetsPath=Path.Combine(root,"include","macro_presets.inc.c");
        if(!File.Exists(presetsPath))throw new IOException("Macro preset table is missing");
        var presets=Regex.Matches(File.ReadAllText(presetsPath),@"/\*\s*(macro_\w+)\s*\*/\s*\{\s*(\w+)\s*,\s*\w+\s*,\s*([^}]+)\}");
        int preset=Value(snapshot.Preset);
        if(preset<0 || preset>=presets.Count || presets[preset].Groups[1].Value!=snapshot.Preset || presets[preset].Groups[2].Value!=snapshot.Behavior || preset+31>511)
            throw new IOException("Macro preset does not match its source table");
        int raw=snapshot.RawParams==null ? 0 : Value(snapshot.RawParams);
        if(raw<short.MinValue || raw>ushort.MaxValue)throw new IOException("Macro parameter exceeds 16 bits");
        int presetParam=Value(presets[preset].Groups[3].Value);
        int effective=presetParam!=0 ? (raw & 0xff00)+(presetParam & 255) : raw & 0xffff;
        int runtime=((effective & 255)<<16)+(effective & 0xff00);
        int packed=(unchecked((ushort)MacroYaw(snapshot.Transform[7]))) | (preset+31);
        return new[] { packed, raw & 0xffff, runtime };
    }
    internal static int SpecialYaw(int degrees)
    {
        int raw=(int)Math.Round(((degrees%360+360)%360)*256.0/360)%256;
        int angle=raw<<8;
        return (short)(angle switch {0x3f00=>0x4000,0x7f00=>0x8000,0xbf00=>0xc000,0xff00=>0,_=>angle});
    }
    private int[] SpecialIdentity(string root,LiveObjectSnapshot snapshot)
    {
        string path=Path.Combine(root,"include","special_presets.inc.c");
        if(!File.Exists(path))throw new IOException("Special preset table is missing");
        var preset=Regex.Match(File.ReadAllText(path),@"\{\s*"+Regex.Escape(snapshot.Preset)+@"\s*,\s*(SPTYPE_\w+)\s*,\s*([^,]+),\s*(\w+)\s*,\s*(\w+)\s*\}");
        if(!preset.Success)throw new IOException("Cannot resolve special preset: "+snapshot.Preset);
        string modelPath=Path.Combine(root,"include","model_ids.h");
        if(!File.Exists(modelPath))throw new IOException("Model ID definitions are missing");
        string source=Regex.Replace(File.ReadAllText(modelPath),@"/\*[\s\S]*?\*/|//[^\r\n]*","");
        int Model(string name,HashSet<string>? seen=null)
        {
            if(Regex.IsMatch(name,@"^(?:0x[0-9a-fA-F]+|\d+)$"))return Literal(name);
            seen??=new();if(!seen.Add(name))throw new IOException("Circular model ID alias");
            var definition=Regex.Match(source,@"(?m)^\s*#define\s+"+Regex.Escape(name)+@"\s+(\w+)");
            if(!definition.Success)throw new IOException("Cannot resolve model ID: "+name);
            return Model(definition.Groups[1].Value,seen);
        }
        int model=Model(preset.Groups[3].Value);
        if(model<0 || model>255 || Model(snapshot.Model)!=model)throw new IOException("Special preset model does not match the source object");
        string type=preset.Groups[1].Value;
        int parameter=0;
        if(type=="SPTYPE_PARAMS_AND_YROT")
        {
            string raw=snapshot.RawParams??snapshot.Transform[6].ToString(System.Globalization.CultureInfo.InvariantCulture);
            if(!Regex.IsMatch(raw,@"^-?(?:0x[0-9a-fA-F]+|\d+)$"))throw new IOException("Cannot safely resolve special parameter: "+raw);
            int value=Literal(raw);
            if(value<short.MinValue || value>ushort.MaxValue)throw new IOException("Special parameter exceeds 16 bits");
            parameter=unchecked(value<<16);
        }
        else if(type=="SPTYPE_DEF_PARAM_AND_YROT")parameter=unchecked(Literal(preset.Groups[2].Value.Trim())<<24);
        else if(type is not "SPTYPE_NO_YROT_OR_PARAMS" and not "SPTYPE_YROT_NO_PARAMS")throw new IOException("Unsupported special preset layout: "+type);
        _=Symbol("gLoadedGraphNodes");_=Symbol("gMacroObjectDefaultParent");_=Symbol("sSegmentTable");
        return new[]{Symbol(preset.Groups[4].Value),model,parameter,type=="SPTYPE_NO_YROT_OR_PARAMS"?0:1};
    }
    private static int Literal(string text)
    {
        bool negative=text.StartsWith('-'); string number=negative ? text[1..] : text;
        int value=number.StartsWith("0x",StringComparison.OrdinalIgnoreCase) ? Convert.ToInt32(number[2..],16) : int.Parse(number,System.Globalization.CultureInfo.InvariantCulture);
        return negative ? -value : value;
    }
    private string Hash() { using var stream = File.OpenRead(_rom); return Convert.ToHexString(SHA256.HashData(stream)); }
    private int Symbol(string name) => _symbols.TryGetValue(name, out var value) ? value : throw new IOException("Missing build symbol: " + name);
    private static int Angle(int degrees) => (short)(degrees * 65536L / 360);
    public string? SelectionRestriction(LevelObject? obj)
    {
        if (obj == null) return "Select an existing normal, macro, or special object to preview its transform.";
        if (obj.IsDeleted) return "Deleted objects cannot be previewed live. Select an existing normal, macro, or special object.";
        if (obj.IsNew || !_original.TryGetValue(obj,out var snapshot) || obj.SourceType!=snapshot.SourceType)
            return "Live preview supports existing normal, macro, and special objects. Mario and new objects require saving and rebuilding the game.";
        if (_macroErrors.TryGetValue(obj,out string? error)) return error;
        if(obj.SourceType==ObjectSourceType.Macro && (obj.RX!=0 || obj.RZ!=0)) return "Macro objects support position and yaw only. Set RX and RZ to zero for live preview.";
        if(obj.SourceType==ObjectSourceType.Special && (obj.RX!=0 || obj.RZ!=0 || (_specials.TryGetValue(obj,out var special) && special[3]==0 && obj.RY!=snapshot.Transform[7]))) return "This special preset supports position edits only; yaw-enabled presets also support yaw. Reset unsupported rotations to preview live.";
        return null;
    }
    internal int[] CreateRequest(LevelObject obj, int area)
    {
        if (SelectionRestriction(obj) is { } restriction) throw new IOException(restriction);
        var snapshot = _original[obj];
        var original = snapshot.Transform;
        if (Hash() != _hash) throw new IOException("Build changed; disconnect and select the rebuilt ROM");
        var source = (obj.SourceFile ?? "").Replace('\\', '/');
        var level = Regex.Match(source, @"/levels/(\w+)/");
        if (!level.Success || !_levels.TryGetValue(level.Groups[1].Value, out var levelId)) throw new IOException("Cannot resolve the source level");
        int[] request = new int[24];
        request[0] = Symbol("gObjectPool"); request[1] = Symbol("gCurrLevelNum"); request[2] = Symbol("gCurrAreaIndex");
        request[3] = levelId; request[4] = area; request[5] = snapshot.SourceType==ObjectSourceType.Special ? _specials[obj][0] : Symbol(snapshot.Behavior); request[6] = original[6];
        Array.Copy(original, 0, request, 7, 6);
        request[13] = obj.X; request[14] = obj.Y; request[15] = obj.Z;
        request[16] = Angle(obj.RX); request[17] = Angle(obj.RY); request[18] = Angle(obj.RZ);
        if (_macros.TryGetValue(obj,out var macro))
        {
            request[17]=MacroYaw(obj.RY);
            request[19]=1; request[20]=macro[0]; request[21]=macro[1]; request[22]=Symbol("sSegmentTable"); request[23]=macro[2];
        }
        if (_specials.TryGetValue(obj,out var special))
        {
            int[] expected=_lastSpecialTransforms.GetValueOrDefault(obj) ?? new[]{original[0],original[1],original[2],0,SpecialYaw(original[7]),0};
            Array.Copy(expected,0,request,7,6);
            request[17]=special[3]==0 ? 0 : SpecialYaw(obj.RY);
            request[19]=2;request[20]=Symbol("gLoadedGraphNodes");request[21]=special[1];request[22]=Symbol("sSegmentTable");request[23]=special[2];
            request[6]=Symbol("gMacroObjectDefaultParent");
        }
        return request;
    }
    public async Task SendAsync(LevelObject obj, int area)
    {
        int[] request = CreateRequest(obj, area);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        using var client = new TcpClient();
        try
        {
            await client.ConnectAsync("127.0.0.1", _port, timeout.Token);
            using var stream = client.GetStream();
            using var payload = new MemoryStream();
            void Word(int value) { Span<byte> bytes = stackalloc byte[4]; System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes, value); payload.Write(bytes); }
            Word(0x524D3634); Word(3);
            var hashBytes = Encoding.ASCII.GetBytes(_hash); payload.WriteByte(0); payload.WriteByte((byte)hashBytes.Length); payload.Write(hashBytes);
            foreach (int value in request) Word(value);
            await stream.WriteAsync(payload.ToArray(), timeout.Token);
            var prefix = new byte[2]; await stream.ReadExactlyAsync(prefix, timeout.Token);
            var reply = new byte[(prefix[0] << 8) | prefix[1]]; await stream.ReadExactlyAsync(reply, timeout.Token);
            var status = Encoding.UTF8.GetString(reply);
            if (status == "open the matching rom first")
                throw new LiveBridgeUnavailableException(new IOException(status), "KernelN64 is open. Load the selected build ROM. Retrying automatically...");
            if (status == "enter the selected level and area first")
                throw new LiveBridgeUnavailableException(new IOException(status), "Enter the selected level and area in KernelN64. Retrying automatically...");
            if (status == "ok" && request[19]==2) _lastSpecialTransforms[obj]=request.Skip(13).Take(6).ToArray();
            if (status != "ok")
            {
                string message = Regex.Replace(status, @"\brom\b", "ROM", RegexOptions.IgnoreCase);
                throw new IOException(message.Length == 0 ? "Live editing request failed" : char.ToUpperInvariant(message[0]) + message[1..]);
            }
        }
        catch (SocketException ex) { throw new LiveBridgeUnavailableException(ex); }
        catch (IOException ex) when (ex.InnerException is SocketException) { throw new LiveBridgeUnavailableException(ex); }
        catch (EndOfStreamException ex) { throw new LiveBridgeUnavailableException(ex); }
        catch (OperationCanceledException ex) when (timeout.IsCancellationRequested) { throw new LiveBridgeUnavailableException(ex); }

    }
}
