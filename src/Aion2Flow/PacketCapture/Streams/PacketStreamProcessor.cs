using System.Buffers;
using System.Text;
using Cloris.Aion2Flow.Battle.Model;
using Cloris.Aion2Flow.Battle.Runtime;
using Cloris.Aion2Flow.Combat.Classification;
using Cloris.Aion2Flow.Combat.Metrics;
using Cloris.Aion2Flow.PacketCapture.Diagnostics;
using Cloris.Aion2Flow.PacketCapture.Protocol;
using Cloris.Aion2Flow.PacketCapture.Readers;
using K4os.Compression.LZ4;

namespace Cloris.Aion2Flow.PacketCapture.Streams;

public sealed class PacketStreamProcessor(CombatMetricsStore store)
    : IDisposable
{
    private const int MaxBufferSize = 1024 * 1024;
    private const int MaxDecompressedSize = 4 * 1024 * 1024;
    private readonly PacketTailBuffer _tail = new(2 * MaxBufferSize);
    private bool _hasParsed;
    private TcpConnection _connection;
    private long _currentFrameOrdinal;
    private long _nextFrameOrdinal;
    private long _currentBatchOrdinal;
    private long _currentAppendBatchOrdinal;
    private long _nextBatchOrdinal;
    private long? _timestampOverrideMilliseconds;

    private static readonly byte[] Pattern = [0x06, 0x00, 0x36];

    public void Dispose()
    {
        _tail.Dispose();
    }

    private static bool TryWriteVarInt(int value, Span<byte> destination, out int written)
    {

        written = 0;
        var num = value;
        while ((uint)num > 0x7fu)
        {
            if (written >= destination.Length) return false;
            destination[written++] = (byte)((num & 0x7f) | 0x80);
            num >>= 7;
        }

        if (written >= destination.Length) return false;
        destination[written++] = (byte)num;
        return true;
    }

    private void TryApplyNpcCatalog(int instanceId, int npcCode, bool requireCatalogEntry = false)
    {
        if (instanceId <= 0 || npcCode <= 0)
        {
            return;
        }

        var hasCatalogEntry = CombatMetricsEngine.TryResolveNpcCatalogEntry(npcCode, out var entry);
        if (requireCatalogEntry && !hasCatalogEntry)
        {
            return;
        }

        var lifecycleId = store.ResolveLifecycleId(instanceId);
        if (hasCatalogEntry &&
            store.TryGetNpcRuntimeState(lifecycleId, out var existing) &&
            existing.NpcCode is int existingCode &&
            existingCode != npcCode &&
            CombatMetricsEngine.TryResolveNpcCatalogEntry(existingCode, out _))
        {
            store.RebindInstanceLifecycle(instanceId);
        }

        store.AppendNpcCode(instanceId, npcCode);

        if (!hasCatalogEntry)
        {
            return;
        }

        store.AppendNpcName(npcCode, entry.Name);

        var kind = CombatMetricsEngine.ResolveNpcKind(entry.Kind);
        if (kind != NpcKind.Unknown && kind != NpcKind.Summon)
        {
            store.AppendNpcKind(instanceId, kind);
        }
    }

    private static string FormatSkillHint(uint rawSkillCode)
    {
        if (rawSkillCode == 0 || rawSkillCode > int.MaxValue)
        {
            return string.Empty;
        }

        var variant = CombatMetricsEngine.ParseSkillVariant((int)rawSkillCode);
        var variantHint = FormatSkillVariantHint(variant);
        var normalized = CombatMetricsEngine.InferOriginalSkillCode((int)rawSkillCode);
        if (!normalized.HasValue)
        {
            return $"|skillRaw={rawSkillCode}{variantHint}";
        }

        if (CombatMetricsEngine.SkillMap is not null && CombatMetricsEngine.SkillMap.TryGetValue(normalized.Value, out var skill))
        {
            return $"|skill={normalized.Value}{variantHint}|skillName={skill.Name}";
        }

        return $"|skill={normalized.Value}{variantHint}";
    }

    private static string FormatResolvedSkillHint(int skillCode)
    {
        if (skillCode <= 0)
        {
            return string.Empty;
        }

        var variant = CombatMetricsEngine.ParseSkillVariant(skillCode);
        var variantHint = FormatSkillVariantHint(variant);
        var normalized = CombatMetricsEngine.InferOriginalSkillCode(skillCode) ?? skillCode;
        if (CombatMetricsEngine.SkillMap is not null && CombatMetricsEngine.SkillMap.TryGetValue(normalized, out var skill))
        {
            return $"|skill={normalized}{variantHint}|skillName={skill.Name}";
        }

        return $"|skill={normalized}{variantHint}";
    }

    private static string FormatResolvedReferenceHint(string prefix, int rawSkillCode)
    {
        if (rawSkillCode <= 0)
        {
            return string.Empty;
        }

        var normalized = CombatMetricsEngine.InferOriginalSkillCode(rawSkillCode) ?? rawSkillCode;
        if (CombatMetricsEngine.SkillMap is not null && CombatMetricsEngine.SkillMap.TryGetValue(normalized, out var skill))
        {
            return $"|{prefix}={normalized}|{prefix}Name={skill.Name}";
        }

        return $"|{prefix}={normalized}";
    }

    private bool UpdateCurrentMapFromSceneState(uint value)
    {
        if (!IsSceneStateMapId(value))
        {
            return false;
        }

        store.UpdateCurrentMap(value);
        return true;
    }

    private static bool IsSceneStateMapId(uint value)
        => value is >= 1000 and < 2000
            or >= 200000 and < 300000
            or >= 600000 and < 700000;

    private static string FormatResolvedCombatHint(ParsedCombatPacket packet)
    {
        var skillCode = packet.SkillCode > 0 ? packet.SkillCode : packet.OriginalSkillCode;
        if (skillCode <= 0)
        {
            return string.Empty;
        }

        var normalized = CombatMetricsEngine.InferOriginalSkillCode(skillCode) ?? skillCode;
        var packetForClassification = packet.DeepClone();
        packetForClassification.SkillCode = normalized;
        packetForClassification.EventKind = CombatEventKind.Damage;
        packetForClassification.ValueKind = CombatValueKind.Unknown;
        packetForClassification.IsNormalized = false;

        var valueKind = CombatEventClassifier.ClassifyValueKind(packetForClassification);
        var variantHint = FormatSkillVariantHint(packet.SkillVariant);

        if (CombatMetricsEngine.SkillMap is not null && CombatMetricsEngine.SkillMap.TryGetValue(normalized, out var skill))
        {
            return $"|skill={normalized}{variantHint}|skillName={skill.Name}|valueKind={valueKind}";
        }

        return $"|skill={normalized}{variantHint}|valueKind={valueKind}";
    }

    private static string FormatEffectHint(ParsedCombatPacket packet)
    {
        var effectLabel = packet.FormatEffectLabel();
        return string.IsNullOrEmpty(effectLabel)
            ? string.Empty
            : $"|effect={effectLabel}";
    }

    private static string FormatSkillVariantHint(SkillVariantInfo variant)
    {
        if (variant.OriginalSkillCode <= 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        if (variant.BaseSkillCode > 0)
        {
            builder.Append("|baseSkill=").Append(variant.BaseSkillCode);
        }

        builder.Append("|charge=").Append(variant.ChargeStage);

        if (variant.SpecializationMask != 0)
        {
            builder.Append("|specs=").Append(FormatSpecializationMask(variant.SpecializationMask));
        }

        return builder.ToString();
    }

    private static string FormatSpecializationMask(int mask)
    {
        if (mask == 0)
        {
            return "-";
        }

        var digits = new List<int>();
        for (var i = 0; i < 5; i++)
        {
            if ((mask & (1 << i)) != 0)
            {
                digits.Add(i + 1);
            }
        }

        return string.Join('+', digits);
    }

    public bool AppendAndProcess(ReadOnlySpan<byte> payload, in TcpConnection connection)
    {
        _connection = connection;
        var previousAppendBatchOrdinal = _currentAppendBatchOrdinal;
        _currentAppendBatchOrdinal = ++_nextBatchOrdinal;

        try
        {
            var hasParsed = false;

            if (_tail.Length != 0)
            {
                if (!payload.IsEmpty)
                {
                    AppendToTail(payload);
                }

                ProcessBufferedPackets(ref hasParsed);
                return hasParsed;
            }

            if (payload.IsEmpty)
            {
                return false;
            }

            var remaining = payload;
            while (TryTakePacket(ref remaining, out var packet))
            {
                if (EmitPacket(packet))
                {
                    hasParsed = true;
                }
            }

            if (!remaining.IsEmpty)
            {
                AppendToTail(remaining);
            }

            return hasParsed;
        }
        finally
        {
            _currentAppendBatchOrdinal = previousAppendBatchOrdinal;
        }
    }

    public bool AppendAndProcess(ReadOnlySpan<byte> payload, in TcpConnection connection, long timestampMilliseconds)
    {
        var previousTimestampOverride = _timestampOverrideMilliseconds;
        _timestampOverrideMilliseconds = timestampMilliseconds > 0
            ? timestampMilliseconds
            : null;

        try
        {
            return AppendAndProcess(payload, connection);
        }
        finally
        {
            _timestampOverrideMilliseconds = previousTimestampOverride;
        }
    }

    private long CurrentTimestampMilliseconds
        => _timestampOverrideMilliseconds ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private void ProcessBufferedPackets(ref bool hasParsed)
    {
        while (TryDequeuePacketLength(out var packetLength))
        {
            var packet = _tail.Data[..packetLength];
            if (EmitPacket(packet))
            {
                hasParsed = true;
            }

            _tail.Consume(packetLength);
        }
    }

    private bool EmitPacket(ReadOnlySpan<byte> data)
    {
        _hasParsed = false;
        ParsePacketEntry(data);

        return _hasParsed;
    }

    private void AppendToTail(ReadOnlySpan<byte> data)
    {
        _tail.Append(data);
    }

    private bool TryDequeuePacketLength(out int packetLength)
    {
        packetLength = 0;

        var buffer = _tail.Data;
        if (buffer.IsEmpty)
        {
            return false;
        }

        if (TryReadTransportLength(buffer, 0, out packetLength) && packetLength <= buffer.Length)
        {
            return true;
        }

        var patternIndex = buffer.IndexOf(Pattern);
        if (patternIndex >= 0)
        {
            packetLength = patternIndex + Pattern.Length;
            return true;
        }

        var keepBytes = Pattern.Length - 1;
        if (buffer.Length > keepBytes)
        {
            _tail.Consume(buffer.Length - keepBytes);
        }

        return false;
    }

    private static bool TryTakePacket(ref ReadOnlySpan<byte> buffer, out ReadOnlySpan<byte> packet)
    {
        packet = default;
        if (buffer.IsEmpty)
        {
            return false;
        }

        if (TryReadTransportLength(buffer, 0, out var packetLength))
        {
            if (packetLength <= buffer.Length)
            {
                packet = buffer[..packetLength];
                buffer = buffer[packetLength..];
                return true;
            }

            return false;
        }

        var patternIndex = buffer.IndexOf(Pattern);
        if (patternIndex >= 0)
        {
            var consumed = patternIndex + Pattern.Length;
            packet = buffer[..consumed];
            buffer = buffer[consumed..];
            return true;
        }

        var keepBytes = Pattern.Length - 1;
        if (buffer.Length > keepBytes)
        {
            buffer = buffer[^keepBytes..];
        }

        return false;
    }

    private void ParsePacketEntry(ReadOnlySpan<byte> packet)
    {
        if (packet.IsEmpty)
        {
            return;
        }

        if (TryParseCompressedContainer(packet)) return;
        if (TryParseFrameBatch(packet)) return;

        var payload = packet.EndsWith(Pattern)
            ? packet[..^Pattern.Length]
            : packet;

        if (payload.IsEmpty)
        {
            return;
        }

        if (packet.EndsWith(Pattern))
        {
            RawPacketDump.AppendFrameEvent("tail-pattern", _connection, $"packetLen={packet.Length}", packet);
        }

        if (TryParsePacketContainer(packet)) return;
        if (ParseFramePayload(payload)) return;

        RawPacketDump.AppendFrameEvent("recovery-path", _connection, $"payloadLen={payload.Length}", payload);
        ParseRecoveryPacket(payload);
    }

    private bool TryParseCompressedContainer(ReadOnlySpan<byte> packet)
    {
        if (!TryReadVarInt(packet, 0, out var lengthInfo))
        {
            return false;
        }

        var totalLength = lengthInfo.Value + lengthInfo.ByteCount - 4;
        if (totalLength <= 0 || totalLength != packet.Length)
        {
            return false;
        }

        var offset = lengthInfo.ByteCount;
        var extraFlag = false;
        if (offset < packet.Length && packet[offset] is >= 0xf0 and < 0xff)
        {
            extraFlag = true;
            offset++;
        }

        if (offset + 6 > packet.Length)
        {
            return false;
        }

        if (packet[offset] != 0xff || packet[offset + 1] != 0xff)
        {
            return false;
        }
        offset += 2;

        var uncompressedLength = packet[offset]
            | (packet[offset + 1] << 8)
            | (packet[offset + 2] << 16)
            | (packet[offset + 3] << 24);

        if (uncompressedLength <= 0 || uncompressedLength > MaxDecompressedSize)
        {
            return false;
        }
        offset += 4;

        if (offset >= packet.Length)
        {
            return false;
        }

        var rented = ArrayPool<byte>.Shared.Rent(uncompressedLength);
        try
        {
            var decoded = LZ4Codec.Decode(packet[offset..], rented.AsSpan(0, uncompressedLength));
            if (decoded <= 0)
            {
                return false;
            }

            var restored = rented.AsSpan(0, decoded);
            RawPacketDump.AppendFrameEvent("compressed-container", _connection, $"extraFlag={extraFlag}|decoded={decoded}|encoded={packet.Length - offset}", restored);
            return TryParseFrameBatch(restored) || TryParsePacketContainer(restored) || ParseFramePayload(restored);
        }
        catch
        {
            return false;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private bool TryParseFrameBatch(ReadOnlySpan<byte> packet)
    {
        var previousBatchOrdinal = _currentBatchOrdinal;
        _currentBatchOrdinal = _currentAppendBatchOrdinal > 0
            ? _currentAppendBatchOrdinal
            : ++_nextBatchOrdinal;

        var offset = 0;
        var parsed = false;
        var frameCount = 0;

        try
        {
            while (offset < packet.Length)
            {
                if (packet.Length - offset >= Pattern.Length && packet[offset..].StartsWith(Pattern))
                {
                    offset += Pattern.Length;
                    continue;
                }

                if (!TryReadTransportLength(packet, offset, out var frameLength))
                {
                    break;
                }

                if (frameLength <= 0 || offset + frameLength > packet.Length)
                {
                    break;
                }

                var frame = packet.Slice(offset, frameLength);
                var framePayload = frame.EndsWith(Pattern)
                    ? frame[..^Pattern.Length]
                    : frame;

                if (framePayload.IsEmpty)
                {
                    offset += frameLength;
                    continue;
                }

                frameCount++;
                RawPacketDump.AppendFrameEvent("frame-batch", _connection, $"offset={offset}|frameLength={frameLength}", frame);

                if (ParseFramePayload(framePayload))
                {
                    parsed = true;
                }

                offset += frameLength;
            }

            return parsed && frameCount > 0;
        }
        finally
        {
            _currentBatchOrdinal = previousBatchOrdinal;
        }
    }

    private bool TryParseExactFrame(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketSpanReader(packet);
        if (!reader.TryReadVarInt(out _)) return false;
        if (reader.Remaining < 2) return false;

        var opcode0 = packet[reader.Offset];
        var opcode1 = packet[reader.Offset + 1];

        return (opcode0, opcode1) switch
        {
            (0x02, 0x38) => ParseCompactControl0238Packet(packet),
            (0x04, 0x38) => Parse0438ValuePacket(packet),
            (0x05, 0x38) => ParsePeriodicValuePacket(packet),
            (0x06, 0x38) => ParseCompactControl0638Packet(packet),
            (0x21, 0x36) => ParseState2136Packet(packet),
            (0x2e, 0x92) => ParseMap2E92Packet(packet),
            (0x01, 0x40) => ParseState0140Packet(packet),
            (0x02, 0x40) => ParseState0240Packet(packet),
            (0x2a, 0x38) => ParseAux2A38Packet(packet),
            (0x2b, 0x38) => ParseAux2B38Packet(packet),
            (0x2c, 0x38) => ParseAux2C38Packet(packet),
            (0x35, 0x38) => Parse3538SidecarPacket(packet),
            (0x1d, 0x37) => ParseState1D37Packet(packet),
            (0x33, 0x36) => ParseOwnNicknamePacket(packet),
            (0x44, 0x36) => ParseOtherNicknamePacket(packet),
            (0x09, 0x94) => Parse0994NicknamePacket(packet),
            (0x0b, 0x94) => Parse0994NicknamePacket(packet),
            (0x49, 0x36) => ParseState4936Packet(packet),
            (0x84, 0x56) => ParseWrapped8456Packet(packet),
            (0x40, 0x36) => ParseSummonPacket(packet),
            (0x41, 0x36) => ParseState4136Packet(packet),
            (0x45, 0x36) => ParseState4536Packet(packet),
            (0x46, 0x36) => ParseState4636Packet(packet),
            (0x04, 0x8d) => ParseNicknamePacket(packet),
            (0x00, 0x8d) => ParseRemainHpPacket(packet),
            (0x21, 0x8d) => ParseBattleTogglePacket(packet),
            _ => false
        };
    }

    private bool TryParsePacketContainer(ReadOnlySpan<byte> packet)
    {
        var parsed = false;

        for (var offset = 0; offset <= packet.Length - 3; offset++)
        {
            if (!TryReadVarInt(packet, offset, out var packetLengthInfo))
            {
                continue;
            }

            var declaredLength = packetLengthInfo.Value;
            if (declaredLength <= Pattern.Length || declaredLength > packet.Length - offset)
            {
                continue;
            }

            var candidate = packet.Slice(offset, declaredLength);
            if (!candidate[^Pattern.Length..].SequenceEqual(Pattern))
            {
                continue;
            }

            var bodyLength = declaredLength - Pattern.Length;
            if (bodyLength <= 0)
            {
                continue;
            }

            RawPacketDump.AppendFrameEvent("container-candidate", _connection, $"offset={offset}|declaredLength={declaredLength}", candidate);
            parsed |= ParsePerfectPacket(candidate[..bodyLength]);
        }

        return parsed;
    }

    private bool ScanForKnownPackets(ReadOnlySpan<byte> packet)
    {
        var parsed = false;

        for (var offset = 0; offset + 1 < packet.Length; offset++)
        {
            int consumed;
            if (packet[offset] == 0x04 && packet[offset + 1] == 0x38)
            {
                if (TryParseDamageAt(packet, offset, out consumed))
                {
                    parsed = true;
                    offset += Math.Max(consumed - 1, 1);
                    continue;
                }
            }
            else if (packet[offset] == 0x05 && packet[offset + 1] == 0x38)
            {
                if (TryParsePeriodicValuePacketAt(packet, offset, out consumed))
                {
                    parsed = true;
                    offset += Math.Max(consumed - 1, 1);
                    continue;
                }
            }
            else if (packet[offset] == 0x40 && packet[offset + 1] == 0x36)
            {
                if (TryParseSummonPacketAt(packet, offset, out consumed))
                {
                    parsed = true;
                    offset += Math.Max(consumed - 1, 1);
                    continue;
                }
            }
            else if (packet[offset] == 0x33 && packet[offset + 1] == 0x36)
            {
                if (TryParseOwnNicknameAt(packet, offset, out consumed))
                {
                    parsed = true;
                    offset += Math.Max(consumed - 1, 1);
                    continue;
                }
            }
            else if (packet[offset] == 0x04 && packet[offset + 1] == 0x8d)
            {
                if (TryParseNicknameAt(packet, offset, out consumed))
                {
                    parsed = true;
                    offset += Math.Max(consumed - 1, 1);
                    continue;
                }
            }
        }

        ScanForEmbeddedNicknames(packet);

        return parsed;
    }

    private bool TryParseNicknameAt(ReadOnlySpan<byte> packet, int opcodeOffset, out int consumed)
    {
        consumed = 0;

        var payload = packet[opcodeOffset..];
        if (payload.Length < 11 || payload[0] != 0x04 || payload[1] != 0x8d)
        {
            return false;
        }

        var reader = new PacketSpanReader(payload);
        if (!reader.TryAdvance(9)) return false;
        if (!reader.TryReadVarInt(out var playerId)) return false;
        if (!reader.TryReadByte(out var nicknameLengthByte)) return false;

        var nicknameLength = nicknameLengthByte;
        if (nicknameLength == 0 || nicknameLength > 72 || reader.Remaining < nicknameLength)
        {
            return false;
        }

        var nameBytes = payload.Slice(reader.Offset, nicknameLength);
        var sanitizedName = SanitizeNickname(Encoding.UTF8.GetString(nameBytes));
        if (sanitizedName is null)
        {
            return false;
        }

        consumed = reader.Offset + nicknameLength;
        store.AppendNickname(playerId, sanitizedName);
        RawPacketDump.AppendFrameEvent("nickname", _connection, $"playerId={playerId}|len={nicknameLength}", payload[..consumed]);
        return _hasParsed = true;
    }

    private bool TryParseOwnNicknameAt(ReadOnlySpan<byte> packet, int opcodeOffset, out int consumed)
    {
        consumed = 0;

        var payload = packet[opcodeOffset..];
        if (!Packet3336NicknameParser.TryParsePayload(payload, out var parsed))
        {
            return false;
        }

        store.AppendNickname(parsed.PlayerId, parsed.Nickname);
        consumed = parsed.TailOffset;
        RawPacketDump.AppendFrameEvent("nickname", _connection, $"playerId={parsed.PlayerId}|kind=own|len={parsed.NicknameLength}|embedded=true", payload[..consumed]);
        return _hasParsed = true;
    }

    private bool ParseFramePayload(ReadOnlySpan<byte> payload)
    {
        var previousFrameOrdinal = _currentFrameOrdinal;
        var previousBatchOrdinal = _currentBatchOrdinal;
        _currentFrameOrdinal = ++_nextFrameOrdinal;
        if (_currentBatchOrdinal <= 0)
        {
            _currentBatchOrdinal = _currentAppendBatchOrdinal > 0
                ? _currentAppendBatchOrdinal
                : ++_nextBatchOrdinal;
        }

        try
        {
            return TryParseExactFrame(payload) || ScanForKnownPackets(payload);
        }
        finally
        {
            _currentFrameOrdinal = previousFrameOrdinal;
            _currentBatchOrdinal = previousBatchOrdinal;
        }
    }

    private long CurrentFrameOrdinal
        => _currentFrameOrdinal > 0 ? _currentFrameOrdinal : ++_nextFrameOrdinal;

    private long CurrentBatchOrdinal
        => _currentBatchOrdinal > 0
            ? _currentBatchOrdinal
            : _currentAppendBatchOrdinal > 0
                ? _currentAppendBatchOrdinal
                : ++_nextBatchOrdinal;

    private bool TryParseRemainHpAt(ReadOnlySpan<byte> packet, int opcodeOffset, out int consumed)
    {
        consumed = 0;

        var payload = packet[opcodeOffset..];
        if (payload.Length < 8 || payload[0] != 0x00 || payload[1] != 0x8d)
        {
            return false;
        }

        var reader = new PacketSpanReader(payload);
        if (!reader.TryAdvance(2)) return false;
        if (!reader.TryReadVarInt(out var npcId)) return false;
        if (npcId == 0) return false;

        if (!reader.TryReadVarInt(out var value0)) return false;
        if (!reader.TryReadVarInt(out var value1)) return false;
        if (!reader.TryReadVarInt(out var value2)) return false;
        if (!reader.TryReadUInt32Le(out var npcHp)) return false;

        if (value0 == 2 && value1 == 1 && value2 == 0)
        {
            store.AppendNpcHp(npcId, checked((int)npcHp), CurrentTimestampMilliseconds);
        }
        consumed = reader.Offset;
        var eventName = value0 == 2 && value1 == 1 && value2 == 0 ? "remain-hp" : "entity-value-008d";
        RawPacketDump.AppendFrameEvent(eventName, _connection, $"npcId={npcId}|value0={value0}|value1={value1}|value2={value2}|value={npcHp}", payload[..consumed]);
        return _hasParsed = true;
    }

    private bool TryParseBattleToggleAt(ReadOnlySpan<byte> packet, int opcodeOffset, out int consumed)
    {
        consumed = 0;

        var payload = packet[opcodeOffset..];
        if (payload.Length < 3 || payload[0] != 0x21 || payload[1] != 0x8d)
        {
            return false;
        }

        var reader = new PacketSpanReader(payload);
        if (!reader.TryAdvance(2)) return false;
        if (!reader.TryReadVarInt(out var npcId)) return false;
        if (npcId == 0) return false;

        bool? isActive = null;
        if (reader.Remaining >= 2 &&
            payload[reader.Offset] == 0x00 &&
            payload[reader.Offset + 1] is 0x00 or 0x01)
        {
            isActive = payload[reader.Offset + 1] == 0x01;
            if (!reader.TryAdvance(2)) return false;
        }

        if (isActive is bool active)
        {
            store.SetNpcBattle(npcId, active, CurrentTimestampMilliseconds);
        }
        else
        {
            store.ToggleNpcBattle(npcId);
        }
        consumed = reader.Offset;
        var activeText = isActive.HasValue ? $"|active={isActive.Value}" : string.Empty;
        RawPacketDump.AppendFrameEvent("battle-toggle", _connection, $"npcId={npcId}{activeText}", payload[..consumed]);
        return _hasParsed = true;
    }

    private bool TryParseDamageAt(ReadOnlySpan<byte> packet, int opcodeOffset, out int consumed)
    {
        var frameOrdinal = CurrentFrameOrdinal;
        var batchOrdinal = CurrentBatchOrdinal;
        var payload = packet[opcodeOffset..];
        if (!Packet0438DamageParser.TryParsePayload(payload, out var parsed, out consumed))
        {
            return false;
        }

        var resolvedSkillCode = ResolveSkillCode(parsed.SkillCodeRaw) ?? parsed.SkillCodeRaw;

        if (parsed.Damage <= 0) return false;

        if (!store.IsKnownEntity(parsed.SourceId) && !store.IsKnownEntity(parsed.TargetId))
        {
            return false;
        }

        var combatPacket = new ParsedCombatPacket
        {
            TargetId = parsed.TargetId,
            LayoutTag = parsed.LayoutTag,
            Flag = parsed.Flag,
            SourceId = parsed.SourceId,
            OriginalSkillCode = parsed.SkillCodeRaw,
            SkillCode = resolvedSkillCode,
            Marker = parsed.Marker,
            Type = parsed.Type,
            Modifiers = parsed.TailMultiHitCount > 0
                ? parsed.Modifiers | DamageModifiers.MultiHit
                : parsed.Modifiers,
            Unknown = parsed.Unknown,
            Damage = parsed.Damage,
            Loop = parsed.Loop,
            MultiHitCount = parsed.TailMultiHitCount,
            DrainHealAmount = parsed.DrainHealAmount,
            RegenerationAmount = parsed.RegenerationAmount,
            DetailRaw = parsed.DetailRaw,
            ResourceKind = parsed.ResourceKind,
            Timestamp = CurrentTimestampMilliseconds,
            FrameOrdinal = frameOrdinal,
            BatchOrdinal = batchOrdinal
        };

        store.AppendCombatPacket(combatPacket);

        if (parsed.RegenerationAmount > 0 && ShouldStoreRegenerationHealing(parsed.TargetId))
        {
            var regenPacket = new ParsedCombatPacket
            {
                TargetId = parsed.TargetId,
                SourceId = parsed.TargetId,
                OriginalSkillCode = parsed.SkillCodeRaw,
                SkillCode = resolvedSkillCode,
                Damage = parsed.RegenerationAmount,
                EventKind = CombatEventKind.Healing,
                ValueKind = CombatValueKind.Healing,
                IsNormalized = true,
                Timestamp = CurrentTimestampMilliseconds,
                FrameOrdinal = frameOrdinal,
                BatchOrdinal = batchOrdinal
            };
            regenPacket.SetEffectTag(PacketEffectTag.RegenerationHealing);
            store.AppendCombatPacket(regenPacket);
        }

        if (ShouldStoreDrainHealing(parsed))
        {
            store.AppendCombatPacket(new ParsedCombatPacket
            {
                TargetId = parsed.SourceId,
                SourceId = parsed.SourceId,
                OriginalSkillCode = parsed.SkillCodeRaw,
                SkillCode = resolvedSkillCode,
                Damage = parsed.DrainHealAmount,
                DrainHealAmount = parsed.DrainHealAmount,
                Timestamp = CurrentTimestampMilliseconds,
                FrameOrdinal = frameOrdinal,
                BatchOrdinal = batchOrdinal
            });
        }

        RawPacketDump.AppendFrameEvent("damage", _connection, $"target={parsed.TargetId}|source={parsed.SourceId}|skillRaw={parsed.SkillCodeRaw}|damage={parsed.Damage}{FormatResolvedCombatHint(combatPacket)}", payload[..consumed]);
        return _hasParsed = true;
    }

    private bool TryParsePeriodicValuePacketAt(ReadOnlySpan<byte> packet, int opcodeOffset, out int consumed)
    {
        var frameOrdinal = CurrentFrameOrdinal;
        var batchOrdinal = CurrentBatchOrdinal;
        consumed = 0;

        var payload = packet[opcodeOffset..];
        if (payload.Length < 2 || payload[0] != 0x05 || payload[1] != 0x38)
        {
            return false;
        }

        var reader = new PacketSpanReader(payload);
        if (!reader.TryAdvance(2)) return false;
        if (!reader.TryReadVarInt(out var targetId)) return false;
        if (reader.Remaining < 1) return false;
        var mode = payload[reader.Offset];
        if (!reader.TryAdvance(1)) return false;
        if (!reader.TryReadVarInt(out var sourceId)) return false;
        if (sourceId == 0 || targetId == 0) return false;
        if (sourceId == targetId)
        {
            return false;
        }
        if (!reader.TryReadVarInt(out var unknownInfo)) return false;
        if (!reader.TryReadUInt32Le(out var skillRaw)) return false;

        var resolvedSkillCode = ResolveSkillCode(skillRaw) ?? ResolveSkillCode(skillRaw / 100);
        if (resolvedSkillCode is null) return false;

        if (!reader.TryReadVarInt(out var damage)) return false;
        if (damage <= 0) return false;

        if (!store.IsKnownEntity(sourceId) && !store.IsKnownEntity(targetId))
        {
            return false;
        }

        var combatPacket = new ParsedCombatPacket
        {
            TargetId = targetId,
            SourceId = sourceId,
            OriginalSkillCode = skillRaw,
            SkillCode = resolvedSkillCode.Value,
            Unknown = unknownInfo,
            Damage = damage,
            Timestamp = CurrentTimestampMilliseconds,
            FrameOrdinal = frameOrdinal,
            BatchOrdinal = batchOrdinal
        };
        combatPacket.SetPeriodicEffect(PeriodicEffectRelation.Target, mode);

        store.AppendCombatPacket(combatPacket);

        consumed = reader.Offset;
        RawPacketDump.AppendFrameEvent("periodic", _connection, $"target={targetId}|source={sourceId}|skill={resolvedSkillCode.Value}|damage={damage}{FormatEffectHint(combatPacket)}", payload[..consumed]);
        return _hasParsed = true;
    }

    private bool TryParseSummonPacketAt(ReadOnlySpan<byte> packet, int opcodeOffset, out int consumed)
    {
        consumed = 0;

        var payload = packet[opcodeOffset..];
        if (payload.Length < 2 || payload[0] != 0x40 || payload[1] != 0x36)
        {
            return false;
        }

        var reader = new PacketSpanReader(payload);
        if (!reader.TryAdvance(2)) return false;
        if (!reader.TryReadVarInt(out var summonId)) return false;
        if (!reader.TryAdvance(3)) return false;

        int? npcCode = null;
        if (reader.TryReadUInt32Le(out var npcValue) && npcValue is >= 2_000_000 and <= 2_999_999)
        {
            npcCode = npcValue;
            TryApplyNpcCatalog(summonId, npcValue);
            store.AppendNpcKind(summonId, NpcKind.Summon);
        }

        ReadOnlySpan<byte> keyPattern = [0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff];
        var keyIdx = payload.IndexOf(keyPattern);
        if (keyIdx == -1) return false;
        var afterPacket = payload[(keyIdx + 8)..];

        ReadOnlySpan<byte> opcodePattern = [0x07, 0x02, 0x06];
        var opcodeIdx = afterPacket.IndexOf(opcodePattern);
        if (opcodeIdx == -1) return false;

        var offset = keyIdx + opcodeIdx + 11;
        if (offset + 2 > payload.Length) return false;

        var realSourceId = (payload[offset] & 0xff) | ((payload[offset + 1] & 0xff) << 8);
        if (realSourceId == 0) return false;

        store.AppendSummon(realSourceId, summonId);
        consumed = Math.Max(offset + 2, reader.Offset);
        var payloadLength = consumed > 0 ? consumed : payload.Length;
        var kind = Packet4036Descriptors.ClassifyKind(payloadLength);
        var npcCodeText = npcCode.HasValue ? $"|npcCode={npcCode.Value}" : string.Empty;
        RawPacketDump.AppendFrameEvent("summon", _connection, $"kind={Packet4036Descriptors.FormatKind(kind, payloadLength)}|owner={realSourceId}|summon={summonId}{npcCodeText}", payload[..consumed]);
        return _hasParsed = true;
    }

    private static int? ResolveSkillCode(int skillCode)
    {
        if (skillCode <= 0)
        {
            return null;
        }

        return CombatMetricsEngine.InferOriginalSkillCode(skillCode);
    }

    private bool ShouldStoreRegenerationHealing(int targetId)
    {
        if (targetId <= 0)
        {
            return false;
        }

        if (store.SummonOwnerByInstance.ContainsKey(targetId))
        {
            return false;
        }

        return !store.TryGetNpcRuntimeState(targetId, out var state) || state.Kind != NpcKind.Summon;
    }

    private void ParseRecoveryPacket(ReadOnlySpan<byte> packet, bool flag = true)
    {
        if (packet.Length < 4)
            return;

        if (packet[2] != 0xff || packet[3] != 0xff)
        {
            var target = store.CurrentTarget;
            var processed = false;
            if (target != 0)
            {
                Span<byte> targetBytes = stackalloc byte[5];
                if (!TryWriteVarInt(target, targetBytes, out var targetByteCount))
                    return;

                Span<byte> damageKeyword = stackalloc byte[2 + 5];
                damageKeyword[0] = 0x04;
                damageKeyword[1] = 0x38;
                targetBytes[..targetByteCount].CopyTo(damageKeyword[2..]);
                var damageNeedle = damageKeyword[..(2 + targetByteCount)];

                Span<byte> periodicKeyword = stackalloc byte[2 + 5];
                periodicKeyword[0] = 0x05;
                periodicKeyword[1] = 0x38;
                targetBytes[..targetByteCount].CopyTo(periodicKeyword[2..]);
                var periodicNeedle = periodicKeyword[..(2 + targetByteCount)];

                var damageIdx = packet.IndexOf(damageNeedle);
                var periodicIdx = packet.IndexOf(periodicNeedle);
                var idx = -1;
                Func<ReadOnlySpan<byte>, bool>? handler = null;

                if (damageIdx > 0 && periodicIdx > 0)
                {
                    if (damageIdx < periodicIdx) { idx = damageIdx; handler = Parse0438ValuePacket; }
                    else { idx = periodicIdx; handler = ParsePeriodicValuePacket; }
                }
                else if (damageIdx > 0)
                {
                    idx = damageIdx; handler = Parse0438ValuePacket;
                }
                else if (periodicIdx > 0)
                {
                    idx = periodicIdx; handler = ParsePeriodicValuePacket;
                }

                if (idx > 0 && handler != null)
                {
                    if (!TryReadVarInt(packet, idx - 1, out var packetLengthInfo))
                    {
                        return;
                    }
                    if (packetLengthInfo.ByteCount == 1)
                    {
                        var startIdx = idx - 1;
                        var endIdx = idx - 1 + packetLengthInfo.Value - 3;
                        if (startIdx >= 0 && startIdx < endIdx && endIdx <= packet.Length)
                        {
                            var extractedPacket = packet[startIdx..endIdx];
                            handler(extractedPacket);
                            processed = true;
                            if (endIdx < packet.Length)
                            {
                                var remainingPacket = packet[endIdx..];
                                ParseRecoveryPacket(remainingPacket, false);
                            }
                        }
                    }
                }
            }

            if (flag && !processed)
            {
                ScanForEmbeddedNicknames(packet);
            }

            return;
        }

        if (packet.Length <= 10) return;
        var newPacket = packet[10..];
        ParsePacketEntry(newPacket);
    }

    private void ScanForEmbeddedNicknames(ReadOnlySpan<byte> packet)
    {
        var originOffset = 0;
        while (originOffset < packet.Length)
        {
            if (!TryReadVarInt(packet, originOffset, out var info))
            {
                originOffset++;
                continue;
            }

            var innerOffset = originOffset + info.ByteCount;

            if (innerOffset + 6 >= packet.Length)
            {
                originOffset++;
                continue;
            }

            if (packet[innerOffset + 3] == 0x01 && packet[innerOffset + 4] == 0x07)
            {
                var possibleNameLength = packet[innerOffset + 5] & 0xff;
                if (innerOffset + 6 + possibleNameLength <= packet.Length)
                {
                    var possibleNameBytes = packet[(innerOffset + 6)..(innerOffset + 6 + possibleNameLength)];
                    var possibleName = Encoding.UTF8.GetString(possibleNameBytes);
                    var sanitizedName = SanitizeNickname(possibleName);
                    if (sanitizedName != null)
                    {
                        store.AppendNickname(info.Value, sanitizedName);
                    }
                }
            }

            if (packet.Length > innerOffset + 5)
            {
                if (packet[innerOffset + 3] == 0x00 && packet[innerOffset + 4] == 0x07)
                {
                    var possibleNameLength = packet[innerOffset + 5] & 0xff;
                    if (packet.Length > innerOffset + possibleNameLength + 6)
                    {
                        var possibleNameBytes = packet[(innerOffset + 6)..(innerOffset + possibleNameLength + 6)];
                        var possibleName = Encoding.UTF8.GetString(possibleNameBytes);
                        var sanitizedName = SanitizeNickname(possibleName);
                        if (sanitizedName != null)
                        {
                            store.AppendNickname(info.Value, sanitizedName);
                        }
                    }
                }
            }

            originOffset++;
        }
    }

    private static string? SanitizeNickname(string nickname)
    {
        var sanitized = nickname.Split('\0')[0].Trim();
        if (string.IsNullOrEmpty(sanitized)) return null;

        var nicknameBuilder = new StringBuilder();
        var onlyNumbers = true;
        var hasHan = false;

        foreach (var ch in sanitized)
        {
            if (!char.IsLetterOrDigit(ch))
            {
                if (nicknameBuilder.Length == 0) return null;
                break;
            }
            if (ch == '\uFFFD')
            {
                if (nicknameBuilder.Length == 0) return null;
                break;
            }
            if (char.IsControl(ch))
            {
                if (nicknameBuilder.Length == 0) return null;
                break;
            }
            nicknameBuilder.Append(ch);
            if (char.IsLetter(ch)) onlyNumbers = false;
            if (char.GetUnicodeCategory(ch) == System.Globalization.UnicodeCategory.OtherLetter)
            {
                hasHan = true;
            }
        }

        var trimmed = nicknameBuilder.ToString();
        if (trimmed.Length == 0) return null;
        if (trimmed.Length < 3 && !hasHan) return null;
        if (onlyNumbers) return null;
        if (trimmed.Length == 1 && char.IsLetter(trimmed[0]) && !hasHan) return null;

        return trimmed;
    }

    private bool ParsePerfectPacket(ReadOnlySpan<byte> packet)
    {
        if (packet.Length < 3) return false;
        return TryParseExactFrame(packet);
    }

    private bool Parse0438ValuePacket(ReadOnlySpan<byte> packet)
    {
        var frameOrdinal = CurrentFrameOrdinal;
        var batchOrdinal = CurrentBatchOrdinal;

        if (Packet0438DamageParser.TryParse(packet, out var parsed))
        {
            var combatPacket = new ParsedCombatPacket
            {
                TargetId = parsed.TargetId,
                LayoutTag = parsed.LayoutTag,
                Flag = parsed.Flag,
                SourceId = parsed.SourceId,
                OriginalSkillCode = parsed.SkillCodeRaw,
                SkillCode = parsed.SkillCodeRaw,
                Marker = parsed.Marker,
                Type = parsed.Type,
                Modifiers = parsed.Modifiers,
                Unknown = parsed.Unknown,
                Damage = parsed.Damage,
                Loop = parsed.Loop,
                DrainHealAmount = parsed.DrainHealAmount,
                RegenerationAmount = parsed.RegenerationAmount,
                DetailRaw = parsed.DetailRaw,
                ResourceKind = parsed.ResourceKind,
                Timestamp = CurrentTimestampMilliseconds,
                FrameOrdinal = frameOrdinal,
                BatchOrdinal = batchOrdinal
            };

            if (parsed.TailMultiHitCount > 0)
            {
                combatPacket.MultiHitCount = parsed.TailMultiHitCount;
                combatPacket.Modifiers |= DamageModifiers.MultiHit;
            }

            store.AppendCombatPacket(combatPacket);

            if (parsed.RegenerationAmount > 0 && ShouldStoreRegenerationHealing(parsed.TargetId))
            {
                var regenPacket = new ParsedCombatPacket
                {
                    TargetId = parsed.TargetId,
                    SourceId = parsed.TargetId,
                    OriginalSkillCode = parsed.SkillCodeRaw,
                    SkillCode = parsed.SkillCodeRaw,
                    Damage = parsed.RegenerationAmount,
                    EventKind = CombatEventKind.Healing,
                    ValueKind = CombatValueKind.Healing,
                    IsNormalized = true,
                    Timestamp = CurrentTimestampMilliseconds,
                    FrameOrdinal = frameOrdinal,
                    BatchOrdinal = batchOrdinal
                };
                regenPacket.SetEffectTag(PacketEffectTag.RegenerationHealing);
                store.AppendCombatPacket(regenPacket);
            }

            if (ShouldStoreDrainHealing(parsed))
            {
                store.AppendCombatPacket(new ParsedCombatPacket
                {
                    TargetId = parsed.SourceId,
                    SourceId = parsed.SourceId,
                    OriginalSkillCode = parsed.SkillCodeRaw,
                    SkillCode = parsed.SkillCodeRaw,
                    Damage = parsed.DrainHealAmount,
                    DrainHealAmount = parsed.DrainHealAmount,
                    Timestamp = CurrentTimestampMilliseconds,
                    FrameOrdinal = frameOrdinal,
                    BatchOrdinal = batchOrdinal
                });
            }

            RawPacketDump.AppendFrameEvent("damage", _connection, $"target={parsed.TargetId}|source={parsed.SourceId}|skillRaw={parsed.SkillCodeRaw}|damage={parsed.Damage}{FormatResolvedCombatHint(combatPacket)}", packet[..(packet.Length - parsed.TailLength)]);
            return _hasParsed = true;
        }

        if (Packet0438CompactValueParser.TryParse(packet, out var compact))
        {
            var tailHint = FormatResolvedReferenceHint("tailSkill", compact.TailRaw);
            var timestamp = CurrentTimestampMilliseconds;
            store.RegisterCompactValue0438(
                compact.TargetId,
                compact.SourceId,
                compact.SkillCodeRaw,
                compact.Marker,
                compact.LayoutTag,
                compact.Type,
                compact.Value,
                timestamp,
                frameOrdinal,
                batchOrdinal);
            RawPacketDump.AppendFrameEvent(
                "compact-value",
                _connection,
                $"target={compact.TargetId}|source={compact.SourceId}|switch={compact.LayoutTag}|flag={compact.Flag}|marker={compact.Marker}|type={compact.Type}|skillRaw={compact.SkillCodeRaw}|unknown={compact.Unknown}|value={compact.Value}|loop={compact.Loop}|tailLen={compact.TailLength}|tailRaw={compact.TailRaw}{FormatResolvedSkillHint(compact.SkillCodeRaw)}{tailHint}",
                packet[..(packet.Length - compact.TailLength)]);
            return _hasParsed = true;
        }

        if (!Packet0438CompactOutcomeParser.TryParse(packet, out var compactOutcome))
        {
            return false;
        }

        var compactOutcomeTimestamp = CurrentTimestampMilliseconds;
        store.RegisterCompactValue0438(
            compactOutcome.TargetId,
            compactOutcome.SourceId,
            compactOutcome.SkillCodeRaw,
            compactOutcome.Marker,
            compactOutcome.LayoutTag,
            compactOutcome.Type,
            compactOutcomeTimestamp,
            frameOrdinal,
            batchOrdinal);
        RawPacketDump.AppendFrameEvent(
            "compact-outcome",
            _connection,
            $"target={compactOutcome.TargetId}|source={compactOutcome.SourceId}|layout={compactOutcome.LayoutTag}|flag={compactOutcome.Flag}|marker={compactOutcome.Marker}|type={compactOutcome.Type}|skillRaw={compactOutcome.SkillCodeRaw}|tailLen={compactOutcome.TailLength}{FormatSkillHint((uint)compactOutcome.SkillCodeRaw)}",
            packet);
        return _hasParsed = true;
    }

    private bool ParsePeriodicValuePacket(ReadOnlySpan<byte> packet)
    {
        var frameOrdinal = CurrentFrameOrdinal;
        var batchOrdinal = CurrentBatchOrdinal;

        if (!Packet0538PeriodicValueParser.TryParse(packet, out var parsed))
        {
            return false;
        }

        if (parsed.IsLinkRecord)
        {
            store.RegisterPeriodicLink0538(
                parsed.TargetId,
                parsed.SourceId,
                parsed.LinkId,
                parsed.Unknown,
                parsed.TailRaw,
                CurrentTimestampMilliseconds,
                frameOrdinal,
                batchOrdinal);

            var linkTailHint = FormatResolvedReferenceHint("tailSkill", parsed.TailRaw);
            RawPacketDump.AppendFrameEvent(
                "periodic-link",
                _connection,
                $"target={parsed.TargetId}|source={parsed.SourceId}|mode={parsed.Mode}|skillRaw={parsed.SkillCodeRaw}|linkId={parsed.LinkId}|unknown={parsed.Unknown}|tailRaw={parsed.TailRaw}|effect={Packet0538PeriodicValueParser.FormatEffectLabel(parsed.TargetId, parsed.SourceId, parsed.Mode)}{linkTailHint}",
                packet);
            return _hasParsed = true;
        }

        var combatPacket = new ParsedCombatPacket
        {
            TargetId = parsed.TargetId,
            SourceId = parsed.SourceId,
            OriginalSkillCode = parsed.SkillCodeRaw,
            SkillCode = parsed.LegacySkillCode,
            Unknown = parsed.Unknown,
            Damage = parsed.Damage,
            Timestamp = CurrentTimestampMilliseconds,
            FrameOrdinal = frameOrdinal,
            BatchOrdinal = batchOrdinal
        };
        combatPacket.SetPeriodicEffect(
            parsed.TargetId == parsed.SourceId ? PeriodicEffectRelation.Self : PeriodicEffectRelation.Target,
            parsed.Mode);

        store.AppendCombatPacket(combatPacket);
        var periodicTailHint = parsed.TailLength > 0
            ? $"|tailLen={parsed.TailLength}|tailRaw={parsed.TailRaw}|tailSkillRaw={parsed.TailSkillCodeRaw}|tailPrefix={parsed.TailPrefixValue}{FormatResolvedReferenceHint("tailSkill", parsed.TailSkillCodeRaw)}"
            : string.Empty;
        RawPacketDump.AppendFrameEvent("periodic", _connection, $"target={parsed.TargetId}|source={parsed.SourceId}|mode={parsed.Mode}|skillRaw={parsed.SkillCodeRaw}|unknown={parsed.Unknown}|damage={parsed.Damage}{periodicTailHint}{FormatEffectHint(combatPacket)}{FormatResolvedCombatHint(combatPacket)}", packet);
        return _hasParsed = true;
    }

    private bool ParseCompactControl0238Packet(ReadOnlySpan<byte> packet)
    {
        if (!Packet0238CompactControlParser.TryParse(packet, out var parsed))
        {
            return false;
        }

        store.RegisterCompactControl0238(parsed.SourceId, parsed.SkillCodeRaw, parsed.Marker, CurrentBatchOrdinal);
        RawPacketDump.AppendFrameEvent(
            "compact-0238",
            _connection,
            $"source={parsed.SourceId}|mode={parsed.Mode}|skillRaw={parsed.SkillCodeRaw}|marker={parsed.Marker}|flag={parsed.Flag}|echoSource={parsed.EchoSourceId}|zero=0x{parsed.ZeroValue:x8}|tailValue=0x{parsed.TailValue:x8}{FormatSkillHint((uint)parsed.SkillCodeRaw)}",
            packet);
        return _hasParsed = true;
    }

    private bool ParseCompactControl0638Packet(ReadOnlySpan<byte> packet)
    {
        if (!Packet0638CompactControlParser.TryParse(packet, out var parsed))
        {
            return false;
        }

        store.RegisterCompactControl0638(parsed.SourceId, parsed.SkillCodeRaw, parsed.Marker, CurrentTimestampMilliseconds, CurrentFrameOrdinal, CurrentBatchOrdinal);
        RawPacketDump.AppendFrameEvent(
            "compact-0638",
            _connection,
            $"source={parsed.SourceId}|skillRaw={parsed.SkillCodeRaw}|marker={parsed.Marker}|flag={parsed.Flag}{FormatSkillHint((uint)parsed.SkillCodeRaw)}",
            packet);
        return _hasParsed = true;
    }

    private bool Parse3538SidecarPacket(ReadOnlySpan<byte> packet)
    {
        if (!Packet3538SidecarParser.TryParse(packet, out var parsed))
        {
            return false;
        }

        store.ObserveBossFocusPulse(parsed.TargetId, CurrentTimestampMilliseconds);
        RawPacketDump.AppendFrameEvent(
            "sidecar-3538",
            _connection,
            $"target={parsed.TargetId}|state={parsed.State}|source={parsed.SourceId}",
            packet);
        return _hasParsed = true;
    }

    private bool ParseSummonPacket(ReadOnlySpan<byte> packet)
    {
        var kind = Packet4036Descriptors.ClassifyKind(packet.Length);
        if (!Packet4036Descriptors.IsCreateKind(kind))
        {
            return Parse4036StatePacket(packet);
        }

        if (Packet4036CreateParser.TryParse(packet, out var parsed))
        {
            if (parsed.NpcCode.HasValue)
            {
                TryApplyNpcCatalog(parsed.SummonId, parsed.NpcCode.Value);
            }

            store.AppendNpcKind(parsed.SummonId, NpcKind.Summon);
            store.AppendSummon(parsed.OwnerId, parsed.SummonId);
            var npcCodeText = parsed.NpcCode.HasValue ? $"|npcCode={parsed.NpcCode.Value}" : string.Empty;
            RawPacketDump.AppendFrameEvent("summon", _connection, $"kind={Packet4036Descriptors.FormatKind(parsed.Kind, parsed.TailOffset)}|owner={parsed.OwnerId}|summon={parsed.SummonId}{npcCodeText}", packet[..Math.Min(parsed.TailOffset, packet.Length)]);
            return _hasParsed = true;
        }

        if (Packet4036CreateParser.TryParseNpcSpawn(packet, out var spawn))
        {
            if (spawn.NpcCode.HasValue)
            {
                TryApplyNpcCatalog(spawn.EntityId, spawn.NpcCode.Value);
            }

            if (spawn.CurrentHp is int currentHp && spawn.MaxHp is int maxHp)
            {
                store.AppendNpcHp(spawn.EntityId, currentHp, maxHp, CurrentTimestampMilliseconds);
            }

            var spawnNpcCodeText = spawn.NpcCode.HasValue ? $"|npcCode={spawn.NpcCode.Value}" : string.Empty;
            var spawnHpText = spawn.CurrentHp is int hp && spawn.MaxHp is int hpMax ? $"|currentHp={hp}|maxHp={hpMax}" : string.Empty;
            RawPacketDump.AppendFrameEvent("npc-spawn", _connection, $"kind={Packet4036Descriptors.FormatKind(spawn.Kind, packet.Length)}|entity={spawn.EntityId}{spawnNpcCodeText}{spawnHpText}", packet);
            return _hasParsed = true;
        }

        return false;
    }

    private bool Parse4036StatePacket(ReadOnlySpan<byte> packet)
    {
        if (Packet4036CreateParser.TryParseNpcSpawn(packet, out var spawn) && spawn.NpcCode.HasValue)
        {
            TryApplyNpcCatalog(spawn.EntityId, spawn.NpcCode.Value, requireCatalogEntry: true);
            if (spawn.CurrentHp is int currentHp && spawn.MaxHp is int maxHp)
            {
                store.AppendNpcHp(spawn.EntityId, currentHp, maxHp, CurrentTimestampMilliseconds);
            }
        }

        if (Packet4036CreateParser.TryParseOwner(packet, out var entityId, out var ownerId))
        {
            store.AppendSummon(ownerId, entityId);
        }

        if (!Packet4036Parser.TryParse(packet, out var parsed))
        {
            return false;
        }

        RawPacketDump.AppendFrameEvent(
            "state-4036",
            _connection,
            $"kind={Packet4036Descriptors.FormatKind(parsed.Kind, parsed.PayloadLength)}|layout={Packet4036Descriptors.FormatLayout(parsed.Kind, parsed.LayoutKind, parsed.PayloadLength, parsed.BodyLength, parsed.Mode0, parsed.Mode1, parsed.Mode2)}|source={parsed.SourceId}|mode={parsed.Mode0:x2}{parsed.Mode1:x2}{parsed.Mode2:x2}|seed=0x{parsed.Seed:x8}|tag=0x{parsed.Tag:x4}|p0=0x{parsed.P0:x8}|p1=0x{parsed.P1:x8}|p2=0x{parsed.P2:x8}|marker=0x{parsed.Marker:x8}|repeat0={parsed.Repeat0}|repeat1={parsed.Repeat1}|linked={parsed.LinkedValue}|gauge0={parsed.Gauge0}|gauge1={parsed.Gauge1}|tailMode={parsed.TailMode}|tailState={parsed.TailState}|tailFlags={parsed.TailFlag0}/{parsed.TailFlag1}|tailValue={parsed.TailValue}|tailHash=0x{parsed.TailHash:x8}|tailTerm={parsed.TailTerminator}|sharedTag=0x{parsed.SharedTag:x8}|sharedGauge={parsed.SharedGauge0}/{parsed.SharedGauge1}/{parsed.SharedGauge2}/{parsed.SharedGauge3}|sharedFlag={parsed.SharedFlag}|sharedMini0=0x{parsed.SharedMini0:x8}|sharedMini1=0x{parsed.SharedMini1:x8}|heavyGauge={parsed.HeavyGauge0}/{parsed.HeavyGauge1}|heavyValue={parsed.HeavyValue0}/{parsed.HeavyValue1}|heavyFlag={parsed.HeavyFlag}|heavyMini0=0x{parsed.HeavyMini0:x8}|heavySentinel=0x{parsed.HeavySentinel0:x8}/0x{parsed.HeavySentinel1:x8}|heavyTrailer=0x{parsed.HeavyTrailer0:x8}/0x{parsed.HeavyTrailer1:x8}|tail0=0x{parsed.Tail0:x8}|tail1=0x{parsed.Tail1:x8}|bodyLen={parsed.BodyLength}",
            packet);

        return _hasParsed = true;
    }

    private bool ParseAux2B38Packet(ReadOnlySpan<byte> packet)
    {
        if (!Packet2B38Parser.TryParse(packet, out var parsed))
        {
            return false;
        }

        RawPacketDump.AppendFrameEvent(
            "aux-2b38",
            _connection,
            $"source={parsed.SourceId}|source2={parsed.SourceIdCopy}|phase={parsed.Phase}|marker={parsed.Marker}|action=0x{parsed.ActionCode:x8}|seq={parsed.Sequence}|state={parsed.StateValue}|detail={parsed.DetailValue}|tailLen={parsed.TailLength}",
            packet);

        return _hasParsed = true;
    }

    private bool ParseAux2A38Packet(ReadOnlySpan<byte> packet)
    {
        var frameOrdinal = CurrentFrameOrdinal;
        var batchOrdinal = CurrentBatchOrdinal;

        if (!Packet2A38Parser.TryParse(packet, out var parsed))
        {
            return false;
        }

        store.RegisterObservation2A38(
            parsed.SourceId,
            parsed.Mode,
            parsed.GroupCode,
            parsed.SequenceId,
            parsed.HeadValue,
            parsed.BuffCodeRaw,
            CurrentTimestampMilliseconds,
            frameOrdinal,
            batchOrdinal);

        RawPacketDump.AppendFrameEvent(
            "aux-2a38",
            _connection,
            $"source={parsed.SourceId}|mode={parsed.Mode}|group={parsed.GroupCode}|seq={parsed.SequenceId}|head=0x{parsed.HeadCode:x8}|headValue=0x{parsed.HeadValue:x4}|timeline=0x{parsed.TimelineValue:x8}|stable=0x{parsed.StableValue:x8}|echoSource={parsed.EchoSourceId}|stack={parsed.StackValue}|buff=0x{parsed.BuffCodeRaw:x8}{FormatSkillHint(parsed.BuffCodeRaw)}|tailSig={parsed.TailSignature}|tailLen={parsed.TailLength}",
            packet);

        return _hasParsed = true;
    }

    private bool ParseAux2C38Packet(ReadOnlySpan<byte> packet)
    {
        var frameOrdinal = CurrentFrameOrdinal;
        var batchOrdinal = CurrentBatchOrdinal;

        if (!Packet2C38Parser.TryParse(packet, out var parsed))
        {
            return false;
        }

        store.RegisterObservation2C38(
            parsed.SourceId,
            parsed.Mode,
            parsed.SequenceId,
            parsed.ResultCode,
            parsed.TailSourceId,
            parsed.TailSkillCodeRaw,
            CurrentTimestampMilliseconds,
            frameOrdinal,
            batchOrdinal);

        RawPacketDump.AppendFrameEvent(
            "aux-2c38",
            _connection,
            $"source={parsed.SourceId}|mode={parsed.Mode}|state={parsed.StateCode}|seq={parsed.SequenceId}|result={parsed.ResultCode}|tailLen={parsed.TailLength}|tailSource={parsed.TailSourceId}|tailSkillRaw={parsed.TailSkillCodeRaw}{FormatSkillHint((uint)parsed.TailSkillCodeRaw)}",
            packet);

        return _hasParsed = true;
    }

    private bool ParseState1D37Packet(ReadOnlySpan<byte> packet)
    {
        if (!Packet1D37Parser.TryParse(packet, out var parsed))
        {
            return false;
        }

        RawPacketDump.AppendFrameEvent(
            "state-1d37",
            _connection,
            $"source={parsed.SourceId}|group={parsed.GroupCode}|state={parsed.StateCode}|tailSig={parsed.TailSignature}|tailLen={parsed.TailLength}",
            packet);

        return _hasParsed = true;
    }

    private bool ParseState4136Packet(ReadOnlySpan<byte> packet)
    {
        if (!Packet4136Parser.TryParse(packet, out var parsed))
        {
            return false;
        }

        RawPacketDump.AppendFrameEvent(
            "state-4136",
            _connection,
            $"source={parsed.SourceId}|state0={parsed.State0}|state1={parsed.State1}|tailLen={parsed.TailLength}",
            packet);

        return _hasParsed = true;
    }

    private bool ParseWrapped8456Packet(ReadOnlySpan<byte> packet)
    {
        if (!Packet8456EnvelopeParser.TryParse(packet, out var parsed))
        {
            return false;
        }

        RawPacketDump.AppendFrameEvent(
            "wrapped-8456",
            _connection,
            $"p0=0x{parsed.Prefix0:x2}|p1=0x{parsed.Prefix1:x2}|p2=0x{parsed.Prefix2:x2}|innerOpcode=0x{parsed.InnerOpcode:x4}|innerValue={parsed.InnerValue}|stamp=0x{parsed.Stamp:x16}|trailer=0x{parsed.Trailer:x2}|tailLen={parsed.TailLength}",
            packet);

        return _hasParsed = true;
    }

    private bool ParseState0140Packet(ReadOnlySpan<byte> packet)
    {
        if (!Packet0140Parser.TryParse(packet, out var parsed))
        {
            return false;
        }

        var sceneMap = UpdateCurrentMapFromSceneState(parsed.Value0);
        var targetId = store.ResolveNpcObservationSource();
        if (targetId > 0)
        {
            store.AppendNpc0140Value(targetId, parsed.Value0);
            if (parsed.Value0 <= int.MaxValue)
            {
                TryApplyNpcCatalog(targetId, (int)parsed.Value0, requireCatalogEntry: true);
            }
        }

        RawPacketDump.AppendFrameEvent(
            "state-0140",
            _connection,
            $"target={targetId}|value0={parsed.Value0}|value1={parsed.Value1}|sceneMap={sceneMap}|tailSig={parsed.TailSignature}|tailLen={parsed.TailLength}",
            packet);

        return _hasParsed = true;
    }

    private bool ParseState2136Packet(ReadOnlySpan<byte> packet)
    {
        if (!Packet2136Parser.TryParse(packet, out var parsed))
        {
            return false;
        }

        var sceneMap = UpdateCurrentMapFromSceneState(parsed.Value0);

        var targetId = store.ResolveNpcObservationSource();
        if (targetId > 0)
        {
            store.AppendNpc2136State(targetId, parsed.Sequence, parsed.Value0);
            if (parsed.Value0 <= int.MaxValue)
            {
                TryApplyNpcCatalog(targetId, (int)parsed.Value0, requireCatalogEntry: true);
            }
        }

        RawPacketDump.AppendFrameEvent(
            "state-2136",
            _connection,
            $"target={targetId}|seq={parsed.Sequence}|value0={parsed.Value0}|value1={parsed.Value1}|value2={parsed.Value2}|sceneMap={sceneMap}|value3=0x{parsed.Value3:x8}|value4=0x{parsed.Value4:x8}|value5=0x{parsed.Value5:x8}|value6=0x{parsed.Value6:x8}|value7={parsed.Value7}|tailMarker=0x{parsed.TailMarker:x4}|tailLen={parsed.TailLength}",
            packet);

        return _hasParsed = true;
    }

    private bool ParseMap2E92Packet(ReadOnlySpan<byte> packet)
    {
        if (!Packet2E92Parser.TryParse(packet, out var parsed))
        {
            return false;
        }

        store.UpdateCurrentMapInstance(parsed.InstanceId);

        RawPacketDump.AppendFrameEvent(
            "map-2e92",
            _connection,
            $"instance={parsed.InstanceId}|contentKey={parsed.ContentKey}",
            packet);

        return _hasParsed = true;
    }

    private bool ParseState0240Packet(ReadOnlySpan<byte> packet)
    {
        if (!Packet0240Parser.TryParse(packet, out var parsed))
        {
            return false;
        }

        var sceneMap = UpdateCurrentMapFromSceneState(parsed.Value0);
        var targetId = store.ResolveNpcObservationSource();
        if (targetId > 0)
        {
            store.AppendNpc0240Value(targetId, parsed.Value0);
            if (parsed.Value0 <= int.MaxValue)
            {
                TryApplyNpcCatalog(targetId, (int)parsed.Value0, requireCatalogEntry: true);
            }
        }

        RawPacketDump.AppendFrameEvent(
            "state-0240",
            _connection,
            $"target={targetId}|value0={parsed.Value0}|value1={parsed.Value1}|sceneMap={sceneMap}|tailSig={parsed.TailSignature}|tailLen={parsed.TailLength}",
            packet);

        return _hasParsed = true;
    }

    private bool ParseState4636Packet(ReadOnlySpan<byte> packet)
    {
        if (!Packet4636Parser.TryParse(packet, out var parsed))
        {
            return false;
        }

        store.AppendNpc4636State(parsed.SourceId, parsed.State0, parsed.State1);
        store.RememberNpcObservationSource(parsed.SourceId);

        RawPacketDump.AppendFrameEvent(
            "state-4636",
            _connection,
            $"source={parsed.SourceId}|state0={parsed.State0}|state1={parsed.State1}|tailLen={parsed.TailLength}",
            packet);

        return _hasParsed = true;
    }

    private bool ParseState4536Packet(ReadOnlySpan<byte> packet)
    {
        if (!Packet4536Parser.TryParse(packet, out var parsed))
        {
            return false;
        }

        store.RememberNpcObservationSource(parsed.SourceId);

        RawPacketDump.AppendFrameEvent(
            "state-4536",
            _connection,
            $"source={parsed.SourceId}|value0={parsed.Value0}|tailLen={parsed.TailLength}",
            packet);

        return _hasParsed = true;
    }

    private bool ParseState4936Packet(ReadOnlySpan<byte> packet)
    {
        if (!Packet4936Parser.TryParse(packet, out var parsed))
        {
            return false;
        }

        RawPacketDump.AppendFrameEvent(
            "state-4936",
            _connection,
            $"source={parsed.SourceId}|mode={parsed.Mode}|group={parsed.GroupCode}|flag={parsed.Flag}|value0=0x{parsed.Value0:x8}|marker=0x{parsed.Marker:x4}|value1=0x{parsed.Value1:x8}|tailSig={parsed.TailSignature}|tailLen={parsed.TailLength}",
            packet);

        return _hasParsed = true;
    }

    private bool ParseOwnNicknamePacket(ReadOnlySpan<byte> packet)
    {
        if (!Packet3336NicknameParser.TryParse(packet, out var parsed))
        {
            return false;
        }

        var reader = new PacketSpanReader(packet);
        if (!reader.TryReadVarInt(out _))
        {
            return false;
        }

        var tailOffset = Math.Min(packet.Length, reader.Offset + parsed.TailOffset);
        store.AppendNickname(parsed.PlayerId, parsed.Nickname);
        RawPacketDump.AppendFrameEvent("nickname", _connection, $"playerId={parsed.PlayerId}|kind=own|len={parsed.NicknameLength}", packet[..tailOffset]);
        return _hasParsed = true;
    }

    private bool ParseOtherNicknamePacket(ReadOnlySpan<byte> packet)
    {
        if (Packet4436NicknameParser.TryParse(packet, out var parsed))
        {
            store.AppendNickname(parsed.PlayerId, parsed.Nickname);
            RawPacketDump.AppendFrameEvent("nickname", _connection, $"playerId={parsed.PlayerId}|kind=other|len={parsed.NicknameLength}|delta={parsed.Delta}", packet);
            return _hasParsed = true;
        }

        return false;
    }

    private bool Parse0994NicknamePacket(ReadOnlySpan<byte> packet)
    {
        if (!Packet0994NicknameParser.TryParse(packet, out var parsed))
        {
            return false;
        }

        store.AppendNickname(parsed.PlayerId, parsed.Nickname);
        RawPacketDump.AppendFrameEvent("nickname", _connection, $"playerId={parsed.PlayerId}|kind=roster|len={parsed.NicknameLength}", packet[..parsed.TailOffset]);
        return _hasParsed = true;
    }

    private bool ParseRemainHpPacket(ReadOnlySpan<byte> packet)
    {
        if (!Packet008DRemainHpParser.TryParse(packet, out var parsed))
        {
            return false;
        }

        var isHealth = Packet008DRemainHpParser.IsHealthValue(parsed);
        if (isHealth)
        {
            store.AppendNpcHp(parsed.NpcId, checked((int)parsed.Hp), CurrentTimestampMilliseconds);
        }

        var eventName = isHealth ? "remain-hp" : "entity-value-008d";
        RawPacketDump.AppendFrameEvent(eventName, _connection, $"npcId={parsed.NpcId}|value0={parsed.Value0}|value1={parsed.Value1}|value2={parsed.Value2}|value={parsed.Hp}", packet[..(packet.Length - parsed.TailLength)]);
        return _hasParsed = true;
    }

    private bool ParseBattleTogglePacket(ReadOnlySpan<byte> packet)
    {
        if (!Packet218DBattleToggleParser.TryParse(packet, out var parsed))
        {
            return false;
        }

        if (parsed.IsActive is bool isActive)
        {
            store.SetNpcBattle(parsed.NpcId, isActive, CurrentTimestampMilliseconds);
        }
        else
        {
            store.ToggleNpcBattle(parsed.NpcId);
        }

        var activeText = parsed.IsActive.HasValue ? $"|active={parsed.IsActive.Value}" : string.Empty;
        RawPacketDump.AppendFrameEvent("battle-toggle", _connection, $"npcId={parsed.NpcId}{activeText}|tailLen={parsed.TailLength}", packet);
        return _hasParsed = true;
    }

    private bool ParseNicknamePacket(ReadOnlySpan<byte> packet)
    {
        if (!Packet048DNicknameParser.TryParse(packet, out var parsed))
        {
            return false;
        }

        store.AppendNickname(parsed.PlayerId, parsed.Nickname);
        RawPacketDump.AppendFrameEvent("nickname", _connection, $"playerId={parsed.PlayerId}|len={parsed.NicknameLength}", packet[..parsed.TailOffset]);
        return _hasParsed = true;
    }

    private static bool TryReadVarInt(ReadOnlySpan<byte> bytes, int offset, out PacketVarIntReadResult result)
    {
        var value = 0;
        var shift = 0;
        var count = 0;

        while (true)
        {
            if (offset + count >= bytes.Length)
            {
                result = default;
                return false;
            }

            var byteVal = bytes[offset + count] & 0xff;
            count++;

            value |= (byteVal & 0x7f) << shift;

            if ((byteVal & 0x80) == 0)
            {
                result = new PacketVarIntReadResult(value, count);
                return true;
            }

            shift += 7;
            if (shift >= 32 || count > 5)
            {
                result = default;
                return false;
            }
        }
    }

    private static bool TryReadTransportLength(ReadOnlySpan<byte> bytes, int offset, out int packetLength)
    {
        packetLength = 0;
        if (!TryReadVarInt(bytes, offset, out var result))
        {
            return false;
        }

        var totalLength = result.Value + result.ByteCount - 4;
        if (totalLength <= 0)
        {
            return false;
        }

        packetLength = totalLength;
        return true;
    }

    private static bool ShouldStoreDrainHealing(Packet0438Damage parsed)
    {
        if (parsed.DrainHealAmount <= 0 || parsed.SourceId == parsed.TargetId)
        {
            return false;
        }

        return true;
    }
}
