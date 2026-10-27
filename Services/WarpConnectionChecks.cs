using System.IO;

namespace Sm64DecompLevelViewer.Services;

public static class WarpConnectionChecks
{
    public static Dictionary<string,string> Levels(string root)
    {
        string file=Path.Combine(root,"include","level_defines.h");
        var result=new Dictionary<string,string>(StringComparer.Ordinal);
        if(!File.Exists(file)) return result;
        string source=File.ReadAllText(file);
        foreach(var call in CSourceCalls.Read(source,name=>name=="DEFINE_LEVEL"))
            if(call.Arguments.Count>=4) result[call.Argument(source,1)]=call.Argument(source,3);
        return result;
    }
    public static string[] Inspect(string levelDirectory,IReadOnlyList<WarpScriptDocument.Node> nodes)
    {
        string root=Directory.GetParent(Directory.GetParent(levelDirectory)!.FullName)!.FullName;
        var levels=Levels(root); var problems=new List<string>();
        var cache=new Dictionary<string,IReadOnlyList<WarpScriptDocument.Node>>();
        foreach(var node in nodes)
        {
            string label=$"area {node.SourceArea}, node {node.Id}: ";
            if(!levels.TryGetValue(node.DestLevel,out string? folder)) { problems.Add(label+"destination level could not be resolved: "+node.DestLevel); continue; }
            string target=Path.Combine(root,"levels",folder,"script.c");
            if(!File.Exists(target)) { problems.Add(label+"destination script is missing"); continue; }
            if(!cache.TryGetValue(target,out var destinations))
            {
                destinations=string.Equals(Path.GetFullPath(target),Path.GetFullPath(Path.Combine(levelDirectory,"script.c")),StringComparison.OrdinalIgnoreCase) ? nodes : new WarpScriptDocument(target).Nodes;
                cache[target]=destinations;
            }
            if(!destinations.Any(other=>WarpScriptDocument.NodeKey(other.SourceArea)==WarpScriptDocument.NodeKey(node.DestArea) && WarpScriptDocument.NodeKey(other.Id)==WarpScriptDocument.NodeKey(node.DestNode)))
                problems.Add(label+$"destination area {node.DestArea}, node {node.DestNode} was not found");
        }
        return problems.ToArray();
    }
}
