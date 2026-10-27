using Sm64DecompLevelViewer.Models;
namespace Sm64DecompLevelViewer.Services.Levels;
internal sealed class LevelObjectEditSession
{
    public LevelObject? Clipboard { get; set; }
    public Stack<LevelObject> Removed { get; } = new();
    public LevelObject Copy(LevelObject source)
    {
                return new LevelObject
                {
                    ModelName = source.ModelName,
                    Behavior = source.Behavior,
                    X = source.X,
                    Y = source.Y,
                    Z = source.Z,
                    RX = source.RX,
                    RZ = source.RZ,
                    RY = source.RY,
                    AreaIndex = source.AreaIndex,
                    Params = source.Params,
                    RawParams = source.RawParams,
                    RawActs = source.RawActs,
                    PresetName = source.PresetName,
                    SourceType = source.SourceType
                };

    }
    public static void Prepare(LevelObject obj, List<LevelObject> objects, int area, string? levelDirectory, string collisionPath)
    {
        obj.AreaIndex = area;
        obj.SourceFile = LevelObjectDestination.ResolveDestination(objects, obj.SourceType, area, levelDirectory, collisionPath);
        obj.SourceIndex = -1;
        obj.SourceLength = 0;
    }
}
