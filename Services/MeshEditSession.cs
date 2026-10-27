using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using OpenTK.Mathematics;
using Sm64DecompLevelViewer.Models;
using StbImageSharp;

namespace Sm64DecompLevelViewer.Services;

public sealed record MeshPaintTexture(string Symbol, string Path, int Width, int Height, string Format, string Size)
{
    public override string ToString() => $"{Symbol} ({Width} x {Height})";
    public string Load(TextureAddressMode s, TextureAddressMode t)
    {
        static string Address(TextureAddressMode mode) => mode == TextureAddressMode.Clamp ? "G_TX_CLAMP" : mode == TextureAddressMode.Mirror ? "G_TX_MIRROR" : "G_TX_WRAP";
        string args = $"{Symbol}, {Format}, " + (Size == "4b" ? "" : $"G_IM_SIZ_{Size}, ") +
            $"{Width}, {Height}, 0, {Address(s)}, {Address(t)}, {(int)Math.Log2(Width)}, {(int)Math.Log2(Height)}, G_TX_NOLOD, G_TX_NOLOD";
        return $"gsDPPipeSync(),\n    gsDPLoadTextureBlock{(Size == "4b" ? "_4b" : "")}({args}),\n    ";
    }
}

// A level edit stays in memory until Save. No source reads, reparsing or disk writes occur in a drag or paint stroke.
public sealed partial class MeshEditSession
{
    private sealed record Source(string Path, string Text, Dictionary<string, List<ModelVertex>> Arrays);
    private sealed record Paint(ModelTriangle Triangle, string? Original, string Texture, string? RestoreState);
    private readonly object sync = new();
    private readonly Dictionary<string, Source> arrays = new();
    private readonly Dictionary<string, Source> lists = new();
    private readonly Dictionary<string, byte[]?> expected = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string, int), ModelVertex[]> instances;
    private readonly Dictionary<(string, int), Vector3> positions = new();
    private readonly Dictionary<(string, int), (int S, int T)> uvs = new();
    private readonly Dictionary<(string, int, int), Paint> paints = new();
    private readonly Dictionary<string, MeshPaintTexture> textures = new();
    private readonly Dictionary<(string, int), string> shadeStates = new();
    private readonly Dictionary<string, string> textureDeclarations = new();
    public ModelVertex[] Vertices { get; }
    public ModelTriangle[] Triangles { get; }
    public VisualMesh Scene { get; }
    public IReadOnlyList<MeshPaintTexture> Textures => textures.Values.OrderBy(t => t.Symbol).ToArray();
    public bool IsDirty { get; private set; }

    public MeshEditSession(VisualMesh scene, string projectRoot, IEnumerable<string>? sourceFiles = null, CollisionMesh? collision = null, string? collisionPath = null)
    {
        Scene = scene;
        InitializeCollision(collision, collisionPath);
        Vertices = scene.SubMeshes.Count == 0 ? scene.Vertices.ToArray() : scene.SubMeshes.SelectMany(s => s.Vertices).ToArray();
        Triangles = scene.SubMeshes.Count == 0 ? scene.Triangles.ToArray() : scene.SubMeshes.SelectMany(s => s.Triangles).ToArray();
        foreach (string path in sourceFiles ?? Directory.GetFiles(scene.LevelDirectory!, "*.c", SearchOption.AllDirectories))
        {
            byte[] bytes = File.ReadAllBytes(path);
            string text = Encoding.UTF8.GetString(bytes);
            string active = ActorSourceReader.Read(path, preserveOffsets: true, sourceText: text);
            foreach (Match declaration in Regex.Matches(active, @"\b(static\s+)?const\s+(Texture|u8|u16|u32|u64)\s+(\w+)\s*\[\]\s*="))
                textureDeclarations[declaration.Groups[3].Value] = (declaration.Groups[1].Success ? "static " : "extern ") +
                    "const " + declaration.Groups[2].Value + " " + declaration.Groups[3].Value + "[];";
            if (!Regex.IsMatch(active, @"\b(Vtx|Gfx)\s+\w+\s*\[")) continue;
            var source = new Source(path, text, new ModelParser().ParseVertexArrays(active));
            expected[path] = bytes;
            foreach (string name in source.Arrays.Keys)
                if (!arrays.TryAdd(name, source)) throw new InvalidDataException("ambiguous vertex source: " + name);
            foreach (Match match in Regex.Matches(active, @"\bGfx\s+(\w+)\s*\[\]\s*=\s*\{"))
            {
                string name = match.Groups[1].Value;
                if (!lists.TryAdd(name, source)) throw new InvalidDataException("ambiguous display list: " + name);
                int start = match.Index + match.Length, end = active.IndexOf('}', start), commandIndex = 0;
                string? combine = null, enable = null;
                string body = active[start..end];
                foreach (var command in CSourceCalls.Read(body, n => n.StartsWith("gs")))
                {
                    if (command.Name is "gsSPDisplayList" or "gsSPBranchList") { combine = null; enable = null; }
                    if (command.Name == "gsDPSetCombineMode") combine = command.Text(body);
                    if (command.Name == "gsSPTexture") enable = command.Text(body);
                    if (command.Name is not ("gsSP1Triangle" or "gsSP2Triangles")) continue;
                    if (combine?.Contains("G_CC_SHADE") == true && enable?.Contains("G_OFF") == true)
                        shadeStates[(name, commandIndex)] = "gsDPPipeSync(),\n    " + combine + ",\n    " + enable;
                    commandIndex++;
                }
            }
        }
        instances = Vertices.Where(v => v.SourceArrayName != null).GroupBy(v => (v.SourceArrayName!, v.SourceArrayIndex))
            .ToDictionary(g => g.Key, g => g.ToArray());
        foreach (var item in scene.TexturePaths)
        {
            string path = System.IO.Path.Combine(projectRoot, item.Value);
            var format = Regex.Match(path, @"\.(rgba16|ia16|ia8|ia4|i8|i4)\.png$", RegexOptions.IgnoreCase);
            if (!format.Success || !File.Exists(path)) continue;
            var image = ImageResult.FromMemory(File.ReadAllBytes(path), ColorComponents.RedGreenBlueAlpha);
            string kind = format.Groups[1].Value.ToLowerInvariant();
            int bits = int.Parse(Regex.Match(kind, @"\d+").Value);
            if (image.Width <= 0 || image.Height <= 0 || (image.Width & (image.Width - 1)) != 0 ||
                (image.Height & (image.Height - 1)) != 0 || image.Width * image.Height * bits / 8 > 4096) continue;
            textures[item.Key] = new MeshPaintTexture(item.Key, path, image.Width, image.Height,
                kind.StartsWith("rgba") ? "G_IM_FMT_RGBA" : kind.StartsWith("ia") ? "G_IM_FMT_IA" : "G_IM_FMT_I", bits + "b");
        }
    }

    public int[] FaceVertices(int triangle)
    {
        Vector3 Point(int i) => new(Vertices[i].X, Vertices[i].Y, Vertices[i].Z);
        int[] Indices(int i) => new[] { Triangles[i].V1, Triangles[i].V2, Triangles[i].V3 };
        var seed = Indices(triangle).Select(Point).ToArray();
        var normal = Vector3.Cross(seed[1] - seed[0], seed[2] - seed[0]);
        if (normal.LengthSquared < 0.0001f) return Indices(triangle);
        normal.Normalize();
        var edges = new Dictionary<(Vector3, Vector3), List<int>>();
        var candidates = new Dictionary<int, Vector3[]>();
        for (int i = 0; i < Triangles.Length; i++)
        {
            var p = Indices(i).Select(Point).ToArray();
            var n = Vector3.Cross(p[1] - p[0], p[2] - p[0]);
            if (n.LengthSquared < 0.0001f || Vector3.Dot(Vector3.Normalize(n), normal) < 0.9999f || Math.Abs(Vector3.Dot(p[0] - seed[0], normal)) > 0.01f) continue;
            candidates[i] = p;
            for (int e = 0; e < 3; e++)
            {
                var edge = (p[e], p[(e + 1) % 3]);
                if (!edges.TryGetValue(edge, out var adjacent)) edges[edge] = adjacent = new();
                adjacent.Add(i);
            }
        }
        var selected = new HashSet<int> { triangle };
        var queue = new Queue<int>(); queue.Enqueue(triangle);
        while (queue.TryDequeue(out int i))
        {
            var p = candidates[i];
            for (int e = 0; e < 3; e++)
                foreach (var edge in new[] { (p[e], p[(e + 1) % 3]), (p[(e + 1) % 3], p[e]) })
                    if (edges.TryGetValue(edge, out var adjacent))
                        foreach (int next in adjacent) if (selected.Add(next)) queue.Enqueue(next);
        }
        var points = selected.SelectMany(i => candidates[i]).ToHashSet();
        // Source arrays often duplicate edge vertices. Move the coincident copies together to avoid cracks.
        return Enumerable.Range(0, Vertices.Length).Where(i => points.Contains(Point(i))).ToArray();
    }

    public int[] WeldedVertex(int index)
    {
        var v = Vertices[index];
        return Enumerable.Range(0, Vertices.Length).Where(i => Vertices[i].X == v.X && Vertices[i].Y == v.Y && Vertices[i].Z == v.Z).ToArray();
    }

    public sealed class Move
    {
        internal bool WasDirty;
        internal bool WasCollisionDirty;
        internal Dictionary<int, Vector3> CollisionStarts = new();
        internal Dictionary<(string, int), Vector3> Starts = new();
        internal Dictionary<(string, int), Vector3?> Previous = new();
        internal Dictionary<(string, int), Matrix4> Transforms = new();
    }

    public Move BeginMove(IEnumerable<int> selected, GeometryEditTarget target = GeometryEditTarget.MeshOnly)
    {
        lock (sync)
        {
            var visual = selected.ToArray();
            var points = visual.Select(i => VisualPosition(i)).ToHashSet();
            var linked = target == GeometryEditTarget.MeshAndCollision && Collision != null
                ? Enumerable.Range(0, Collision.Vertices.Count).Where(i => points.Contains(CollisionPosition(i))).ToArray() : Array.Empty<int>();
            if (target == GeometryEditTarget.MeshAndCollision && linked.Length == 0)
                throw new InvalidDataException("no matching collision vertices at this selection; choose mesh only or select a shared corner");
            return BeginGeometryMove(target == GeometryEditTarget.CollisionOnly ? Array.Empty<int>() : visual, linked);
        }
    }

    private Move BeginGeometryMove(IEnumerable<int> selected, IEnumerable<int> collisionSelected)
    {
        lock (sync)
        {
            var move = new Move { WasDirty = IsDirty, WasCollisionDirty = collisionDirty };
            foreach (int index in selected)
            {
                var v = Vertices[index];
                if (v.SourceArrayName == null || !arrays.TryGetValue(v.SourceArrayName, out var source) ||
                    v.SourceArrayIndex < 0 || v.SourceArrayIndex >= source.Arrays[v.SourceArrayName].Count)
                    throw new InvalidDataException("this vertex has no editable source");
                var key = (v.SourceArrayName, v.SourceArrayIndex);
                move.Transforms.TryAdd(key, v.SourceTransform);
                var original = source.Arrays[key.Item1][key.Item2];
                move.Starts[key] = positions.TryGetValue(key, out var position) ? position : new Vector3(original.X, original.Y, original.Z);
                move.Previous[key] = positions.TryGetValue(key, out position) ? position : null;
            }
            foreach (int i in collisionSelected) move.CollisionStarts[i] = CollisionPosition(i);
            return move;
        }
    }

    public bool PreviewMove(Move move, Vector3 worldDelta)
    {
        lock (sync)
        {
            var changes = new Dictionary<(string, int), Vector3>();
            foreach (var start in move.Starts)
            {
                var transform = move.Transforms[start.Key];
                if (Math.Abs(transform.Determinant) < 0.000001f) return false;
                var local = Vector3.TransformVector(worldDelta, Matrix4.Invert(transform));
                var p = start.Value + new Vector3(MathF.Round(local.X), MathF.Round(local.Y), MathF.Round(local.Z));
                if (!float.IsFinite(p.X + p.Y + p.Z) || p.X < short.MinValue || p.X > short.MaxValue ||
                    p.Y < short.MinValue || p.Y > short.MaxValue || p.Z < short.MinValue || p.Z > short.MaxValue) return false;
                changes[start.Key] = p;
            }
            var collisionChanges = new Dictionary<int, Vector3>();
            foreach (var start in move.CollisionStarts)
            {
                var p = start.Value + new Vector3(MathF.Round(worldDelta.X), MathF.Round(worldDelta.Y), MathF.Round(worldDelta.Z));
                if (!float.IsFinite(p.X + p.Y + p.Z) || p.X < -32767 || p.X > 32767 || p.Y < -32767 || p.Y > 32767 || p.Z < -32767 || p.Z > 32767) return false;
                collisionChanges[start.Key] = p;
            }
            foreach (var change in changes) SetPosition(change.Key, change.Value);
            foreach (var change in collisionChanges) SetCollisionPosition(change.Key, change.Value);
            if (collisionChanges.Count > 0) collisionDirty = true;
            IsDirty = true;
            return true;
        }
    }

    public void CancelMove(Move move)
    {
        lock (sync)
        {
            foreach (var start in move.Starts)
            {
                SetPosition(start.Key, start.Value);
                if (move.Previous[start.Key] == null) positions.Remove(start.Key);
            }
            IsDirty = move.WasDirty;
            foreach (var start in move.CollisionStarts) SetCollisionPosition(start.Key, start.Value);
            collisionDirty = move.WasCollisionDirty;
        }
    }

    private void SetPosition((string, int) key, Vector3 position)
    {
        positions[key] = position;
        foreach (var vertex in instances[key])
        {
            var world = Vector3.TransformPosition(position, vertex.SourceTransform);
            vertex.X = vertex.RestX = (int)MathF.Round(world.X);
            vertex.Y = vertex.RestY = (int)MathF.Round(world.Y);
            vertex.Z = vertex.RestZ = (int)MathF.Round(world.Z);
        }
    }

    public bool PaintFace(int index, string texture)
    {
        lock (sync)
        {
            var t = Triangles[index];
            if (t.SourceDisplayList == null || t.SourceCommandIndex < 0 || !lists.ContainsKey(t.SourceDisplayList))
                throw new InvalidDataException("this face has no editable display-list source");
            if (!textures.ContainsKey(texture)) throw new InvalidDataException("select a supported level texture");
            var key = (t.SourceDisplayList, t.SourceCommandIndex, t.SourceTrianglePart);
            bool repainted = paints.TryGetValue(key, out var previous);
            string? original = repainted ? previous!.Original : t.TextureName;
            string? restoreState = repainted ? previous!.RestoreState : null;
            if (!repainted && (original == null || !textures.ContainsKey(original)))
            {
                // New primitives use an explicit SHADE + texture-off material. Preserve both and any inherited texture image.
                if (!shadeStates.TryGetValue((key.Item1, key.Item2), out restoreState))
                    throw new InvalidDataException("this material cannot be painted while preserving its custom texture state");
                original = t.SourceTextureImage;
                if (original != null && !textures.ContainsKey(original)) throw new InvalidDataException("the inherited texture format cannot be restored safely");
            }
            if (t.TextureName == texture) return false;
            if (restoreState != null) MapUntexturedFace(t, textures[texture]);
            paints[key] = new Paint(t, original, texture, restoreState);
            foreach (var face in Triangles.Where(f => f.SourceDisplayList == key.Item1 && f.SourceCommandIndex == key.Item2 && f.SourceTrianglePart == key.Item3))
                face.TextureName = texture;
            IsDirty = true;
            return true;
        }
    }

    private void MapUntexturedFace(ModelTriangle face, MeshPaintTexture texture)
    {
        var ids = new[] { face.V1, face.V2, face.V3 };
        if (ids.Any(i =>
        {
            var vertex = Vertices[i];
            return vertex.SourceArrayName == null || !arrays.TryGetValue(vertex.SourceArrayName, out var source) ||
                source.Arrays[vertex.SourceArrayName][vertex.SourceArrayIndex].S != 0 || source.Arrays[vertex.SourceArrayName][vertex.SourceArrayIndex].T != 0;
        })) return;
        var p = ids.Select(i => new Vector3(Vertices[i].X, Vertices[i].Y, Vertices[i].Z)).ToArray();
        var normal = Vector3.Cross(p[1] - p[0], p[2] - p[0]);
        int drop = Math.Abs(normal.X) > Math.Abs(normal.Y) && Math.Abs(normal.X) > Math.Abs(normal.Z) ? 0 : Math.Abs(normal.Y) > Math.Abs(normal.Z) ? 1 : 2;
        Vector2 Project(Vector3 v) => drop == 0 ? new(v.Z, v.Y) : drop == 1 ? new(v.X, v.Z) : new(v.X, v.Y);
        var projected = p.Select(Project).ToArray();
        var min = projected.Aggregate(Vector2.ComponentMin); var max = projected.Aggregate(Vector2.ComponentMax);
        var changes = new Dictionary<(string, int), (int S, int T)>();
        for (int i = 0; i < ids.Length; i++)
        {
            var vertex = Vertices[ids[i]];
            if (vertex.SourceArrayName == null || Math.Abs(vertex.SourceUvScale.X) < 0.00001f || Math.Abs(vertex.SourceUvScale.Y) < 0.00001f) continue;
            var uv = (projected[i] - min) / Vector2.ComponentMax(max - min, Vector2.One);
            int s = (int)MathF.Round(uv.X * (texture.Width - 1) * 32 / vertex.SourceUvScale.X);
            int t = (int)MathF.Round(uv.Y * (texture.Height - 1) * 32 / vertex.SourceUvScale.Y);
            if (s < short.MinValue || s > short.MaxValue || t < short.MinValue || t > short.MaxValue) throw new InvalidDataException("texture coordinates exceed the source limits");
            var key = (vertex.SourceArrayName, vertex.SourceArrayIndex);
            changes[key] = (s, t);
        }
        foreach (var change in changes)
        {
            uvs[change.Key] = change.Value;
            foreach (var instance in instances[change.Key])
            { instance.S = (int)(change.Value.S * instance.SourceUvScale.X); instance.T = (int)(change.Value.T * instance.SourceUvScale.Y); }
        }
    }

    public void Save()
    {
        lock (sync)
        {
            if (!IsDirty) return;
            var staged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in paints.GroupBy(p => lists[p.Key.Item1].Path))
            {
                var source = lists[group.First().Key.Item1];
                string text = source.Text;
                var replacements = new List<(int Start, int End, string Text)>();
                foreach (var list in group.GroupBy(p => p.Key.Item1))
                {
                    string active = ActorSourceReader.Read(source.Path, preserveOffsets: true, sourceText: text);
                    var header = Regex.Match(active, @"\bGfx\s+" + Regex.Escape(list.Key) + @"\[\]\s*=\s*\{");
                    if (!header.Success) throw new InvalidDataException("display list source changed");
                    int start = header.Index + header.Length, end = active.IndexOf('}', start);
                    var calls = CSourceCalls.Read(active[start..end], name => name is "gsSP1Triangle" or "gsSP2Triangles");
                    foreach (var command in list.GroupBy(p => p.Key.Item2))
                    {
                        var call = calls[command.Key];
                        var parts = command.ToDictionary(p => p.Key.Item3, p => p.Value);
                        var output = new StringBuilder();
                        for (int part = 0; part < (call.Name == "gsSP2Triangles" ? 2 : 1); part++)
                        {
                            parts.TryGetValue(part, out var paint);
                            if (paint != null)
                            {
                                output.Append(textures[paint.Texture].Load(paint.Triangle.TextureAddressS, paint.Triangle.TextureAddressT));
                                if (paint.RestoreState != null) output.Append("gsDPSetCombineMode(G_CC_MODULATEIA, G_CC_MODULATEIA),\n    gsSPTexture(0xFFFF, 0xFFFF, 0, G_TX_RENDERTILE, G_ON),\n    ");
                            }
                            output.Append("gsSP1Triangle(" + string.Join(", ", Enumerable.Range(part * 4, 4).Select(i => call.Argument(active[start..end], i))) + ")");
                            if (paint != null)
                            {
                                string restore = paint.Triangle.SourceTextureRestore ?? (paint.Original == null ? "" : textures[paint.Original].Load(paint.Triangle.TextureAddressS, paint.Triangle.TextureAddressT));
                                if (paint.RestoreState != null) restore += paint.RestoreState;
                                if (restore.Length > 0) output.Append(",\n    ").Append(restore.TrimEnd().TrimEnd(','));
                            }
                            if (part == 0 && call.Name == "gsSP2Triangles") output.Append(",\n    ");
                        }
                        replacements.Add((start + call.Start, start + call.End, output.ToString()));
                    }
                }
                foreach (var replacement in replacements.OrderByDescending(r => r.Start)) text = text[..replacement.Start] + replacement.Text + text[replacement.End..];
                var declarations = group.SelectMany(p => new[] { p.Value.Texture, p.Value.Original }).Where(s => s != null && textureDeclarations.ContainsKey(s))
                    .Distinct().Select(s => SizedTextureDeclaration(s!)).ToArray();
                if (declarations.Length > 0)
                {
                    var firstList = Regex.Match(CSourceCalls.MaskComments(text), @"(?:static\s+)?const\s+Gfx\s+\w+\[\]\s*=");
                    text = text.Insert(firstList.Index, string.Join("\n", declarations) + "\n");
                }
                staged[source.Path] = text;
            }
            foreach (var name in positions.Keys.Concat(uvs.Keys).Select(k => k.Item1).Distinct())
            {
                var source = arrays[name];
                var vertices = source.Arrays[name].Select(v => new ModelVertex(v.X, v.Y, v.Z, v.S, v.T, v.NX, v.NY, v.NZ, v.Alpha)).ToList();
                foreach (var p in positions.Where(p => p.Key.Item1 == name)) { vertices[p.Key.Item2].X = (int)p.Value.X; vertices[p.Key.Item2].Y = (int)p.Value.Y; vertices[p.Key.Item2].Z = (int)p.Value.Z; }
                foreach (var uv in uvs.Where(p => p.Key.Item1 == name)) { vertices[uv.Key.Item2].S = uv.Value.S; vertices[uv.Key.Item2].T = uv.Value.T; }
                staged[source.Path] = LevelMeshService.StageVisualMesh(staged.GetValueOrDefault(source.Path, source.Text), name, vertices);
            }
            foreach (string text in staged.Values) GraphicsSourceValidation.Validate(text);
            var bytes = staged.ToDictionary(p => p.Key, p => Encoding.UTF8.GetBytes(p.Value));
            if (collisionDirty && Collision != null && collisionSourcePath != null)
                foreach (var item in CollisionSourceWriter.Stage(collisionSourcePath, Collision)) bytes[item.Key] = item.Value;
            SourceFileTransaction.Commit(bytes, bytes.Keys.ToDictionary(p => p, p => expected[p]));
            foreach (var p in bytes) expected[p.Key] = p.Value;
            IsDirty = false;
            collisionDirty = false;
        }
    }

    private string SizedTextureDeclaration(string symbol)
    {
        string declaration = textureDeclarations[symbol];
        if (!declaration.StartsWith("static ")) return declaration;
        if (!textures.TryGetValue(symbol, out var texture)) throw new InvalidDataException("cannot size the static texture declaration: " + symbol);
        string type = Regex.Match(declaration, @"const\s+(\w+)").Groups[1].Value;
        int elementBytes = type == "u64" ? 8 : type == "u32" ? 4 : type == "u16" ? 2 : 1;
        int bytes = texture.Width * texture.Height * int.Parse(texture.Size[..^1]) / 8;
        if (bytes % elementBytes != 0) throw new InvalidDataException("texture declaration has an incompatible element type");
        return declaration.Replace("[];", $"[{bytes / elementBytes}];");
    }
}
