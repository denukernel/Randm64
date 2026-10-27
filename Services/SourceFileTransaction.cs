using System.IO;

namespace Sm64DecompLevelViewer.Services;

public static class SourceFileTransaction
{
    public static void Commit(IReadOnlyDictionary<string,byte[]> writes,IReadOnlyDictionary<string,byte[]?> expected)
    {
        var changes=writes.ToDictionary(p=>p.Key,p=>(byte[]?)p.Value,StringComparer.OrdinalIgnoreCase);
        var versions=new Dictionary<string,byte[]?>(expected,StringComparer.OrdinalIgnoreCase);
        LevelSourceRecovery.Stage(changes,versions);
        Apply(changes,versions,true);
    }

    internal static void Apply(IReadOnlyDictionary<string,byte[]?> writes,IReadOnlyDictionary<string,byte[]?> expected,bool backups)
    {
        var temporary=new Dictionary<string,string>(); var committed=new List<string>();
        var createdDirectories=new List<string>();
        try
        {
            foreach(var item in writes)
            {
                if(!expected.ContainsKey(item.Key)) throw new ArgumentException("Missing source version: "+item.Key);
                if(item.Value==null) continue;
                string? parent=Path.GetDirectoryName(item.Key);
                var missing=new Stack<string>();
                while(!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
                {
                    missing.Push(parent);
                    parent=Path.GetDirectoryName(parent);
                }
                while(missing.Count>0)
                {
                    string directory=missing.Pop();
                    Directory.CreateDirectory(directory);
                    createdDirectories.Add(directory);
                }
                string temp=item.Key+".randm64-"+Guid.NewGuid().ToString("N")+".tmp";
                temporary[item.Key]=temp;
                using var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None);
                stream.Write(item.Value); stream.Flush(true);
            }
            foreach(var item in expected)
            {
                byte[]? actual=File.Exists(item.Key) ? File.ReadAllBytes(item.Key) : null;
                if((actual==null)!=(item.Value==null) || actual!=null && !actual.SequenceEqual(item.Value!))
                    throw new IOException("Source changed while saving: "+item.Key+". Reload before saving again.");
            }
            foreach(var item in writes)
            {
                if(backups && expected[item.Key]!=null && !LevelSourceRecovery.IsManifest(item.Key) && !File.Exists(item.Key+".bak")) File.WriteAllBytes(item.Key+".bak",expected[item.Key]!);
                if(item.Value==null) File.Delete(item.Key);
                else File.Move(temporary[item.Key],item.Key,true);
                committed.Add(item.Key);
            }
        }
        catch
        {
            foreach(string path in committed.AsEnumerable().Reverse())
            {
                if(expected[path]==null) File.Delete(path);
                else File.WriteAllBytes(path,expected[path]!);
            }
            throw;
        }
        finally
        {
            foreach(string path in temporary.Values) if(File.Exists(path)) File.Delete(path);
            foreach(string directory in createdDirectories.AsEnumerable().Reverse())
                if(Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
        }
    }
}
