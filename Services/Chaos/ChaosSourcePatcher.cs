using System.IO;
namespace Sm64DecompLevelViewer.Services.Chaos;
internal sealed class ChaosSourcePatcher
{
    private readonly string _projectRoot;
    public ChaosSourcePatcher(string projectRoot) { _projectRoot = projectRoot; }
        internal bool PatchSourceFile(string relativePath, string oldText, string newText)
        {
            string fullPath = Path.Combine(_projectRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath)) return false;

            string backupPath = fullPath + ".bak";
            if (!File.Exists(backupPath))
            {
                File.Copy(fullPath, backupPath);
            }

            // Read the current file content to allow consecutive edits to accumulate
            string content = File.ReadAllText(fullPath);
            content = content.Replace("\r\n", "\n");
            
            string normalizedOld = oldText.Replace("\r\n", "\n");
            string normalizedNew = newText.Replace("\r\n", "\n");

            if (content.Contains(normalizedOld) && !content.Contains(normalizedNew))
            {
                content = content.Replace(normalizedOld, normalizedNew);
                File.WriteAllText(fullPath, content);
                File.SetLastWriteTime(fullPath, DateTime.Now);
            }
            return content.Contains(normalizedNew);
        }
}
