using System.Globalization;
using Velopack.Locators;

namespace Cloris.Aion2Flow.Services.Logging;

internal static class LogDirectoryResolver
{
    private const string DumpLogDirectoryName = "dumps";

    public static string GetDefaultLogDirectory()
    {
        if (TryResolveVelopackLogDirectory(out var logDirectory))
        {
            return logDirectory;
        }

        return ResolveLogDirectory(AppContext.BaseDirectory);
    }

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
    {
        var appDirectory = new DirectoryInfo(Path.GetFullPath(baseDirectory));
        return Path.Combine(appDirectory.FullName, "logs");
    }

    internal static string ResolveLogDirectory(string baseDirectory, string? velopackRootAppDirectory)
    {
        if (!string.IsNullOrWhiteSpace(velopackRootAppDirectory))
        {
            return Path.Combine(Path.GetFullPath(velopackRootAppDirectory), "logs");
        }

        return ResolveLogDirectory(baseDirectory);
    }

    private static bool TryResolveVelopackLogDirectory(out string logDirectory)
    {
        logDirectory = string.Empty;

        try
        {
            var locator = VelopackLocator.Current;
            if (locator.CurrentlyInstalledVersion is null)
            {
                return false;
            }

            logDirectory = ResolveLogDirectory(AppContext.BaseDirectory, locator.RootAppDir);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
