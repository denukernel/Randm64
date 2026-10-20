using System.IO;
using Sm64DecompLevelViewer.Services;

internal static class LevelSavingAudit
{
    public static void Run(string root)
    {
        int areas=0; var errors=new List<string>();
        foreach(string level in Directory.GetDirectories(Path.Combine(root,"levels")))
        {
            string directory=Path.Combine(level,"areas"); if(!Directory.Exists(directory)) continue;
            foreach(string collision in Directory.GetFiles(directory,"collision.inc.c",SearchOption.AllDirectories))
            {
                try
                {
                    var mesh=new CollisionParser().ParseCollisionFile(collision,"","") ?? throw new Exception("Collision could not be parsed");
                    var staged=CollisionSourceWriter.Stage(collision,mesh);
                    string source=System.Text.Encoding.UTF8.GetString(staged[collision]);
                    CollisionSourceValidation.Validate(source,collision);
                    areas++;
                }
                catch(Exception ex) { errors.Add(Path.GetRelativePath(root,collision)+": "+ex.Message); }
            }
        }
        foreach(string error in errors) Console.WriteLine("SAVE AUDIT FAIL: "+error);
        Console.WriteLine($"SAVE AUDIT TOTAL: {areas} areas passed, {errors.Count} failures; no real files written.");
        if(errors.Count>0) throw new Exception("Level save audit failed");
    }
}
