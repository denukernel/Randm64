using System.IO;
using Sm64DecompLevelViewer.Models;

namespace Sm64DecompLevelViewer.Services;

public static class LevelObjectDestination
{
    public static string ResolveDestination(IEnumerable<LevelObject> objects, ObjectSourceType type,
        int area, string? levelDirectory, string? collisionPath)
    {
        if (type == ObjectSourceType.Mario) throw new InvalidDataException("a Mario start cannot be duplicated");
        string? path = null;
        if (type == ObjectSourceType.Special) path = collisionPath;
        if (type == ObjectSourceType.Normal && levelDirectory != null) path = Path.Combine(levelDirectory, "script.c");
        if (type == ObjectSourceType.Macro && levelDirectory != null) path = Path.Combine(levelDirectory, "areas", area.ToString(), "macro.inc.c");
        if (!File.Exists(path))
            path = objects.FirstOrDefault(o => o.SourceType == type && (o.AreaIndex == area || o.AreaIndex < 0) && File.Exists(o.SourceFile))?.SourceFile;
        if (!File.Exists(path)) throw new InvalidDataException("no source section exists for this object type in the selected area; add its source section before placing objects");
        // Check the destination before adding an object to the live scene.
        var probe = new LevelObject { SourceType = type, AreaIndex = area };
        string source = File.ReadAllText(path!);
        ObjectMacroWriter.InsertionPoint(source, CSourceCalls.Read(source, LevelSaver.ObjectCall), probe);
        return path!;
    }
}
