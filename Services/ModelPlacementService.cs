using System.IO;
using OpenTK.Mathematics;
using Sm64DecompLevelViewer.Models;
using StbImageSharp;

namespace Sm64DecompLevelViewer.Services;

public static class ModelPlacementService
{
    // Choose the nearest floor below the model. If the model starts below the level, recover to the nearest floor above it.
    public static float GroundOffset(EditableGeometry model,CollisionMesh collision,ISet<int>? excluded=null)
    {
        var min=model.Vertices.Aggregate(Vector3.ComponentMin); var max=model.Vertices.Aggregate(Vector3.ComponentMax);
        float x=(min.X+max.X)/2,z=(min.Z+max.Z)/2;
        var floors=new List<float>();
        for(int i=0;i<collision.Triangles.Count;i++)
        {
            if(excluded?.Contains(i)==true) continue;
            var t=collision.Triangles[i];
            var a=collision.Vertices[t.V1]; var b=collision.Vertices[t.V2]; var c=collision.Vertices[t.V3];
            var normal=Vector3.Cross(new Vector3(b.X-a.X,b.Y-a.Y,b.Z-a.Z),new Vector3(c.X-a.X,c.Y-a.Y,c.Z-a.Z));
            if(normal.Y<=0.01f) continue;
            double determinant=(b.Z-c.Z)*(double)(a.X-c.X)+(c.X-b.X)*(double)(a.Z-c.Z);
            if(Math.Abs(determinant)<0.000001) continue;
            double u=((b.Z-c.Z)*(double)(x-c.X)+(c.X-b.X)*(double)(z-c.Z))/determinant;
            double v=((c.Z-a.Z)*(double)(x-c.X)+(a.X-c.X)*(double)(z-c.Z))/determinant;
            if(u < -0.00001 || v < -0.00001 || u+v>1.00001) continue;
            floors.Add((float)(u*a.Y+v*b.Y+(1-u-v)*c.Y));
        }
        if(floors.Count==0) throw new InvalidOperationException("No floor at this X/Z position. Move the model over terrain, then drop it to ground.");
        float floor=floors.Where(y=>y<=min.Y+1).DefaultIfEmpty(float.NaN).Max();
        if(float.IsNaN(floor)) floor=floors.Min();
        return floor-min.Y;
    }

    public static EditableGeometry FromVisual(VisualMesh mesh,Func<ModelTriangle,bool>? filter=null)
    {
        var result=new EditableGeometry();
        var materials=new Dictionary<(string?,TextureAddressMode,TextureAddressMode,float),int>();
        foreach(var tri in mesh.Triangles.Where(t=>filter?.Invoke(t)!=false))
        {
            var key=(tri.TextureName,tri.TextureAddressS,tri.TextureAddressT,tri.Opacity);
            if(!materials.TryGetValue(key,out int materialIndex))
            {
                var material=new ImportedMaterial { Name=tri.TextureName ?? "Solid color",Color=new Vector4(1,1,1,tri.Opacity),WrapS=tri.TextureAddressS,WrapT=tri.TextureAddressT };
                if(tri.TextureName!=null && mesh.TexturePaths.TryGetValue(tri.TextureName,out string? path) && File.Exists(path))
                {
                    var image=ImageResult.FromMemory(File.ReadAllBytes(path),ColorComponents.RedGreenBlueAlpha);
                    material.Width=image.Width; material.Height=image.Height; material.Pixels=image.Data;
                }
                materialIndex=result.Materials.Count; materials[key]=materialIndex; result.Materials.Add(material);
            }
            var m=result.Materials[materialIndex]; int start=result.Vertices.Count;
            foreach(int index in new[]{tri.V1,tri.V2,tri.V3})
            {
                var vertex=mesh.Vertices[index]; result.Vertices.Add(new Vector3(vertex.X,vertex.Y,vertex.Z));
                result.TextureCoordinates.Add(new Vector2(vertex.S/(32f*Math.Max(1,m.Width)),vertex.T/(32f*Math.Max(1,m.Height))));
                result.VertexColors.Add(!tri.LightingEnabled ? vertex.VertexColor : tri.ShadeLightSymbol!=null && mesh.LightGroupColors.TryGetValue(tri.ShadeLightSymbol,out var color) ? new Vector4(color,1) : Vector4.One);
            }
            result.Triangles.Add((start,start+1,start+2)); result.TriangleMaterials.Add(materialIndex);
        }
        return result;
    }
}
