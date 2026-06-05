namespace Cloris.Aion2Flow.Tests.Protocol;

public static class FixtureHelper
{
    public static string GetPath(string relativePath) => Path.Combine(AppContext.BaseDirectory, "Fixtures", relativePath.Replace('/', Path.DirectorySeparatorChar));
}
