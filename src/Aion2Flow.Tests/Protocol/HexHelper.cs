namespace Cloris.Aion2Flow.Tests.Protocol;

internal static class HexHelper
{
    public static byte[] Parse(string hex)
    {
        return Convert.FromHexString(hex);
    }

    public static byte[] FromFixture(string relativePath)
    {
        return FixtureHelper.LoadHex(relativePath);
    }
}
