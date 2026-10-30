using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Sm64DecompLevelViewer.Models;

namespace Sm64DecompLevelViewer.Services;

internal static class ObjectMacroWriter
{
    private static int Integer(string text)=>int.TryParse(text,NumberStyles.Integer,CultureInfo.InvariantCulture,out int n) ? n : text.StartsWith("0x",StringComparison.OrdinalIgnoreCase) && int.TryParse(text[2..],NumberStyles.HexNumber,CultureInfo.InvariantCulture,out n) ? n : 0;
    internal static LevelSaver.State Baseline(string source,CSourceCall c,LevelObject o)
    {
        var state=LevelSaver.Current(o); var a=Enumerable.Range(0,c.Arguments.Count).Select(i=>c.Argument(source,i)).ToArray();
        if(c.Name is "OBJECT" or "OBJECT_WITH_ACTS" && a.Length>=9) return state with { Model=a[0],X=Integer(a[1]),Y=Integer(a[2]),Z=Integer(a[3]),RX=Integer(a[4]),RY=Integer(a[5]),RZ=Integer(a[6]),Raw=a[7],Params=unchecked((uint)Integer(a[7])),Behavior=a[8],Acts=a.Length>9 ? a[9] : null };
        if(c.Name=="MARIO_POS" && a.Length==5) return state with { Area=Integer(a[0]),RY=Integer(a[1]),X=Integer(a[2]),Y=Integer(a[3]),Z=Integer(a[4]) };
        if(c.Name.StartsWith("MACRO_OBJECT",StringComparison.Ordinal) && a.Length>=5) return state with { Preset=a[0],RY=Integer(a[1]),X=Integer(a[2]),Y=Integer(a[3]),Z=Integer(a[4]),Raw=a.Length>5 ? a[5] : null,Params=a.Length>5 ? unchecked((uint)Integer(a[5])) : state.Params };
        if(c.Name.StartsWith("SPECIAL_OBJECT",StringComparison.Ordinal) && a.Length>=4) return state with { Preset=a[0],X=Integer(a[1]),Y=Integer(a[2]),Z=Integer(a[3]),RY=a.Length>4 ? (int)Math.Round(Integer(a[4])*360.0/256) : 0,Raw=a.Length>5 ? a[5] : null,Params=a.Length>5 ? unchecked((uint)Integer(a[5])) : 0 };
        throw new InvalidDataException("Unsupported object macro: "+c.Name);
    }
    private static string Parameter(LevelObject o)=>!string.IsNullOrWhiteSpace(o.RawParams) ? o.RawParams : $"0x{o.Params:X8}";
    internal static string Preset(LevelObject o)=>string.IsNullOrWhiteSpace(o.PresetName) ? o.ModelName : o.PresetName;
    internal static int Yaw(LevelObject o)=>(int)Math.Round(((o.RY%360+360)%360)*256.0/360)%256;
    internal static string Rewrite(string source,string path,CSourceCall call,LevelObject o,LevelSaver.State old)
    {
        var now=LevelSaver.Current(o); if(now==old) return call.Text(source);
        ValidateObject(o);
        var replacements=new Dictionary<int,string>();
        void Change(int index,object value,object? previous) { if(!Equals(value,previous)) replacements[index]=Convert.ToString(value,CultureInfo.InvariantCulture)!; }
        if(call.Name is "OBJECT" or "OBJECT_WITH_ACTS")
        {
            if(call.Arguments.Count!=(call.Name=="OBJECT" ? 9 : 10)) throw new InvalidDataException("Malformed object record.");
            Change(0,o.ModelName,old.Model); Change(1,o.X,old.X); Change(2,o.Y,old.Y); Change(3,o.Z,old.Z); Change(4,o.RX,old.RX); Change(5,o.RY,old.RY); Change(6,o.RZ,old.RZ); Change(8,o.Behavior,old.Behavior);
            if(o.Params!=old.Params || o.RawParams!=old.Raw) replacements[7]=Parameter(o);
            if(call.Name=="OBJECT_WITH_ACTS" && o.RawActs!=old.Acts && o.RawActs!=null) replacements[9]=o.RawActs;
        }
        else if(call.Name=="MARIO_POS") { Change(0,o.AreaIndex,old.Area); Change(1,o.RY,old.RY); Change(2,o.X,old.X); Change(3,o.Y,old.Y); Change(4,o.Z,old.Z); }
        else if(call.Name.StartsWith("MACRO_OBJECT",StringComparison.Ordinal))
        {
            if(call.Arguments.Count==5 && (o.Params!=old.Params || o.RawParams!=old.Raw)) return NewMacro(source,path,o);
            Change(0,Preset(o),string.IsNullOrEmpty(old.Preset) ? old.Model : old.Preset); Change(1,o.RY,old.RY); Change(2,o.X,old.X); Change(3,o.Y,old.Y); Change(4,o.Z,old.Z);
            if(call.Arguments.Count>5 && (o.Params!=old.Params || o.RawParams!=old.Raw)) replacements[5]=Parameter(o);
        }
        else
        {
            if(Preset(o)!=(string.IsNullOrEmpty(old.Preset) ? old.Model : old.Preset)) return NewMacro(source,path,o);
            Change(1,o.X,old.X); Change(2,o.Y,old.Y); Change(3,o.Z,old.Z);
            if(call.Arguments.Count>4 && o.RY!=old.RY) replacements[4]=Yaw(o).ToString(CultureInfo.InvariantCulture);
            if(call.Arguments.Count>5 && (o.Params!=old.Params || o.RawParams!=old.Raw)) replacements[5]=Parameter(o);
        }
        string rewritten=CSourceCalls.ReplaceArguments(source,call,replacements);
        if(call.Name=="OBJECT" && o.RawActs!=old.Acts && o.RawActs!=null)
        {
            int close=rewritten.LastIndexOf(')');
            rewritten="OBJECT_WITH_ACTS"+rewritten["OBJECT".Length..close]+", "+o.RawActs+rewritten[close..];
        }
        return rewritten;
    }
    private static void ValidateObject(LevelObject o)
    {
        var identifiers=o.SourceType is ObjectSourceType.Macro or ObjectSourceType.Special ? new[]{Preset(o)} : new[]{o.ModelName,o.Behavior};
        foreach(string id in identifiers.Where(s=>!string.IsNullOrEmpty(s)))
            if(!Regex.IsMatch(id,@"^[A-Za-z_]\w*$")) throw new InvalidDataException("Invalid object identifier: "+id);
        if(o.X<short.MinValue || o.X>short.MaxValue || o.Y<short.MinValue || o.Y>short.MaxValue || o.Z<short.MinValue || o.Z>short.MaxValue) throw new InvalidDataException("Object coordinates must fit signed 16-bit values.");
        if((o.SourceType==ObjectSourceType.Normal || o.SourceType==ObjectSourceType.Mario) && o.AreaIndex<1) throw new InvalidDataException("Object destination area is missing.");
    }
    internal static string NewMacro(string source,string path,LevelObject o)
    {
        ValidateObject(o);
        return o.SourceType switch
        {
            ObjectSourceType.Normal when string.IsNullOrWhiteSpace(o.Behavior)=>throw new InvalidDataException("A new object needs a behavior."),
            ObjectSourceType.Normal=>$"{(o.RawActs==null ? "OBJECT" : "OBJECT_WITH_ACTS")}({o.ModelName}, {o.X}, {o.Y}, {o.Z}, {o.RX}, {o.RY}, {o.RZ}, {Parameter(o)}, {o.Behavior}{(o.RawActs==null ? "" : ", "+o.RawActs)})",
            ObjectSourceType.Mario=>$"MARIO_POS({o.AreaIndex}, {o.RY}, {o.X}, {o.Y}, {o.Z})",
            ObjectSourceType.Macro=>o.Params!=0 || !string.IsNullOrWhiteSpace(o.RawParams) ? $"MACRO_OBJECT_WITH_BHV_PARAM({Preset(o)}, {o.RY}, {o.X}, {o.Y}, {o.Z}, {Parameter(o)})" : $"MACRO_OBJECT({Preset(o)}, {o.RY}, {o.X}, {o.Y}, {o.Z})",
            ObjectSourceType.Special=>SpecialObjectFormats.Format(source,path,o),
            _=>throw new InvalidDataException("Unsupported new object type.")
        };
    }
    internal static int InsertionPoint(string source,List<CSourceCall> calls,LevelObject o)
    {
        var all=CSourceCalls.Read(source,n=>n is "AREA" or "END_AREA" or "MACRO_OBJECT_END" or "COL_SPECIAL_INIT" or "COL_END" or "COL_WATER_BOX_INIT" or "COL_TRI_STOP");
        if(o.SourceType==ObjectSourceType.Normal)
        {
            var area=all.FirstOrDefault(c=>c.Name=="AREA" && Integer(c.Argument(source,0))==o.AreaIndex) ?? throw new InvalidDataException("Destination area not found: "+o.AreaIndex);
            return all.FirstOrDefault(c=>c.Name=="END_AREA" && c.Start>area.End)?.Start ?? throw new InvalidDataException("Destination area has no END_AREA.");
        }
        if(o.SourceType==ObjectSourceType.Macro) return all.LastOrDefault(c=>c.Name=="MACRO_OBJECT_END")?.Start ?? throw new InvalidDataException("Macro object terminator is missing.");
        if(o.SourceType==ObjectSourceType.Special)
        {
            var init=all.SingleOrDefault(c=>c.Name=="COL_SPECIAL_INIT");
            if(init==null)
            {
                var stop=all.SingleOrDefault(c=>c.Name=="COL_TRI_STOP") ?? throw new InvalidDataException("A unique collision triangle section is required for special objects.");
                return all.FirstOrDefault(c=>c.Start>stop.End && c.Name is "COL_WATER_BOX_INIT" or "COL_END")?.Start
                    ?? throw new InvalidDataException("Collision terminator is missing.");
            }
            var last=calls.LastOrDefault(c=>c.Start>init.End && c.Name.StartsWith("SPECIAL_OBJECT",StringComparison.Ordinal));
            int index=last?.End ?? init.End; var mask=CSourceCalls.MaskComments(source);
            while(index<mask.Length && char.IsWhiteSpace(mask[index])) index++;
            if(index<mask.Length && mask[index]==',') index++;
            return index;
        }
        throw new InvalidDataException("Adding another Mario start record is not supported.");
    }
}
