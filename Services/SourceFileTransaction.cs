using System.IO;
using System.Runtime.ExceptionServices;
using System.Collections.Concurrent;

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

    internal static void Apply(IReadOnlyDictionary<string,byte[]?> writes,IReadOnlyDictionary<string,byte[]?> expected,bool backups,bool parallelPreparation=false)
    {
        var temporary=new Dictionary<string,string>(); var committed=new ConcurrentQueue<string>();
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
            }
            void Prepare(KeyValuePair<string,byte[]?> item)
            {
                if(item.Value==null) return;
                string temp=temporary[item.Key];
                using (var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None))
                {
                    stream.Write(item.Value); stream.Flush(true);
                }
                if (!File.ReadAllBytes(temp).SequenceEqual(item.Value))
                    throw new IOException("Staged recovery data did not match the requested contents: "+item.Key);
            }
            void CheckVersion(KeyValuePair<string,byte[]?> item)
            {
                byte[]? actual=File.Exists(item.Key) ? File.ReadAllBytes(item.Key) : null;
                if((actual==null)!=(item.Value==null) || actual!=null && !actual.SequenceEqual(item.Value!))
                    throw new IOException("Source changed while saving: "+item.Key+". Reload before saving again.");
            }
            if(parallelPreparation)
            {
                // Independent reads and temporary writes run in parallel; no live
                // file is changed until all preparation and version checks pass.
                try
                {
                    var options=new ParallelOptions { MaxDegreeOfParallelism=4 };
                    Parallel.ForEach(writes,options,Prepare);
                    Parallel.ForEach(expected,options,CheckVersion);
                }
                catch(AggregateException ex)
                {
                    ExceptionDispatchInfo.Capture(ex.Flatten().InnerExceptions[0]).Throw();
                    throw;
                }
            }
            else
            {
                foreach(var item in writes) Prepare(item);
                foreach(var item in expected) CheckVersion(item);
            }
            void CommitOne(KeyValuePair<string,byte[]?> item)
            {
                if(backups && expected[item.Key]!=null && !LevelSourceRecovery.IsManifest(item.Key) && !File.Exists(item.Key+".bak")) File.WriteAllBytes(item.Key+".bak",expected[item.Key]!);
                if(item.Value==null) File.Delete(item.Key);
                else File.Move(temporary[item.Key],item.Key,true);
                committed.Enqueue(item.Key);
            }
            if(parallelPreparation)
            {
                try { Parallel.ForEach(writes,new ParallelOptions { MaxDegreeOfParallelism=4 },CommitOne); }
                catch(AggregateException ex) { ExceptionDispatchInfo.Capture(ex.Flatten().InnerExceptions[0]).Throw(); throw; }
            }
            else foreach(var item in writes) CommitOne(item);
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
