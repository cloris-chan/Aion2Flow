namespace Cloris.Aion2Flow.Capture;

internal static class CaptureBufferLimits
{
    public const int WinDivertPacketBufferSize = 16 * 1024;
    public const int StreamTailBufferSize = 64 * 1024;
    public const int ReassemblyPendingByteLimit = 64 * 1024;
    public const int ReassemblyPendingSegmentLimit = 1024;
}
