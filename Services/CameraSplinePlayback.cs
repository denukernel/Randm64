using OpenTK.Mathematics;

namespace Sm64DecompLevelViewer.Services;

// Matches evaluate_cubic_spline / move_point_along_spline in src/game/camera.c.
public sealed class CameraSplinePlayback
{
    public CameraSplinePlayback(float initialProgress = 0) { Progress = initialProgress; }
    public static Vector3 ToWorld(string? name, Vector3 point) => name?.StartsWith("sIntroStartToPipe",StringComparison.Ordinal) == true
        ? new Vector3(-point.X-1328,point.Y+260,-point.Z+4664) : point;
    public static (Vector3 Position,Vector3 Focus) WorldPose(string name,Vector3 position,Vector3 focus)
    {
        position=ToWorld(name,position); focus=ToWorld(name,focus);
        if (name.StartsWith("sIntroStartToPipe",StringComparison.Ordinal))
        {
            var delta=focus-position;
            float length=delta.Length, horizontal=new Vector2(delta.X,delta.Z).Length;
            float pitch=Math.Clamp(MathF.Atan2(delta.Y,horizontal),-0x3B00*MathF.PI/32768,0x3B00*MathF.PI/32768);
            var yaw=horizontal>0 ? new Vector2(delta.X,delta.Z)/horizontal : Vector2.UnitY;
            focus=position+new Vector3(yaw.X*MathF.Cos(pitch),MathF.Sin(pitch),yaw.Y*MathF.Cos(pitch))*length;
        }
        return (position,focus);
    }
    public int Segment { get; private set; }
    public float Progress { get; private set; }
    public bool Finished { get; private set; }
    public static Vector3 Evaluate(IReadOnlyList<SplinePoint> points, int segment, float u)
    {
        u = Math.Clamp(u, 0, 1);
        float a = 1 - u;
        float[] weights = { a*a*a/6, u*u*u/2-u*u+2f/3, -u*u*u/2+u*u/2+u/2+1f/6, u*u*u/6 };
        var result = Vector3.Zero;
        for (int i=0;i<4;i++)
        {
            var p = points[Math.Clamp(segment+i,0,points.Count-1)];
            result += new Vector3(p.X,p.Y,p.Z) * weights[i];
        }
        return result;
    }
    public Vector3 Advance(IReadOnlyList<SplinePoint> points)
    {
        if (points.Count < 4 || Segment+3 >= points.Count || points[Segment].Index == -1 || points[Segment+1].Index == -1 || points[Segment+2].Index == -1)
        { Finished = true; return points.Count == 0 ? Vector3.Zero : Evaluate(points,Math.Max(0,points.Count-4),1); }
        var position = Evaluate(points, Segment, Progress);
        float first = points[Segment+1].Speed == 0 ? 0 : 1f/points[Segment+1].Speed;
        float second = points[Segment+2].Speed == 0 ? 0 : 1f/points[Segment+2].Speed;
        Progress += (second-first)*Progress+first;
        if (Progress >= 1)
        {
            Segment++;
            Progress--;
            if (Segment+3 >= points.Count || points[Segment+3].Index == -1) Finished = true;
        }
        return position;
    }
}
