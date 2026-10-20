using System.IO;
using System.Text.RegularExpressions;
using Sm64DecompLevelViewer.Models;

namespace Sm64DecompLevelViewer.Services;

internal static class SpecialObjectFormats
{
    private static Dictionary<string,string> Presets(string path)
    {
        for(var directory=new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(path))!);directory!=null;directory=directory.Parent)
        {
            string file=Path.Combine(directory.FullName,"include","special_presets.inc.c");
            if(!File.Exists(file)) continue;
            return Regex.Matches(ActorSourceReader.Read(file),@"\{\s*(\w+)\s*,\s*(SPTYPE_\w+)\s*,")
                .GroupBy(m=>m.Groups[1].Value).ToDictionary(g=>g.Key,g=>g.First().Groups[2].Value);
        }
        return new();
    }
    private static int Count(string type)=>type switch { "SPTYPE_NO_YROT_OR_PARAMS"=>4,"SPTYPE_YROT_NO_PARAMS" or "SPTYPE_DEF_PARAM_AND_YROT"=>5,"SPTYPE_PARAMS_AND_YROT"=>6,_=>throw new InvalidDataException("Unsupported special-object preset format: "+type) };
    public static string Format(string source,string path,LevelObject o)
    {
        string preset=ObjectMacroWriter.Preset(o); var presets=Presets(path);
        int count;
        if(presets.TryGetValue(preset,out string? type)) count=Count(type);
        else
        {
            var existing=CSourceCalls.Read(source,n=>n.StartsWith("SPECIAL_OBJECT",StringComparison.Ordinal)).Where(c=>c.Argument(source,0)==preset).ToList();
            if(existing.Count==0 || existing.Select(c=>c.Arguments.Count).Distinct().Count()!=1) throw new InvalidDataException("Cannot determine the record size for special preset "+preset+". Its special_presets.inc.c definition is required.");
            count=existing[0].Arguments.Count;
        }
        string name=count switch { 4=>"SPECIAL_OBJECT",5=>"SPECIAL_OBJECT_WITH_YAW",6=>"SPECIAL_OBJECT_WITH_YAW_AND_PARAM",_=>throw new InvalidDataException("Unsupported special object record size.") };
        return $"{name}({preset}, {o.X}, {o.Y}, {o.Z}{(count>=5 ? ", "+ObjectMacroWriter.Yaw(o) : "")}{(count>=6 ? ", "+(!string.IsNullOrWhiteSpace(o.RawParams) ? o.RawParams : $"0x{o.Params:X8}") : "")})";
    }
    public static void Validate(string source,string path)
    {
        var presets=Presets(path);
        foreach(var call in CSourceCalls.Read(source,n=>n is "SPECIAL_OBJECT" or "SPECIAL_OBJECT_WITH_YAW" or "SPECIAL_OBJECT_WITH_YAW_AND_PARAM"))
        {
            int declared=call.Name switch { "SPECIAL_OBJECT"=>4,"SPECIAL_OBJECT_WITH_YAW"=>5,_=>6 };
            if(call.Arguments.Count!=declared || presets.TryGetValue(call.Argument(source,0),out var type) && Count(type)!=declared)
                throw new InvalidDataException("Special-object record size does not match its preset: "+call.Argument(source,0)+". This would corrupt the game's terrain stream.");
        }
    }
}
