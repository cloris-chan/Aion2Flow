namespace Cloris.Aion2Flow.PacketCapture.Protocol;

public enum Packet4036Kind : byte
{
    Unknown,
    Create177,
    Create198,
    State97,
    State113,
    State120,
    State137,
    State152
}

public enum Packet4036LayoutKind : byte
{
    Unknown,
    State97Main0C2000,
    State97Main0D2000,
    State97Variant0D2000,
    State97Variant0F2000,
    State97Outlier852100,
    State113Main0D2000,
    State120Main852100,
    State120Main0C2000,
    State120Wide0C2000,
    State120Wide852100,
    State137Main0F2000,
    State137Main0C2200,
    State137Main0C2000,
    State137Variant0C3000,
    State137Variant0D2000,
    State137Main072000,
    State137Wide0C2200,
    State152Main852100,
    State152Main1F1000,
    State152Wide1F1000
}

internal static class Packet4036Descriptors
{
    public static Packet4036Kind ClassifyKind(int payloadLength)
    {
        return payloadLength switch
        {
            >= 190 => Packet4036Kind.Create198,
            >= 175 => Packet4036Kind.Create177,
            >= 150 => Packet4036Kind.State152,
            >= 135 => Packet4036Kind.State137,
            >= 118 => Packet4036Kind.State120,
            >= 110 => Packet4036Kind.State113,
            >= 95 => Packet4036Kind.State97,
            _ => Packet4036Kind.Unknown
        };
    }

    public static bool IsCreateKind(Packet4036Kind kind)
        => kind is Packet4036Kind.Create177 or Packet4036Kind.Create198;

    public static Packet4036LayoutKind ClassifyLayout(Packet4036Kind kind, int bodyLength, byte mode0, byte mode1, byte mode2)
    {
        return (kind, bodyLength, mode0, mode1, mode2) switch
        {
            (Packet4036Kind.State97, 92, 0x0C, 0x20, 0x00) => Packet4036LayoutKind.State97Main0C2000,
            (Packet4036Kind.State97, 93, 0x0C, 0x20, 0x00) => Packet4036LayoutKind.State97Main0C2000,
            (Packet4036Kind.State97, 93, 0x0D, 0x20, 0x00) => Packet4036LayoutKind.State97Main0D2000,
            (Packet4036Kind.State97, 94, 0x0D, 0x20, 0x00) => Packet4036LayoutKind.State97Variant0D2000,
            (Packet4036Kind.State97, 94, 0x0F, 0x20, 0x00) => Packet4036LayoutKind.State97Variant0F2000,
            (Packet4036Kind.State97, 102, 0x85, 0x21, 0x00) => Packet4036LayoutKind.State97Outlier852100,
            (Packet4036Kind.State113, 105, 0x0D, 0x20, 0x00) => Packet4036LayoutKind.State113Main0D2000,
            (Packet4036Kind.State120, 114, 0x85, 0x21, 0x00) => Packet4036LayoutKind.State120Main852100,
            (Packet4036Kind.State120, 114, 0x0C, 0x20, 0x00) => Packet4036LayoutKind.State120Main0C2000,
            (Packet4036Kind.State120, 126, 0x0C, 0x20, 0x00) => Packet4036LayoutKind.State120Wide0C2000,
            (Packet4036Kind.State120, 126, 0x85, 0x21, 0x00) => Packet4036LayoutKind.State120Wide852100,
            (Packet4036Kind.State137, 128, 0x0F, 0x20, 0x00) => Packet4036LayoutKind.State137Main0F2000,
            (Packet4036Kind.State137, 130, 0x0C, 0x22, 0x00) => Packet4036LayoutKind.State137Main0C2200,
            (Packet4036Kind.State137, 130, 0x0C, 0x20, 0x00) => Packet4036LayoutKind.State137Main0C2000,
            (Packet4036Kind.State137, 130, 0x0C, 0x30, 0x00) => Packet4036LayoutKind.State137Variant0C3000,
            (Packet4036Kind.State137, 131, 0x0D, 0x20, 0x00) => Packet4036LayoutKind.State137Variant0D2000,
            (Packet4036Kind.State137, 132, 0x07, 0x20, 0x00) => Packet4036LayoutKind.State137Main072000,
            (Packet4036Kind.State137, 142, 0x0C, 0x22, 0x00) => Packet4036LayoutKind.State137Wide0C2200,
            (Packet4036Kind.State152, 143, 0x85, 0x21, 0x00) => Packet4036LayoutKind.State152Main852100,
            (Packet4036Kind.State152, 148, 0x1F, 0x10, 0x00) => Packet4036LayoutKind.State152Main1F1000,
            (Packet4036Kind.State152, 153, 0x1F, 0x10, 0x00) => Packet4036LayoutKind.State152Wide1F1000,
            _ => Packet4036LayoutKind.Unknown
        };
    }

    public static string FormatKind(Packet4036Kind kind, int payloadLength)
    {
        return kind switch
        {
            Packet4036Kind.Create177 => "create-177",
            Packet4036Kind.Create198 => "create-198",
            Packet4036Kind.State97 => "state-97",
            Packet4036Kind.State113 => "state-113",
            Packet4036Kind.State120 => "state-120",
            Packet4036Kind.State137 => "state-137",
            Packet4036Kind.State152 => "state-152",
            _ when payloadLength > 0 => $"state-{payloadLength}",
            _ => "state-unknown"
        };
    }

    public static string FormatLayout(Packet4036Kind kind, Packet4036LayoutKind layoutKind, int payloadLength, int bodyLength, byte mode0, byte mode1, byte mode2)
    {
        return layoutKind switch
        {
            Packet4036LayoutKind.State97Main0C2000 => "state97-main-0c2000",
            Packet4036LayoutKind.State97Main0D2000 => "state97-main-0d2000",
            Packet4036LayoutKind.State97Variant0D2000 => "state97-variant-0d2000",
            Packet4036LayoutKind.State97Variant0F2000 => "state97-variant-0f2000",
            Packet4036LayoutKind.State97Outlier852100 => "state97-outlier-852100",
            Packet4036LayoutKind.State113Main0D2000 => "state113-main-0d2000",
            Packet4036LayoutKind.State120Main852100 => "state120-main-852100",
            Packet4036LayoutKind.State120Main0C2000 => "state120-main-0c2000",
            Packet4036LayoutKind.State120Wide0C2000 => "state120-wide-0c2000",
            Packet4036LayoutKind.State120Wide852100 => "state120-wide-852100",
            Packet4036LayoutKind.State137Main0F2000 => "state137-main-0f2000",
            Packet4036LayoutKind.State137Main0C2200 => "state137-main-0c2200",
            Packet4036LayoutKind.State137Main0C2000 => "state137-main-0c2000",
            Packet4036LayoutKind.State137Variant0C3000 => "state137-variant-0c3000",
            Packet4036LayoutKind.State137Variant0D2000 => "state137-variant-0d2000",
            Packet4036LayoutKind.State137Main072000 => "state137-main-072000",
            Packet4036LayoutKind.State137Wide0C2200 => "state137-wide-0c2200",
            Packet4036LayoutKind.State152Main852100 => "state152-main-852100",
            Packet4036LayoutKind.State152Main1F1000 => "state152-main-1f1000",
            Packet4036LayoutKind.State152Wide1F1000 => "state152-wide-1f1000",
            _ => $"{FormatKind(kind, payloadLength)}-body{bodyLength}-{mode0:x2}{mode1:x2}{mode2:x2}"
        };
    }
}