using System.Runtime.InteropServices;
using Cloris.Aion2Flow.WinDivert.Interop;

namespace Cloris.Aion2Flow.WinDivert;

public sealed class WinDivertSession : IDisposable
{
    private const int ERROR_INVALID_HANDLE = 6;
    private const int ERROR_INSUFFICIENT_BUFFER = 122;
    private const int ERROR_NO_DATA = 232;
    private const int ERROR_OPERATION_ABORTED = 995;

    private readonly WinDivertSafeHandle _handle;
    private readonly long _sessionId;
    private volatile bool _receiveShutdownRequested;
    private bool _disposed;
    private static long _nextSessionId;

    public WinDivertSession(string filter, WinDivertLayer layer, WinDivertFlags flags, short priority = 0)
    {
        _sessionId = Interlocked.Increment(ref _nextSessionId);
        WinDivertLog.Write(WinDivertLogLevel.Info, $"WinDivert session {_sessionId} opening: layer={layer}, priority={priority}, flags={flags}, filter='{filter}'.");

        WinDivertOpenResult result;
        try
        {
            result = WinDivertHandleFactory.Open(filter, layer, priority, flags);
        }
        catch (Exception ex)
        {
            WinDivertLog.Write(WinDivertLogLevel.Error, $"WinDivert session {_sessionId} native load failed: {ex}");
            throw;
        }

        _handle = new WinDivertSafeHandle(result.Handle);
        if (!result.Succeeded)
        {
            var detail = result.RecoveryAttempted
                ? result.RetryError != 0
                    ? $" initialError={WinDivertDriverRecovery.FormatError(result.InitialError)}, recovery='{result.Recovery.Detail}', retryError={WinDivertDriverRecovery.FormatError(result.RetryError)}"
                    : $" initialError={WinDivertDriverRecovery.FormatError(result.InitialError)}, recovery='{result.Recovery.Detail}'"
                : $" error={WinDivertDriverRecovery.FormatError(result.InitialError)}";
            WinDivertLog.Write(WinDivertLogLevel.Error, $"WinDivert session {_sessionId} failed to open:{detail}.");
            throw new IOException($"Failed to open WinDivert. LastError: {result.FinalError}. {result.Recovery.Detail}");
        }

        if (result.RecoveryAttempted)
            WinDivertLog.Write(WinDivertLogLevel.Info, $"WinDivert session {_sessionId} opened after isolated service recovery. InitialError={result.InitialError}.");
        else
            WinDivertLog.Write(WinDivertLogLevel.Info, $"WinDivert session {_sessionId} opened successfully.");
    }

    public int Receive(Span<byte> buffer, ref WinDivertAddress address)
    {
        if (WinDivertInterop.WinDivertRecv(_handle, buffer, out uint readLen, ref address))
            return (int)readLen;

        int error = Marshal.GetLastPInvokeError();

        if (_handle.IsClosed || _handle.IsInvalid ||
            error == ERROR_OPERATION_ABORTED || error == ERROR_INVALID_HANDLE ||
            (error == ERROR_NO_DATA && _receiveShutdownRequested))
        {
            throw new OperationCanceledException("WinDivert session closed.");
        }

        if (error == ERROR_INSUFFICIENT_BUFFER)
        {
            throw new InternalBufferOverflowException($"Packet too large for buffer ({buffer.Length} bytes). Packet lost.");
        }

        WinDivertLog.Write(WinDivertLogLevel.Error, $"WinDivert session {_sessionId} receive failed: {WinDivertDriverRecovery.FormatError(error)}.");
        throw new IOException($"WinDivertRecv failed with error: {error}");
    }

    public void ShutdownReceive()
    {
        if (_disposed || _handle.IsClosed || _handle.IsInvalid)
            return;

        _receiveShutdownRequested = true;

        if (WinDivertInterop.WinDivertShutdown(_handle, WinDivertShutdownMode.Receive))
        {
            WinDivertLog.Write(WinDivertLogLevel.Debug, $"WinDivert session {_sessionId} receive shutdown requested.");
            return;
        }

        var error = Marshal.GetLastPInvokeError();
        if (error is ERROR_INVALID_HANDLE or ERROR_OPERATION_ABORTED)
            return;

        WinDivertLog.Write(WinDivertLogLevel.Warning, $"WinDivert session {_sessionId} receive shutdown failed: {WinDivertDriverRecovery.FormatError(error)}.");
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        if (disposing)
        {
            _handle.Dispose();
            WinDivertLog.Write(WinDivertLogLevel.Info, $"WinDivert session {_sessionId} closed.");
        }

        _disposed = true;
    }
}
