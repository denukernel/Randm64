using System.Globalization;
using System.IO;
using System.Text;
using Sm64DecompLevelViewer.Models;

namespace Sm64DecompLevelViewer.Services;

public class LevelSaver
{
    internal sealed record State(string Model,string Preset,int X,int Y,int Z,int RX,int RY,int RZ,uint Params,string? Raw,string Behavior,int Area,string? Acts);
    private sealed record Anchor(string Text,int Ordinal,int Count,State State,bool Deleted=false);
    private sealed record Edit(LevelObject? Object,int Start,int End,string Text);
    private readonly Dictionary<LevelObject,Anchor> anchors=new();
    public string? LastError { get; private set; }
    internal static State Current(LevelObject o)=>new(o.ModelName,o.PresetName,o.X,o.Y,o.Z,o.RX,o.RY,o.RZ,o.Params,o.RawParams,o.Behavior,o.AreaIndex,o.RawActs);
    internal static bool ObjectCall(string n)=>n is "OBJECT" or "OBJECT_WITH_ACTS" or "MARIO_POS" or "MACRO_OBJECT" or "MACRO_OBJECT_WITH_BHV_PARAM" or "SPECIAL_OBJECT" or "SPECIAL_OBJECT_WITH_YAW" or "SPECIAL_OBJECT_WITH_YAW_AND_PARAM";
    internal static bool Kind(CSourceCall c,LevelObject o)=>o.SourceType switch
    {
        ObjectSourceType.Normal=>c.Name is "OBJECT" or "OBJECT_WITH_ACTS",
        ObjectSourceType.Macro=>c.Name.StartsWith("MACRO_OBJECT",StringComparison.Ordinal),
        ObjectSourceType.Special=>c.Name.StartsWith("SPECIAL_OBJECT",StringComparison.Ordinal),
        ObjectSourceType.Mario=>c.Name=="MARIO_POS", _=>false
    };
    public void CaptureSources(IEnumerable<LevelObject> objects)
    {
        foreach(var group in objects.Where(o=>!o.IsNew && !o.IsDeleted && o.SourceFile!=null).GroupBy(o=>o.SourceFile!))
        {
            string source=File.ReadAllText(group.Key); var calls=CSourceCalls.Read(source,ObjectCall);
            foreach(var o in group)
            {
                var call=calls.FirstOrDefault(c=>c.Start==o.SourceIndex && Kind(c,o));
                if(call==null) continue;
                var identical=calls.Where(c=>c.Text(source)==call.Text(source)).ToList();
                anchors[o]=new Anchor(call.Text(source),identical.FindIndex(c=>c.Start==call.Start),identical.Count,Current(o));
            }
        }
    }
    public bool SaveLevel(List<LevelObject> objects,string? collisionPath=null)
    {
        LastError=null;
        try
        {
            if(!string.IsNullOrWhiteSpace(collisionPath))
            {
                var collision=new CollisionParser().ParseCollisionFile(collisionPath,"","nonpersistent") ?? throw new InvalidDataException("Area collision could not be parsed.");
                _=CollisionSourceWriter.Stage(collisionPath,collision);
            }
            if(objects.Any(o=>!o.IsDeleted && string.IsNullOrWhiteSpace(o.SourceFile))) throw new InvalidDataException("An object has no destination source file.");
            var writes=new Dictionary<string,byte[]>(StringComparer.OrdinalIgnoreCase);
            var expected=new Dictionary<string,byte[]?>(StringComparer.OrdinalIgnoreCase);
            var updates=new List<(LevelObject Object,int Index,int Length,string Text,bool Deleted)>();
            foreach(var group in objects.Where(o=>o.SourceFile!=null).GroupBy(o=>Path.GetFullPath(o.SourceFile!),StringComparer.OrdinalIgnoreCase))
            {
                string path=group.Key;
                byte[] bytes=File.ReadAllBytes(path); string source=File.ReadAllText(path);
                var calls=CSourceCalls.Read(source,ObjectCall); var edits=new List<Edit>();
                var locations=new Dictionary<LevelObject,(int Start,int End)>();
                foreach(var o in group)
                {
                    if(o.IsNew)
                    {
                        if(!o.IsDeleted)
                        {
                            int insertion=ObjectMacroWriter.InsertionPoint(source,calls,o);
                            edits.Add(new Edit(o,insertion,insertion,"\n    "+ObjectMacroWriter.NewMacro(source,path,o)+","));
                        }
                        continue;
                    }
                    if(anchors.TryGetValue(o,out var old) && old.Deleted) continue;
                    if(old==null)
                    {
                        var at=calls.FirstOrDefault(c=>c.Start==o.SourceIndex && Kind(c,o));
                        if(at==null) throw new InvalidDataException("Object source offsets are stale. Reopen the level before saving: "+path);
                        var same=calls.Where(c=>c.Text(source)==at.Text(source)).ToList();
                        old=new Anchor(at.Text(source),same.FindIndex(c=>c.Start==at.Start),same.Count,ObjectMacroWriter.Baseline(source,at,o));
                    }
                    var matches=calls.Where(c=>Kind(c,o) && c.Text(source)==old.Text).ToList();
                    if(matches.Count==0 || matches.Count!=1 && matches.Count!=old.Count)
                        throw new InvalidDataException("An object's source changed or became ambiguous. Reopen the level before saving: "+path);
                    var call=matches.Count==1 ? matches[0] : matches[old.Ordinal]; locations[o]=(call.Start,call.End);
                    if(o.IsDeleted)
                    {
                        int end=call.End; string mask=CSourceCalls.MaskComments(source);
                        while(end<mask.Length && char.IsWhiteSpace(mask[end])) end++;
                        end=end<mask.Length && mask[end]==',' ? end+1 : call.End;
                        edits.Add(new Edit(o,call.Start,end,""));
                    }
                    else
                    {
                        string replacement=ObjectMacroWriter.Rewrite(source,path,call,o,old.State);
                        if(replacement!=call.Text(source)) edits.Add(new Edit(o,call.Start,call.End,replacement));
                    }
                }
                string result=Apply(source,edits);
                var stagedCalls=CSourceCalls.Read(result,n=>ObjectCall(n)||n=="COL_SPECIAL_INIT"||n=="COL_END");
                var counts=new List<Edit>();
                foreach(var init in stagedCalls.Where(c=>c.Name=="COL_SPECIAL_INIT"))
                {
                    int next=stagedCalls.Where(c=>c.Start>init.Start && c.Name is "COL_SPECIAL_INIT" or "COL_END").Select(c=>c.Start).DefaultIfEmpty(result.Length).Min();
                    int count=stagedCalls.Count(c=>c.Start>init.End && c.Start<next && c.Name.StartsWith("SPECIAL_OBJECT",StringComparison.Ordinal));
                    string text=CSourceCalls.ReplaceArguments(result,init,new(){[0]=count.ToString(CultureInfo.InvariantCulture)});
                    if(text!=init.Text(result)) counts.Add(new Edit(null,init.Start,init.End,text));
                }
                result=Apply(result,counts);
                if(result!=source)
                {
                    if(CSourceCalls.Read(result,n=>n=="COL_INIT").Count>0) CollisionSourceValidation.Validate(result,path);
                    writes[path]=EncodeLike(bytes,result); expected[path]=bytes;
                }
                var finalCalls=CSourceCalls.Read(result,ObjectCall);
                foreach(var o in group)
                {
                    if(o.IsDeleted) { updates.Add((o,-1,0,"",true)); continue; }
                    int projected; var own=edits.FirstOrDefault(e=>ReferenceEquals(e.Object,o));
                    if(own!=null && o.IsNew) projected=MappedIndex(own.Start,edits,own)+own.Text.IndexOf(ObjectMacroWriter.NewMacro(source,path,o),StringComparison.Ordinal);
                    else projected=MappedIndex(locations[o].Start,edits,own);
                    projected=MappedIndex(projected,counts,null);
                    var final=finalCalls.FirstOrDefault(c=>c.Start==projected && Kind(c,o)) ?? throw new InvalidDataException("Unable to validate staged object source: "+path);
                    updates.Add((o,final.Start,final.End-final.Start,final.Text(result),false));
                }
            }
            SourceFileTransaction.Commit(writes,expected);
            foreach(var update in updates)
            {
                var o=update.Object; o.SourceIndex=update.Index; o.SourceLength=update.Length; o.IsNew=false;
                anchors[o]=new Anchor(update.Text,0,1,Current(o),update.Deleted);
            }
            CaptureSources(objects.Where(o=>!o.IsDeleted));
            return true;
        }
        catch(Exception ex) { LastError=ex.Message; LogService.Log("Level save aborted: "+ex.Message); return false; }
    }
    private static byte[] EncodeLike(byte[] original,string source)
    {
        Encoding encoding=original.Length>=2 && original[0]==255 && original[1]==254 ? Encoding.Unicode : original.Length>=2 && original[0]==254 && original[1]==255 ? Encoding.BigEndianUnicode : new UTF8Encoding(original.Length>=3 && original[0]==239 && original[1]==187 && original[2]==191);
        return encoding.GetPreamble().Concat(encoding.GetBytes(source)).ToArray();
    }
    private static int MappedIndex(int index,List<Edit> edits,Edit? own)
    {
        int ownIndex=own==null ? int.MaxValue : edits.IndexOf(own);
        return index+edits.Where((e,i)=>!ReferenceEquals(e,own) && (e.Start<index || e.Start==index && e.Start==e.End && i<ownIndex)).Sum(e=>e.Text.Length-(e.End-e.Start));
    }
    private static string Apply(string source,List<Edit> edits)
    {
        var ordered=edits.OrderBy(e=>e.Start).ToList();
        for(int i=1;i<ordered.Count;i++) if(ordered[i-1].End>ordered[i].Start) throw new InvalidDataException("Overlapping source edits were rejected.");
        var output=new StringBuilder(source);
        foreach(var e in ordered.AsEnumerable().Reverse()) output.Remove(e.Start,e.End-e.Start).Insert(e.Start,e.Text);
        return output.ToString();
    }
}
