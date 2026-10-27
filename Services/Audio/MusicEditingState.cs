namespace Sm64DecompLevelViewer.Services.Audio;
internal sealed class MusicEditingState
{
    public HashSet<M64Note> SelectedNotes { get; } = new();
    public Stack<(List<M64Track> Tracks, byte Channel, Dictionary<byte, byte> Volumes)> UndoStack { get; } = new();
    public void Capture(List<M64Track> tracks, byte channel, Dictionary<byte, byte> volumes) =>
        UndoStack.Push((tracks.Select(M64SequenceSaver.Snapshot).ToList(), channel, new(volumes)));
}
