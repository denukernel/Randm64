using System.IO;
using System.Text.RegularExpressions;
using Sm64DecompLevelViewer.Models;

namespace Sm64DecompLevelViewer.Services;

public static class LevelWaterBuilder
{
    private static int Number(string text) => text.Trim().StartsWith("0x", StringComparison.OrdinalIgnoreCase)
        ? Convert.ToInt32(text.Trim()[2..], 16) : int.Parse(text.Trim());

    private static Dictionary<string,string> ReadDefinitions(string path) => File.Exists(path)
        ? Regex.Matches(LevelSceneLoader.Clean(File.ReadAllText(path)), @"(?m)^\s*#define\s+(MOVTEX\w+)\s+([^\r\n]+)")
            .ToDictionary(m=>m.Groups[1].Value,m=>m.Groups[2].Value)
        : new();
    private static int? Resolve(string expression, Dictionary<string,string> definitions, HashSet<string>? visiting = null)
    {
        visiting ??= new();
        expression=expression.Trim().Replace("(","").Replace(")","");
        if(expression.Contains('|'))
        {
            int result=0;
            foreach(string part in expression.Split('|')) { int? value=Resolve(part,definitions,visiting); if(!value.HasValue)return null; result|=value.Value; }
            return result;
        }
        if(expression.Contains("<<"))
        {
            var parts=expression.Split("<<");
            if(parts.Length!=2)return null;
            int? left=Resolve(parts[0],definitions,visiting), right=Resolve(parts[1],definitions,visiting);
            return left.HasValue && right is >=0 and <32 ? left.Value << right.Value : null;
        }
        if(definitions.TryGetValue(expression,out string? definition))
        {
            if(!visiting.Add(expression))return null;
            int? value=Resolve(definition,definitions,visiting);visiting.Remove(expression);return value;
        }
        try { return Number(expression); } catch(FormatException) { return null; } catch(OverflowException) { return null; }
    }

    public static VisualMesh Build(string projectRoot, VisualMesh scene, CollisionMesh collision,string? movingVertices = null,string? generatedSource = null)
    {
        var mesh = new VisualMesh { LevelName = "water" };
        if (scene.LevelDirectory == null) return mesh;
        string area = Path.Combine(scene.LevelDirectory, "areas", scene.AreaIndex.ToString());
        var files = Directory.GetFiles(area, "*movtext*.c", SearchOption.AllDirectories).ToList();
        string shared = Path.Combine(scene.LevelDirectory, "movtext.inc.c");
        if (File.Exists(shared)) files.Add(shared);
        string quadSource = LevelSceneLoader.Clean(string.Join("\n", files.Select(File.ReadAllText))+"\n"+generatedSource);
        // Assets can be declared in a different area than the geo callback using them.
        string source = LevelSceneLoader.Clean(string.Join("\n", Directory.GetFiles(scene.LevelDirectory, "*movtext*.c", SearchOption.AllDirectories).Select(File.ReadAllText))+"\n"+generatedSource);
        var arrays = Regex.Matches(source, @"(?:static\s+)?(?:const\s+)?Movtex\s+(\w+)\s*\[\s*\]\s*=\s*\{([\s\S]*?)\};")
            .ToDictionary(match => match.Groups[1].Value, match => match.Groups[2].Value);
        var usedRegions = new HashSet<int>();
        foreach (Match collection in Regex.Matches(quadSource, @"MovtexQuadCollection\s+\w+\s*\[\s*\]\s*=\s*\{([\s\S]*?)\};"))
            foreach (Match entry in Regex.Matches(collection.Groups[1].Value, @"\{\s*(\d+)\s*,\s*(\w+)\s*\}"))
            {
                int id = Number(entry.Groups[1].Value);
                var box = collision.WaterBoxes.FirstOrDefault(box => box.Id == id);
                if (box == null || !arrays.TryGetValue(entry.Groups[2].Value, out var data)) continue;
                var points = Regex.Matches(data, @"MOV_TEX_4_BOX_TRIS\(\s*(-?\d+)\s*,\s*(-?\d+)\s*\)");
                if (points.Count != 4 || !usedRegions.Add(id)) continue;
                string texture = Regex.Match(data, @"MOV_TEX_DEFINE\(\s*(\w+)").Groups[1].Value;
                var alpha = Regex.Match(data, @"MOV_TEX_ALPHA\(\s*(0x[\da-fA-F]+|\d+)");
                float opacity = alpha.Success ? Number(alpha.Groups[1].Value) / 255f : 0.6f;
                int start = mesh.Vertices.Count;
                for (int i = 0; i < 4; i++)
                {
                    int x = Number(points[i].Groups[1].Value), z = Number(points[i].Groups[2].Value);
                    mesh.Vertices.Add(new ModelVertex(x, box.Y, z, x / 4, z / 4, 0, 127, 0, 255));
                }
                AddTriangle(start, start + 1, start + 2, texture, opacity);
                AddTriangle(start, start + 2, start + 3, texture, opacity);
            }
        // Flowing surfaces use the game's indexed vertex lists, including vertical waterfalls.
        string movingPath = Path.Combine(projectRoot, "src", "game", "moving_texture.c");
        var definitions = ReadDefinitions(Path.Combine(projectRoot,"src","game","moving_texture.h"));
        string geoPath = Path.Combine(area,"geo.inc.c");
        string activeGeo = File.Exists(geoPath) ? LevelSceneLoader.Clean(File.ReadAllText(geoPath)) : "";
        var callbacks = Regex.Matches(activeGeo, @"GEO_ASM\(\s*([^,]+),\s*(geo_movtex_draw_\w+)\s*\)")
            .Select(m => (Id: Resolve(m.Groups[1].Value,definitions), Callback: m.Groups[2].Value)).ToHashSet();
        if (File.Exists(movingPath))
            foreach (Match table in Regex.Matches(LevelSceneLoader.Clean(File.ReadAllText(movingPath)), @"struct\s+MovtexObject\s+(\w+)\s*\[\s*\]\s*=\s*\{([\s\S]*?)\};"))
            foreach (Match entry in Regex.Matches(table.Groups[2].Value,
                @"\{\s*(?<id>M(?:OVTEX|OVTEX_)\w*)\s*,\s*((?:TEXTURE|TEX)_\w+)\s*,\s*\d+\s*,\s*(\w+)\s*,\s*\w+\s*,\s*\w+\s*,\s*(\w+)\s*,\s*[^,]+,\s*[^,]+,\s*[^,]+,\s*(0x[\da-fA-F]+|\d+)\s*,"))
            {
                if (movingVertices == null)
                {
                    int? id = Resolve(entry.Groups["id"].Value,definitions);
                    bool selected = id.HasValue && callbacks.Any(c => c.Id == id && table.Groups[1].Value == (c.Callback switch
                    {
                        "geo_movtex_draw_nocolor" => "gMovtexNonColored",
                        "geo_movtex_draw_colored" or "geo_movtex_draw_colored_no_update" => "gMovtexColored",
                        "geo_movtex_draw_colored_2_no_update" => "gMovtexColored2", _ => ""
                    }));
                    if (!selected) continue;
                }
                if(movingVertices!=null && entry.Groups[2].Value!=movingVertices) continue;
                if (!arrays.TryGetValue(entry.Groups[2].Value, out var data)) continue;
                var vertices = Regex.Matches(data, @"MOV_TEX_(?:(?:ROT|LIGHT)_)?TRIS\(([^)]*)\)");
                if (vertices.Count == 0) continue;
                int start = mesh.Vertices.Count;
                foreach (Match vertex in vertices)
                {
                    var fields = vertex.Groups[1].Value.Split(',').Select(Number).ToArray();
                    // ROT_TRIS carries three normal/color components before its final UV pair.
                    mesh.Vertices.Add(new ModelVertex(fields[0], fields[1], fields[2],
                        fields[^2] * 1024, fields[^1] * 1024, 0, 127, 0, 255));
                }
                var list = Regex.Match(source, @"Gfx\s+" + Regex.Escape(entry.Groups[3].Value) + @"\[\]\s*=\s*\{([\s\S]*?)\};");
                foreach (Match triangle in Regex.Matches(list.Groups[1].Value, @"gsSP(1Triangle|2Triangles)\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*,\s*[^,)]*(?:,\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*,[^)]*)?\)"))
                {
                    AddIndexed(2);
                    if (triangle.Groups[1].Value == "2Triangles") AddIndexed(5);
                    void AddIndexed(int group)
                    {
                        int a = Number(triangle.Groups[group].Value), b = Number(triangle.Groups[group + 1].Value), c = Number(triangle.Groups[group + 2].Value);
                        if (Math.Max(a, Math.Max(b, c)) < vertices.Count)
                            AddTriangle(start + a, start + b, start + c, entry.Groups[1].Value, Number(entry.Groups[4].Value) / 255f);
                    }
                }
            }
        return mesh;

        void AddTriangle(int a, int b, int c, string texture, float opacity)
        {
            mesh.Triangles.Add(new ModelTriangle(a, b, c, texture)
                { LightingEnabled = false, DecalTexture = true, Opacity = opacity });
            string symbol = texture switch
            {
                "TEXTURE_WATER" => "texture_waterbox_water", "TEXTURE_JRB_WATER" => "texture_waterbox_jrb_water",
                "TEXTURE_MIST" => "texture_waterbox_mist", "TEXTURE_LAVA" => "texture_waterbox_lava",
                "TEXTURE_UNK_WATER" => "texture_waterbox_unknown_water",
                "TEX_QUICKSAND_SSL" => "ssl_quicksand", "TEX_PYRAMID_SAND_SSL" => "ssl_pyramid_sand",
                "TEX_YELLOW_TRI_TTC" => "ttc_yellow_triangle", _ => ""
            };
            if (scene.TexturePaths.TryGetValue(symbol, out string? levelTexture))
            {
                mesh.TexturePaths[texture] = levelTexture;
                return;
            }
            if (symbol.Length == 0) return;
            string segment = Path.Combine(projectRoot, "bin", "segment2.c");
            if (mesh.TexturePaths.ContainsKey(texture) || !File.Exists(segment)) return;
            var declaration = Regex.Match(File.ReadAllText(segment), @"Texture\s+" + symbol + @"\[\]\s*=\s*\{\s*#include\s+""([^""]+)""");
            if (declaration.Success)
            {
                string path = Path.Combine(projectRoot, declaration.Groups[1].Value.Replace(".inc.c", ".png"));
                if (File.Exists(path)) mesh.TexturePaths[texture] = path;
            }
        }
    }
}
