namespace Sm64DecompLevelViewer.Services.Chaos;

internal sealed record M64ModePreviewResult(byte[] Data, int ChangedBytes, int[] ModesWithoutTargets);

// The mode picker always auditions the checked effects, regardless of the parent
// window's separate "Alienate Audio Values" setting.
internal static class M64ModePreview
{
    internal static M64ModePreviewResult Create(byte[] source, string region, IEnumerable<int> modes,
        double intensity, int seed, M64MutationSettings settings)
    {
        int[] selected = modes.Distinct().ToArray();
        byte[] data = new M64Corruptor(new Random(seed)).Mutate(source, region, selected,
            intensity, alienate: false, settings: settings);
        // Report absent targets even at zero intensity, when mutation returns early.
        var targets = M64CommandScanner.Scan(source, region);
        var percussion = targets.Where(v => v.Instrument == 127 && v.Channel >= 0).Select(v => v.Channel).ToHashSet();
        var eligible = targets.Where(v =>
            (v.Channel < 0 ? settings.IncludeSequenceControls : settings.Channels.Contains(v.Channel)) &&
            !(settings.ProtectPercussion && (v.Instrument == 127 || percussion.Contains(v.Channel)))).ToArray();
        return new(data, source.Where((value, index) => value != data[index]).Count(),
            selected.Where(mode => !eligible.Any(v => M64Corruptor.AppliesTo(mode, v))).ToArray());
    }
}
