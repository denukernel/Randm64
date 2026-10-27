using OpenTK.Mathematics;

namespace Sm64DecompLevelViewer.Services;

public static class LevelCameraNavigation
{
    public static Vector3 Movement(float yaw, Vector3 input, float speed, float elapsed, bool fast, bool precise)
    {
        float radians = MathHelper.DegreesToRadians(yaw);
        var forward = new Vector3(-MathF.Cos(radians), 0, -MathF.Sin(radians));
        var right = new Vector3(MathF.Sin(radians), 0, -MathF.Cos(radians));
        var direction = right * input.X + Vector3.UnitY * input.Y + forward * input.Z;
        if (direction.LengthSquared > 1) direction.Normalize();
        return direction * Math.Max(0, speed) * Math.Clamp(elapsed, 0, 0.1f) * (precise ? 0.25f : fast ? 4 : 1);
    }

    public static Vector3 Pan(float yaw, float pitch, float distance, int height, Vector2 pixels)
    {
        float yawRadians = MathHelper.DegreesToRadians(yaw), pitchRadians = MathHelper.DegreesToRadians(pitch);
        var right = new Vector3(MathF.Sin(yawRadians), 0, -MathF.Cos(yawRadians));
        var forward = new Vector3(-MathF.Cos(pitchRadians) * MathF.Cos(yawRadians), -MathF.Sin(pitchRadians), -MathF.Cos(pitchRadians) * MathF.Sin(yawRadians));
        var up = Vector3.Cross(right, forward);
        float unitsPerPixel = 2 * distance * MathF.Tan(MathHelper.DegreesToRadians(22.5f)) / Math.Max(1, height);
        return (-right * pixels.X + up * pixels.Y) * unitsPerPixel;
    }

    public static float Zoom(float distance, float wheel, bool precise)
    {
        if (wheel == 0) return distance;
        return Math.Clamp(distance * MathF.Exp(-wheel * (precise ? 0.03f : 0.15f)), 100, 100000);
    }
}
