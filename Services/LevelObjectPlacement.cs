using System.IO;
using System.Text.RegularExpressions;
using OpenTK.Mathematics;
using Sm64DecompLevelViewer.Models;

namespace Sm64DecompLevelViewer.Services;

/// <summary>Preview spawn adjustments without changing the editable source coordinates.</summary>
public sealed class LevelObjectPlacement
{
    private readonly HashSet<string> _floorBehaviors = new();
    public LevelObjectPlacement(string root)
    {
        string path=Path.Combine(root,"data","behavior_data.c");
        if(!File.Exists(path)) return;
        string source=CSourceCalls.MaskComments(ActorSourceReader.Read(path));
        foreach(Match match in Regex.Matches(source,@"\bBehaviorScript\s+(\w+)\s*\[\s*\]\s*=\s*\{([\s\S]*?)\};"))
        {
            string init=match.Groups[2].Value.Split("BEGIN_LOOP",StringSplitOptions.None)[0];
            if(Regex.IsMatch(init,@"\bDROP_TO_FLOOR\s*\(")) _floorBehaviors.Add(match.Groups[1].Value);
        }
    }
    public Vector3 Position(LevelObject obj,CollisionMesh? mesh)
    {
        var position=new Vector3(obj.X,obj.Y,obj.Z);
        if(mesh==null || !_floorBehaviors.Contains(obj.Behavior)) return position;
        // bhv_cmd_drop_to_floor queries at source Y + 200. Never snap upward to another floor.
        float floor=float.NegativeInfinity;
        foreach(var tri in mesh.Triangles)
        {
            var a=mesh.Vertices[tri.V1]; var b=mesh.Vertices[tri.V2]; var c=mesh.Vertices[tri.V3];
            double d=(b.Z-c.Z)*(double)(a.X-c.X)+(c.X-b.X)*(double)(a.Z-c.Z);
            if(d>=-0.000001) continue; // upward-facing triangle
            double u=((b.Z-c.Z)*(double)(obj.X-c.X)+(c.X-b.X)*(double)(obj.Z-c.Z))/d;
            double v=((c.Z-a.Z)*(double)(obj.X-c.X)+(a.X-c.X)*(double)(obj.Z-c.Z))/d;
            if(u < -0.00001 || v < -0.00001 || u+v>1.00001) continue;
            float y=(float)(u*a.Y+v*b.Y+(1-u-v)*c.Y);
            if(y<=obj.Y+200 && y>floor) floor=y;
        }
        if(float.IsFinite(floor)) position.Y=floor;
        return position;
    }
}
