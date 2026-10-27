using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using Sm64DecompLevelViewer.Models;
using Sm64DecompLevelViewer.Services;
using System.IO;
using System.Linq;

namespace Sm64DecompLevelViewer.Rendering;
internal static class RayIntersections
{
    internal static bool RayIntersectsBox(Vector3 rayOrigin, Vector3 rayDir, Vector3 boxCenter, float halfSize, out float distance)
    {
        distance = float.MaxValue;

        Vector3 boxMin = boxCenter - new Vector3(halfSize);
        Vector3 boxMax = boxCenter + new Vector3(halfSize);

        float tMin = 0.0f;
        float tMax = float.MaxValue;

        for (int i = 0; i < 3; i++)
        {
            float origin = i == 0 ? rayOrigin.X : (i == 1 ? rayOrigin.Y : rayOrigin.Z);
            float dir = i == 0 ? rayDir.X : (i == 1 ? rayDir.Y : rayDir.Z);
            float min = i == 0 ? boxMin.X : (i == 1 ? boxMin.Y : boxMin.Z);
            float max = i == 0 ? boxMax.X : (i == 1 ? boxMax.Y : boxMax.Z);

            if (MathF.Abs(dir) < 0.0001f)
            {
                if (origin < min || origin > max)
                    return false;
            }
            else
            {
                float t1 = (min - origin) / dir;
                float t2 = (max - origin) / dir;

                if (t1 > t2)
                {
                    float temp = t1;
                    t1 = t2;
                    t2 = temp;
                }

                tMin = MathF.Max(tMin, t1);
                tMax = MathF.Min(tMax, t2);

                if (tMin > tMax)
                    return false;
            }
        }

        distance = tMin;
        return true;
    }
    internal static bool RayIntersectsTriangle(Vector3 rayOrigin, Vector3 rayDir, Vector3 v0, Vector3 v1, Vector3 v2, out float distance)
    {
        distance = 0f;
        const float epsilon = 0.000001f;

        Vector3 edge1 = v1 - v0;
        Vector3 edge2 = v2 - v0;

        Vector3 h = Vector3.Cross(rayDir, edge2);
        float a = Vector3.Dot(edge1, h);

        if (a > -epsilon && a < epsilon)
            return false; // Ray is parallel to the triangle.

        float f = 1.0f / a;
        Vector3 s = rayOrigin - v0;
        float u = f * Vector3.Dot(s, h);

        if (u < 0.0f || u > 1.0f)
            return false;

        Vector3 q = Vector3.Cross(s, edge1);
        float v = f * Vector3.Dot(rayDir, q);

        if (v < 0.0f || u + v > 1.0f)
            return false;

        // At this stage we can compute t to find out where the intersection point is on the line.
        float t = f * Vector3.Dot(edge2, q);

        if (t > epsilon) // ray intersection
        {
            distance = t;
            return true;
        }

        return false;
    }
}
