using Velopack.Locators;

namespace Cloris.Aion2Flow.Services;

internal static class WorkingDirectoryResolver
{
    public static string GetWorkingDirectory()
        => GetWorkingDirectory(AppContext.BaseDirectory);

    public static string GetWorkingDirectory(string baseDirectory)
    {
        if (TryGetVelopackRoot(out var root))
        {
            return root;
        }

        return Path.GetFullPath(baseDirectory);
    }

    public static string GetWorkingDirectory(string baseDirectory, string? velopackRootAppDirectory)
    {
        if (!string.IsNullOrWhiteSpace(velopackRootAppDirectory))
        {
            return Path.GetFullPath(velopackRootAppDirectory);
        }

        return Path.GetFullPath(baseDirectory);
    }

    private static bool TryGetVelopackRoot(out string root)
    {
        root = string.Empty;

        try
        {
            var locator = VelopackLocator.Current;
            if (locator.CurrentlyInstalledVersion is null || string.IsNullOrWhiteSpace(locator.RootAppDir))
            {
                return false;
            }

            root = Path.GetFullPath(locator.RootAppDir);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
