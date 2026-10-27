using OpenTK.Mathematics;
using Sm64DecompLevelViewer.Models;

namespace Sm64DecompLevelViewer.Services;

public static class ObjectSpawnPlacement
{
    public static int GroundY(int x, int y, int z, CollisionMesh? collision)
    {
        if (collision == null) throw new InvalidOperationException("terrain collision is unavailable; disable place on terrain to enter an explicit position");
        var point = new EditableGeometry();
        point.Vertices.Add(new Vector3(x, y, z));
        var excluded = collision.Triangles.Select((triangle, index) => (triangle, index))
            .Where(item => item.triangle.SurfaceType.Contains("DEATH", StringComparison.OrdinalIgnoreCase))
            .Select(item => item.index).ToHashSet();
        int ground = checked((int)Math.Ceiling(y + ModelPlacementService.GroundOffset(point, collision, excluded)));
        if (ground < short.MinValue || ground > short.MaxValue) throw new InvalidOperationException("terrain height is outside the object coordinate range");
        return ground;
    }
}
