using Cloris.Aion2Flow.WinDivert;

namespace Cloris.Aion2Flow.Tests.WinDivert;

public sealed class WinDivertDriverRecoveryTests
{
    [Theory]
    [InlineData("Aion2FlowWinDivert22_00000001_00000002", 1)]
    [InlineData("Aion2FlowWinDivert22_00003039_00000002", 12_345)]
    [InlineData("Aion2FlowWinDivert22_7FFFFFFF_00000002", int.MaxValue)]
    public void TryGetRecoveryServiceOwnerProcessId_ParsesOwnerProcessId(string serviceName, int expectedProcessId)
    {
        Assert.True(WinDivertDriverRecovery.TryGetRecoveryServiceOwnerProcessId(serviceName, out var processId));
        Assert.Equal(expectedProcessId, processId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("WinDivert")]
    [InlineData("Aion2FlowWinDivert22")]
    [InlineData("Aion2FlowWinDivert22_00000000_00000002")]
    [InlineData("Aion2FlowWinDivert22_80000000_00000002")]
    [InlineData("Aion2FlowWinDivert22_0000000G_00000002")]
    [InlineData("Aion2FlowWinDivert22_00000001X00000002")]
    public void TryGetRecoveryServiceOwnerProcessId_RejectsInvalidNames(string serviceName)
    {
        Assert.False(WinDivertDriverRecovery.TryGetRecoveryServiceOwnerProcessId(serviceName, out var processId));
        Assert.Equal(0, processId);
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
}
