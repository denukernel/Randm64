using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using OpenTK.Mathematics;
using Sm64DecompLevelViewer.Services;

internal static class ModelImportChecks
{
    private static void Check(bool pass,string message) { if(!pass) throw new Exception(message); }
    public static void Run(string? captureOutput = null)
    {
        string directory=Path.Combine(Path.GetTempPath(),"Randm64Import-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var texture=new ImportedMaterial { Width=2,Height=2,Pixels=new byte[]{255,0,0,255,0,255,0,255,0,0,255,255,255,255,255,0} };
            byte[] png=ModelImportService.EncodePng(texture);
            File.WriteAllBytes(Path.Combine(directory,"diffuse.png"),png);
            File.WriteAllText(Path.Combine(directory,"model.mtl"),"newmtl painted\nKd 0.5 1 1\nmap_Kd diffuse.png\nnewmtl solid\nKd 0.2 0.3 0.4\n");
            string obj=Path.Combine(directory,"model.obj");
            File.WriteAllText(obj,"mtllib model.mtl\nv 0 0 0\nv 1 0 0\nv 0 1 0\nv 1 1 0\nvt 0 0\nvt 1 0\nvt 0 1\nvt 1 1\nusemtl painted\nf 1/1 2/2 3/3\nusemtl solid\nf 2/2 4/4 3/3\n");
            var imported=ModelImportService.Import(obj,Vector3.Zero,100);
            Check(imported.Triangles.Count==2 && imported.Materials.Any(m=>m.Pixels!=null),"OBJ + MTL imports textures and both materials");
            Check(imported.TextureCoordinates.Distinct().Count()>1,"UV coordinates preserved");
            string model=Path.Combine(directory,"model.inc.c"),geo=Path.Combine(directory,"geo.inc.c"),collision=Path.Combine(directory,"collision.inc.c");
            File.WriteAllText(model,"/* original */\n");
            File.WriteAllText(geo,"const GeoLayout imported_geo[] = { GEO_CAMERA(0, 0, 0, 0, 0, 0, 0, camera_main), GEO_OPEN_NODE(), GEO_CLOSE_NODE(), GEO_END(), };");
            File.WriteAllText(collision,"const Collision col[] = { COL_INIT(), COL_VERTEX_INIT(0), COL_TRI_STOP(), COL_END(), };");
            LevelMeshCreation.Append(model,geo,collision,imported,true,directory);
            var parser=new GeoLayoutParser(); var transforms=parser.ParseActorGeoLayout(geo,"imported_geo");
            var modelParser=new ModelParser();
            var mesh=modelParser.ParseModelFile(model,"","",transforms,parser.DlToJointIndex,parser.DisplayListInstances)!;
            modelParser.ParseActorTextureMapping(directory,model,mesh,directory);
            Check(mesh.Triangles.Count==2 && mesh.TexturePaths.Count==1,"Saved imported model resolves its own texture asset");
            Check(mesh.Vertices.Any(v=>v.S!=0 || v.T!=0),"Saved imported vertices retain UV coordinates");
            Check(File.ReadAllText(geo).Contains("LAYER_TRANSPARENT"),"Texture alpha is assigned a transparent rendering layer");
            Check(Directory.GetFiles(directory,"randm64*.rgba16.inc.c").Length==1,"N64 texture source persisted alongside PNG");
            if(captureOutput!=null) LevelRenderCapture.Capture(directory,mesh,new(),new(),Path.Combine(captureOutput,"imported-materials.png"),new Vector3(50,50,0),220);
            string saved=File.ReadAllText(model); int fileCount=Directory.GetFiles(directory).Length;
            imported.TextureCoordinates[0]=new Vector2(1000000,0);
            try { LevelMeshCreation.Append(model,geo,collision,imported,true,directory); throw new Exception("Invalid UV coordinates were accepted"); }
            catch(ArgumentException) { }
            Check(File.ReadAllText(model)==saved && Directory.GetFiles(directory).Length==fileCount,"Failed textured import leaves source and asset files unchanged");
            string glb=WriteGlb(directory,png);
            var binary=ModelImportService.Import(glb,Vector3.UnitX,10);
            string unicode=Path.Combine(directory,"Sem título.glb"); File.Copy(glb,unicode);
            Check(ModelImportService.Import(unicode,Vector3.Zero).Triangles.Count==1,"Unicode GLB filename imports");
            string unicodeFolder=Path.Combine(directory,"modelos e textúras"); Directory.CreateDirectory(unicodeFolder);
            foreach(string file in new[]{"model.obj","model.mtl","diffuse.png"}) File.Copy(Path.Combine(directory,file),Path.Combine(unicodeFolder,file));
            Check(ModelImportService.Import(Path.Combine(unicodeFolder,"model.obj"),Vector3.Zero).Materials.Any(m=>m.Pixels!=null),"Unicode folder imports OBJ sidecar materials and textures");
            var raw=ModelImportService.Load(glb);
            var adjusted=ModelImportService.Transform(raw,new Vector3(10,20,30),new Vector3(2,3,4),new Vector3(0,0,90));
            Check((adjusted.Vertices[1]-new Vector3(10,30,30)).Length<0.001f,"Preview scale, XYZ rotation and position match confirmation");
            Check(raw.Vertices[1].X==5,"Preview transform does not mutate source vertices");
            Check(binary.Triangles.Count==1 && binary.Materials.Any(m=>m.Pixels!=null),"GLB embedded base-color texture imports");
            Check(Math.Abs(binary.Vertices.Min(v=>v.X)-31)<0.01f && Math.Abs(binary.Vertices.Max(v=>v.X)-51)<0.01f,"GLB node transforms, import scale, and offset are applied");
            Check(binary.Materials.Any(m=>Math.Abs(m.Color.Y-0.5f)<0.01f),"GLB base-color tint preserved");
            using(var reader=new BinaryReader(File.OpenRead(glb)))
            {
                reader.BaseStream.Position=12; int size=reader.ReadInt32(); reader.ReadInt32();
                var json=JsonNode.Parse(Encoding.UTF8.GetString(reader.ReadBytes(size)))!;
                int length=reader.ReadInt32(); reader.ReadInt32();
                File.WriteAllBytes(Path.Combine(directory,"buffer.bin"),reader.ReadBytes(length));
                json["buffers"]![0]!["uri"]="buffer.bin";
                string gltf=Path.Combine(directory,"external.gltf"); File.WriteAllText(gltf,json.ToJsonString());
                Check(ModelImportService.Import(gltf,Vector3.Zero).Triangles.Count==1,"glTF with external binary buffer imports");
            }
            using(var assimp=new Assimp.AssimpContext())
            {
                var scene=assimp.ImportFile(obj,Assimp.PostProcessSteps.Triangulate);
                foreach(var format in new[]{("fbx","fbx"),("fbxa","fbx"),("collada","dae"),("stl","stl"),("ply","ply"),("3ds","3ds")})
                {
                    string file=Path.Combine(directory,"exported."+format.Item2);
                    Check(assimp.ExportFile(scene,file,format.Item1),"Fixture exporter unavailable: "+format.Item1);
                    var result=ModelImportService.Import(file,Vector3.Zero,1);
                    Check(result.Triangles.Count==2,"Imported "+format.Item2+" triangle count changed");
                    if(format.Item2 is "fbx" or "dae") Check(result.Materials.Any(m=>m.Pixels!=null),format.Item2+" texture association missing");
                    Console.WriteLine("IMPORT PASS: "+format.Item2);
                }
            }
            File.Delete(Path.Combine(directory,"diffuse.png"));
            try { ModelImportService.Import(obj,Vector3.Zero); throw new Exception("Missing texture was silently ignored"); } catch(FileNotFoundException) { }
            Console.WriteLine("IMPORT TOTAL: OBJ/MTL, embedded GLB, FBX, DAE, STL, PLY, 3DS, materials, UVs, alpha, transforms, collision, and persistent RGBA16 textures passed");
        }
        finally { Directory.Delete(directory,true); }
    }
    private static string WriteGlb(string directory,byte[] png)
    {
        using var binary=new MemoryStream(); using var writer=new BinaryWriter(binary,Encoding.UTF8,true);
        foreach(float value in new float[]{0,0,0,1,0,0,0,1,0,0,0,1,0,0,1}) writer.Write(value);
        writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)2); writer.Write((ushort)0);
        int imageOffset=(int)binary.Length; writer.Write(png); while(binary.Length%4!=0) writer.Write((byte)0);
        var json=JsonNode.Parse("""
        {"asset":{"version":"2.0"},"scene":0,"scenes":[{"nodes":[0]}],"nodes":[{"mesh":0,"translation":[3,0,0],"scale":[2,2,2]}],
        "meshes":[{"primitives":[{"attributes":{"POSITION":0,"TEXCOORD_0":1},"indices":2,"material":0}]}],
        "accessors":[{"bufferView":0,"componentType":5126,"count":3,"type":"VEC3","min":[0,0,0],"max":[1,1,0]},
        {"bufferView":1,"componentType":5126,"count":3,"type":"VEC2"},{"bufferView":2,"componentType":5123,"count":3,"type":"SCALAR"}],
        "materials":[{"pbrMetallicRoughness":{"baseColorTexture":{"index":0},"baseColorFactor":[1,0.5,1,1]},"alphaMode":"BLEND"}],
        "textures":[{"source":0}],"images":[{"bufferView":3,"mimeType":"image/png"}]}
        """)!;
        json["buffers"]=new JsonArray(new JsonObject { ["byteLength"]=(int)binary.Length });
        json["bufferViews"]=new JsonArray(new JsonObject { ["buffer"]=0,["byteOffset"]=0,["byteLength"]=36 },
            new JsonObject { ["buffer"]=0,["byteOffset"]=36,["byteLength"]=24 },new JsonObject { ["buffer"]=0,["byteOffset"]=60,["byteLength"]=6 },
            new JsonObject { ["buffer"]=0,["byteOffset"]=imageOffset,["byteLength"]=png.Length });
        byte[] document=Encoding.UTF8.GetBytes(json.ToJsonString()); int padded=(document.Length+3)&~3;
        string path=Path.Combine(directory,"embedded.glb");
        using var output=new BinaryWriter(File.Create(path)); output.Write(0x46546C67); output.Write(2); output.Write(12+8+padded+8+(int)binary.Length);
        output.Write(padded); output.Write(0x4E4F534A); output.Write(document); for(int i=document.Length;i<padded;i++) output.Write((byte)32);
        output.Write((int)binary.Length); output.Write(0x004E4942); output.Write(binary.ToArray());
        return path;
    }
}
