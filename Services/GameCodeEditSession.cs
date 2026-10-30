using System.IO;
using System.Text.RegularExpressions;

namespace Sm64DecompLevelViewer.Services;

public sealed record GameCodeFields(string First,string Second,string DataType,bool HasSecond,bool HasType,string Help);

/// <summary>Edits exact source spans in an immutable function snapshot, without regenerating surrounding C.</summary>
public sealed class GameCodeEditSession
{
    public string Source { get; }
    public string Function { get; }
    public IReadOnlyList<GameCodeBlock> Blocks { get; }
    private readonly SourceFunctionTarget target;
    private readonly string masked;
    private static string Mask(string text)=>Regex.Replace(CSourceCalls.MaskComments(text),@"""(?:\\.|[^""\\])*""|'(?:\\.|[^'\\])*'",m=>new string(' ',m.Length));
    public GameCodeEditSession(string source,string function)
    {
        Source=source; Function=function; Blocks=GameCodeBlocks.Read(source,function);
        target=SourceBlocks.Functions(source).Single(f=>f.Name==function); masked=Mask(source);
    }
    private (IReadOnlyList<GameCodeBlock> Siblings,GameCodeBlock? Parent) Find(GameCodeBlock block)
    {
        (IReadOnlyList<GameCodeBlock>,GameCodeBlock?)? Walk(IReadOnlyList<GameCodeBlock> list,GameCodeBlock? parent)
        {
            if(list.Any(b=>ReferenceEquals(b,block))) return (list,parent);
            foreach(var child in list) if(Walk(child.Children,child) is {} found) return found;
            return null;
        }
        return Walk(Blocks,null) ?? throw new InvalidDataException("The selected block is stale. Refresh the game-code view.");
    }
    private int Token(GameCodeBlock block)
    {
        int p=block.Start; while(p<block.End && char.IsWhiteSpace(masked[p])) p++; return p;
    }
    private bool Managed(int start,int end)
    {
        foreach(Match begin in Regex.Matches(Source,@"/\* RANDM64_BLOCKS_BEGIN \w+ \*/"))
        {
            int finish=Source.IndexOf("/* RANDM64_BLOCKS_END ",begin.Index,StringComparison.Ordinal);
            if(finish<0) finish=Source.Length; else { int close=Source.IndexOf("*/",finish,StringComparison.Ordinal); finish=close<0 ? Source.Length : close+2; }
            if(start<finish && end>begin.Index) return true;
        }
        return false;
    }
    private void Editable(GameCodeBlock block)
    {
        Find(block);
        if(block.Kind is "source" or "case" || Managed(Token(block),block.End) || block.Text(Source).Contains('#'))
            throw new InvalidDataException("This source, label, regional, or generated-hook block is protected. Use Source text, or My block program for generated hooks.");
        string code=masked[Token(block)..block.End];
        if(Regex.IsMatch(code,@"(?m)^\s*\w+\s*:(?!:)")) throw new InvalidDataException("Statements with labels require source-text editing.");
    }
    private (int Start,int End) Header(GameCodeBlock block)
    {
        string kind=block.Kind=="do" ? "while" : block.Kind;
        var calls=CSourceCalls.Read(Source[block.Start..block.End],n=>n==kind);
        var call=block.Kind=="do" ? calls.LastOrDefault() : calls.FirstOrDefault();
        if(call==null) throw new InvalidDataException("Control header could not be resolved");
        int opening=Source.IndexOf('(',block.Start+call.Start,call.End-call.Start);
        return (opening+1,block.Start+call.End-1);
    }
    private static readonly Regex Assignment=new(@"^(?<left>[\s\S]*?)\s*(?<op>\+=|-=|\*=|/=|%=|&=|\|=|\^=|(?<![=!<>])=(?!=))\s*(?<right>[\s\S]*?)\s*;\s*$");
    private static readonly Regex Declaration=new(@"^(?<type>(?:(?:static|const|volatile|unsigned|signed)\s+)*(?:int|float|double|char|short|long|s8|u8|s16|u16|s32|u32|s64|u64|f32|f64))\s+(?<name>[A-Za-z_]\w*)\s*(?:=\s*(?<value>[\s\S]*?))?\s*;\s*$");
    private static bool TopLevelComma(string expression)
    {
        string mask=Mask(expression); int depth=0;
        foreach(char c in mask) { if(c is '(' or '[' or '{') depth++; if(c is ')' or ']' or '}') depth--; if(c==',' && depth==0) return true; }
        return false;
    }
    public GameCodeFields? Fields(GameCodeBlock block)
    {
        try { Editable(block); } catch(InvalidDataException) { return null; }
        int token=Token(block); string code=Source[token..block.End];
        if(block.Kind is "if" or "while" or "for" or "switch" or "do")
        {
            var span=Header(block); return new(Source[span.Start..span.End].Trim(),"","",false,false,block.Kind=="for" ? "For header: initializer; condition; step. Keep declarations outside the loop header for IDO." : "Edit this condition or switch expression. The body stays unchanged.");
        }
        if(block.Kind=="assignment")
        {
            var match=Assignment.Match(code);
            if(match.Success && !code.Contains("/*") && !code.Contains("//"))
            {
                try { SourceBlocks.Generate(new[]{new SourceCodeBlock { Kind=SourceBlockKind.Set,First=match.Groups["left"].Value.Trim(),Second=match.Groups["right"].Value.Trim() }}); }
                catch(InvalidDataException) { return null; }
                return new(match.Groups["left"].Value.Trim(),match.Groups["right"].Value.Trim(),"",true,false,"Edit the destination and value. The existing assignment operator is retained.");
            }
        }
        if(block.Kind=="call")
        {
            var call=CSourceCalls.Read(code,_=>true).FirstOrDefault();
            if(call!=null && call.Start==0 && code[call.End..].Trim()==";")
            {
                int open=code.IndexOf('('); return new(call.Name,code[(open+1)..(call.End-1)],"",true,false,"Function name and comma-separated arguments. Existing code outside this call is preserved.");
            }
        }
        if(block.Kind=="declaration")
        {
            var declaration=Declaration.Match(code);
            if(declaration.Success && !TopLevelComma(declaration.Groups["value"].Value) && !code.Contains("/*") && !code.Contains("//")) return new(declaration.Groups["name"].Value,declaration.Groups["value"].Value,declaration.Groups["type"].Value,true,true,"Single scalar local declaration. Array, pointer, compound, and macro declarations stay protected.");
        }
        if(block.Kind=="flow" && Regex.IsMatch(code,@"^return\b")) return new(code[6..].Trim().TrimEnd(';').Trim(),"","",false,false,"Return expression; leave empty for return without a value.");
        return null;
    }
    private string Replace(int start,int length,string text)
    {
        string result=Source[..start]+text+Source[(start+length)..];
        if(CSourceCalls.Read(text,n=>n is "sins" or "coss").Count>0 && !Regex.IsMatch(result,"#\\s*include\\s*[<\"]engine/math_util\\.h[>\"]"))
            result="#include \"engine/math_util.h\""+(Source.Contains("\r\n") ? "\r\n" : "\n")+result;
        return result;
    }
    public string Update(GameCodeBlock block,string first,string second,string dataType)
    {
        Editable(block); var fields=Fields(block) ?? throw new InvalidDataException("This block does not have supported editable fields");
        if(fields.First==first && fields.Second==second && fields.DataType==dataType) return Source;
        int token=Token(block); string code=Source[token..block.End];
        if(block.Kind is "if" or "while" or "for" or "switch" or "do")
        {
            if(block.Kind=="for")
            {
                string mask=Mask(first); int level=0; var ends=new List<int>();
                for(int i=0;i<mask.Length;i++) { if(mask[i] is '(' or '[') level++; if(mask[i] is ')' or ']') level--; if(mask[i]==';' && level==0) ends.Add(i); }
                if(ends.Count!=2) throw new InvalidDataException("A for header needs initializer; condition; step");
                var parts=new[]{first[..ends[0]],first[(ends[0]+1)..ends[1]],first[(ends[1]+1)..]};
                foreach(string part in parts) SourceBlocks.Expression(part,true);
                if(Regex.IsMatch(parts[0].Trim(),@"^(?:int|float|double|char|short|long|s8|u8|s16|u16|s32|u32|s64|u64|f32|f64|unsigned|signed|const|volatile|static|struct|union|enum)\b|^[A-Za-z_]\w*\s+\**\s*[A-Za-z_]\w*\s*=")) throw new InvalidDataException("Declare loop variables before statements for IDO compatibility");
            }
            else SourceBlocks.Expression(first);
            var span=Header(block);
            string old=Source[span.Start..span.End]; int leading=old.Length-old.TrimStart().Length,trailing=old.Length-old.TrimEnd().Length;
            return Replace(span.Start+leading,old.Length-leading-trailing,first.Trim());
        }
        if(block.Kind=="assignment")
        {
            SourceBlocks.Generate(new[]{new SourceCodeBlock { Kind=SourceBlockKind.Set,First=first,Second=second }});
            var match=Assignment.Match(code); string result=code;
            foreach(var replacement in new[]{("right",second.Trim()),("left",first.Trim())})
            {
                var group=match.Groups[replacement.Item1]; result=result.Remove(group.Index,group.Length).Insert(group.Index,replacement.Item2);
            }
            return Replace(token,block.End-token,result);
        }
        if(block.Kind=="call")
        {
            SourceBlocks.Generate(new[]{new SourceCodeBlock { Kind=SourceBlockKind.Call,First=first,Second=second }});
            var call=CSourceCalls.Read(code,_=>true).First(); int open=code.IndexOf('(');
            string result=first+code[call.Name.Length..(open+1)]+second+code[(call.End-1)..];
            return Replace(token,block.End-token,result);
        }
        if(block.Kind=="declaration")
        {
            if(!Declaration.IsMatch(dataType+" "+first+";")) throw new InvalidDataException("Use a scalar C89 declaration type and variable name");
            SourceBlocks.Generate(new[]{new SourceCodeBlock { Kind=SourceBlockKind.Declare,First=first,Second="0",DataType="int" }});
            if(second.Trim().Length>0) SourceBlocks.Expression(second);
            if(TopLevelComma(second)) throw new InvalidDataException("Use one initializer expression; multiple declarations require source-text editing");
            return Replace(token,block.End-token,dataType+" "+first+(second.Trim().Length>0 ? " = "+second.Trim() : "")+";");
        }
        SourceBlocks.Expression(first,true);
        return Replace(token,block.End-token,"return"+(first.Trim().Length>0 ? " "+first.Trim() : "")+";");
    }
    private (int Start,int End,bool Braced) Body(GameCodeBlock block)
    {
        int start=Token(block);
        if(block.Kind is "if" or "while" or "for" or "switch") start=Header(block).End+1;
        else if(block.Kind=="else") start+=4;
        else if(block.Kind=="do") start+=2;
        else if(block.Kind!="scope") throw new InvalidDataException("Select a control or scope block for inside insertion");
        while(start<block.End && char.IsWhiteSpace(masked[start])) start++;
        if(start<block.End && masked[start]=='{')
        {
            int depth=1,end=start+1;
            for(;end<block.End;end++) { if(masked[end]=='{') depth++; if(masked[end]=='}' && --depth==0) break; }
            if(end>=block.End) throw new InvalidDataException("The body boundary is unresolved");
            return (start+1,end,true);
        }
        var child=block.Children.FirstOrDefault(c=>c.Kind!="else") ?? throw new InvalidDataException("The unbraced body is unresolved");
        return (child.Start,child.End,false);
    }
    public string Remove(GameCodeBlock block)
    {
        Editable(block); var location=Find(block); int token=Token(block);
        if(location.Parent is {} parent && parent.Kind is "if" or "else" or "while" or "for" or "do")
        {
            var body=Body(parent);
            if(!body.Braced && block.Kind!="else") return Replace(token,block.End-token,"{}");
        }
        return Replace(token,block.End-token,"");
    }
    public string Insert(GameCodeBlock? selected,IReadOnlyList<SourceCodeBlock> blocks,string placement)
    {
        if(blocks.Count==1 && blocks[0].Kind==SourceBlockKind.Else)
        {
            if(selected==null || selected.Kind!="if" || selected.Children.Any(c=>c.Kind=="else")) throw new InvalidDataException("Select an if block without an else branch first");
            Editable(selected);
            string branch=" else {\n"+SourceBlocks.Generate(blocks[0].Children)+"    }";
            return Replace(selected.End,0,Source.Contains("\r\n") ? branch.Replace("\n","\r\n") : branch);
        }
        string code=SourceBlocks.Generate(blocks); if(blocks.Count==0) throw new InvalidDataException("Add a block first");
        string newline=Source.Contains("\r\n") ? "\r\n" : "\n";
        bool declaration=blocks.Count==1 && blocks[0].Kind==SourceBlockKind.Declare;
        string snippet=("\n    {\n"+code+"    }\n").Replace("\n",newline);
        if(declaration) snippet=("\n    "+blocks[0].DataType+" "+blocks[0].First+" = "+blocks[0].Second+";\n").Replace("\n",newline);
        if(placement=="function start" || selected==null)
        {
            int position=target.BodyStart;
            foreach(var block in Blocks)
            {
                if(block.Kind=="declaration" || string.IsNullOrWhiteSpace(block.Text(Source))) position=block.End;
                else { if(block.Kind=="source") throw new InvalidDataException("Choose an explicit insertion point after local declarations; the function begins with unsupported source"); break; }
            }
            return Replace(position,0,snippet);
        }
        Editable(selected);
        if(placement=="inside selected")
        {
            var body=Body(selected);
            if(body.Braced)
            {
                int position=body.End;
                if(declaration)
                {
                    position=body.Start;
                    foreach(var child in selected.Children.Where(c=>c.Kind!="else")) { if(child.Kind=="declaration" || string.IsNullOrWhiteSpace(child.Text(Source))) position=child.End; else break; }
                }
                return Replace(position,0,snippet);
            }
            return Replace(body.Start,body.End-body.Start," {"+(declaration ? snippet : "")+Source[body.Start..body.End]+(!declaration ? snippet : "")+"    }");
        }
        if(placement is not ("before selected" or "after selected")) throw new InvalidDataException("Choose an insertion position");
        var location=Find(selected);
        if(selected.Kind=="else") throw new InvalidDataException("Insert inside an else block, or beside the whole if block");
        if(location.Parent is {} parent && parent.Kind is "if" or "else" or "while" or "for" or "do")
        {
            var body=Body(parent);
            if(!body.Braced)
            {
                if(declaration && placement=="after selected") throw new InvalidDataException("Insert local declarations before statements");
                return Replace(selected.Start,selected.Length," {"+(placement=="before selected" ? snippet : "")+selected.Text(Source)+(placement=="after selected" ? snippet : "")+"    }");
            }
        }
        var siblings=location.Siblings; int index=Enumerable.Range(0,siblings.Count).Single(i=>ReferenceEquals(siblings[i],selected));
        int insertion=placement=="before selected" ? index : index+1;
        if(declaration && siblings.Take(insertion).Any(b=>b.Kind!="declaration" && !string.IsNullOrWhiteSpace(b.Text(Source)))) throw new InvalidDataException("Insert local declarations before statements; use function start or inside selected");
        if(!declaration && siblings.Skip(insertion).Any(b=>b.Kind=="declaration")) throw new InvalidDataException("Insert executable blocks after all local declarations in this scope for IDO compatibility");
        return Replace(placement=="before selected" ? selected.Start : selected.End,0,snippet);
    }
}
