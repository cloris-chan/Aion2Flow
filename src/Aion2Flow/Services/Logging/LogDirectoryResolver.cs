namespace Cloris.Aion2Flow.Services.Logging;

internal static class LogDirectoryResolver
{
    private const string LogDirectoryName = "logs";

    public static string GetDefaultLogDirectory()
        => ResolveLogDirectory(AppContext.BaseDirectory);

    internal static string ResolveLogDirectory(string baseDirectory)
        => Path.Combine(WorkingDirectoryResolver.GetWorkingDirectory(baseDirectory), LogDirectoryName);

    internal static string ResolveLogDirectory(string baseDirectory, string? velopackRootAppDirectory)
        => Path.Combine(WorkingDirectoryResolver.GetWorkingDirectory(baseDirectory, velopackRootAppDirectory), LogDirectoryName);
}
