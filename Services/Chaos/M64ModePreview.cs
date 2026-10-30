namespace Sm64DecompLevelViewer.Services.Chaos;

internal sealed record M64ModePreviewResult(byte[] Data, int ChangedBytes, int[] ModesWithoutTargets);

// The mode picker always auditions the checked effects, regardless of the parent
// window's separate "Alienate Audio Values" setting.
internal static class M64ModePreview
{
    internal static M64ModePreviewResult Create(byte[] source, string region, IEnumerable<int> modes,
        double intensity, int seed, M64MutationSettings settings,
        Func<byte[], string, double, Random, M64MutationSettings, int[], byte[]>? harmony = null)
    {
        int[] selected = modes.Distinct().ToArray();
        byte[] data = new M64Corruptor(new Random(seed), harmony).Mutate(source, region, selected,
            intensity, alienate: false, settings: settings);
        // Report absent targets even at zero intensity, when mutation returns early.
        var targets = M64CommandScanner.Scan(source, region);
        var percussion = targets.Where(v => v.Instrument == 127 && v.Channel >= 0).Select(v => v.Channel).ToHashSet();
        var eligible = targets.Where(v =>
            (v.Channel < 0 ? settings.IncludeSequenceControls : settings.Channels.Contains(v.Channel)) &&
            !(settings.ProtectPercussion && (v.Instrument == 127 || percussion.Contains(v.Channel)))).ToArray();
        int changed = Math.Abs(data.Length - source.Length) + source.Take(Math.Min(source.Length, data.Length)).Where((value, index) => value != data[index]).Count();
        return new(data, changed,
            selected.Where(mode => !eligible.Any(v => M64Corruptor.AppliesTo(mode, v))).ToArray());
    }
}
