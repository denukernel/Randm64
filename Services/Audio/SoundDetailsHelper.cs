using System.IO;
namespace Randm64.Services;

public static class SoundDetailsHelper
{
    // Preserve the source filename. Numeric sample indices do not establish gameplay usage.
    public static string GetDescription(string category, string fileName) => "";
    public static string FormatDisplayName(string category, string fileName) => Path.GetFileName(fileName);
}
