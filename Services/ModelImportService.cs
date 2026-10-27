using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Assimp;
using OpenTK.Mathematics;
using StbImageSharp;

namespace Sm64DecompLevelViewer.Services;

public sealed class ImportedMaterial
{
    public string Name { get; set; } = "Material";
    public Vector4 Color { get; set; } = Vector4.One;
    public byte[]? Pixels { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public Sm64DecompLevelViewer.Models.TextureAddressMode WrapS { get; set; }
    public Sm64DecompLevelViewer.Models.TextureAddressMode WrapT { get; set; }
    public int UvChannel { get; set; }
    public bool Transparent => Color.W < 0.999f || Pixels != null && Enumerable.Range(0,Width*Height).Any(i=>Pixels[i*4+3]<255);
}

public static class ModelImportService
{
    public const string FileFilter="3D models|*.obj;*.glb;*.gltf;*.fbx;*.dae;*.3ds;*.ply;*.stl|Wavefront OBJ|*.obj|glTF / GLB|*.gltf;*.glb|Autodesk FBX|*.fbx|Collada|*.dae|3DS|*.3ds|PLY / STL|*.ply;*.stl";
    public static EditableGeometry Import(string path,Vector3 offset,float scale=1)
    {
        var result=Transform(Load(path),offset,new Vector3(scale),Vector3.Zero);
        LevelMeshCreation.Validate(result);
        return result;
    }
    // Route native file requests through managed IO: Assimp's Windows fopen cannot open Unicode filenames.
    private sealed class ModelFiles : FileIOSystem
    {
        private readonly string source;
        public string VirtualName => "__randm64_import__"+Path.GetExtension(source);
        public ModelFiles(string path) { source=Path.GetFullPath(path); }
        public override IOStream OpenFile(string path,FileIOMode mode)
        {
            string local=Path.GetFileName(path)==VirtualName ? source :
                Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!,Uri.UnescapeDataString(path)));
            return base.OpenFile(local,mode);
        }
    }
    public static EditableGeometry Load(string path)
    {
        using var importer=new AssimpContext();
        importer.SetIOSystem(new ModelFiles(path));
        var steps=PostProcessSteps.Triangulate|PostProcessSteps.PreTransformVertices|
            PostProcessSteps.SortByPrimitiveType|PostProcessSteps.ValidateDataStructure|PostProcessSteps.TransformUVCoords|PostProcessSteps.FlipUVs;
        using var input=File.OpenRead(path);
        var scene=Path.GetExtension(path).Equals(".glb",StringComparison.OrdinalIgnoreCase)
            ? importer.ImportFileFromStream(input,steps,"glb")
            : importer.ImportFile("__randm64_import__"+Path.GetExtension(path),steps);
        if(scene==null || scene.MeshCount==0) throw new InvalidDataException("The model contains no mesh geometry.");
        var result=new EditableGeometry();
        foreach(var material in scene.Materials)
        {
            var color=material.HasColorDiffuse ? material.ColorDiffuse : new Color4D(1,1,1,1);
            var target=new ImportedMaterial { Name=material.Name,Color=new Vector4(color.R,color.G,color.B,Math.Min(color.A,material.Opacity)) };
            // Assimp 5 uses texture type 12 for glTF's base-color map.
            if(material.GetMaterialTexture((TextureType)12,0,out var slot) || material.GetMaterialTexture(TextureType.Diffuse,0,out slot))
            {
                target.WrapS=Address(slot.WrapModeU); target.WrapT=Address(slot.WrapModeV);
                target.UvChannel=slot.UVIndex;
                byte[] pixels; int width,height;
                var embedded=scene.GetEmbeddedTexture(slot.FilePath);
                if(embedded!=null && !embedded.IsCompressed)
                {
                    width=embedded.Width; height=embedded.Height; pixels=new byte[checked(width*height*4)];
                    for(int i=0;i<embedded.NonCompressedData.Length;i++)
                    { var p=embedded.NonCompressedData[i]; pixels[i*4]=p.R; pixels[i*4+1]=p.G; pixels[i*4+2]=p.B; pixels[i*4+3]=p.A; }
                }
                else
                {
                    byte[] bytes;
                    if(embedded!=null) bytes=embedded.CompressedData;
                    else
                    {
                        string texturePath=Uri.UnescapeDataString(slot.FilePath).Replace('\\',Path.DirectorySeparatorChar).Replace('/',Path.DirectorySeparatorChar);
                        string local=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!,texturePath));
                        if(!File.Exists(local))
                        {
                            string folder=Path.GetDirectoryName(Path.GetFullPath(path))!,name=Path.GetFileName(texturePath);
                            local=new[]{Path.Combine(folder,name),Path.Combine(folder,"textures",name)}.FirstOrDefault(File.Exists) ?? local;
                        }
                        if(!File.Exists(local)) throw new FileNotFoundException($"Material '{material.Name}' is missing texture '{slot.FilePath}'. Keep the model's texture files alongside it.",local);
                        bytes=File.ReadAllBytes(local);
                    }
                    var decoded=ImageResult.FromMemory(bytes,ColorComponents.RedGreenBlueAlpha);
                    pixels=decoded.Data; width=decoded.Width; height=decoded.Height;
                }
                ResizeForN64(target,pixels,width,height);
            }
            result.Materials.Add(target);
        }
        foreach(var mesh in scene.Meshes)
        {
            if(mesh.FaceCount==0) continue;
            int start=result.Vertices.Count;
            int channel=mesh.MaterialIndex>=0 && mesh.MaterialIndex<result.Materials.Count ? result.Materials[mesh.MaterialIndex].UvChannel : 0;
            for(int i=0;i<mesh.VertexCount;i++)
            {
                var p=mesh.Vertices[i]; result.Vertices.Add(new Vector3(p.X,p.Y,p.Z));
                var uv=mesh.HasTextureCoords(channel) ? mesh.TextureCoordinateChannels[channel][i] : new Vector3D();
                result.TextureCoordinates.Add(new Vector2(uv.X,uv.Y));
                var c=mesh.HasVertexColors(0) ? mesh.VertexColorChannels[0][i] : new Color4D(1,1,1,1);
                result.VertexColors.Add(new Vector4(c.R,c.G,c.B,c.A));
            }
            foreach(var face in mesh.Faces)
            {
                if(face.IndexCount!=3) continue;
                if(mesh.MaterialIndex<0 || mesh.MaterialIndex>=result.Materials.Count) throw new InvalidDataException("Invalid model material index.");
                if(result.Materials[mesh.MaterialIndex].Pixels!=null && !mesh.HasTextureCoords(channel)) throw new InvalidDataException("A textured mesh has no UV coordinates: "+mesh.Name);
                result.Triangles.Add((start+face.Indices[0],start+face.Indices[1],start+face.Indices[2]));
                result.TriangleMaterials.Add(mesh.MaterialIndex);
            }
        }
        if(result.Triangles.Count==0) throw new InvalidDataException("The model contains no triangles.");
        return result;
    }
    public static EditableGeometry Transform(EditableGeometry source,Vector3 position,Vector3 scale,Vector3 rotation)
    {
        if(!float.IsFinite(scale.X)||!float.IsFinite(scale.Y)||!float.IsFinite(scale.Z)||scale.X<=0||scale.Y<=0||scale.Z<=0)
            throw new ArgumentException("Scale values must be positive finite numbers.");
        var matrix=Matrix4.CreateScale(scale)*Matrix4.CreateRotationX(MathHelper.DegreesToRadians(rotation.X))*
            Matrix4.CreateRotationY(MathHelper.DegreesToRadians(rotation.Y))*Matrix4.CreateRotationZ(MathHelper.DegreesToRadians(rotation.Z));
        var result=new EditableGeometry();
        result.Vertices.AddRange(source.Vertices.Select(p=>Vector3.TransformPosition(p,matrix)+position));
        result.Triangles.AddRange(source.Triangles); result.TextureCoordinates.AddRange(source.TextureCoordinates);
        result.VertexColors.AddRange(source.VertexColors); result.TriangleMaterials.AddRange(source.TriangleMaterials);
        result.Materials.AddRange(source.Materials);
        return result;
    }
    public static void ReplaceTexture(ImportedMaterial material,string path)
    {
        var image=ImageResult.FromMemory(File.ReadAllBytes(path),ColorComponents.RedGreenBlueAlpha);
        ResizeForN64(material,image.Data,image.Width,image.Height);
    }
    private static Sm64DecompLevelViewer.Models.TextureAddressMode Address(TextureWrapMode wrap) => wrap==TextureWrapMode.Clamp || wrap==TextureWrapMode.Decal
        ? Sm64DecompLevelViewer.Models.TextureAddressMode.Clamp : wrap==TextureWrapMode.Mirror ? Sm64DecompLevelViewer.Models.TextureAddressMode.Mirror : Sm64DecompLevelViewer.Models.TextureAddressMode.Repeat;
    private static void ResizeForN64(ImportedMaterial material,byte[] pixels,int width,int height)
    {
        if(width<=0 || height<=0 || (long)width*height>67108864) throw new InvalidDataException("Unsupported texture dimensions.");
        int w=32,h=32;
        if(width>=height*2) w=64; else if(height>=width*2) h=64;
        var output=new byte[w*h*4];
        for(int y=0;y<h;y++) for(int x=0;x<w;x++)
            Array.Copy(pixels,((int)((y+0.5)*height/h)*width+(int)((x+0.5)*width/w))*4,output,(y*w+x)*4,4);
        // Keep the editor's PNG and the game's RGBA16 texture identical in color and alpha precision.
        for(int i=0;i<output.Length;i+=4)
        {
            for(int c=0;c<3;c++) output[i+c]=(byte)((output[i+c]>>3)*255/31);
            output[i+3]=(byte)(output[i+3]>=128 ? 255 : 0);
        }
        material.Pixels=output; material.Width=w; material.Height=h;
    }
    public static byte[] EncodePng(ImportedMaterial material)
    {
        var encoder=new PngBitmapEncoder();
        var bgra=(byte[])material.Pixels!.Clone();
        for(int i=0;i<bgra.Length;i+=4) (bgra[i],bgra[i+2])=(bgra[i+2],bgra[i]);
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(material.Width,material.Height,96,96,PixelFormats.Bgra32,null,bgra,material.Width*4)));
        using var output=new MemoryStream(); encoder.Save(output); return output.ToArray();
    }
}
