using Cloris.Aion2Flow.WinDivert;

namespace Cloris.Aion2Flow.Tests.WinDivert;

public sealed class WinDivertDriverRecoveryTests
{
    [Theory]
    [InlineData("Aion2FlowWinDivert")]
    [InlineData("Aion2FlowWinDivert22")]
    [InlineData("Aion2FlowWinDivert22_00000001_00000002")]
    [InlineData("Aion2FlowWinDivert_legacy")]
    public void IsRecoveryServiceName_AcceptsAion2FlowPrefix(string serviceName)
    {
        Assert.True(WinDivertDriverRecovery.IsRecoveryServiceName(serviceName));
    }

    [Theory]
    [InlineData("")]
    [InlineData("WinDivert")]
    [InlineData("OtherAion2FlowWinDivert")]
    [InlineData("aion2flowwindivert")]
    public void IsRecoveryServiceName_RejectsNonAion2FlowPrefix(string serviceName)
    {
        Assert.False(WinDivertDriverRecovery.IsRecoveryServiceName(serviceName));
    }

    [Fact]
    public void IsAion2FlowBundledDriverPath_AcceptsBundledDriver()
    {
        var baseDirectory = Path.GetFullPath(@"C:\Apps\Aion2Flow\current");
        var driverPath = Path.Combine(baseDirectory, "WinDivert64.sys");

        Assert.True(WinDivertDriverRecovery.IsAion2FlowBundledDriverPath($@"""{driverPath}""", driverPath, baseDirectory));
    }

    [Fact]
    public void IsAion2FlowBundledDriverPath_AcceptsSysUnderAppDirectory()
    {
        var baseDirectory = Path.GetFullPath(@"C:\Apps\Aion2Flow\current");
        var driverPath = Path.Combine(baseDirectory, "WinDivert64.sys");
        var servicePath = Path.Combine(baseDirectory, "drivers", "WinDivert64.sys");

        Assert.True(WinDivertDriverRecovery.IsAion2FlowBundledDriverPath($@"\??\{servicePath}", driverPath, baseDirectory));
    }

    [Theory]
    [InlineData(@"C:\Windows\System32\drivers\WinDivert64.sys")]
    [InlineData(@"C:\Apps\Aion2FlowOther\current\WinDivert64.sys")]
    [InlineData(@"C:\Apps\Aion2Flow\current\WinDivert.dll")]
    public void IsAion2FlowBundledDriverPath_RejectsNonBundledPaths(string servicePath)
    {
        var baseDirectory = Path.GetFullPath(@"C:\Apps\Aion2Flow\current");
        var driverPath = Path.Combine(baseDirectory, "WinDivert64.sys");

        Assert.False(WinDivertDriverRecovery.IsAion2FlowBundledDriverPath(servicePath, driverPath, baseDirectory));
    }

    [Fact]
    public void ShouldDeleteStandardWinDivertService_AcceptsMissingDriverPath()
    {
        var baseDirectory = Path.GetFullPath(@"C:\Apps\Aion2Flow\current");
        var driverPath = Path.Combine(baseDirectory, "WinDivert64.sys");
        var missingPath = Path.Combine(Path.GetTempPath(), "Aion2FlowTests", Guid.NewGuid().ToString("N"), "WinDivert64.sys");

        Assert.False(File.Exists(missingPath));
        Assert.True(WinDivertDriverRecovery.ShouldDeleteStandardWinDivertService($@"\??\{missingPath}", driverPath, baseDirectory, out var reason));
        Assert.Contains("missing driver", reason);
    }

    [Fact]
    public void ShouldDeleteStandardWinDivertService_RejectsExistingExternalDriverPath()
    {
        var baseDirectory = Path.GetFullPath(@"C:\Apps\Aion2Flow\current");
        var driverPath = Path.Combine(baseDirectory, "WinDivert64.sys");
        var externalPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.sys");
        File.WriteAllText(externalPath, string.Empty);
        try
        {
            Assert.True(File.Exists(externalPath));
            Assert.False(WinDivertDriverRecovery.ShouldDeleteStandardWinDivertService(externalPath, driverPath, baseDirectory, out var reason));
            Assert.Equal(string.Empty, reason);
        }
        finally
        {
            File.Delete(externalPath);
        }
    }
}
