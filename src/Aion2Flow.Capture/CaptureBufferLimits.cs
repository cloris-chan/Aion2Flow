namespace Cloris.Aion2Flow.Capture;

internal static class CaptureBufferLimits
{
    public const int WinDivertPacketBufferSize = 70 * 1024;
    public const int StreamTailBufferSize = 1024 * 1024;
    public const int ReassemblyPendingByteLimit = 1024 * 1024;
    public const int ReassemblyPendingSegmentLimit = 1024;
}
