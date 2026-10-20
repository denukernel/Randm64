using System.IO;
using System.Text.RegularExpressions;
using Sm64DecompLevelViewer.Models;

namespace Sm64DecompLevelViewer.Services;

public static class LevelSceneLoader
{
    public static string Clean(string text) => Regex.Replace(text, @"/\*[\s\S]*?\*/|//[^\r\n]*", "");

    public static VisualMesh Load(string projectRoot, string levelDirectory, int area, string? textureBin = null)
    {
        string areaDirectory = Path.Combine(levelDirectory, "areas", area.ToString());
        string geoPath = Path.Combine(areaDirectory, "geo.inc.c");
        if (!File.Exists(geoPath)) throw new FileNotFoundException("Area geometry not found", geoPath);
        string script = Clean(File.ReadAllText(Path.Combine(levelDirectory, "script.c")));
        var root = Regex.Match(script, @"\bAREA\s*\(\s*" + area + @"\s*,\s*(\w+)");
        var geo = new GeoLayoutParser();
        var transforms = geo.ParseActorGeoLayout(geoPath, root.Success ? root.Groups[1].Value : null,
            Directory.GetFiles(levelDirectory, "geo.inc.c", SearchOption.AllDirectories));
        if (geo.DisplayListInstances.Count == 0) throw new InvalidDataException("The selected area has no visual display lists.");
        var files = GetModelSourceFiles(levelDirectory);
        string source = string.Join("\n", files.Select(path => ActorSourceReader.Read(path)));
        var parser = new ModelParser();
        var mesh = parser.ParseModelFile(geoPath, "Area " + area, Path.GetFileName(levelDirectory), transforms,
            geo.DlToJointIndex, geo.DisplayListInstances, geo.RootScale, source)
            ?? throw new InvalidDataException("Unable to parse level scene.");
        mesh.LevelDirectory = levelDirectory;
        mesh.AreaIndex = area;
        mesh.Joints = geo.ParsedJoints;
        ResolveLevelMaterials(projectRoot,levelDirectory,mesh,textureBin,files);
        string geoSource = Clean(ActorSourceReader.Read(geoPath));
        var background = Regex.Match(geoSource, @"GEO_BACKGROUND\s*\(\s*(\w+)\s*,\s*geo_skybox_main");
        mesh.SkyboxBin = background.Success ? background.Groups[1].Value switch
        {
            "BACKGROUND_OCEAN_SKY" => "water", "BACKGROUND_FLAMING_SKY" => "bitfs",
            "BACKGROUND_UNDERWATER_CITY" => "wdw", "BACKGROUND_BELOW_CLOUDS" => "clouds",
            "BACKGROUND_SNOW_MOUNTAINS" => "ccm", "BACKGROUND_DESERT" => "ssl",
            "BACKGROUND_HAUNTED" => "bbh", "BACKGROUND_GREEN_SKY" => "bidw",
            "BACKGROUND_ABOVE_CLOUDS" => "cloud_floor", "BACKGROUND_PURPLE_SKY" => "bits", _ => null
        } : null;
        return mesh;
    }

    public static void ResolveLevelMaterials(string projectRoot,string levelDirectory,VisualMesh mesh,
        string? textureBin = null,IEnumerable<string>? modelFiles = null)
    {
        if (string.IsNullOrWhiteSpace(textureBin))
        {
            string yaml = Path.Combine(levelDirectory,"level.yaml");
            if (File.Exists(yaml)) textureBin = Regex.Match(File.ReadAllText(yaml),@"texture-bin:\s*(\w+)").Groups[1].Value;
        }
        var parser = new ModelParser();
        parser.ParseTextureMapping(levelDirectory,mesh,projectRoot);
        foreach (string file in modelFiles ?? GetModelSourceFiles(levelDirectory))
            parser.ParseActorTextureMapping(Path.GetDirectoryName(file)!,file,mesh,projectRoot);
        parser.ResolveSharedTextures(mesh,textureBin ?? "",projectRoot);
        if(!string.IsNullOrEmpty(textureBin))
        {
            string bank=Path.Combine(projectRoot,"bin",textureBin+".c");
            if(File.Exists(bank)) parser.ParseActorTextureMapping(Path.GetDirectoryName(bank)!,bank,mesh,projectRoot);
        }
        string segment2=Path.Combine(projectRoot,"bin","segment2.c");
        if(File.Exists(segment2)) parser.ParseActorTextureMapping(Path.GetDirectoryName(segment2)!,segment2,mesh,projectRoot);
    }
    public static string[] GetModelSourceFiles(string levelDirectory) => Directory.GetFiles(levelDirectory,"*.inc.c",SearchOption.AllDirectories)
        .Where(path => Path.GetFileName(path) is "model.inc.c" or "light.inc.c" or "lights.inc.c" || Regex.IsMatch(Path.GetFileName(path),@"^\d+\.inc\.c$"))
        .Concat(File.Exists(Path.Combine(levelDirectory,"leveldata.c")) ? new[]{Path.Combine(levelDirectory,"leveldata.c")} : Array.Empty<string>()).ToArray();
}
