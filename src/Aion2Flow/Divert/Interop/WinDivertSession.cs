using System.Runtime.InteropServices;

namespace Cloris.Aion2Flow.Divert.Interop;

public sealed class WinDivertSession : IDisposable
{
    private const int ERROR_INVALID_HANDLE = 6;
    private const int ERROR_INSUFFICIENT_BUFFER = 122;
    private const int ERROR_OPERATION_ABORTED = 995;

    private readonly WinDivertSafeHandle _handle;
    private bool _disposed;

    public WinDivertSession(string filter, WinDivertLayer layer, WinDivertFlags flags, short priority = 0)
    {
        var handle = WinDivertInterop.WinDivertOpen(filter, layer, priority, flags);
        _handle = new WinDivertSafeHandle(handle);
        if (_handle.IsInvalid)
        {
            throw new IOException($"Failed to open WinDivert. LastError: {Marshal.GetLastPInvokeError()}");
        }
    }

    public int Receive(Span<byte> buffer, ref WinDivertAddress address)
    {
        if (WinDivertInterop.WinDivertRecv(_handle, buffer, out uint readLen, ref address))
            return (int)readLen;

        int error = Marshal.GetLastPInvokeError();

        if (_handle.IsClosed || _handle.IsInvalid ||
            error == ERROR_OPERATION_ABORTED || error == ERROR_INVALID_HANDLE)
        {
            throw new OperationCanceledException("WinDivert session closed.");
        }

        if (error == ERROR_INSUFFICIENT_BUFFER)
        {
            throw new InternalBufferOverflowException($"Packet too large for buffer ({buffer.Length} bytes). Packet lost.");
        }

        throw new IOException($"WinDivertRecv failed with error: {error}");
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
            _handle.Dispose();

        _disposed = true;
    }
}