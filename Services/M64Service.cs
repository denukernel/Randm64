using Sm64DecompLevelViewer.Services.Audio;
namespace Sm64DecompLevelViewer.Services;

// Compatibility facade; parsing state belongs to one parser per loaded sequence.
public class M64Service
{
    private readonly M64SequenceParser _parser = new();
    public byte Tempo { get => _parser.Tempo; set => _parser.Tempo = value; }
    public M64TempoMap TempoMap => _parser.TempoMap;
    public int LoopStartTick { get => _parser.LoopStartTick; set => _parser.LoopStartTick = value; }
    public List<string> LoadWarnings => _parser.LoadWarnings;
    public int GetChannelLayerCount(byte ch) => _parser.GetChannelLayerCount(ch);
    public List<M64Track> LoadM64(string filePath) => _parser.LoadM64(filePath);
    internal List<M64Track> LoadPreview(string sequencePath, byte[] data) => _parser.LoadM64(sequencePath, data);
    public List<string> DiagnoseM64(string filePath) => M64Diagnostics.DiagnoseM64(filePath);
    public M64SaveResult SaveM64(string filePath, List<M64Track> tracks, bool isNotesModified = true) => _parser.SaveM64(filePath, tracks, isNotesModified);
    public List<M64Track> ImportMidi(string filePath) => _parser.ImportMidi(filePath);
}
