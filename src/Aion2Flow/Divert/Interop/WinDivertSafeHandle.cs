using Microsoft.Win32.SafeHandles;

namespace Cloris.Aion2Flow.Divert.Interop;

public class WinDivertSafeHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public WinDivertSafeHandle(nint handle, bool ownsHandle = true) : base(ownsHandle)
    {
        this.handle = handle;
    }
    protected override bool ReleaseHandle() => WinDivertInterop.WinDivertClose(handle);
}
