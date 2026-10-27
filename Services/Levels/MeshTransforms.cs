namespace Sm64DecompLevelViewer.Services.Levels;
internal static class MeshTransforms
{
    public static bool Fits(IEnumerable<(int X, int Y, int Z)> points, double sx, double sy, double sz, int dx = 0, int dy = 0, int dz = 0)
    {
            return points.All(v => Math.Round(v.X * sx + dx) >= short.MinValue && Math.Round(v.X * sx + dx) <= short.MaxValue
                && Math.Round(v.Y * sy + dy) >= short.MinValue && Math.Round(v.Y * sy + dy) <= short.MaxValue
                && Math.Round(v.Z * sz + dz) >= short.MinValue && Math.Round(v.Z * sz + dz) <= short.MaxValue);
    }
    public static void Translate(IEnumerable<Sm64DecompLevelViewer.Models.Levels.IMeshPosition> vertices, int dx, int dy, int dz)
    {
        foreach (var vertex in vertices) { vertex.X += dx; vertex.Y += dy; vertex.Z += dz; }
    }
    public static void Scale(IEnumerable<Sm64DecompLevelViewer.Models.Levels.IMeshPosition> vertices, double sx, double sy, double sz)
    {
        foreach (var vertex in vertices)
        {
            vertex.X = (int)Math.Round(vertex.X * sx);
            vertex.Y = (int)Math.Round(vertex.Y * sy);
            vertex.Z = (int)Math.Round(vertex.Z * sz);
        }
    }
}
