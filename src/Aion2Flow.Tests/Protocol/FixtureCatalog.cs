using Cloris.Aion2Flow.Combat.Classification;
using Cloris.Aion2Flow.PacketCapture.Protocol;

namespace Cloris.Aion2Flow.Tests.Protocol;

public static class FixtureCatalog
{
    public sealed record NicknameSample(string Path, int PlayerId, string Nickname);

    public sealed record DamageSample(string Path, int TargetId, int SourceId, int SkillCodeRaw, int Type, DamageModifiers Modifiers, int Unknown, int Damage, int Loop);

    public sealed record ObservedDamageSample(string Path, int TargetId, int SourceId, int SkillCodeRaw, int Type, int Damage);

    public sealed record Compact0438Sample(string Path, int TargetId, int SourceId, int SkillCodeRaw, int Marker, int Type, int Unknown, int Value, int Loop, int TailLength);

    public sealed record PeriodicValueSample(string Path, int TargetId, int Mode, int SourceId, int SkillCodeRaw, int LegacySkillCode, int Damage, int LinkId = 0, int TailRaw = 0, bool IsLinkRecord = false, int TailLength = 0, int TailSkillCodeRaw = 0, int TailPrefixValue = 0);

    public sealed record RemainHpSample(string Path, int NpcId, int Value0, int Value1, int Value2, uint Hp, int TailLength);

    public sealed record BattleToggleSample(string Path, int NpcId, int TailLength);

    public sealed record Create4036Sample(string Path, Packet4036Kind Kind, int OwnerId, int SummonId, int? NpcCode);
    public sealed record NpcSpawn4036Sample(string Path, Packet4036Kind Kind, int EntityId, int? NpcCode);

    public sealed record State4136Sample(string Path, int SourceId, byte State0, byte State1);

    public sealed record State2136Sample(string Path, uint Sequence, uint Value0, uint Value1, uint Value7, ushort TailMarker);
    public sealed record State0140Sample(string Path, uint Value0, ushort Value1, int TailLength);
    public sealed record State0240Sample(string Path, uint Value0, ushort Value1, int TailLength);
    public sealed record State4536Sample(string Path, int SourceId, byte Value0, int TailLength);

    public sealed record State4636Sample(string Path, int SourceId, byte State0, byte State1);

    public sealed record State1D37Sample(string Path, int SourceId, int GroupCode, int StateCode, string TailSignature);

    public sealed record Compact0238Sample(string Path, int SourceId, int Mode, int SkillCodeRaw, int Marker, int Flag, int EchoSourceId, int ZeroValue, int TailValue);
    public sealed record Compact0638Sample(string Path, int SourceId, int SkillCodeRaw, int Marker, int Flag);
    public sealed record Aux2B38Sample(string Path, int SourceId, int Phase, int Marker, int ActionCode);
    public sealed record Aux2A38Sample(string Path, int SourceId, int Mode, int GroupCode, int SequenceId, uint BuffCodeRaw);
    public sealed record Aux2C38Sample(string Path, int SourceId, int Mode, int StateCode, int SequenceId, int ResultCode);
    public sealed record State4936Sample(string Path, int SourceId, int Mode, int GroupCode, int Flag, uint Value0, ushort Marker, uint Value1);

    public sealed record State4036Sample(
        string Path,
        Packet4036Kind Kind,
        Packet4036LayoutKind LayoutKind,
        int SourceId,
        byte Mode0,
        byte Mode1,
        byte Mode2,
        int LinkedValue,
        uint Gauge0,
        uint Gauge1,
        byte TailMode,
        byte TailState,
        byte TailFlag0,
        byte TailFlag1,
        uint TailValue,
        uint SharedTag,
        uint SharedGauge0,
        uint SharedGauge1,
        uint SharedGauge2,
        uint SharedGauge3,
        uint SharedFlag,
        uint SharedMini0,
        uint SharedMini1,
        uint HeavyGauge0,
        uint HeavyGauge1,
        uint HeavyValue0,
        uint HeavyValue1,
        uint HeavyFlag,
        uint HeavyMini0,
        uint HeavySentinel0,
        uint HeavySentinel1,
        uint HeavyTrailer0,
        uint HeavyTrailer1);
    public sealed record Wrapped8456Sample(string Path, byte Prefix0, byte Prefix1, byte Prefix2, ushort InnerOpcode, uint InnerValue, byte Trailer);

    public static IEnumerable<object[]> OwnNicknameSamples()
    {
        yield return [new NicknameSample("nickname/3336-own-thanks.hex", 190, "謝謝惠顧")];
    }

    public static IEnumerable<object[]> OtherNicknameSamples()
    {
        yield return [new NicknameSample("nickname/4436-apogee.hex", 9417, "Apogee")];
        yield return [new NicknameSample("nickname/4436-flower-water-flower.hex", 6017, "花氵花")];
        yield return [new NicknameSample("nickname/4436-single-han.hex", 11145, "溾")];
    }

    public static IEnumerable<object[]> RosterNicknameSamples()
    {
        yield return [new NicknameSample("nickname/0994-single-han.hex", 11145, "溾")];
        yield return [new NicknameSample("nickname/0b94-single-han.hex", 11145, "溾")];
    }

    public static IEnumerable<object[]> DamageSamples()
    {
        yield return [new DamageSample("combat/0438-damage.hex", 16215, 16215, 17410040, 2, DamageModifiers.None, 16963, 4100, 2)];
    }

    public static IEnumerable<object[]> ObservedDamageSamples()
    {
        yield return [new ObservedDamageSample("combat/0438-summon-hit.hex", 17640, 18345, 17150342, 3, 4609)];
        yield return [new ObservedDamageSample("combat/0438-summon-hit-20623.hex", 17640, 20623, 17150342, 3, 4609)];
    }

    public static IEnumerable<object[]> Compact0438Samples()
    {
        yield return [new Compact0438Sample("combat/0438-compact-self.hex", 12450, 12450, 17750010, 224, 2, 12444, 13516, 1, 7)];
        yield return [new Compact0438Sample("combat/0438-compact-other.hex", 11696, 9092, 18190021, 206, 2, 1761919, 108, 1, 7)];
    }

    public static IEnumerable<object[]> PeriodicValueSamples()
    {
        yield return [new PeriodicValueSample("combat/0538-dot.hex", 17640, 2, 1724, 1707024011, 17070240, 1713)];
        yield return [new PeriodicValueSample("combat/0538-hot-initial.hex", 12115, 1, 12115, 1709125011, 17091250, 4747)];
        yield return [new PeriodicValueSample("combat/0538-hot-tick.hex", 12115, 3, 12115, 1709125011, 17091250, 4273, TailLength: 2)];
        yield return [new PeriodicValueSample("combat/0538-dot-tick-17080240.hex", 22090, 2, 12115, 1708024011, 17080240, 1117)];
        yield return [new PeriodicValueSample("combat/0538-summon-self-tick.hex", 18345, 2, 18345, 1715000611, 17150006, 1621)];
        yield return [new PeriodicValueSample("combat/0538-summon-self-terminal.hex", 18029, 2, 18029, 1715000611, 17150006, 1434)];
        yield return [new PeriodicValueSample("combat/0538-mode48-link.hex", 16047, 48, 16047, 2001, 20, 29240, 29240, 1237540, true, 4, 1237540)];
    }

    public static IEnumerable<object[]> RemainHpSamples()
    {
        yield return [new RemainHpSample("combat/008d-remain-hp.hex", 16215, 1, 1, 2, 157000, 0)];
        yield return [new RemainHpSample("combat/008d-summon-hp-zero.hex", 18029, 2, 1, 0, 0, 0)];
    }

    public static IEnumerable<object[]> BattleToggleSamples()
    {
        yield return [new BattleToggleSample("combat/218d-battle-toggle.hex", 17640, 2)];
    }

    public static IEnumerable<object[]> Create4036Samples()
    {
        yield return [new Create4036Sample("protocol/4036-create-198.hex", Packet4036Kind.Create198, 1182, 27203, null)];
        yield return [new Create4036Sample("protocol/4036-create-198-summon-skill.hex", Packet4036Kind.Create198, 12115, 18345, null)];
        yield return [new Create4036Sample("protocol/4036-create-198-summon-skill-20623.hex", Packet4036Kind.Create198, 2855, 20623, null)];
    }

    public static IEnumerable<object[]> NpcSpawn4036Samples()
    {
        yield return [new NpcSpawn4036Sample("state/4036-state-97.hex", Packet4036Kind.State97, 21258, 2701954)];
        yield return [new NpcSpawn4036Sample("state/4036-state-120-852100.hex", Packet4036Kind.State120, 19973, 2910001)];
        yield return [new NpcSpawn4036Sample("state/4036-state-137-npc-2400032.hex", Packet4036Kind.State137, 29994, 2400032)];
        yield return [new NpcSpawn4036Sample("state/4036-state-152-852100.hex", Packet4036Kind.State152, 191528, 2311317)];
        yield return [new NpcSpawn4036Sample("protocol/4036-create-198.hex", Packet4036Kind.Create198, 27203, null)];
        yield return [new NpcSpawn4036Sample("protocol/4036-create-198-summon-skill.hex", Packet4036Kind.Create198, 18345, null)];
        yield return [new NpcSpawn4036Sample("state/4036-create-198-boss-2702396.hex", Packet4036Kind.Create198, 21544, 2702396)];
    }

    public static IEnumerable<object[]> State4136Samples()
    {
        yield return [new State4136Sample("state/4136-state.hex", 21258, 0, 3)];
        yield return [new State4136Sample("state/4136-summon-despawn.hex", 32407, 0, 3)];
        yield return [new State4136Sample("state/4136-summon-despawn-22130.hex", 22130, 0, 3)];
    }

    public static IEnumerable<object[]> State2136Samples()
    {
        yield return [new State2136Sample("state/2136-boss-scene-200003.hex", 6, 200003, 7602133, 2, 79)];
        yield return [new State2136Sample("state/2136-boss-scene-1010.hex", 7, 1010, 7612633, 2, 0)];
    }

    public static IEnumerable<object[]> State0140Samples()
    {
        yield return [new State0140Sample("state/0140-boss-tail-430d03.hex", 200003, 1, 9)];
        yield return [new State0140Sample("state/0140-boss-tail-f203.hex", 1010, 1, 9)];
    }

    public static IEnumerable<object[]> State0240Samples()
    {
        yield return [new State0240Sample("state/0240-boss-tail-430d03.hex", 200003, 256, 0)];
        yield return [new State0240Sample("state/0240-boss-tail-f203.hex", 1010, 256, 0)];
    }

    public static IEnumerable<object[]> State4536Samples()
    {
        yield return [new State4536Sample("state/4536-boss-observed-4370.hex", 4370, 0, 0)];
    }

    public static IEnumerable<object[]> State4636Samples()
    {
        yield return [new State4636Sample("state/4636-state.hex", 2851, 2, 80)];
    }

    public static IEnumerable<object[]> State1D37Samples()
    {
        yield return [new State1D37Sample("state/1d37-state.hex", 100637, 35, 3, "C56E806E801B")];
        yield return [new State1D37Sample("state/1d37-group39-state3.hex", 1234, 39, 3, "AABBCCDD")];
        yield return [new State1D37Sample("state/1d37-group47-state3.hex", 2234, 47, 3, "11223344")];
        yield return [new State1D37Sample("state/1d37-group46-state4.hex", 3234, 46, 4, "10203040")];
        yield return [new State1D37Sample("state/1d37-group46-state9.hex", 4234, 46, 9, "55667788")];
    }

    public static IEnumerable<object[]> Compact0238Samples()
    {
        yield return [new Compact0238Sample("state/0238-compact-control.hex", 6618, 0, 17750010, 32, 0, 6618, 0, 98007)];
    }

    public static IEnumerable<object[]> Compact0638Samples()
    {
        yield return [new Compact0638Sample("state/0638-compact-control.hex", 6618, 17750010, 32, 0)];
    }

    public static IEnumerable<object[]> Aux2B38Samples()
    {
        yield return [new Aux2B38Sample("state/2b38-aux.hex", 3374, 19, 1301, unchecked((int)0x0AD79417))];
        yield return [new Aux2B38Sample("state/2b38-hot-refresh.hex", 6370, 19, 97, unchecked((int)0x0A2FEAF5))];
        yield return [new Aux2B38Sample("state/2b38-dot-refresh.hex", 17640, 19, 94, unchecked((int)0x0A2E3CE1))];
    }

    public static IEnumerable<object[]> Aux2A38Samples()
    {
        yield return [new Aux2A38Sample("state/2a38-buff-apply-08.hex", 8158, 1, 19, 109, 0x00BDF8DA)];
        yield return [new Aux2A38Sample("state/2a38-buff-apply-09.hex", 8158, 1, 19, 110, 0x00BDF8DA)];
        yield return [new Aux2A38Sample("state/2a38-dot-apply.hex", 22090, 1, 19, 7, 0x01049FB0)];
        yield return [new Aux2A38Sample("state/2a38-summon-apply.hex", 18345, 1, 19, 4, 0x0105B031)];
    }

    public static IEnumerable<object[]> Aux2C38Samples()
    {
        yield return [new Aux2C38Sample("state/2c38-buff-remove-6d.hex", 8158, 1, 0, 109, 1)];
        yield return [new Aux2C38Sample("state/2c38-buff-remove-6e.hex", 8158, 1, 0, 110, 1)];
        yield return [new Aux2C38Sample("state/2c38-dot-remove.hex", 22090, 1, 0, 9, 1)];
        yield return [new Aux2C38Sample("state/2c38-dot-natural-remove-94.hex", 17640, 1, 0, 94, 1)];
        yield return [new Aux2C38Sample("state/2c38-hot-natural-remove.hex", 6370, 1, 0, 100, 1)];
        yield return [new Aux2C38Sample("state/2c38-hot-manual-remove.hex", 6370, 1, 0, 97, 12)];
        yield return [new Aux2C38Sample("state/2c38-summon-remove.hex", 32407, 1, 0, 4, 19)];
        yield return [new Aux2C38Sample("state/2c38-summon-transition-18029.hex", 18029, 1, 0, 4, 19)];
        yield return [new Aux2C38Sample("state/2c38-summon-transition-20623.hex", 20623, 1, 0, 4, 19)];
    }

    public static IEnumerable<object[]> State4936Samples()
    {
        yield return [new State4936Sample("state/4936-buff-apply.hex", 8158, 2, 19, 0, 0x000001EA, 0x0092, 0x00000A69)];
        yield return [new State4936Sample("state/4936-buff-remove.hex", 8158, 2, 19, 0, 0x00000185, 0x0092, 0x00000681)];
        yield return [new State4936Sample("state/4936-hot-apply.hex", 12115, 3, 70, 0, 0x00001237, 0x01A9, 0x00001176)];
        yield return [new State4936Sample("state/4936-hot-remove.hex", 12115, 3, 70, 0, 0x00000A67, 0x01A9, 0x0000104A)];
    }

    public static IEnumerable<object[]> State4036Samples()
    {
        yield return [new State4036Sample("state/4036-state-97.hex", Packet4036Kind.State97, Packet4036LayoutKind.State97Main0D2000, 21258, 0x0D, 0x20, 0x00, 28125, 100, 100, 0x06, 0x02, 0x01, 0x01, 60, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0)];
        yield return [new State4036Sample("state/4036-state-120-852100.hex", Packet4036Kind.State120, Packet4036LayoutKind.State120Main852100, 19973, 0x85, 0x21, 0x00, 0, 0, 0, 0x00, 0x00, 0x00, 0x00, 0, 0x24AF24AF, 100, 100, 100, 100, 1, 0x0108000E, 0x00001400, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0)];
        yield return [new State4036Sample("state/4036-state-152-852100.hex", Packet4036Kind.State152, Packet4036LayoutKind.State152Main852100, 191528, 0x85, 0x21, 0x00, 0, 0, 0, 0x00, 0x00, 0x00, 0x00, 0, 0, 0, 0, 0, 0, 0, 0, 0, 100, 100, 2500, 2500, 1, 0x0111010E, 0xFFFFFFFF, 0xFFFFFFFF, 0x2AD57580, 0x000003BB)];
    }

    public static IEnumerable<object[]> Wrapped8456Samples()
    {
        yield return [new Wrapped8456Sample("state/8456-wrap-4036.hex", 0x01, 0x34, 0xB4, 0x3640, 1, 0x00)];
        yield return [new Wrapped8456Sample("state/8456-wrap-4136.hex", 0x01, 0x74, 0x50, 0x3641, 3, 0x00)];
    }
}
