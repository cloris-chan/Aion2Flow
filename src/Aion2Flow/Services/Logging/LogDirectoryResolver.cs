using System.Globalization;

namespace Cloris.Aion2Flow.Services.Logging;

internal static class LogDirectoryResolver
{
    private const string LogDirectoryName = "logs";
    private const string DumpLogDirectoryName = "dumps";

    public static string GetDefaultLogDirectory()
        => Path.Combine(WorkingDirectoryResolver.GetWorkingDirectory(), LogDirectoryName);

    public static string FormatLogSessionTimestamp(DateTimeOffset timestamp)
        => timestamp.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);

    public static string ResolveDumpLogRootDirectory(string logDirectory)
        => Path.Combine(logDirectory, DumpLogDirectoryName);

    public static string ResolveDumpLogDirectory(string logDirectory, DateTimeOffset timestamp)
        => Path.Combine(ResolveDumpLogRootDirectory(logDirectory), FormatLogSessionTimestamp(timestamp));

    public static string ResolveUniqueDumpLogDirectory(string logDirectory, DateTimeOffset timestamp)
    {
        var baseDirectory = ResolveDumpLogDirectory(logDirectory, timestamp);
        if (!Directory.Exists(baseDirectory))
        {
            return baseDirectory;
        }

        for (var suffix = 1; suffix <= 999; suffix++)
        {
            var candidate = string.Create(
                CultureInfo.InvariantCulture,
                $"{baseDirectory}-{suffix:00}");
            if (!Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return $"{baseDirectory}-{Guid.NewGuid():N}";
    }

    internal static string ResolveLogDirectory(string baseDirectory)
        => Path.Combine(WorkingDirectoryResolver.GetWorkingDirectory(baseDirectory), LogDirectoryName);

    internal static string ResolveLogDirectory(string baseDirectory, string? velopackRootAppDirectory)
        => Path.Combine(WorkingDirectoryResolver.GetWorkingDirectory(baseDirectory, velopackRootAppDirectory), LogDirectoryName);
}
