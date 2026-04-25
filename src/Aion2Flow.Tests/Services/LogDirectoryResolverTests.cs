using Cloris.Aion2Flow.Services.Logging;

namespace Cloris.Aion2Flow.Tests.Services;

public sealed class LogDirectoryResolverTests
{
    [Fact]
    public void ResolveLogDirectory_Uses_Velopack_Root_For_Current_Directory_When_Installed()
    {
        var root = Path.Combine(Path.GetTempPath(), $"aion2flow-log-test-{Guid.NewGuid():N}");
        var current = Path.Combine(root, "current");

        try
        {
            Directory.CreateDirectory(current);

            var logDirectory = LogDirectoryResolver.ResolveLogDirectory(current, root);

            Assert.Equal(Path.Combine(root, "logs"), logDirectory);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void ResolveLogDirectory_Uses_Velopack_Root_For_App_Version_Directory_When_Installed()
    {
        var root = Path.Combine(Path.GetTempPath(), $"aion2flow-log-test-{Guid.NewGuid():N}");
        var versionDirectory = Path.Combine(root, "app-1.2.3");

        try
        {
            Directory.CreateDirectory(Path.Combine(root, "current"));
            Directory.CreateDirectory(versionDirectory);

            var logDirectory = LogDirectoryResolver.ResolveLogDirectory(versionDirectory, root);

            Assert.Equal(Path.Combine(root, "logs"), logDirectory);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void ResolveLogDirectory_Uses_Executable_Directory_For_Development_Build()
    {
        var root = Path.Combine(Path.GetTempPath(), $"aion2flow-log-test-{Guid.NewGuid():N}");
        var appDirectory = Path.Combine(root, "bin", "Debug", "net10.0-windows");

        try
        {
            Directory.CreateDirectory(appDirectory);

            var logDirectory = LogDirectoryResolver.ResolveLogDirectory(appDirectory);

            Assert.Equal(Path.Combine(appDirectory, "logs"), logDirectory);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void ResolveDumpLogDirectory_Places_Dump_Files_In_Dumps_Timestamp_Subdirectory()
    {
        var logDirectory = Path.Combine("portable-root", "logs");
        var timestamp = new DateTimeOffset(2026, 4, 25, 13, 45, 36, TimeSpan.Zero);

        var dumpDirectory = LogDirectoryResolver.ResolveDumpLogDirectory(logDirectory, timestamp);

        Assert.Equal(Path.Combine(logDirectory, "dumps", "20260425134536"), dumpDirectory);
        Assert.Equal("20260425134536", LogDirectoryResolver.FormatLogSessionTimestamp(timestamp));
    }

    [Fact]
    public void ResolveUniqueDumpLogDirectory_Adds_Suffix_When_Timestamp_Directory_Exists()
    {
        var root = Path.Combine(Path.GetTempPath(), $"aion2flow-log-test-{Guid.NewGuid():N}");
        var logDirectory = Path.Combine(root, "logs");
        var timestamp = new DateTimeOffset(2026, 4, 25, 13, 45, 36, TimeSpan.Zero);

        try
        {
            Directory.CreateDirectory(LogDirectoryResolver.ResolveDumpLogDirectory(logDirectory, timestamp));

            var dumpDirectory = LogDirectoryResolver.ResolveUniqueDumpLogDirectory(logDirectory, timestamp);

            Assert.Equal(Path.Combine(logDirectory, "dumps", "20260425134536-01"), dumpDirectory);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
