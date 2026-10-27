namespace Sm64DecompLevelViewer.Services.Chaos;
internal sealed class ChaosQueueState
{
    public bool IsRunning { get; set; }
    public bool IsAccumulating { get; set; }
    public HashSet<string> PreparedAssets { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> FilesModified { get; } = new();
    public Dictionary<string, string> Macros { get; } = new();
    public HashSet<int> GameLogicModes { get; } = new();
}
