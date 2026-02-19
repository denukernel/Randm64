using System.IO;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using Sm64DecompLevelViewer.Models;

namespace Sm64DecompLevelViewer.Services;

public class YamlLevelParser
{
    private readonly IDeserializer _deserializer;

    public YamlLevelParser()
    {
        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(HyphenatedNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
    }

    public LevelMetadata? ParseLevelYaml(string yamlFilePath)
    {
        try
        {
            if (!File.Exists(yamlFilePath))
            {
                Console.WriteLine($"YAML file not found: {yamlFilePath}");
                return null;
            }

            var yamlContent = File.ReadAllText(yamlFilePath);
            var levelMetadata = _deserializer.Deserialize<LevelMetadata>(yamlContent);

            if (levelMetadata != null)
            {
                levelMetadata.LevelPath = Path.GetDirectoryName(yamlFilePath) ?? string.Empty;
            }

            return levelMetadata;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error parsing YAML file {yamlFilePath}: {ex.Message}");
            return null;
        }
    }
}
