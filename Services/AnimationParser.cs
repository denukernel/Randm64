using System.IO;
using System.Text.RegularExpressions;
using OpenTK.Mathematics;
using Sm64DecompLevelViewer.Models;

namespace Sm64DecompLevelViewer.Services;

public class MarioAnimation
{
    public string Name { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public int AnimationId { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public int Flags { get; set; }
    public int TranslationDivisor { get; set; }
    public float TranslationHeight { get; set; } = 189;
    public int StartFrame { get; set; }
    public int LoopStart { get; set; }
    public int FrameCount { get; set; } = 1;
    public ushort[] Indices { get; set; } = Array.Empty<ushort>();
    public short[] Values { get; set; } = Array.Empty<short>();

    public int AdvanceFrame(int frame, int count, bool loopPreview)
    {
        int first = Math.Clamp(LoopStart, 0, Math.Max(0, FrameCount - 1));
        int last = Math.Max(first, FrameCount - 1);
        int direction = (Flags & 2) != 0 ? -1 : 1;
        long next = frame + (long)count * direction;
        if (next >= first && next <= last) return (int)next;
        if (!loopPreview && (Flags & 1) != 0) return direction < 0 ? first : last;
        int length = last - first + 1;
        return first + (int)(((next - first) % length + length) % length);
    }
}

public class AnimationParser
{
    private static readonly Regex Headers = new(@"(?:static\s+)?const\s+struct\s+Animation\s+(\w+)\s*(?:\[\s*\])?\s*=\s*\{([\s\S]*?)\};");
    private static string Clean(string text) => Regex.Replace(text, @"/\*[\s\S]*?\*/|//[^\r\n]*", "");
    private static int Number(string value)
    {
        value = value.Trim();
        bool negative = value.StartsWith('-');
        string digits = negative ? value[1..] : value;
        int number = digits.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? Convert.ToInt32(digits[2..], 16) : int.Parse(digits, System.Globalization.CultureInfo.InvariantCulture);
        return negative ? -number : number;
    }

    public List<MarioAnimation> ScanAnimations(string projectRoot)
    {
        string directory = Path.Combine(projectRoot, "assets", "anims");
        var animations = new List<MarioAnimation>();
        if (!Directory.Exists(directory)) return animations;
        var names = new Dictionary<int, string>();
        string idsPath = Path.Combine(projectRoot, "include", "mario_animation_ids.h");
        if (File.Exists(idsPath))
            foreach (Match id in Regex.Matches(File.ReadAllText(idsPath), @"/\*\s*0x([\da-fA-F]+)\s*\*/\s*MARIO_ANIM_(\w+)"))
                names[Convert.ToInt32(id.Groups[1].Value, 16)] = id.Groups[2].Value.Replace('_', ' ').ToLowerInvariant();
        foreach (string file in Directory.GetFiles(directory, "anim_*.inc.c").OrderBy(f => f))
            foreach (Match header in Headers.Matches(Clean(File.ReadAllText(file))))
            {
                string symbol = header.Groups[1].Value;
                string label = symbol;
                if (int.TryParse(symbol.Replace("anim_", ""), System.Globalization.NumberStyles.HexNumber, null, out int id) && names.TryGetValue(id, out string? name))
                    label = $"{id:X2}: {name}";
                animations.Add(new MarioAnimation { Name = label, Symbol = symbol, FilePath = file });
            }
        return animations;
    }

    public List<MarioAnimation> ScanActorAnimations(string projectRoot, string actor, int? jointCount = null)
    {
        if (actor == "mario") return ScanAnimations(projectRoot);
        string directory = Path.Combine(projectRoot, "actors", actor, "anims");
        if (!Directory.Exists(directory)) directory = Path.Combine(projectRoot, "actors", actor);
        if (!Directory.Exists(directory)) return new();
        var animations = new Dictionary<string, MarioAnimation>();
        foreach (string file in Directory.GetFiles(directory, "*.inc.c").OrderBy(file => file))
        {
            if (Path.GetFileName(file) is "geo.inc.c" or "model.inc.c" or "table.inc.c" or "data.inc.c") continue;
            foreach (Match header in Headers.Matches(Clean(ActorSourceReader.Read(file))))
            {
                string symbol = header.Groups[1].Value;
                animations.TryAdd(symbol, new MarioAnimation
                    { Name = symbol, Symbol = symbol, FilePath = file, TranslationHeight = 0 });
            }
        }
        // Tables define the game's animation IDs; filesystem order does not.
        string table = Path.Combine(directory, "table.inc.c");
        var result = new List<MarioAnimation>();
        if (File.Exists(table))
            foreach (Match entry in Regex.Matches(Clean(File.ReadAllText(table)), @"&\s*(\w+)"))
                if (animations.Remove(entry.Groups[1].Value, out var animation)) result.Add(animation);
        result.AddRange(animations.Values);
        var labels = new Dictionary<int, string>();
        string constants = Path.Combine(projectRoot, "include", "object_constants.h");
        if (File.Exists(constants))
            foreach (Match label in Regex.Matches(File.ReadAllText(constants), @"#define\s+" +
                Regex.Escape(actor.ToUpperInvariant()) + @"_ANIM_(\w+)\s+(0x[\dA-Fa-f]+|\d+)"))
                labels.TryAdd(Number(label.Groups[2].Value), label.Groups[1].Value.Replace('_', ' ').ToLowerInvariant());
        for (int i = 0; i < result.Count; i++)
        {
            result[i].AnimationId = i;
            result[i].Name = $"{i}: {(labels.TryGetValue(i, out var label) ? label : result[i].Symbol)}";
        }
        if (jointCount.HasValue)
            result = result.Where(animation =>
            {
                LoadAnimationData(animation);
                // Unused short animation stubs occur in the original tables (e.g. Bowser).
                return jointCount.Value > 0 && (animation.Indices.Length - 6) / 6 >= jointCount.Value;
            }).ToList();
        return result;
    }

    public static MarioAnimation? SelectDefaultAnimation(IReadOnlyList<MarioAnimation> animations, string actor) =>
        animations.FirstOrDefault(animation => actor == "mario" && animation.Symbol.Equals("anim_C5", StringComparison.OrdinalIgnoreCase)) ??
        animations.FirstOrDefault(animation => animation.Name.EndsWith(": idle")) ??
        // Koopa's normal walking cycle is animation 9 in koopa_walk(), not animation 0's transition.
        animations.FirstOrDefault(animation => actor == "koopa" && animation.AnimationId == 9) ?? animations.FirstOrDefault();

    public void LoadAnimationData(MarioAnimation anim)
    {
        string content = Clean(ActorSourceReader.Read(anim.FilePath));
        var header = Headers.Matches(content).Cast<Match>().FirstOrDefault(m =>
            string.IsNullOrEmpty(anim.Symbol) || m.Groups[1].Value == anim.Symbol)
            ?? throw new InvalidDataException("Animation header not found.");
        var fields = header.Groups[2].Value.Split(',').Select(s => s.Trim()).ToArray();
        if (fields.Length < 9) throw new InvalidDataException("Incomplete animation header.");
        anim.Symbol = header.Groups[1].Value;
        anim.Flags = Number(fields[0]);
        anim.TranslationDivisor = Number(fields[1]);
        anim.StartFrame = Number(fields[2]);
        anim.LoopStart = Number(fields[3]);
        anim.FrameCount = Number(fields[4]);
        if (anim.FrameCount <= 0) throw new InvalidDataException("Animation has no frames.");
        int[] ReadArray(string type, string symbol)
        {
            string pattern = @"\b" + type + @"\s+" + Regex.Escape(symbol) + @"\s*\[\s*\]\s*=\s*\{([\s\S]*?)\};";
            var match = Regex.Match(content, pattern);
            if (!match.Success)
            {
                // Some clips refer to another clip's value/index arrays in the same bank.
                string bank = Path.Combine(Path.GetDirectoryName(anim.FilePath)!, "data.inc.c");
                string shared = File.Exists(bank) ? ActorSourceReader.Read(bank) : string.Join("\n",
                    Directory.GetFiles(Path.GetDirectoryName(anim.FilePath)!, "*.inc.c").Select(File.ReadAllText));
                match = Regex.Match(Clean(shared), pattern);
            }
            if (!match.Success) throw new InvalidDataException($"Missing animation array: {symbol}");
            return match.Groups[1].Value.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Where(s => !string.IsNullOrWhiteSpace(s)).Select(Number).ToArray();
        }
        anim.Values = ReadArray("s16", fields[6]).Select(n => unchecked((short)n)).ToArray();
        anim.Indices = ReadArray("u16", fields[7]).Select(n => checked((ushort)n)).ToArray();
        if (anim.Indices.Length < 12 || anim.Indices.Length % 6 != 0)
            throw new InvalidDataException("Invalid animation channel table.");
        for (int i = 0; i < anim.Indices.Length; i += 2)
            if (anim.Indices[i] == 0 || (long)anim.Indices[i] + anim.Indices[i + 1] > anim.Values.Length)
                throw new InvalidDataException("Animation channel exceeds its value array.");
    }

    public Dictionary<int, Matrix4> GetFrameTransforms(MarioAnimation anim, int frame, List<GeoNode> joints, float rootScale = 1)
    {
        var worlds = new Dictionary<int, Matrix4>();
        if (anim.Indices.Length == 0 || anim.Values.Length == 0) return worlds;
        frame = Math.Clamp(frame, 0, Math.Max(0, anim.FrameCount - 1));
        float translationScale = anim.TranslationDivisor == 0 ? 1 : anim.TranslationHeight / anim.TranslationDivisor;
        short Channel(int pair)
        {
            if (pair + 1 >= anim.Indices.Length) return 0;
            int index = anim.Indices[pair + 1] + Math.Min(frame, Math.Max(0, (int)anim.Indices[pair] - 1));
            return index < anim.Values.Length ? anim.Values[index] : (short)0;
        }
        for (int i = 0; i < joints.Count; i++)
        {
            var joint = joints[i];
            // Preserve static geo transforms between animated joints and root scale.
            Matrix4 bind = joint.RestWorldMatrix;
            Matrix4 parent = Matrix4.Identity;
            if (joint.ParentIndex >= 0)
            {
                bind *= Matrix4.Invert(joints[joint.ParentIndex].RestWorldMatrix);
                parent = worlds[joint.ParentIndex];
            }
            Vector3 translation = Vector3.Zero;
            if (i == 0 && (anim.Flags & 0x40) == 0)
            {
                if ((anim.Flags & 0x08) == 0) translation.X = Channel(0) * translationScale;
                if ((anim.Flags & 0x10) == 0) translation.Y = Channel(2) * translationScale;
                if ((anim.Flags & 0x08) == 0) translation.Z = Channel(4) * translationScale;
            }
            int pair = 6 + i * 6;
            float radians = MathF.PI / 32768;
            var rotation = Matrix4.CreateRotationX(Channel(pair) * radians) *
                Matrix4.CreateRotationY(Channel(pair + 2) * radians) *
                Matrix4.CreateRotationZ(Channel(pair + 4) * radians);
            worlds[i] = rotation * Matrix4.CreateTranslation(translation) * bind * parent;
        }
        return worlds;
    }

    public void ApplyFrame(VisualMesh mesh, MarioAnimation? animation, int frame, Vector3? cameraFacing = null)
    {
        var matrices = animation == null ? mesh.Joints.ToDictionary(j => j.JointIndex, j => j.RestWorldMatrix)
            : GetFrameTransforms(animation, frame, mesh.Joints, mesh.RootScale);
        var facing = cameraFacing ?? Vector3.UnitZ;
        if (facing.LengthSquared < 0.001f) facing = Vector3.UnitZ;
        facing.Normalize();
        var right = Vector3.Cross(Vector3.UnitY, facing);
        if (right.LengthSquared < 0.001f) right = Vector3.UnitX;
        right.Normalize();
        var up = Vector3.Cross(facing, right);
        var anchors = new Dictionary<GeoBillboard, Vector3>();
        foreach (var vertex in mesh.Vertices)
        {
            if (vertex.Billboard is { } billboard)
            {
                if (!anchors.TryGetValue(billboard, out var center))
                {
                    var anchor = billboard.RestWorldMatrix;
                    if (billboard.ParentJointIndex >= 0 && matrices.TryGetValue(billboard.ParentJointIndex, out var parent))
                        anchor = anchor * Matrix4.Invert(mesh.Joints[billboard.ParentJointIndex].RestWorldMatrix) * parent;
                    anchors[billboard] = center = anchor.Row3.Xyz;
                }
                // The game's billboard matrix retains its translated anchor but replaces
                // inherited joint rotation/geo scale with the camera axes (object scale is 1).
                var point = vertex.BillboardPosition;
                var world = center + right * point.X + up * point.Y + facing * point.Z;
                vertex.X = (int)MathF.Round(world.X);
                vertex.Y = (int)MathF.Round(world.Y);
                vertex.Z = (int)MathF.Round(world.Z);
                vertex.SetNormal(facing);
                continue;
            }
            if (!matrices.TryGetValue(vertex.JointIndex, out var matrix)) continue;
            var position = Vector3.TransformPosition(new Vector3(vertex.RefX, vertex.RefY, vertex.RefZ), matrix);
            vertex.X = (int)MathF.Round(position.X);
            vertex.Y = (int)MathF.Round(position.Y);
            vertex.Z = (int)MathF.Round(position.Z);
            vertex.SetNormal(Vector3.TransformNormal(vertex.ReferenceNormal, matrix));
        }
    }
}
