using System.IO;
using OpenTK.Mathematics;
using Sm64DecompLevelViewer.Models;

namespace Sm64DecompLevelViewer.Services;

public enum GeometryEditTarget { MeshAndCollision, MeshOnly, CollisionOnly }

public sealed partial class MeshEditSession
{
    public CollisionMesh? Collision { get; private set; }
    public CollisionSurfacePolicy? SurfacePolicy { get; private set; }
    private string? collisionSourcePath;
    private bool collisionDirty;

    private void InitializeCollision(CollisionMesh? collision, string? path)
    {
        if (collision == null || path == null || !File.Exists(path)) return;
        Collision = collision;
        collisionSourcePath = path;
        SurfacePolicy = CollisionSurfacePolicy.ForPath(path);
        foreach (string file in new[] { path, Path.Combine(Path.GetDirectoryName(path)!, "room.inc.c"), Path.Combine(Path.GetDirectoryName(path)!, "rooms.inc.c") })
            if (File.Exists(file)) expected[file] = File.ReadAllBytes(file);
    }

    private Vector3 VisualPosition(int i) => new(Vertices[i].X, Vertices[i].Y, Vertices[i].Z);
    public Vector3 CollisionPosition(int i) => new(Collision!.Vertices[i].X, Collision.Vertices[i].Y, Collision.Vertices[i].Z);
    private void SetCollisionPosition(int i, Vector3 p)
    {
        var v = Collision!.Vertices[i]; v.X = (int)p.X; v.Y = (int)p.Y; v.Z = (int)p.Z;
    }

    public Move BeginCollisionMove(IEnumerable<int> selected, GeometryEditTarget target = GeometryEditTarget.CollisionOnly)
    {
        lock (sync)
        {
            if (Collision == null) throw new InvalidDataException("no collision data is loaded");
            var ids = selected.ToArray();
            var points = ids.Select(CollisionPosition).ToHashSet();
            var visual = target == GeometryEditTarget.MeshAndCollision
                ? Enumerable.Range(0, Vertices.Length).Where(i => points.Contains(VisualPosition(i))).ToArray() : Array.Empty<int>();
            return BeginGeometryMove(visual, ids);
        }
    }

    public int[] CollisionFaceVertices(int triangle)
    {
        if (Collision == null) return Array.Empty<int>();
        var t = Collision.Triangles[triangle];
        var a = CollisionPosition(t.V1); var b = CollisionPosition(t.V2); var c = CollisionPosition(t.V3);
        var normal = Vector3.Cross(b - a, c - a);
        if (normal.LengthSquared < 0.0001f) return new[] { t.V1, t.V2, t.V3 };
        normal.Normalize();
        var points = new HashSet<Vector3> { a, b, c };
        bool expanded;
        do
        {
            expanded = false;
            foreach (var face in Collision.Triangles)
            {
                var p = new[] { CollisionPosition(face.V1), CollisionPosition(face.V2), CollisionPosition(face.V3) };
                if (p.Count(points.Contains) < 2) continue;
                var n = Vector3.Cross(p[1] - p[0], p[2] - p[0]);
                if (n.LengthSquared < 0.0001f || Vector3.Dot(Vector3.Normalize(n), normal) < 0.9999f || Math.Abs(Vector3.Dot(p[0] - a, normal)) > 0.01f) continue;
                foreach (var vertex in p) expanded |= points.Add(vertex);
            }
        } while (expanded);
        return Enumerable.Range(0, Collision.Vertices.Count).Where(i => points.Contains(CollisionPosition(i))).ToArray();
    }

    public int[] WeldedCollisionVertex(int i)
    {
        var point = CollisionPosition(i);
        return Enumerable.Range(0, Collision!.Vertices.Count).Where(v => CollisionPosition(v) == point).ToArray();
    }

    public bool PaintCollisionSurface(int triangle, string surface, int? force)
    {
        lock (sync)
        {
            if (Collision == null || SurfacePolicy == null) throw new InvalidDataException("no collision is loaded");
            SurfacePolicy.Validate(surface, force);
            var t = Collision.Triangles[triangle];
            if (t.SurfaceType == surface && t.SpecialParam == force) return false;
            t.SurfaceType = surface;
            t.SpecialParam = force;
            collisionDirty = true;
            IsDirty = true;
            return true;
        }
    }
}
