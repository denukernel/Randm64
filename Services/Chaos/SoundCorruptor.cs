using System.IO;

namespace Sm64DecompLevelViewer.Services.Chaos;

internal sealed class SoundCorruptor
{
    private readonly Random _random;
    public SoundCorruptor(Random random) { _random = random; }
    internal void ModifyAiffFile(string filePath, IEnumerable<int> modes, bool applyPitchVariation)
    {
        byte[] original = File.ReadAllBytes(filePath);
        byte[] bytes = SfxSampleEffects.Apply(original, modes, applyPitchVariation, _random);
        ProjectFileDocument.Commit(filePath, original, bytes);
    }
    internal void CorruptBinaryFile(string filePath, double intensity, int mode)
    {
        if (!filePath.EndsWith(".aiff", StringComparison.OrdinalIgnoreCase) && !filePath.EndsWith(".aif", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("binary sample corruption requires an aiff file; music scripts use the structured corruptor");
        byte[] original = File.ReadAllBytes(filePath);
        byte[] bytes = PcmSampleFile.Corrupt(original, intensity, mode, _random);
        ProjectFileDocument.Commit(filePath, original, bytes);
    }
}
