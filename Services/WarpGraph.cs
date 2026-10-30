using System.IO;

namespace Sm64DecompLevelViewer.Services;
public sealed record WarpGraphArea(string Key,string Level,string Area,string LevelPath);
public sealed record WarpGraphConnection(WarpGraphArea Source,WarpGraphArea? Destination,WarpScriptDocument.Node Node,string Warning)
{
    public string Label => $"{Source.Level}/{Source.Area} [{Node.Id}] → {Node.DestLevel}/{Node.DestArea} [{Node.DestNode}]";
}
public sealed record WarpGraphData(List<WarpGraphArea> Areas,List<WarpGraphConnection> Connections,List<string> Errors);
public static class WarpGraph
{
    public static WarpGraphData Load(string root)
    {
        root=ProjectObjectIndex.Root(root);
        var data=new WarpGraphData(new(),new(),new());
        var documents=new Dictionary<string,(string Path,WarpScriptDocument Doc)>();
        foreach(var level in new LevelScanner().ScanLevels(root))
        {
            try
            {
                var doc=new WarpScriptDocument(Path.Combine(level.LevelPath,"script.c")); documents[level.ShortName]=(level.LevelPath,doc);
                foreach(string area in doc.Areas) data.Areas.Add(new(level.ShortName+"/"+WarpScriptDocument.NodeKey(area),level.ShortName,area,level.LevelPath));
            }
            catch(Exception ex) { data.Errors.Add(level.ShortName+": "+ex.Message); }
        }
        var definitions=WarpConnectionChecks.Levels(root);
        foreach(var pair in documents)
        {
            var nodes=pair.Value.Doc.Nodes;
            foreach(var node in nodes)
            {
                var from=data.Areas.FirstOrDefault(a=>a.Level==pair.Key && WarpScriptDocument.NodeKey(a.Area)==WarpScriptDocument.NodeKey(node.SourceArea));
                if(from==null) { data.Errors.Add(pair.Key+": warp outside a declared area"); continue; }
                string warning=""; WarpGraphArea? to=null;
                if(!definitions.TryGetValue(node.DestLevel,out string? folder)) warning="Destination level expression is unresolved";
                else if(!documents.ContainsKey(folder)) warning="Destination script is missing or unreadable";
                else
                {
                    to=data.Areas.FirstOrDefault(a=>a.Level==folder && WarpScriptDocument.NodeKey(a.Area)==WarpScriptDocument.NodeKey(node.DestArea));
                    if(to==null) warning="Destination area is missing or unresolved";
                    else
                    {
                        warning=WarpConnectionChecks.DestinationProblem(node,documents[folder].Doc.Nodes) ?? "";
                    }
                }
                if(nodes.Count(n=>WarpScriptDocument.NodeKey(n.SourceArea)==WarpScriptDocument.NodeKey(node.SourceArea) && WarpScriptDocument.NodeKey(n.Id)==WarpScriptDocument.NodeKey(node.Id))>1)
                    warning+=(warning.Length>0 ? "; " : "")+"Duplicate source warp id";
                data.Connections.Add(new(from,to,node,warning));
            }
        }
        return data;
    }
}
