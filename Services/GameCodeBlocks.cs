using System.IO;
using System.Text.RegularExpressions;

namespace Sm64DecompLevelViewer.Services;

public sealed record GameCodeBlock(string Kind,string Label,int Start,int Length,IReadOnlyList<GameCodeBlock> Children)
{
    public int End => Start+Length;
    public string Text(string source)=>source.Substring(Start,Length);
}

/// <summary>A lossless source-span view. Unsupported syntax remains an opaque source block.</summary>
public static class GameCodeBlocks
{
    public static IReadOnlyList<GameCodeBlock> Read(string source,string function)
    {
        var targets=SourceBlocks.Functions(source).Where(f=>f.Name==function).ToList();
        if(targets.Count!=1) throw new InvalidDataException("Select a unique function to view its game code as blocks");
        var target=targets[0];
        return new Reader(source).Scope(target.BodyStart,target.BodyEnd,0);
    }
    private sealed class Reader
    {
        private readonly string source,mask;
        private int count;
        public Reader(string source)
        {
            this.source=source;
            mask=Regex.Replace(CSourceCalls.MaskComments(source),@"""(?:\\.|[^""\\])*""|'(?:\\.|[^'\\])*'",m=>new string(' ',m.Length));
        }
        private int Skip(int p,int end) { while(p<end && char.IsWhiteSpace(mask[p])) p++; return p; }
        private bool Word(int p,string word,int end)=>p+word.Length<=end && mask.AsSpan(p,word.Length).SequenceEqual(word) && (p+word.Length==end || !char.IsLetterOrDigit(mask[p+word.Length]) && mask[p+word.Length]!='_');
        private GameCodeBlock Block(string kind,string label,int start,int end,IReadOnlyList<GameCodeBlock>? children=null)=>new(kind,Regex.Replace(label,@"\s+"," ").Trim(),start,end-start,children ?? Array.Empty<GameCodeBlock>());
        private GameCodeBlock Raw(int start,int end,string label="C source")=>Block("source",label,start,end);
        private int Closing(int opening,int end,char left,char right)
        {
            int depth=1;
            for(int p=opening+1;p<end;p++) { if(mask[p]==left) depth++; if(mask[p]==right && --depth==0) return p; }
            return -1;
        }
        public IReadOnlyList<GameCodeBlock> Scope(int start,int end,int depth)
        {
            if(depth>64) return new[]{Raw(start,end,"Nested C source")};
            // Branches can intentionally contain unmatched braces. Keep these exact bytes together.
            int balance=0;
            for(int i=start;i<end;i++) { if(mask[i]=='{') balance++; if(mask[i]=='}') balance--; }
            if(balance!=0) return new[]{Raw(start,end,"Conditional or unmatched C source")};
            var blocks=new List<GameCodeBlock>(); int position=start;
            while(position<end)
            {
                if(++count>5000) { blocks.Add(Raw(position,end,"Remaining C source")); break; }
                var block=One(position,end,depth);
                if(block.End<=position || block.End>end) { blocks.Add(Raw(position,end)); break; }
                blocks.Add(block); position=block.End;
            }
            return blocks;
        }
        private GameCodeBlock One(int start,int end,int depth)
        {
            if(depth>64) return Raw(start,end,"Nested C source");
            int p=Skip(start,end); if(p>=end) return Raw(start,end,"Comments / whitespace");
            if(mask[p]=='#')
            {
                int lineEnd=source.IndexOf('\n',p); if(lineEnd<0 || lineEnd>=end) lineEnd=end; else lineEnd++;
                var directive=Regex.Match(mask[p..lineEnd],@"^#\s*(\w+)");
                if(directive.Success && directive.Groups[1].Value is "if" or "ifdef" or "ifndef")
                {
                    int nesting=1;
                    foreach(Match next in Regex.Matches(mask[lineEnd..end],@"(?m)^[ \t]*#\s*(if|ifdef|ifndef|endif)\b[^\r\n]*(?:\r?\n|$)"))
                    {
                        nesting+=next.Groups[1].Value=="endif" ? -1 : 1;
                        if(nesting==0) return Raw(start,lineEnd+next.Index+next.Length,"Regional / conditional C source");
                    }
                    return Raw(start,end,"Conditional C source");
                }
                return Raw(start,lineEnd,"Preprocessor source");
            }
            if(mask[p]=='{')
            {
                int close=Closing(p,end,'{','}');
                return close<0 ? Raw(start,end) : Block("scope","scope",start,close+1,Scope(p+1,close,depth+1));
            }
            foreach(string keyword in new[]{"if","while","for","switch"})
            {
                if(!Word(p,keyword,end)) continue;
                int opening=Skip(p+keyword.Length,end);
                if(opening>=end || mask[opening]!='(') return Raw(start,end);
                int close=Closing(opening,end,'(',')'); if(close<0) return Raw(start,end);
                var body=One(close+1,end,depth+1);
                var children=body.Kind=="scope" ? body.Children.ToList() : new List<GameCodeBlock>{body};
                int finish=body.End;
                if(keyword=="if")
                {
                    int otherwise=Skip(finish,end);
                    if(Word(otherwise,"else",end))
                    {
                        var alternative=One(otherwise+4,end,depth+1);
                        children.Add(Block("else","else",finish,alternative.End,alternative.Kind=="scope" ? alternative.Children : new[]{alternative})); finish=alternative.End;
                    }
                }
                return Block(keyword,source[p..(close+1)],start,finish,children);
            }
            if(Word(p,"do",end))
            {
                var body=One(p+2,end,depth+1); int next=Skip(body.End,end);
                if(Word(next,"while",end))
                {
                    int opening=Skip(next+5,end),close=opening<end && mask[opening]=='(' ? Closing(opening,end,'(',')') : -1;
                    int semi=close<0 ? end : Skip(close+1,end);
                    if(semi<end && mask[semi]==';') return Block("do","do "+source[next..(close+1)],start,semi+1,body.Kind=="scope" ? body.Children : new[]{body});
                }
                return Raw(start,body.End,"do / C source");
            }
            if(Word(p,"case",end) || Word(p,"default",end))
            {
                int colon=mask.IndexOf(':',p,end-p);
                if(colon>=0) return Block("case",source[p..(colon+1)],start,colon+1);
            }
            int parens=0,brackets=0,braces=0;
            for(int i=p;i<end;i++)
            {
                switch(mask[i]) { case '(': parens++; break; case ')': parens--; break; case '[': brackets++; break; case ']': brackets--; break; case '{': braces++; break; case '}': braces--; break; }
                if(parens<0 || brackets<0 || braces<0) return Raw(start,end);
                if(mask[i]!=';' || parens!=0 || brackets!=0 || braces!=0) continue;
                string text=source[p..(i+1)],code=mask[p..(i+1)].Trim();
                string kind=Regex.IsMatch(code,@"^(return|break|continue|goto)\b") ? "flow" : Regex.IsMatch(code,@"^(?:(?:static|const|volatile|unsigned|signed)\s+)*(?:int|float|double|char|short|long|s8|u8|s16|u16|s32|u32|s64|u64|f32|f64|struct\s+\w+|Vec\w+)\b") ? "declaration" : Regex.IsMatch(code,@"(?<![=!<>])(?:\+=|-=|\*=|/=|%=|&=|\|=|\^=|=(?!=))|\+\+|--") ? "assignment" : Regex.IsMatch(code,@"^[A-Za-z_]\w*\s*\(") ? "call" : "source";
                if(code.Contains('#')) kind="source";
                return Block(kind,text,start,i+1);
            }
            return Raw(start,end);
        }
    }
}
