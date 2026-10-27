using System.IO;
using System.Text.Json;
namespace Sm64DecompLevelViewer.Services.Chaos;
internal static class ChaosPresetStore
{
    public static ChaosPreset? Load(string path) => JsonSerializer.Deserialize<ChaosPreset>(File.ReadAllText(path));
    public static void Save(string path, ChaosPreset preset) => File.WriteAllText(path, JsonSerializer.Serialize(preset, new JsonSerializerOptions { WriteIndented = true }));
}
