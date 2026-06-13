using System.Runtime.InteropServices;
using Cloris.Aion2Flow.WinDivert.Interop;

namespace Cloris.Aion2Flow.WinDivert;

internal interface IWinDivertHandleOpener
{
    nint Open(string filter, WinDivertLayer layer, short priority, WinDivertFlags flags, out int error);
}

internal sealed class WinDivertHandleOpener : IWinDivertHandleOpener
{
    public static WinDivertHandleOpener Instance { get; } = new();

    private WinDivertHandleOpener()
    {
    }

    public nint Open(string filter, WinDivertLayer layer, short priority, WinDivertFlags flags, out int error)
    {
        var handle = WinDivertInterop.WinDivertOpen(filter, layer, priority, flags);
        error = handle is 0 or -1 ? Marshal.GetLastPInvokeError() : 0;
        return handle;
    }
}

internal static class WinDivertHandleFactory
{
    public static WinDivertOpenResult Open(string filter, WinDivertLayer layer, short priority, WinDivertFlags flags, IWinDivertHandleOpener? opener = null, IWinDivertDriverRecovery? recovery = null)
    {
        opener ??= WinDivertHandleOpener.Instance;
        recovery ??= WinDivertDriverRecovery.Instance;

        var openFlags = flags | WinDivertFlags.NoInstall;
        var handle = opener.Open(filter, layer, priority, openFlags, out var initialError);
        if (IsValid(handle))
            return new WinDivertOpenResult(handle, initialError, 0, default, false);

        if ((flags & WinDivertFlags.NoInstall) != 0 || !IsRecoverableError(initialError))
            return new WinDivertOpenResult(handle, initialError, 0, default, false);

        var recoveryResult = recovery.TryRecover(initialError);
        handle = opener.Open(filter, layer, priority, openFlags, out var retryError);
        return new WinDivertOpenResult(handle, initialError, retryError, recoveryResult, true);
    }

    internal static bool IsValid(nint handle) => handle is not 0 and not -1;

    private static bool IsRecoverableError(int error) => error is 2 or 3 or 31 or 110 or 193 or 577 or 654 or 1053 or 1058 or 1060 or 1062 or 1067 or 1072 or 1275;
}

internal readonly record struct WinDivertOpenResult(nint Handle, int InitialError, int RetryError, WinDivertRecoveryResult Recovery, bool RecoveryAttempted)
{
    public bool Succeeded => WinDivertHandleFactory.IsValid(Handle);

    public int FinalError => Succeeded switch
    {
        true => 0,
        false when RetryError != 0 => RetryError,
        false when Recovery.ErrorCode != 0 => Recovery.ErrorCode,
        _ => InitialError
    };
}
