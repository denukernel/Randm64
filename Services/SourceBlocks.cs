using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization;

namespace Sm64DecompLevelViewer.Services;

public enum SourceBlockKind { Set, Add, Multiply, Call, If, Else, Repeat, While, Declare }
public sealed class SourceCodeBlock
{
    public SourceBlockKind Kind { get; set; }
    public string First { get; set; } = "";
    public string Second { get; set; } = "";
    public string DataType { get; set; } = "int";
    public List<SourceCodeBlock> Children { get; set; } = new();
    [JsonIgnore] public bool IsContainer => Kind is SourceBlockKind.If or SourceBlockKind.Else or SourceBlockKind.Repeat or SourceBlockKind.While;
    [JsonIgnore] public string Label => Kind switch
    {
        SourceBlockKind.Set=>$"set {First} to {Second}",SourceBlockKind.Add=>$"change {First} by {Second}",SourceBlockKind.Multiply=>$"multiply {First} by {Second}",
        SourceBlockKind.Call=>$"call {First}({Second})",SourceBlockKind.If=>$"if {First}",SourceBlockKind.Else=>"else",SourceBlockKind.Repeat=>$"repeat {First} times",SourceBlockKind.While=>$"while {First}",_=>$"local {DataType} {First} = {Second}"
    };
    public static SourceCodeBlock Create(SourceBlockKind kind) => new() { Kind=kind,First=kind switch { SourceBlockKind.Call=>"function_name",SourceBlockKind.If=>"1",SourceBlockKind.While=>"0",SourceBlockKind.Repeat=>"10",SourceBlockKind.Else=>"",_=>"value" },Second=kind is SourceBlockKind.Set or SourceBlockKind.Declare ? "0" : kind is SourceBlockKind.Add or SourceBlockKind.Multiply ? "1" : "" };
}
public sealed class SourceBlockProject
{
    public int Version { get; set; } = 1;
    public string TargetFile { get; set; } = "";
    public string Function { get; set; } = "";
    public List<SourceCodeBlock> Blocks { get; set; } = new();
    public string ToJson() => JsonSerializer.Serialize(this,new JsonSerializerOptions { WriteIndented=true });
    public static SourceBlockProject Parse(string json)
    {
        var project=JsonSerializer.Deserialize<SourceBlockProject>(json,new JsonSerializerOptions { MaxDepth=64 }) ?? throw new InvalidDataException("Empty block project");
        if(project.Version!=1 || project.Blocks==null || project.TargetFile==null || project.Function==null) throw new InvalidDataException("Unsupported or incomplete block project");
        SourceBlocks.Generate(project.Blocks); return project;
    }
}
public sealed record SourceFunctionTarget(string Name,int BodyStart,int BodyEnd,string Signature);
public static class SourceBlocks
{
    private static string Mask(string source)=>Regex.Replace(CSourceCalls.MaskComments(source),@"""(?:\\.|[^""\\])*""|'(?:\\.|[^'\\])*'",m=>new string(' ',m.Length));
    public static IReadOnlyList<SourceFunctionTarget> Functions(string source)
    {
        bool RegionsClosed(string body)
        {
            int depth=0;
            foreach(Match directive in Regex.Matches(CSourceCalls.MaskComments(body),@"(?m)^\s*#\s*(if|ifdef|ifndef|else|elif|endif)\b"))
            {
                string command=directive.Groups[1].Value;
                if(command is "if" or "ifdef" or "ifndef") depth++;
                else if(command=="endif") { if(--depth<0) return false; }
                else if(depth==0) return false;
            }
            return depth==0;
        }
        try
        {
            var direct=DiscoverFunctions(source);
            if(direct.All(f=>RegionsClosed(source[f.BodyStart..f.BodyEnd]))) return direct;
        }
        catch(InvalidDataException) when(Regex.IsMatch(source,@"(?m)^\s*#\s*if")) { }
        if(!Regex.IsMatch(source,@"(?m)^\s*#\s*if")) throw new InvalidDataException("Function boundaries could not be determined");
        string active=ActorSourceReader.Read("source.c",preserveOffsets:true,sourceText:source);
        var functions=DiscoverFunctions(active).Where(f=>RegionsClosed(source[f.BodyStart..f.BodyEnd])).ToList();
        string rawMask=Mask(source);
        var definitions=CSourceCalls.Read(source,_=>true).Where(call=>
        {
            int next=call.End; while(next<rawMask.Length && char.IsWhiteSpace(rawMask[next])) next++;
            return next<rawMask.Length && rawMask[next]=='{';
        }).GroupBy(call=>call.Name).ToDictionary(g=>g.Key,g=>g.Count());
        // Keep duplicate names ambiguous even if the US branch selects only one.
        return functions.SelectMany(f=>Enumerable.Repeat(f,definitions.GetValueOrDefault(f.Name,1)>1 ? 2 : 1)).ToList();
    }
    private static IReadOnlyList<SourceFunctionTarget> DiscoverFunctions(string source)
    {
        string mask=Mask(source); var depth=new int[mask.Length+1];
        for(int i=0;i<mask.Length;i++) depth[i+1]=depth[i]+(mask[i]=='{' ? 1 : mask[i]=='}' ? -1 : 0);
        var result=new List<SourceFunctionTarget>();
        foreach(var call in CSourceCalls.Read(source,_=>true))
        {
            if(depth[call.Start]!=0 || call.Name is "if" or "while" or "for" or "switch") continue;
            int opening=call.End; while(opening<mask.Length && char.IsWhiteSpace(mask[opening])) opening++;
            if(opening>=mask.Length || mask[opening]!='{') continue;
            int beginning=Math.Max(mask.LastIndexOf(';',call.Start),Math.Max(mask.LastIndexOf('}',call.Start),mask.LastIndexOf('{',call.Start)))+1;
            string prefix=mask[beginning..call.Start];
            if(prefix.Contains('=')) continue;
            int end=opening+1; while(end<mask.Length && depth[end]>0) end++;
            if(end>mask.Length || depth[end]!=0 || mask[end-1]!='}') throw new InvalidDataException("Unbalanced function: "+call.Name);
            result.Add(new(call.Name,opening+1,end-1,source[beginning..call.End].Trim()));
        }
        return result;
    }
    public static string Expression(string value,bool allowEmpty=false)
    {
        if(string.IsNullOrWhiteSpace(value)) { if(allowEmpty) return ""; throw new InvalidDataException("A block expression is empty"); }
        if(value.Length>4096) throw new InvalidDataException("Block expressions support up to 4096 characters");
        var stack=new Stack<char>(); char quote='\0'; bool escape=false;
        for(int i=0;i<value.Length;i++)
        {
            char c=value[i]; if(c is '\r' or '\n' or '\0') throw new InvalidDataException("Block expressions must be on one line");
            if(quote!='\0') { if(escape) escape=false; else if(c=='\\') escape=true; else if(c==quote) quote='\0'; continue; }
            if(c is '"' or '\'') { quote=c; continue; }
            if(c is ';' or '{' or '}' or '#' || c=='/' && i+1<value.Length && value[i+1] is '/' or '*') throw new InvalidDataException("Use expressions without statements, comments, braces or directives in block fields");
            if(c is '(' or '[') stack.Push(c);
            if(c is ')' or ']') if(stack.Count==0 || stack.Pop()!=(c==')' ? '(' : '[')) throw new InvalidDataException("Unbalanced block expression");
        }
        if(quote!='\0' || stack.Count>0) throw new InvalidDataException("Unbalanced block expression");
        return value.Trim();
    }
    private static string Identifier(string value)
    {
        if(!Regex.IsMatch(value,@"^[A-Za-z_]\w*$") || value.StartsWith("randm64_block_",StringComparison.Ordinal)) throw new InvalidDataException("Use a C identifier; randm64_block_ names are reserved");
        if(new[]{"auto","break","case","char","const","continue","default","do","double","else","enum","extern","float","for","goto","if","int","long","register","return","short","signed","sizeof","static","struct","switch","typedef","union","unsigned","void","volatile","while"}.Contains(value)) throw new InvalidDataException("A C keyword cannot be used as an identifier");
        return value;
    }
    private static string Destination(string value)
    {
        value=Expression(value);
        if(!Regex.IsMatch(value,@"^[A-Za-z_]\w*(?:(?:->|\.)[A-Za-z_]\w*|\[[^\]]+\])*$")) throw new InvalidDataException("An assignment destination must be a variable, field, or array element");
        return value;
    }
    public static string Generate(IReadOnlyList<SourceCodeBlock> blocks)
    {
        var output=new StringBuilder(); int total=0,loop=0;
        void Line(int depth,string text)=>output.Append(' ',depth*4).Append(text).Append('\n');
        void Scope(IReadOnlyList<SourceCodeBlock> list,int depth)
        {
            if(depth>32 || (total+=list.Count)>256) throw new InvalidDataException("Block projects support up to 256 blocks and 32 nested scopes");
            if(list.Any(b=>b==null)) throw new InvalidDataException("Null block in project");
            var names=new HashSet<string>(StringComparer.Ordinal);
            foreach(var block in list.Where(b=>b.Kind==SourceBlockKind.Declare))
            {
                string name=Identifier(block.First);
                if(!names.Add(name)) throw new InvalidDataException("Duplicate local variable: "+name);
                if(block.DataType is not ("int" or "float" or "short" or "unsigned int" or "s16" or "s32" or "u16" or "u32" or "f32")) throw new InvalidDataException("Choose a supported C89 local variable type");
                Line(depth,block.DataType+" "+name+";");
            }
            foreach(var block in list)
            {
                if(block.Children==null || !Enum.IsDefined(block.Kind)) throw new InvalidDataException("Invalid block kind or children");
                if(!block.IsContainer && block.Children.Count>0) throw new InvalidDataException("Only control blocks can contain other blocks");
                switch(block.Kind)
                {
                    case SourceBlockKind.Declare: Line(depth,Identifier(block.First)+" = "+Expression(block.Second)+";"); break;
                    case SourceBlockKind.Set: case SourceBlockKind.Add: case SourceBlockKind.Multiply:
                        Line(depth,Destination(block.First)+(block.Kind==SourceBlockKind.Set ? " = " : block.Kind==SourceBlockKind.Add ? " += " : " *= ")+Expression(block.Second)+";"); break;
                    case SourceBlockKind.Call: Line(depth,Identifier(block.First)+"("+Expression(block.Second,true)+");"); break;
                    case SourceBlockKind.If:
                        if(block.Children.Count(b=>b.Kind==SourceBlockKind.Else)>1 || block.Children.Any(b=>b.Kind==SourceBlockKind.Else && !ReferenceEquals(b,block.Children.Last()))) throw new InvalidDataException("An else block must be the final child of its if block");
                        Line(depth,"if ("+Expression(block.First)+") {");
                        Scope(block.Children.Where(b=>b.Kind!=SourceBlockKind.Else).ToList(),depth+1);
                        if(block.Children.LastOrDefault() is { Kind:SourceBlockKind.Else } otherwise) { total++; Line(depth,"} else {"); Scope(otherwise.Children,depth+1); }
                        Line(depth,"}"); break;
                    case SourceBlockKind.Else: throw new InvalidDataException("Place else as the final child of an if block");
                    case SourceBlockKind.While:
                        Line(depth,"while ("+Expression(block.First)+") {"); Scope(block.Children,depth+1); Line(depth,"}"); break;
                    case SourceBlockKind.Repeat:
                        string counter="randm64_block_i"+loop++,count="randm64_block_n"+loop;
                        Line(depth,"{"); Line(depth+1,"int "+counter+";"); Line(depth+1,"int "+count+";");
                        Line(depth+1,count+" = "+Expression(block.First)+";");
                        Line(depth+1,"for ("+counter+" = 0; "+counter+" < "+count+"; "+counter+"++) {"); Scope(block.Children,depth+2); Line(depth+1,"}"); Line(depth,"}"); break;
                }
            }
        }
        Scope(blocks,2); return output.ToString();
    }
    public static string Apply(string source,string function,IReadOnlyList<SourceCodeBlock> blocks)
    {
        string generated=Generate(blocks);
        var targets=Functions(source).Where(f=>f.Name==function).ToList();
        if(targets.Count!=1) throw new InvalidDataException("Select a unique function definition; duplicate regional definitions need manual source editing");
        var target=targets[0]; string body=source[target.BodyStart..target.BodyEnd];
        string begin="/* RANDM64_BLOCKS_BEGIN "+function+" */",end="/* RANDM64_BLOCKS_END "+function+" */";
        string metadata=Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(blocks)));
        string comment="/* RANDM64_BLOCK_DATA\n"+string.Join("\n",metadata.Chunk(76).Select(chars=>"    "+new string(chars)))+"\n    */";
        string section=begin+"\n    "+comment+"\n    {\n"+generated+"    }\n    "+end;
        int a=body.IndexOf(begin,StringComparison.Ordinal),b=body.IndexOf(end,StringComparison.Ordinal);
        if(a>=0 || b>=0)
        {
            if(a<0 || b<a || body.IndexOf(begin,a+begin.Length,StringComparison.Ordinal)>=0 || body.IndexOf(end,b+end.Length,StringComparison.Ordinal)>=0) throw new InvalidDataException("Managed block markers are incomplete or ambiguous");
            ReadManaged(source,function);
            body=body[..a]+section+body[(b+end.Length)..];
        }
        else
        {
            if(blocks.Count==0) throw new InvalidDataException("Add at least one block before previewing");
            body="\n    "+section+"\n    /* Original function body retains its own declaration scope. */\n    {"+body+"    }\n";
        }
        string result=source[..target.BodyStart]+body+source[target.BodyEnd..];
        if(CSourceCalls.Read(generated,n=>n is "sins" or "coss").Count>0 && !Regex.IsMatch(result,"#\\s*include\\s*[<\"]engine/math_util\\.h[>\"]")) result="#include \"engine/math_util.h\"\n"+result;
        string newline=source.Contains("\r\n") ? "\r\n" : "\n";
        return result.Replace("\r\n","\n").Replace("\n",newline);
    }
    public static List<SourceCodeBlock>? ReadManaged(string source,string function)
    {
        var targets=Functions(source).Where(f=>f.Name==function).ToList(); if(targets.Count!=1) return null;
        var target=targets[0]; string body=source[target.BodyStart..target.BodyEnd].Replace("\r\n","\n");
        string begin="/* RANDM64_BLOCKS_BEGIN "+function+" */",end="/* RANDM64_BLOCKS_END "+function+" */";
        int a=body.IndexOf(begin,StringComparison.Ordinal),b=body.IndexOf(end,StringComparison.Ordinal);
        if(a<0 && b<0) return null;
        if(a<0 || b<a || body.IndexOf(begin,a+begin.Length,StringComparison.Ordinal)>=0 || body.IndexOf(end,b+end.Length,StringComparison.Ordinal)>=0) throw new InvalidDataException("Managed block markers are incomplete or ambiguous");
        string section=body[a..(b+end.Length)];
        var metadata=Regex.Match(section,@"/\* RANDM64_BLOCK_DATA\s+([A-Za-z0-9+/=\s]+?)\s*\*/");
        if(!metadata.Success || metadata.Groups[1].Length>1_000_000) throw new InvalidDataException("Managed block design is missing or too large; use the source view");
        var blocks=JsonSerializer.Deserialize<List<SourceCodeBlock>>(Convert.FromBase64String(metadata.Groups[1].Value),new JsonSerializerOptions { MaxDepth=64 }) ?? throw new InvalidDataException("Invalid managed block design");
        string generated=Generate(blocks);
        string expected=begin+"\n    "+metadata.Value+"\n    {\n"+generated+"    }\n    "+end;
        if(section!=expected) throw new InvalidDataException("The managed C section was edited manually. Use the source view to preserve those edits; visual replacement is disabled.");
        return blocks;
    }
}
