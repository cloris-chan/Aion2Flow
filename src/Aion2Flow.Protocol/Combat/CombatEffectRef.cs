using System.Buffers.Binary;

namespace Cloris.Aion2Flow.Protocol.Combat;

public readonly record struct CombatEffectRef(uint RawId, int ResourceSkillCode, int EffectIndex)
{
    public bool IsEmpty => RawId == 0;

    public static CombatEffectRef FromDetail(ReadOnlySpan<byte> detail)
    {
        if (detail.Length < sizeof(uint))
            return default;

        var rawId = BinaryPrimitives.ReadUInt32LittleEndian(detail);
        return rawId == 0 ? default : new CombatEffectRef(rawId, checked((int)(rawId / 100)), checked((int)(rawId % 100)));
    }
}
