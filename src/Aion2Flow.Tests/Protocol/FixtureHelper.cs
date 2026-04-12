using System.Text;

namespace Cloris.Aion2Flow.Tests.Protocol;

internal static class FixtureHelper
{
    public static string GetPath(string relativePath)
        => Path.Combine(AppContext.BaseDirectory, "Fixtures", relativePath.Replace('/', Path.DirectorySeparatorChar));

    public static byte[] LoadHex(string relativePath)
    {
        var fullPath = GetPath(relativePath);
        var text = File.ReadAllText(fullPath, Encoding.UTF8);
        var hex = text.Replace("\r", string.Empty)
            .Replace("\n", string.Empty)
            .Replace(" ", string.Empty)
            .Trim();

        return Convert.FromHexString(hex);
    }
}
