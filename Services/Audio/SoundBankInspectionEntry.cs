using System.IO;
using System.Text.Json;

namespace Sm64DecompLevelViewer.Services;
public sealed record SoundBankInspectionEntry(int Index,string Instrument,string Range,string SamplePath,string Tuning,string Pan,string Envelope,string ReleaseRate,string Source,string Warning)
{
    public string Sample => Path.GetFileName(SamplePath);
}
public static class SequenceBankInspection
{
    public static IReadOnlyList<string> Banks(string root,string sequence)
    {
        using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"sound","sequences.json")));
        string name=Path.GetFileNameWithoutExtension(sequence);
        if(!doc.RootElement.TryGetProperty(name,out var mapping)) throw new InvalidDataException("Sequence has no exact mapping in sound/sequences.json: "+name);
        if(mapping.ValueKind==JsonValueKind.Object && mapping.TryGetProperty("banks",out var banks)) mapping=banks;
        if(mapping.ValueKind!=JsonValueKind.Array) throw new InvalidDataException("Unresolved sequence bank mapping");
        return mapping.EnumerateArray().Select(b=>b.GetString() ?? throw new InvalidDataException("Unresolved bank id")).ToArray();
    }
}
