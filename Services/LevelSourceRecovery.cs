using System.IO;
using System.Text.Json;
using System.Text;
using System.Text.RegularExpressions;

namespace Sm64DecompLevelViewer.Services;

/// <summary>A per-level baseline shared by imports, collision, objects, materials and mesh edits.</summary>
public static class LevelSourceRecovery
{
    public const string ManifestName=".randm64-recovery.json";
    public sealed class Baseline
    {
        public int Version { get; set; } = 1;
        public Dictionary<string,byte[]?> Files { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
    public static bool IsManifest(string path)=>Path.GetFileName(path)==ManifestName;
    public static bool IsRepairSnapshot(string path)=>Regex.IsMatch(Path.GetFileName(path), @"\.before-texture-repair-[0-9a-f]{32}\.bak$", RegexOptions.IgnoreCase);
    private static bool IsBackup(string path)=>path.EndsWith(".bak",StringComparison.OrdinalIgnoreCase);
    private static bool IsSourceBackup(string path)=>IsBackup(path) && !IsRepairSnapshot(path) && !IsBackup(path[..^4]) && !IsManifest(path[..^4]);
    public static string? LevelDirectory(string path)
    {
        for(var dir=new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(path))!);dir?.Parent!=null;dir=dir.Parent)
            if(dir.Parent.Name.Equals("levels",StringComparison.OrdinalIgnoreCase)) return dir.FullName;
        return null;
    }
    private static string SafePath(string root,string relative)
    {
        string path=Path.GetFullPath(Path.Combine(root,relative));
        if(Path.IsPathRooted(relative) || !path.StartsWith(Path.GetFullPath(root)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Recovery entry is outside its level: "+relative);
        if(File.Exists(path) && (File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0) throw new InvalidDataException("Recovery cannot follow file links: "+path);
        // Refuse link traversal as well as '..' traversal.
        for(var dir=new DirectoryInfo(Path.GetDirectoryName(path)!);dir!=null && dir.FullName.Length>=root.Length;dir=dir.Parent)
            if(dir.Exists && (dir.Attributes&FileAttributes.ReparsePoint)!=0) throw new InvalidDataException("Recovery cannot follow directory links: "+dir.FullName);
        return path;
    }
    private static Baseline Read(string path)
    {
        var baseline=JsonSerializer.Deserialize<Baseline>(File.ReadAllBytes(path)) ?? throw new InvalidDataException("Empty level recovery record.");
        if(baseline.Version!=1) throw new InvalidDataException("Unsupported level recovery version.");
        baseline.Files=new(baseline.Files,StringComparer.OrdinalIgnoreCase);
        // Older texture repairs accidentally registered their sidecar snapshots as created source files.
        // Validate the path before ignoring only those known null-baseline entries. Keep the snapshots on disk.
        foreach(var item in baseline.Files.Where(p=>p.Value==null && IsRepairSnapshot(p.Key)).ToArray())
        {
            SafePath(Path.GetDirectoryName(path)!,item.Key);
            baseline.Files.Remove(item.Key);
        }
        return baseline;
    }
    internal static void Stage(Dictionary<string,byte[]?> writes,Dictionary<string,byte[]?> expected)
    {
        foreach(var group in writes.Keys.ToArray().Where(p=>!IsManifest(p) && !IsBackup(p)).GroupBy(LevelDirectory).Where(g=>g.Key!=null))
        {
            string root=group.Key!,manifest=Path.Combine(root,ManifestName);
            byte[]? original=File.Exists(manifest) ? File.ReadAllBytes(manifest) : null;
            var baseline=original==null ? new Baseline() : Read(manifest);
            // Adopt existing first backups when upgrading an already edited level.
            if(original==null)
                foreach(string backup in Directory.GetFiles(root,"*.bak",SearchOption.AllDirectories).Where(IsSourceBackup))
                    baseline.Files[Path.GetRelativePath(root,backup[..^4])]=File.ReadAllBytes(backup);
            foreach(string path in group)
            {
                string relative=Path.GetRelativePath(root,Path.GetFullPath(path));
                SafePath(root,relative);
                if(!baseline.Files.ContainsKey(relative)) baseline.Files[relative]=expected[path];
            }
            writes[manifest]=JsonSerializer.SerializeToUtf8Bytes(baseline);
            expected[manifest]=original;
        }
    }
    public static IReadOnlyList<string> FindChoices(string root)
    {
        var manifests=Directory.GetFiles(root,ManifestName,SearchOption.AllDirectories);
        var covered=manifests.Select(p=>Path.GetDirectoryName(p)!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var choices=new List<string>(manifests);
        foreach(var group in Directory.GetFiles(root,"*.bak",SearchOption.AllDirectories).Where(IsSourceBackup).GroupBy(LevelDirectory))
        {
            if(group.Key==null) choices.AddRange(group);
            else if(!covered.Contains(group.Key)) choices.Add(group.Order(StringComparer.OrdinalIgnoreCase).First());
        }
        return choices.Order(StringComparer.OrdinalIgnoreCase).ToList();
    }
    public static string Describe(string root,string choice)
    {
        string? level=LevelDirectory(choice);
        return level==null ? Path.GetRelativePath(root,choice) : Path.GetRelativePath(root,level)+" - entire level (models, textures, collision, rooms, objects)";
    }
    private static Dictionary<string,byte[]?> PrepareRestore(string root,IEnumerable<string> selected)
    {
        root=Path.GetFullPath(root);
        var changes=new Dictionary<string,byte[]?>(StringComparer.OrdinalIgnoreCase);
        var levels=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(string choice in selected)
        {
            SafePath(root,Path.GetRelativePath(root,choice));
            if(!File.Exists(choice)) throw new FileNotFoundException("Recovery source is missing.",choice);
            if(!IsManifest(choice) && !IsSourceBackup(choice)) throw new InvalidDataException("Not a source recovery record: "+choice);
            string? level=LevelDirectory(choice);
            if(level!=null) { levels.Add(level); continue; }
            if(!choice.EndsWith(".bak",StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Not a source backup: "+choice);
            changes[choice[..^4]]=File.ReadAllBytes(choice); changes[choice]=null;
        }
        foreach(string level in levels)
        {
            string manifest=Path.Combine(level,ManifestName);
            if(File.Exists(manifest))
            {
                foreach(var item in Read(manifest).Files)
                {
                    string path=SafePath(level,item.Key);
                    if(IsManifest(path) || path.EndsWith(".bak",StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Invalid baseline target: "+path);
                    changes[path]=item.Value;
                    if(File.Exists(path+".bak")) changes[path+".bak"]=null;
                }
                changes[manifest]=null;
            }
            // Legacy saves have no created-file list. Restore all backups in the level together;
            // keep unknown assets rather than deleting files whose ownership is unavailable.
            foreach(string backup in Directory.GetFiles(level,"*.bak",SearchOption.AllDirectories).Where(IsSourceBackup))
            {
                string path=SafePath(level,Path.GetRelativePath(level,backup[..^4]));
                if(!changes.ContainsKey(path)) changes[path]=File.ReadAllBytes(backup);
                changes[backup]=null;
            }
        }
        foreach(var item in changes.Where(p=>p.Value!=null && Path.GetFileName(p.Key).Equals("collision.inc.c",StringComparison.OrdinalIgnoreCase)))
        {
            using var reader=new StreamReader(new MemoryStream(item.Value!),Encoding.UTF8,true);
            CollisionSourceValidation.Validate(ActorSourceReader.Read(item.Key,sourceText:reader.ReadToEnd()),item.Key);
        }
        return changes;
    }
    public static int ValidateRestore(string root,IEnumerable<string> selected)=>PrepareRestore(root,selected).Count(p=>p.Value!=null);
    public static int Restore(string root,IEnumerable<string> selected)
    {
        var changes=PrepareRestore(root,selected);
        if(changes.Count==0) return 0;
        var expected=changes.Keys.ToDictionary(p=>p,p=>File.Exists(p) ? File.ReadAllBytes(p) : null,StringComparer.OrdinalIgnoreCase);
        // Restore and retire recovery records in one rollback-capable operation. Never consume
        // backups on failure and never create a backup of the broken version during recovery.
        SourceFileTransaction.Apply(changes,expected,false);
        return changes.Count(p=>p.Value!=null);
    }
}
