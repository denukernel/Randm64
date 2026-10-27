using System.IO;
using System.Linq;
using Sm64DecompLevelViewer.Models;
using Sm64DecompLevelViewer.Services;
using OpenTK.Mathematics;
using System.Runtime.InteropServices;

namespace Sm64DecompLevelViewer.Services.Levels;
internal static class LevelFloorQuery
{
        internal static double? FindFloorY(CollisionMesh? _collisionMesh, double x, double currentY, double z)
        {
            if (_collisionMesh == null) return null;

            double? bestFloorY = null;
            double minDistanceBelow = double.MaxValue;

            foreach (var tri in _collisionMesh.Triangles)
            {
                if (tri.V1 >= _collisionMesh.Vertices.Count || 
                    tri.V2 >= _collisionMesh.Vertices.Count || 
                    tri.V3 >= _collisionMesh.Vertices.Count)
                    continue;

                var v0 = _collisionMesh.Vertices[tri.V1];
                var v1 = _collisionMesh.Vertices[tri.V2];
                var v2 = _collisionMesh.Vertices[tri.V3];

                // Calculate triangle normal vector Y component
                // normal = (v1 - v0) x (v2 - v0)
                double edge1X = v1.X - v0.X;
                double edge1Y = v1.Y - v0.Y;
                double edge1Z = v1.Z - v0.Z;

                double edge2X = v2.X - v0.X;
                double edge2Y = v2.Y - v0.Y;
                double edge2Z = v2.Z - v0.Z;

                double normalY = edge1Z * edge2X - edge1X * edge2Z;

                // Floors must face upwards (normalY > 0.01)
                if (normalY <= 0.01)
                    continue;

                // Check if (x, z) is within the projected 2D triangle
                if (!IsPointInTriangle2D(x, z, v0.X, v0.Z, v1.X, v1.Z, v2.X, v2.Z))
                    continue;

                double normalX = edge1Y * edge2Z - edge1Z * edge2Y;
                double normalZ = edge1X * edge2Y - edge1Y * edge2X;

                // Compute Y coordinate on the triangle's plane at (x, z)
                double y = v0.Y - (normalX * (x - v0.X) + normalZ * (z - v0.Z)) / normalY;

                // We want the floor closest to the object, preferably below it.
                // We allow a small tolerance of 50 units for being "at" or slightly below the floor.
                if (y <= currentY + 50)
                {
                    double dist = currentY - y;
                    if (dist < minDistanceBelow)
                    {
                        minDistanceBelow = dist;
                        bestFloorY = y;
                    }
                }
            }

            // Fallback: If no floor is found below the object, check for the closest floor overall (above or below)
            if (!bestFloorY.HasValue)
            {
                double minAbsDistance = double.MaxValue;
                foreach (var tri in _collisionMesh.Triangles)
                {
                    if (tri.V1 >= _collisionMesh.Vertices.Count || 
                        tri.V2 >= _collisionMesh.Vertices.Count || 
                        tri.V3 >= _collisionMesh.Vertices.Count)
                        continue;

                    var v0 = _collisionMesh.Vertices[tri.V1];
                    var v1 = _collisionMesh.Vertices[tri.V2];
                    var v2 = _collisionMesh.Vertices[tri.V3];

                    double edge1X = v1.X - v0.X;
                    double edge1Y = v1.Y - v0.Y;
                    double edge1Z = v1.Z - v0.Z;

                    double edge2X = v2.X - v0.X;
                    double edge2Y = v2.Y - v0.Y;
                    double edge2Z = v2.Z - v0.Z;

                    double normalY = edge1Z * edge2X - edge1X * edge2Z;

                    if (normalY <= 0.01)
                        continue;

                    if (!IsPointInTriangle2D(x, z, v0.X, v0.Z, v1.X, v1.Z, v2.X, v2.Z))
                        continue;

                    double normalX = edge1Y * edge2Z - edge1Z * edge2Y;
                    double normalZ = edge1X * edge2Y - edge1Y * edge2X;

                    double y = v0.Y - (normalX * (x - v0.X) + normalZ * (z - v0.Z)) / normalY;

                    double dist = Math.Abs(currentY - y);
                    if (dist < minAbsDistance)
                    {
                        minAbsDistance = dist;
                        bestFloorY = y;
                    }
                }
            }

            return bestFloorY;
        }
        internal static bool IsPointInTriangle2D(double px, double pz, double x1, double z1, double x2, double z2, double x3, double z3)
        {
            double d1 = (px - x2) * (z1 - z2) - (x1 - x2) * (pz - z2);
            double d2 = (px - x3) * (z2 - z3) - (x2 - x3) * (pz - z3);
            double d3 = (px - x1) * (z3 - z1) - (x3 - x1) * (pz - z1);

            bool has_neg = (d1 < 0) || (d2 < 0) || (d3 < 0);
            bool has_pos = (d1 > 0) || (d2 > 0) || (d3 > 0);

            return !(has_neg && has_pos);
        }
}
