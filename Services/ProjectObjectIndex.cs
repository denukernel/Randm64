using System.IO;
using Sm64DecompLevelViewer.Models;
using Sm64DecompLevelViewer.Services.Levels;
using System.Text.RegularExpressions;
using System.Security.Cryptography;

namespace Sm64DecompLevelViewer.Services;

public sealed record ObjectNavigationReference(string LevelPath, int Area, string SourceFile, int Start, int Length, ObjectSourceType Type, string SourceText, string SourceVersion);
public sealed record ProjectObjectEntry(LevelMetadata Level, LevelObject Object, ObjectNavigationReference Reference)
{
    public string LevelName => Level.ShortName;
    public int Area => Reference.Area;
    public string Behavior => Object.Behavior;
    public string Model => Object.ModelName;
    public string Preset => Object.PresetName;
    public string Parameters => Object.RawParams ?? $"0x{Object.Params:X8}";
    public string Acts => Object.RawActs ?? "all acts";
    public bool CoordinatesResolved { get; internal set; } = true;
    public string Position => CoordinatesResolved ? $"{Object.X}, {Object.Y}, {Object.Z}" : "unresolved source expression";
    public string Source => $"{Reference.SourceFile}:{Reference.Start}";
    public string Type => Object.SourceType.ToString();
    public string StarIndex { get; internal set; } = "";
    public string Resolution => !CoordinatesResolved ? "unresolved coordinate expression" : string.IsNullOrWhiteSpace(Behavior) || Behavior.StartsWith("(unresolved",StringComparison.Ordinal) || Model.StartsWith("(unresolved",StringComparison.Ordinal) ? "unresolved preset, model or behavior" : "source declaration";
}

public static class ProjectObjectIndex
{
    public static string Root(string root) => Directory.Exists(Path.Combine(root,"levels")) ? root : Path.Combine(root,"howtomake");
    public static List<ProjectObjectEntry> Scan(string root, CancellationToken cancel, IProgress<string>? progress, List<string> errors, string? onlyLevel = null)
    {
        root=Root(root);
        cancel.ThrowIfCancellationRequested();
        var result=new List<ProjectObjectEntry>();
        var sources=new Dictionary<string,(string Text,string Version,List<CSourceCall> Calls)>(StringComparer.OrdinalIgnoreCase);
        var evidence=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        string ReadEvidence(string file)
        {
            if(!evidence.TryGetValue(file,out string? text)) evidence[file]=text=File.Exists(file) ? CSourceCalls.MaskComments(File.ReadAllText(file)) : "";
            return text;
        }
        foreach(var level in new LevelScanner().ScanLevels(root).Where(l=>onlyLevel==null || string.Equals(l.LevelPath,onlyLevel,StringComparison.OrdinalIgnoreCase)))
        {
            cancel.ThrowIfCancellationRequested(); progress?.Report(level.ShortName);
            try
            {
                var areas=new WarpScriptDocument(Path.Combine(level.LevelPath,"script.c")).Areas;
                var collisions=new CollisionParser().FindCollisionFiles(level.LevelPath);
                foreach(string area in areas)
                {
                    cancel.ThrowIfCancellationRequested();
                    if(!int.TryParse(area,out int number)) { errors.Add(level.ShortName+": unresolved area "+area); continue; }
                    collisions.TryGetValue("Area "+number,out string? collision);
                    var objects=ScanArea(root,level,number,collision,errors,cancel);
                    foreach(var obj in objects.Where(o=>o.SourceType!=ObjectSourceType.Mario))
                    {
                        cancel.ThrowIfCancellationRequested();
                        if(obj.SourceFile==null) continue;
                        if(!sources.TryGetValue(obj.SourceFile,out var snapshot))
                        {
                            byte[] bytes=File.ReadAllBytes(obj.SourceFile);
                            using var reader=new StreamReader(new MemoryStream(bytes)); string text=reader.ReadToEnd();
                            sources[obj.SourceFile]=snapshot=(text,Convert.ToHexString(SHA256.HashData(bytes)),CSourceCalls.Read(text,LevelSaver.ObjectCall));
                        }
                        string source=snapshot.Text;
                        var call=snapshot.Calls.FirstOrDefault(c=>c.Start==obj.SourceIndex);
                        if(call==null) { errors.Add(obj.SourceFile+": unresolved object source span"); continue; }
                        obj.AreaIndex=number;
                        var entry=new ProjectObjectEntry(level,obj,new(level.LevelPath,number,obj.SourceFile,call.Start,call.End-call.Start,obj.SourceType,call.Text(source),snapshot.Version));
                        int coordinateStart=obj.SourceType==ObjectSourceType.Normal ? 1 : obj.SourceType==ObjectSourceType.Macro ? 2 : 1;
                        entry.CoordinatesResolved=Enumerable.Range(coordinateStart,3).All(i=>System.Text.RegularExpressions.Regex.IsMatch(call.Argument(source,i),@"^-?(?:[0-9]+|0[xX][0-9a-fA-F]+)$"));
                        if(ObjectiveCatalog.Kind(entry.Behavior)!="custom / source reference" && ObjectiveCatalog.Describe(root,entry,ReadEvidence).CanEditStar && ObjectiveCatalog.TryParameters(entry.Parameters,out uint parameters)) entry.StarIndex=(parameters>>24).ToString();
                        result.Add(entry);
                    }
                }
            }
            catch(OperationCanceledException) { throw; }
            catch(Exception ex) { errors.Add(level.ShortName+": "+ex.Message); }
        }
        return result;
    }
    private static List<LevelObject> ScanArea(string root,LevelMetadata level,int area,string? collision,List<string> errors,CancellationToken cancel)
    {
        string script=Path.Combine(level.LevelPath,"script.c");
        var parser=new ObjectParser();
        var objects=parser.ParseScriptFile(script,area);
        string? macroList=parser.ParseMacroListName(script,area);
        if(macroList!=null)
        {
            var macroParser=new MacroObjectParser();
            var presets=macroParser.ParsePresets(Path.Combine(root,"include","macro_presets.inc.c"));
            bool found=false;
            foreach(string file in Directory.EnumerateFiles(level.LevelPath,"*.c",SearchOption.AllDirectories))
            {
                cancel.ThrowIfCancellationRequested();
                try
                {
                    string source=File.ReadAllText(file);
                    if(!Regex.IsMatch(CSourceCalls.MaskComments(source),@"\b"+Regex.Escape(macroList)+@"\s*\[")) continue;
                    found=true;
                    var calls=CSourceCalls.Read(source,n=>n is "MACRO_OBJECT" or "MACRO_OBJECT_WITH_BHV_PARAM");
                    foreach(var call in calls)
                    {
                        string preset=call.Argument(source,0);
                        if(!presets.ContainsKey(preset)) presets[preset]=new MacroPreset { Behavior="(unresolved preset)",Model="(unresolved model)" };
                    }
                    var parsed=macroParser.ParseMacroFile(file,presets);
                    objects.AddRange(parsed);
                    if(parsed.Count!=calls.Count) errors.Add(file+": some macro placements could not be parsed");
                }
                catch(Exception ex) { errors.Add(file+": "+ex.Message); }
            }
            if(!found) errors.Add(script+": macro array was not found: "+macroList);
        }
        if(collision!=null)
        {
            try
            {
                string presetPath=Path.Combine(root,"include","special_presets.inc.c");
                string definitions=File.Exists(presetPath) ? CSourceCalls.MaskComments(File.ReadAllText(presetPath)) : "";
                var presets=Regex.Matches(definitions,@"\{\s*(\w+)\s*,\s*SPTYPE_\w+\s*,\s*[^,]+,\s*([^,]+),\s*(\w+)\s*\}")
                    .GroupBy(m=>m.Groups[1].Value).ToDictionary(g=>g.Key,g=>g.First());
                var models=new SpecialObjectParser().ParsePresets(presetPath);
                string source=ActorSourceReader.Read(collision,preserveOffsets:true);
                foreach(var call in CSourceCalls.Read(source,n=>n is "SPECIAL_OBJECT" or "SPECIAL_OBJECT_WITH_YAW" or "SPECIAL_OBJECT_WITH_YAW_AND_PARAM"))
                {
                    cancel.ThrowIfCancellationRequested();
                    try
                    {
                        string Arg(int i)=>call.Argument(source,i);
                        string preset=Arg(0); presets.TryGetValue(preset,out var match);
                        string? raw=call.Arguments.Count>5 ? Arg(5) : null;
                        ObjectiveCatalog.TryParameters(raw ?? "0",out uint value);
                        objects.Add(new LevelObject { SourceType=ObjectSourceType.Special,PresetName=preset,ModelName=match?.Groups[2].Value.Trim() ?? models.GetValueOrDefault(preset,"(unresolved model)"),Behavior=match?.Groups[3].Value ?? "(unresolved preset)",
                            X=int.Parse(Arg(1)),Y=int.Parse(Arg(2)),Z=int.Parse(Arg(3)),RY=call.Arguments.Count>4 ? (int)Math.Round(int.Parse(Arg(4))*360.0/256) : 0,
                            Params=value,RawParams=raw,AreaIndex=area,SourceFile=collision,SourceIndex=call.Start,SourceLength=call.End-call.Start });
                    }
                    catch(Exception ex) { errors.Add(collision+" at "+call.Start+": "+ex.Message); }
                }
            }
            catch(OperationCanceledException) { throw; }
            catch(Exception ex) { errors.Add(collision+": "+ex.Message); }
        }
        return objects;
    }
    public static void Validate(ObjectNavigationReference reference)
    {
        Validate(new[]{reference});
    }
    public static void Validate(IEnumerable<ObjectNavigationReference> references)
    {
        foreach(var group in references.GroupBy(r=>r.SourceFile,StringComparer.OrdinalIgnoreCase))
        {
            byte[] bytes=File.ReadAllBytes(group.Key);
            using var reader=new StreamReader(new MemoryStream(bytes)); string source=reader.ReadToEnd();
            string version=Convert.ToHexString(SHA256.HashData(bytes));
            foreach(var reference in group)
                if(version!=reference.SourceVersion || reference.Start<0 || reference.Start+reference.Length>source.Length || source.Substring(reference.Start,reference.Length)!=reference.SourceText)
                    throw new InvalidDataException("The object source changed. Refresh the results before opening or saving it.");
        }
    }
}
