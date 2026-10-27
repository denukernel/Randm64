namespace Sm64DecompLevelViewer.Services.Build;

internal static class BuildCommandFactory
{
    internal static int Jobs(AppSettings settings) => settings.BuildJobs > 0 ? settings.BuildJobs : Math.Max(1, Environment.ProcessorCount);

    public static string CreateMakeArguments(RepositoryBuildProfile profile, AppSettings settings)
    {
        string? error = profile.Validate(settings);
        if (error != null) throw new InvalidOperationException(error);
        var args = new List<string> { "make", "-j" + Jobs(settings) };
        void Add(string variable, string value)
        {
            if (profile.Has(variable)) args.Add(variable + "=" + Quote(value));
        }
        Add("VERSION", settings.BuildVersion);
        Add("TARGET_N64", settings.BuildTargetPlatform == "PC" ? "0" : "1");
        Add("COMPILER", settings.BuildCompiler);
        Add("GRUCODE", settings.BuildGrucode);
        Add("COMPARE", settings.BuildCompare ? "1" : "0");
        Add("NON_MATCHING", settings.BuildNonMatching ? "1" : "0");
        return string.Join(" ", args);
    }

    internal static string Quote(string value) => "'" + value.Replace("'", "'\"'\"'") + "'";
}
