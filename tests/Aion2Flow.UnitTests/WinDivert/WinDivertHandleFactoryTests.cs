using Cloris.Aion2Flow.WinDivert;

namespace Cloris.Aion2Flow.Tests.WinDivert;

public sealed class WinDivertHandleFactoryTests
{
    [Fact]
    public void Open_WhenInitialOpenSucceeds_DoesNotRecover()
    {
        var opener = new StubOpener(new OpenAttempt((nint)42, 0));
        var recovery = new StubRecovery(WinDivertRecoveryResult.Success("unused"));

        var result = WinDivertHandleFactory.Open(
            "tcp",
            WinDivertLayer.Network,
            0,
            WinDivertFlags.Sniff,
            opener,
            recovery);

        Assert.True(result.Succeeded);
        Assert.Equal((nint)42, result.Handle);
        Assert.False(result.RecoveryAttempted);
        Assert.Equal(1, opener.CallCount);
        Assert.Equal(0, recovery.CallCount);
        Assert.Equal(WinDivertFlags.Sniff | WinDivertFlags.NoInstall, opener.OpenFlags[0]);
    }

    [Fact]
    public void Open_WhenDriverServiceIsInvalid_RecoversAndRetries()
    {
        var opener = new StubOpener(
            new OpenAttempt((nint)(-1), 193),
            new OpenAttempt((nint)84, 0));
        var recovery = new StubRecovery(WinDivertRecoveryResult.Success("started"));

        var result = WinDivertHandleFactory.Open(
            "tcp",
            WinDivertLayer.Network,
            0,
            WinDivertFlags.Sniff,
            opener,
            recovery);

        Assert.True(result.Succeeded);
        Assert.True(result.RecoveryAttempted);
        Assert.Equal(193, result.InitialError);
        Assert.Equal(2, opener.CallCount);
        Assert.Equal(1, recovery.CallCount);
        Assert.Equal(193, recovery.LastOpenError);
        Assert.All(opener.OpenFlags, flags => Assert.Equal(WinDivertFlags.Sniff | WinDivertFlags.NoInstall, flags));
    }

    [Fact]
    public void Open_WhenRecoveryAndRetryFail_ReturnsRetryError()
    {
        var opener = new StubOpener(
            new OpenAttempt((nint)(-1), 193),
            new OpenAttempt((nint)(-1), 2));
        var recovery = new StubRecovery(WinDivertRecoveryResult.Failure(5, "denied"));

        var result = WinDivertHandleFactory.Open(
            "tcp",
            WinDivertLayer.Network,
            0,
            WinDivertFlags.Sniff,
            opener,
            recovery);

        Assert.False(result.Succeeded);
        Assert.True(result.RecoveryAttempted);
        Assert.Equal(2, result.FinalError);
        Assert.Equal(2, opener.CallCount);
        Assert.Equal(1, recovery.CallCount);
        Assert.All(opener.OpenFlags, flags => Assert.Equal(WinDivertFlags.Sniff | WinDivertFlags.NoInstall, flags));
    }

    [Fact]
    public void Open_WhenAnotherProcessLoadsDriverDuringRecovery_UsesRetryHandle()
    {
        var opener = new StubOpener(
            new OpenAttempt((nint)(-1), 193),
            new OpenAttempt((nint)126, 0));
        var recovery = new StubRecovery(WinDivertRecoveryResult.Failure(1056, "concurrent load"));

        var result = WinDivertHandleFactory.Open(
            "tcp",
            WinDivertLayer.Network,
            0,
            WinDivertFlags.Sniff,
            opener,
            recovery);

        Assert.True(result.Succeeded);
        Assert.True(result.RecoveryAttempted);
        Assert.Equal((nint)126, result.Handle);
        Assert.Equal(2, opener.CallCount);
        Assert.Equal(1, recovery.CallCount);
        Assert.All(opener.OpenFlags, flags => Assert.Equal(WinDivertFlags.Sniff | WinDivertFlags.NoInstall, flags));
    }

    [Fact]
    public void Open_WhenInstallIsDisabled_DoesNotRecover()
    {
        var opener = new StubOpener(new OpenAttempt((nint)(-1), 193));
        var recovery = new StubRecovery(WinDivertRecoveryResult.Success("unused"));

        var result = WinDivertHandleFactory.Open(
            "tcp",
            WinDivertLayer.Network,
            0,
            WinDivertFlags.Sniff | WinDivertFlags.NoInstall,
            opener,
            recovery);

        Assert.False(result.Succeeded);
        Assert.False(result.RecoveryAttempted);
        Assert.Equal(193, result.FinalError);
        Assert.Equal(0, recovery.CallCount);
        Assert.Equal(WinDivertFlags.Sniff | WinDivertFlags.NoInstall, opener.OpenFlags[0]);
    }

    [Fact]
    public void Open_WhenErrorIsNotRecoverable_DoesNotRecover()
    {
        var opener = new StubOpener(new OpenAttempt((nint)(-1), 5));
        var recovery = new StubRecovery(WinDivertRecoveryResult.Success("unused"));

        var result = WinDivertHandleFactory.Open(
            "tcp",
            WinDivertLayer.Network,
            0,
            WinDivertFlags.Sniff,
            opener,
            recovery);

        Assert.False(result.Succeeded);
        Assert.False(result.RecoveryAttempted);
        Assert.Equal(5, result.FinalError);
        Assert.Equal(0, recovery.CallCount);
        Assert.Equal(WinDivertFlags.Sniff | WinDivertFlags.NoInstall, opener.OpenFlags[0]);
    }

    private readonly record struct OpenAttempt(nint Handle, int Error);

    private sealed class StubOpener(params OpenAttempt[] attempts) : IWinDivertHandleOpener
    {
        private int _nextAttempt;

        public int CallCount { get; private set; }
        public List<WinDivertFlags> OpenFlags { get; } = [];

        public nint Open(string filter, WinDivertLayer layer, short priority, WinDivertFlags flags, out int error)
        {
            var attempt = attempts[_nextAttempt++];
            CallCount++;
            OpenFlags.Add(flags);
            error = attempt.Error;
            return attempt.Handle;
        }
    }

    private sealed class StubRecovery(WinDivertRecoveryResult result) : IWinDivertDriverRecovery
    {
        public int CallCount { get; private set; }
        public int LastOpenError { get; private set; }

        public WinDivertRecoveryResult TryRecover(int openError)
        {
            CallCount++;
            LastOpenError = openError;
            return result;
        }
    }
}
