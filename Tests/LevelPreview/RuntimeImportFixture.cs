using System.IO;
using OpenTK.Mathematics;
using Sm64DecompLevelViewer.Services;

internal static class RuntimeImportFixture
{
    public static void Write(string modelFile,string directory)
    {
        Directory.CreateDirectory(directory);
        string model=Path.Combine(directory,"model.inc.c"),geo=Path.Combine(directory,"geo.inc.c"),collision=Path.Combine(directory,"collision.inc.c");
        const string headers="#include <ultra64.h>\n#include \"sm64.h\"\n#include \"types.h\"\n#include \"macros.h\"\n";
        File.WriteAllText(model,headers);
        File.WriteAllText(geo,headers+"#include \"geo_commands.h\"\nconst GeoLayout fixture_geo[] = { GEO_CAMERA(0,0,0,0,0,0,0,0), GEO_OPEN_NODE(), GEO_CLOSE_NODE(), GEO_END(), };\n");
        File.WriteAllText(collision,headers+"#include \"surface_terrains.h\"\nconst Collision fixture_collision[] = { COL_INIT(), COL_VERTEX_INIT(0), COL_TRI_STOP(), COL_END(), };\n");
        var geometry=ModelImportService.Import(modelFile,Vector3.Zero,1232);
        LevelMeshCreation.Append(model,geo,collision,geometry,true,directory);
        var vertices=new ModelParser().ParseVertexArrays(File.ReadAllText(model)).First();
        vertices.Value[0].X++;
        if(!new LevelMeshService().SaveVisualMesh(model,vertices.Key,vertices.Value)) throw new Exception("Saved imported vertex fixture failed");
        Console.WriteLine("RUNTIME FIXTURE: textured GLB emitted for MIPS compilation");
    }
}
