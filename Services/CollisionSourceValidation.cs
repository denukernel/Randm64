using System.Globalization;
using System.IO;

namespace Sm64DecompLevelViewer.Services;

public static class CollisionSourceValidation
{
    public static bool HasForce(string name)=>name is "SURFACE_0004" or "SURFACE_FLOWING_WATER" or "SURFACE_DEEP_MOVING_QUICKSAND" or "SURFACE_SHALLOW_MOVING_QUICKSAND" or "SURFACE_MOVING_QUICKSAND" or "SURFACE_HORIZONTAL_WIND" or "SURFACE_INSTANT_MOVING_QUICKSAND";
    private static int Number(string text)
    {
        if(int.TryParse(text,NumberStyles.Integer,CultureInfo.InvariantCulture,out int value)) return value;
        if(text.StartsWith("0x",StringComparison.OrdinalIgnoreCase) && int.TryParse(text[2..],NumberStyles.HexNumber,CultureInfo.InvariantCulture,out value)) return value;
        throw new InvalidDataException("Unsupported collision expression: "+text+". Keep the original source until this expression can be evaluated safely.");
    }
    public static void Validate(string source,string path)
    {
        var calls=CSourceCalls.Read(source,n=>n.StartsWith("COL_",StringComparison.Ordinal)||n.StartsWith("SPECIAL_OBJECT",StringComparison.Ordinal));
        bool active=false; int vertexCount=-1,vertices=0,remaining=0,water=-1,special=-1; string? surface=null;
        void FinishSurfaces() { if(surface!=null && remaining!=0) throw new InvalidDataException($"{surface} triangle count is incorrect ({remaining} records missing). Saving this collision would corrupt the terrain stream."); surface=null; }
        void FinishSpecial() { if(special>0) throw new InvalidDataException("COL_SPECIAL_INIT count does not match its object records."); }
        foreach(var call in calls)
        {
            string Arg(int index)=>call.Argument(source,index);
            int N(int index)=>Number(Arg(index));
            switch(call.Name)
            {
                case "COL_INIT":
                    if(active) throw new InvalidDataException("Collision block has no COL_END.");
                    active=true; vertexCount=-1; vertices=0; remaining=0; water=-1; special=-1; surface=null; break;
                case "COL_VERTEX_INIT" when active: vertexCount=N(0); if(vertexCount<0||vertexCount>32767) throw new InvalidDataException("Invalid collision vertex count."); break;
                case "COL_VERTEX" when active:
                    if(call.Arguments.Count!=3) throw new InvalidDataException("Invalid collision vertex.");
                    for(int i=0;i<3;i++) if(Math.Abs((long)N(i))>32767) throw new InvalidDataException("Collision vertex exceeds the signed 16-bit coordinate range.");
                    vertices++; break;
                case "COL_TRI_INIT" when active:
                    FinishSurfaces(); if(vertexCount!=vertices) throw new InvalidDataException("COL_VERTEX_INIT count does not match its vertices.");
                    surface=Arg(0); remaining=N(1); if(remaining<0) throw new InvalidDataException("Negative collision triangle count."); break;
                case "COL_TRI" or "COL_TRI_SPECIAL" when active:
                    if(surface==null || remaining--<=0) throw new InvalidDataException("Collision has more triangles than declared.");
                    if(call.Arguments.Count!=(HasForce(surface) ? 4 : 3) || (call.Name=="COL_TRI_SPECIAL")!=HasForce(surface))
                        throw new InvalidDataException("Wrong collision command width for "+surface+"; the game would read subsequent commands as triangle data.");
                    for(int i=0;i<3;i++) if(N(i)<0 || N(i)>=vertices) throw new InvalidDataException("Collision triangle references a missing vertex.");
                    break;
                case "COL_TRI_STOP" when active: FinishSurfaces(); if(vertices!=vertexCount) throw new InvalidDataException("Collision vertex count mismatch."); break;
                case "COL_SPECIAL_INIT" when active: FinishSurfaces(); FinishSpecial(); special=N(0); break;
                case "SPECIAL_OBJECT" or "SPECIAL_OBJECT_WITH_YAW" or "SPECIAL_OBJECT_WITH_YAW_AND_PARAM" when active:
                    if(special--<=0) throw new InvalidDataException("More special objects than COL_SPECIAL_INIT declares."); break;
                case "COL_WATER_BOX_INIT" when active: FinishSpecial(); water=N(0); break;
                case "COL_WATER_BOX" when active:
                    if(water--<=0 || call.Arguments.Count!=6) throw new InvalidDataException("Water box count or record is invalid."); break;
                case "COL_END" when active:
                    FinishSurfaces(); FinishSpecial(); if(water>0) throw new InvalidDataException("Water box count mismatch."); active=false; break;
            }
        }
        if(active) throw new InvalidDataException("Collision block has no COL_END.");
        SpecialObjectFormats.Validate(source,path);
    }
}
