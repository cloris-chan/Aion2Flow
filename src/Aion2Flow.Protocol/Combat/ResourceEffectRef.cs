using System.Buffers.Binary;

namespace Cloris.Aion2Flow.Protocol.Combat;

public readonly record struct ResourceEffectRef(uint RawId)
{
    public bool IsEmpty => RawId == 0;

    public static ResourceEffectRef FromRaw(uint rawId) => rawId == 0 ? default : new ResourceEffectRef(rawId);

    public static ResourceEffectRef FromRaw(int rawId) => rawId == 0 ? default : new ResourceEffectRef(unchecked((uint)rawId));

    public static ResourceEffectRef FromDetail(ReadOnlySpan<byte> detail)
    {
        if (detail.Length < sizeof(uint))
            return default;

        return FromRaw(BinaryPrimitives.ReadUInt32LittleEndian(detail));
    }

    public override string ToString() => RawId.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
