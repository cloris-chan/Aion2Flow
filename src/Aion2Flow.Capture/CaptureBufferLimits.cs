namespace Cloris.Aion2Flow.Capture;

internal static class CaptureBufferLimits
{
    public const int WinDivertPacketBufferSize = 70 * 1024;
    public const int StreamTailBufferSize = 1024 * 1024;
    public const int ReassemblyPendingByteLimit = 1024 * 1024;
    public const int ReassemblyPendingSegmentLimit = 1024;
    public const int CandidateStreamByteLimit = 64 * 1024;
    public const int CandidateStreamSegmentLimit = 256;
    public const int CandidateStreamCountLimit = 64;
    public const int CandidateStreamsTotalByteLimit = 1024 * 1024;
    public static readonly TimeSpan CandidateAnchorRecoveryDelay = TimeSpan.FromMilliseconds(250);
    public static readonly TimeSpan CandidateStreamLifetime = TimeSpan.FromSeconds(2);
}
