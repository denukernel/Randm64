using OpenTK.Mathematics;
using Sm64DecompLevelViewer.Models;

namespace Sm64DecompLevelViewer.Services;
public sealed record CollisionFinding(string Kind,int Triangle,int Vertex,string Detail);
public sealed record CollisionDiagnosticSettings(double SmallArea=1,double SteepDegrees=38,double VisualDistance=5);
public static class CollisionDiagnostics
{
    public static List<CollisionFinding> Analyze(CollisionMesh mesh,VisualMesh? visual,CollisionDiagnosticSettings settings,CancellationToken cancel=default)
    {
        if(!double.IsFinite(settings.SmallArea) || settings.SmallArea<0 || !double.IsFinite(settings.SteepDegrees) || settings.SteepDegrees<0 || settings.SteepDegrees>90 || !double.IsFinite(settings.VisualDistance) || settings.VisualDistance<0)
            throw new ArgumentException("Diagnostic thresholds must be finite and nonnegative; slope must be 0 to 90 degrees.");
        var result=new List<CollisionFinding>(); var faces=new HashSet<string>();
        var edges=new Dictionary<(int,int),List<int>>();
        Vector3 P(int i) { var v=mesh.Vertices[i]; return new(v.X,v.Y,v.Z); }
        for(int i=0;i<mesh.Triangles.Count;i++)
        {
            cancel.ThrowIfCancellationRequested(); var t=mesh.Triangles[i]; var ids=new[]{t.V1,t.V2,t.V3};
            if(ids.Any(v=>v<0 || v>=mesh.Vertices.Count)) { result.Add(new("invalid index",i,-1,"Triangle references a missing vertex")); continue; }
            string key=string.Join(",",ids.OrderBy(v=>v));
            if(!faces.Add(key)) result.Add(new("duplicate",i,-1,"Same vertex set as an earlier triangle; winding and surface may differ"));
            var cross=Vector3.Cross(P(t.V2)-P(t.V1),P(t.V3)-P(t.V1)); double area=cross.Length/2;
            if(area<1e-8) result.Add(new("degenerate",i,-1,"Triangle has zero area"));
            else
            {
                if(area<settings.SmallArea) result.Add(new("small triangle",i,-1,$"Area {area:0.###} square world units"));
                double slope=Math.Acos(Math.Clamp(cross.Y/cross.Length,-1,1))*180/Math.PI;
                if(cross.Y>0 && slope>settings.SteepDegrees) result.Add(new("steep surface",i,-1,$"Upward-facing slope {slope:0.##} degrees"));
            }
            for(int j=0;j<3;j++)
            {
                int a=ids[j],b=ids[(j+1)%3]; if(a==b) continue; var edge=(Math.Min(a,b),Math.Max(a,b));
                if(!edges.TryGetValue(edge,out var list)) edges[edge]=list=new(); list.Add(i);
            }
        }
        foreach(var edge in edges.Where(e=>e.Value.Count==1)) result.Add(new("possible gap",edge.Value[0],-1,$"Boundary edge {edge.Key.Item1} to {edge.Key.Item2}; open boundaries can be intentional"));
        if(visual!=null)
        {
            var triangles=new List<(Vector3 A,Vector3 B,Vector3 C)>();
            void Add(List<ModelVertex> vertices,List<ModelTriangle> indices)
            {
                Vector3 V(int i)=>new(vertices[i].X,vertices[i].Y,vertices[i].Z);
                foreach(var t in indices) if(new[]{t.V1,t.V2,t.V3}.All(i=>i>=0 && i<vertices.Count)) triangles.Add((V(t.V1),V(t.V2),V(t.V3)));
            }
            if(visual.SubMeshes.Count>0) foreach(var sub in visual.SubMeshes.Where(s=>s.IsVisible)) Add(sub.Vertices,sub.Triangles); else Add(visual.Vertices,visual.Triangles);
            if(triangles.Count>0)
            {
                var tree=new SurfaceTree(triangles.ToArray());
                for(int i=0;i<mesh.Vertices.Count;i++)
                {
                    cancel.ThrowIfCancellationRequested();
                    if(!tree.Near(P(i),(float)settings.VisualDistance)) result.Add(new("possible visual mismatch",mesh.Triangles.FindIndex(t=>t.V1==i || t.V2==i || t.V3==i),i,$"Vertex farther than {settings.VisualDistance} units from visible geometry; intentional invisible collision is possible"));
                }
            }
        }
        return result;
    }
    private sealed class SurfaceTree
    {
        private readonly Vector3 min,max; private readonly SurfaceTree? left,right;
        private readonly (Vector3 A,Vector3 B,Vector3 C)[]? leaves;
        public SurfaceTree((Vector3 A,Vector3 B,Vector3 C)[] faces)
        {
            min=new(float.MaxValue); max=new(float.MinValue);
            foreach(var f in faces) { min=Vector3.ComponentMin(min,Vector3.ComponentMin(f.A,Vector3.ComponentMin(f.B,f.C))); max=Vector3.ComponentMax(max,Vector3.ComponentMax(f.A,Vector3.ComponentMax(f.B,f.C))); }
            if(faces.Length<=16) { leaves=faces; return; }
            var extent=max-min; int axis=extent.X>=extent.Y && extent.X>=extent.Z ? 0 : extent.Y>=extent.Z ? 1 : 2;
            Array.Sort(faces,(a,b)=>((a.A+a.B+a.C)[axis]).CompareTo((b.A+b.B+b.C)[axis]));
            left=new(faces[..(faces.Length/2)]); right=new(faces[(faces.Length/2)..]);
        }
        public bool Near(Vector3 p,float distance)
        {
            if((p-Vector3.Clamp(p,min,max)).LengthSquared>distance*distance) return false;
            if(leaves==null) return left!.Near(p,distance) || right!.Near(p,distance);
            return leaves.Any(f=>DistanceSquared(p,f.A,f.B,f.C)<=distance*distance);
        }
    }
    public static float DistanceSquared(Vector3 p,Vector3 a,Vector3 b,Vector3 c)
    {
        float Segment(Vector3 x,Vector3 y) { var d=y-x; float t=d.LengthSquared==0 ? 0 : Math.Clamp(Vector3.Dot(p-x,d)/d.LengthSquared,0,1); return (p-(x+t*d)).LengthSquared; }
        var ab=b-a; var ac=c-a; var n=Vector3.Cross(ab,ac);
        if(n.LengthSquared<1e-12f) return Math.Min(Segment(a,b),Math.Min(Segment(b,c),Segment(c,a)));
        var projected=p-n*(Vector3.Dot(p-a,n)/n.LengthSquared);
        float u=Vector3.Dot(Vector3.Cross(b-a,projected-a),n),v=Vector3.Dot(Vector3.Cross(c-b,projected-b),n),w=Vector3.Dot(Vector3.Cross(a-c,projected-c),n);
        return u>=0 && v>=0 && w>=0 ? (p-projected).LengthSquared : Math.Min(Segment(a,b),Math.Min(Segment(b,c),Segment(c,a)));
    }
}
