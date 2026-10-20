using System.IO;
using System.Text;
using OpenTK.Mathematics;

namespace Sm64DecompLevelViewer.Services;

internal static class ImportedMeshWriter
{
    internal sealed record Result(string Source,List<(string Symbol,string Layer)> Lists,Dictionary<string,byte[]> Assets);
    public static Result Build(string modelPath,string? projectRoot,EditableGeometry geometry,string symbol)
    {
        var source=new StringBuilder($"\n/* Imported level geometry: {symbol} */\n");
        var lists=new List<(string,string)>();
        var assets=new Dictionary<string,byte[]>();
        var groups=Enumerable.Range(0,geometry.Triangles.Count).GroupBy(i=>geometry.TriangleMaterials.Count==0 ? -1 : geometry.TriangleMaterials[i]);
        foreach(var group in groups)
        {
            var material=group.Key<0 ? new ImportedMaterial { Color=new Vector4(176,184,196,255)/255f } : geometry.Materials[group.Key];
            string name=symbol+"_m"+lists.Count;
            if(material.Pixels!=null)
            {
                if(projectRoot==null) throw new ArgumentException("A decomp project root is required to save imported textures.");
                string png=Path.Combine(Path.GetDirectoryName(modelPath)!,name+".rgba16.png");
                string relative=Path.GetRelativePath(projectRoot,png);
                if(relative.StartsWith("..") || Path.IsPathRooted(relative)) throw new ArgumentException("Imported model source must be inside the decomp repository.");
                var bytes=new StringBuilder();
                for(int i=0;i<material.Width*material.Height;i++)
                {
                    int p=i*4;
                    int rgba=(material.Pixels[p]>>3)<<11 | (material.Pixels[p+1]>>3)<<6 | (material.Pixels[p+2]>>3)<<1 | (material.Pixels[p+3]>=128 ? 1 : 0);
                    bytes.Append($"0x{rgba>>8:X2}, 0x{rgba&255:X2},");
                    if(i%8==7) bytes.AppendLine();
                }
                assets[png]=ModelImportService.EncodePng(material);
                assets[png.Replace(".png",".inc.c")]=Encoding.UTF8.GetBytes(bytes.ToString());
                source.AppendLine($"ALIGNED8 static const Texture {name}_texture[] = {{\n#include \"{relative.Replace('\\','/').Replace(".png",".inc.c")}\"\n}};");
            }
            foreach(int i in group)
            {
                var triangle=geometry.Triangles[i];
                source.AppendLine($"static const Vtx {symbol}_v{i}[] = {{");
                foreach(int index in new[]{triangle.A,triangle.B,triangle.C})
                {
                    var p=geometry.Vertices[index];
                    var uv=geometry.TextureCoordinates.Count==0 ? Vector2.Zero : geometry.TextureCoordinates[index];
                    var color=Vector4.Clamp((geometry.VertexColors.Count==0 ? Vector4.One : geometry.VertexColors[index])*material.Color,Vector4.Zero,Vector4.One);
                    int s=Coordinate(uv.X,material.Width),t=Coordinate(uv.Y,material.Height);
                    source.AppendLine("    {{{"+$"{(int)MathF.Round(p.X)}, {(int)MathF.Round(p.Y)}, {(int)MathF.Round(p.Z)}"+"}, 0, {"+$"{s}, {t}"+"}, {"+$"0x{(byte)(color.X*255):x2}, 0x{(byte)(color.Y*255):x2}, 0x{(byte)(color.Z*255):x2}, 0x{(byte)(color.W*255):x2}"+"}}},");
                }
                source.AppendLine("};");
            }
            source.AppendLine($"const Gfx {name}[] = {{\n    gsDPPipeSync(),\n    gsSPClearGeometryMode(G_LIGHTING),");
            if(material.Pixels!=null)
            {
                string Address(Models.TextureAddressMode mode)=>mode==Models.TextureAddressMode.Clamp ? "G_TX_CLAMP" : mode==Models.TextureAddressMode.Mirror ? "G_TX_WRAP | G_TX_MIRROR" : "G_TX_WRAP";
                source.AppendLine($"    gsDPSetCombineMode(G_CC_MODULATEIA, G_CC_MODULATEIA),\n    gsSPTexture(0xFFFF, 0xFFFF, 0, G_TX_RENDERTILE, G_ON),\n    gsDPLoadTextureBlock({name}_texture, G_IM_FMT_RGBA, G_IM_SIZ_16b, {material.Width}, {material.Height}, 0, {Address(material.WrapS)}, {Address(material.WrapT)}, {(int)Math.Log2(material.Width)}, {(int)Math.Log2(material.Height)}, G_TX_NOLOD, G_TX_NOLOD),");
            }
            else source.AppendLine("    gsSPTexture(0xFFFF, 0xFFFF, 0, G_TX_RENDERTILE, G_OFF),\n    gsDPSetCombineMode(G_CC_SHADE, G_CC_SHADE),");
            foreach(int i in group) source.AppendLine($"    gsSPVertex({symbol}_v{i}, 3, 0),\n    gsSP1Triangle(0, 1, 2, 0x0),");
            source.AppendLine("    gsSPTexture(0xFFFF, 0xFFFF, 0, G_TX_RENDERTILE, G_OFF),\n    gsDPPipeSync(),\n    gsDPSetCombineMode(G_CC_SHADE, G_CC_SHADE),\n    gsSPSetGeometryMode(G_LIGHTING),\n    gsSPEndDisplayList(),\n};");
            bool transparent=material.Transparent || group.Any(i=>new[]{geometry.Triangles[i].A,geometry.Triangles[i].B,geometry.Triangles[i].C}.Any(v=>geometry.VertexColors.Count>0 && geometry.VertexColors[v].W<0.999f));
            lists.Add((name,transparent ? "LAYER_TRANSPARENT" : "LAYER_OPAQUE"));
        }
        return new Result(source.ToString(),lists,assets);
    }
    private static int Coordinate(float uv,int dimension)
    {
        float value=MathF.Round(uv*dimension*32);
        if(!float.IsFinite(value) || value<short.MinValue || value>short.MaxValue) throw new ArgumentException("UV tiling exceeds the N64 signed 16-bit coordinate range. Reduce texture tiling before import.");
        return (int)value;
    }
}
