namespace Cloris.Aion2Flow.Divert.Interop;

[Flags]
#pragma warning disable CA1711
public enum WinDivertFlags : ulong
#pragma warning restore CA1711
{
    None = 0,
    Sniff = 1 << 0,
    Drop = 1 << 1,
    ReceiveOnly = 1 << 2,
    SendOnly = 1 << 3,
    NoInstall = 1 << 4,
    Fragments = 1 << 5,
}
