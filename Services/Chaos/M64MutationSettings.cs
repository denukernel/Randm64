using System.IO;

namespace Sm64DecompLevelViewer.Services.Chaos;

public sealed class M64MutationSettings
{
    public List<int> Channels { get; set; } = Enumerable.Range(0, 16).ToList();
    public bool ProtectPercussion { get; set; }
    public int? MaxPitchDisplacement { get; set; }
    public bool IncludeSequenceControls { get; set; } = true;
    public bool PreventVolumeLoss { get; set; } = true;

    internal void Validate()
    {
        if (Channels == null || Channels.Any(c => c < 0 || c > 15) || Channels.Distinct().Count() != Channels.Count)
            throw new InvalidDataException("M64 channels must be unique values from 0 to 15");
        if (MaxPitchDisplacement is < 0 or > 63) throw new InvalidDataException("M64 pitch limit must be 0–63 semitones");
    }
}
