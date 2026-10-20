using System.Text;
using System.IO;
using System.Text.RegularExpressions;

namespace Sm64DecompLevelViewer.Services;

public sealed record CSourceArgument(int Start,int Length);
public sealed record CSourceCall(string Name,int Start,int End,List<CSourceArgument> Arguments)
{
    public string Text(string source)=>source[Start..End];
    public string Argument(string source,int index)=>CSourceCalls.MaskComments(source.Substring(Arguments[index].Start,Arguments[index].Length)).Trim();
}

/// <summary>Balanced C macro calls with source coordinates preserved through comments and nested expressions.</summary>
public static class CSourceCalls
{
    public static string MaskComments(string source)
    {
        var output=source.ToCharArray();
        bool quoted=false; char quote='\0';
        for(int i=0;i<source.Length;i++)
        {
            if(quoted) { if(source[i]=='\\') i++; else if(source[i]==quote) quoted=false; continue; }
            if(source[i]=='"'||source[i]=='\'') { quoted=true; quote=source[i]; continue; }
            if(i+1<source.Length && source[i]=='/' && (source[i+1]=='/'||source[i+1]=='*'))
            {
                bool line=source[i+1]=='/'; int start=i; i+=2;
                while(i<source.Length && (line ? source[i]!='\n' : !(i+1<source.Length && source[i]=='*' && source[i+1]=='/'))) i++;
                int end=line ? i : Math.Min(source.Length,i+2);
                for(int j=start;j<end;j++) if(output[j]!='\r'&&output[j]!='\n') output[j]=' ';
                i=end-1;
            }
        }
        return new string(output);
    }
    public static List<CSourceCall> Read(string source,Func<string,bool> include)
    {
        string masked=MaskComments(source);
        string tokens=Regex.Replace(masked,@"""(?:\\.|[^""\\])*""|'(?:\\.|[^'\\])*'",m=>new string(' ',m.Length));
        var calls=new List<CSourceCall>();
        foreach(Match token in Regex.Matches(tokens,@"\b[A-Za-z_]\w*\s*\("))
        {
            string name=Regex.Match(token.Value,@"\w+").Value;
            if(!include(name)) continue;
            int lineStart=masked.LastIndexOf('\n',token.Index)+1;
            if(masked[lineStart..token.Index].TrimStart().StartsWith('#')) continue;
            int opening=token.Index+token.Length-1,start=opening+1,depth=1; bool quoted=false; char quote='\0';
            var args=new List<CSourceArgument>();
            for(int i=start;i<masked.Length;i++)
            {
                char c=masked[i];
                if(quoted) { if(c=='\\') i++; else if(c==quote) quoted=false; continue; }
                if(c=='"'||c=='\'') { quoted=true; quote=c; continue; }
                if(c=='(') depth++;
                if(c==')') depth--;
                if(c==',' && depth==1 || depth==0)
                {
                    if(depth!=0 || i>start || args.Count>0) args.Add(new CSourceArgument(start,i-start));
                    start=i+1;
                }
                if(depth==0) { calls.Add(new CSourceCall(name,token.Index,i+1,args)); break; }
            }
        }
        return calls;
    }
    public static string ReplaceArguments(string source,CSourceCall call,Dictionary<int,string> replacements)
    {
        var result=new StringBuilder(call.Text(source));
        foreach(var replacement in replacements.OrderByDescending(p=>p.Key))
        {
            var argument=call.Arguments[replacement.Key];
            string original=source.Substring(argument.Start,argument.Length),masked=MaskComments(original);
            var token=Regex.Match(masked,@"\S[\s\S]*\S|\S");
            if(!token.Success) throw new InvalidDataException("Empty argument in "+call.Name);
            int offset=argument.Start-call.Start+token.Index;
            result.Remove(offset,token.Length).Insert(offset,replacement.Value);
        }
        return result.ToString();
    }
}
