namespace Sm64DecompLevelViewer.Services.Levels;
internal sealed class SplineEditSession
{
    public CutsceneService Service { get; } = new();
    public string Path { get; set; } = "";
    public string? Name { get; set; }
    public List<SplinePoint>? Points { get; set; }
    public void Save()
    {
        if (Name != null && Points != null) Service.SaveSpline(Path, Name, Points);
    }
}
