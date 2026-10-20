using System.Text;
using System.Text.RegularExpressions;

namespace Sm64DecompLevelViewer.Services;

internal static class LevelGeneratedGeometry
{
    // geo_misc.c builds RR's carpet from this authored table and two indexed halves.
    public static string? FlyingCarpet(string source)
    {
        var table=Regex.Match(LevelSceneLoader.Clean(source),@"\bflying_carpet_static_vertex_data\s*\[\s*\]\s*=\s*\{([^}]*)\}");
        if(!table.Success) return null;
        var values=Regex.Matches(table.Groups[1].Value,@"-?\d+").Select(m=>int.Parse(m.Value)).ToArray();
        if(values.Length!=84) return null;
        var result=new StringBuilder("\nstatic const Vtx preview_carpet_vertices[] = {\n");
        for(int n=0;n<21;n++)
        {
            int y=(int)Math.Round(Math.Sin(((n/3<<12)+(n%3<<14))*Math.PI/32768)*20);
            result.AppendLine("    {{{"+$"{values[n*4]}, {y}, {values[n*4+1]}"+"}, 0, {"+$"{values[n*4+2]}, {values[n*4+3]}"+"}, {0x00, 0x7f, 0x00, 0xff}}},");
        }
        result.AppendLine("};\nconst Gfx preview_flying_carpet[] = {\n    gsSPDisplayList(dl_flying_carpet_begin),\n    gsSPVertex(preview_carpet_vertices, 12, 0),\n    gsSPDisplayList(dl_flying_carpet_model_half),\n    gsSPVertex(preview_carpet_vertices + 9, 12, 0),\n    gsSPDisplayList(dl_flying_carpet_model_half),\n    gsSPDisplayList(dl_flying_carpet_end),\n    gsSPEndDisplayList(),\n};");
        return source+result;
    }
}
