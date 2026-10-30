using System.IO;

namespace Sm64DecompLevelViewer.Services;
public static class SourceEditorCatalog
{
    public static readonly string[] Modules={"mario","goddard","behaviors","camera","other game","audio","menu","engine","all source"};
    public static string Resolve(string root,string relative)
    {
        string fullRoot=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)+Path.DirectorySeparatorChar;
        if(Path.IsPathRooted(relative)) throw new InvalidDataException("Select a source file inside the project");
        string path=Path.GetFullPath(Path.Combine(fullRoot,relative));
        if(!path.StartsWith(fullRoot,StringComparison.OrdinalIgnoreCase) || Path.GetExtension(path) is not (".c" or ".h")) throw new InvalidDataException("Select a project C source or header file");
        for(var dir=new DirectoryInfo(Path.GetDirectoryName(path)!);dir!=null && dir.FullName.Length>fullRoot.Length-1;dir=dir.Parent)
            if(dir.Exists && dir.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new InvalidDataException("Source files through directory links are unsupported");
        if(File.Exists(path) && File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)) throw new InvalidDataException("Linked source files are unsupported");
        return path;
    }
    public static IReadOnlyList<string> Files(string root,string module)
    {
        var result=new List<string>();
        var options=new EnumerationOptions { RecurseSubdirectories=true,AttributesToSkip=FileAttributes.ReparsePoint,IgnoreInaccessible=false };
        string[] folders=module switch { "mario" or "camera" or "other game"=>new[]{"src/game"},"goddard"=>new[]{"src/goddard"},"behaviors"=>new[]{"src/game/behaviors","data"},"audio"=>new[]{"src/audio"},"menu"=>new[]{"src/menu"},"engine"=>new[]{"src/engine"},_=>new[]{"src","data","include","levels","actors"} };
        foreach(string folder in folders)
        {
            string directory=Path.Combine(root,folder); if(!Directory.Exists(directory)) continue;
            foreach(string path in Directory.EnumerateFiles(directory,"*",options))
            {
                if(Path.GetExtension(path) is not (".c" or ".h")) continue;
                string relative=Path.GetRelativePath(root,path).Replace('\\','/'); string name=Path.GetFileName(path);
                bool match=module switch
                {
                    "mario"=>relative.StartsWith("src/game/") && name.StartsWith("mario"),"goddard"=>relative.StartsWith("src/goddard/"),
                    "behaviors"=>relative.StartsWith("src/game/behaviors/") || relative=="data/behavior_data.c", "camera"=>relative.StartsWith("src/game/") && name.Contains("camera"),
                    "other game"=>relative.StartsWith("src/game/"),"audio"=>relative.StartsWith("src/audio/"),"menu"=>relative.StartsWith("src/menu/"),"engine"=>relative.StartsWith("src/engine/"),_=>true
                };
                if(match) result.Add(relative);
            }
        }
        return result.OrderBy(s=>s,StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
