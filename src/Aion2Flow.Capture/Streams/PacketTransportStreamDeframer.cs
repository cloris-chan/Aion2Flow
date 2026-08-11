using System.Buffers.Binary;

namespace Cloris.Aion2Flow.Capture.Streams;

internal sealed class PacketTransportStreamDeframer : IDisposable
{
    private readonly PacketTailBuffer _raw = new(CaptureBufferLimits.TransportRawBufferSize);
    private readonly PacketTailBuffer _canonical = new(CaptureBufferLimits.StreamTailBufferSize);
    private bool _allowUnalignedDirectRecovery;
    private int _pendingDirectRecoveryTickOffset = -1;
    private int _remainingCanonicalPrefixLength;
    private int _remainingDirectPrefixLength;
    private int _remainingEnvelopeBodyLength;

    public PacketTransportStreamDeframer(
        PacketTransportFraming framing = PacketTransportFraming.Auto,
        int transportPrefixLength = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(transportPrefixLength);
        if ((framing is PacketTransportFraming.Auto or PacketTransportFraming.DirectRecovery) &&
            transportPrefixLength != 0)
        {
            throw new ArgumentException(
                "An automatic or unaligned direct stream cannot discard a transport prefix.",
                nameof(transportPrefixLength));
        }

        if (framing == PacketTransportFraming.LengthPrefixed)
        {
            ActivateLengthPrefixed(transportPrefixLength);
        }
        else
        {
            _remainingDirectPrefixLength = transportPrefixLength;
            _allowUnalignedDirectRecovery = framing == PacketTransportFraming.DirectRecovery;
        }
    }

    public bool IsLengthPrefixed { get; private set; }

    public bool IsDirectRecoveryEnabled => _allowUnalignedDirectRecovery;

    public bool IsFaulted { get; private set; }

    public long LengthPrefixedStartByteOffset { get; private set; }

    public long TotalRawConsumedByteCount { get; private set; }

    public int BufferedByteCount => checked(_raw.Length + _canonical.Length);

    public bool HasPendingData =>
        BufferedByteCount != 0 ||
        _remainingEnvelopeBodyLength != 0 ||
        _remainingCanonicalPrefixLength != 0 ||
        _remainingDirectPrefixLength != 0;

    public bool TryGetPendingDirectRecoveryTickOffset(out int offset)
    {
        offset = _pendingDirectRecoveryTickOffset;
        return offset >= 0;
    }

    public ReadOnlySpan<byte> RawData => _raw.Data;

    public ReadOnlySpan<byte> CanonicalData => IsLengthPrefixed ? _canonical.Data : _raw.Data;

    public void Dispose()
    {
        _canonical.Dispose();
        _raw.Dispose();
    }

    public void Append(ReadOnlySpan<byte> payload)
    {
        if (IsFaulted)
        {
            return;
        }

        if (payload.Length > _raw.Capacity - _raw.Length)
        {
            Fault();
            return;
        }

        _raw.Append(payload);
    }

    public PacketTransportDataAvailability PrepareCanonicalData()
    {
        if (IsFaulted)
        {
            return PacketTransportDataAvailability.Invalid;
        }

        if (!IsLengthPrefixed)
        {
            if (_remainingDirectPrefixLength != 0)
            {
                if (_raw.Length < _remainingDirectPrefixLength)
                    return PacketTransportDataAvailability.NeedMore;

                ConsumeRaw(_remainingDirectPrefixLength);
                _remainingDirectPrefixLength = 0;
            }

            if (_allowUnalignedDirectRecovery)
            {
                return _raw.Length == 0
                    ? PacketTransportDataAvailability.NeedMore
                    : PacketTransportDataAvailability.Available;
            }

            var probe = PacketTransportCodec.ProbeLengthPrefixedStream(
                _raw.Data,
                PacketTransportCodec.MaximumEnvelopeBodyLength);
            if (probe.Kind == PacketLengthPrefixedProbeKind.Complete)
            {
                ActivateLengthPrefixed(probe.CanonicalPrefixLength);
            }
            else if (probe.Kind == PacketLengthPrefixedProbeKind.Ambiguous)
            {
                _pendingDirectRecoveryTickOffset = probe.DirectRecoveryTickOffset;
                return PacketTransportDataAvailability.NeedMore;
            }
            else
            {
                _pendingDirectRecoveryTickOffset = -1;
                if (probe.Kind == PacketLengthPrefixedProbeKind.NeedMore)
                {
                    var canonical = PacketTransportCodec.ProbeCanonicalFrame(
                        _raw.Data,
                        CaptureBufferLimits.StreamTailBufferSize);
                    if (canonical.Kind == PacketCanonicalFrameProbeKind.Complete &&
                        PacketTransportCodec.HasRecognizedCanonicalFrameStart(_raw.Data, in canonical))
                    {
                        return PacketTransportDataAvailability.Available;
                    }
                }

                return probe.Kind == PacketLengthPrefixedProbeKind.NeedMore
                    ? PacketTransportDataAvailability.NeedMore
                    : _raw.Length == 0
                        ? PacketTransportDataAvailability.NeedMore
                        : PacketTransportDataAvailability.Available;
            }
        }

        while (_canonical.Length == 0)
        {
            var progress = PumpLengthPrefixedData();
            if (progress == PacketTransportPumpResult.Invalid)
            {
                Fault();
                return PacketTransportDataAvailability.Invalid;
            }

            if (progress == PacketTransportPumpResult.NeedMore)
            {
                return PacketTransportDataAvailability.NeedMore;
            }
        }

        return PacketTransportDataAvailability.Available;
    }

    public bool TryExpandCanonicalData()
    {
        if (!IsLengthPrefixed || IsFaulted)
        {
            return false;
        }

        var progress = PumpLengthPrefixedData();
        if (progress != PacketTransportPumpResult.Invalid)
        {
            return progress == PacketTransportPumpResult.Progress;
        }

        Fault();
        return false;
    }

    public void ConsumeCanonical(int count)
    {
        if (IsLengthPrefixed)
        {
            _canonical.Consume(count);
            return;
        }

        ConsumeRaw(count);
    }

    public void DiscardRawPrefix(int count)
    {
        if (IsLengthPrefixed)
        {
            throw new InvalidOperationException();
        }

        ConsumeRaw(count);
    }

    public void ActivateLengthPrefixed(int canonicalPrefixLength = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(canonicalPrefixLength);
        if (IsLengthPrefixed)
        {
            return;
        }

        IsLengthPrefixed = true;
        _pendingDirectRecoveryTickOffset = -1;
        _allowUnalignedDirectRecovery = false;
        _remainingDirectPrefixLength = 0;
        LengthPrefixedStartByteOffset = TotalRawConsumedByteCount;
        _remainingCanonicalPrefixLength = canonicalPrefixLength;
    }

    public bool ResolvePendingDirectRecovery()
    {
        if (IsLengthPrefixed || _pendingDirectRecoveryTickOffset < 0)
            return false;

        _pendingDirectRecoveryTickOffset = -1;
        _allowUnalignedDirectRecovery = true;
        return true;
    }

    public bool ResolvePendingLengthPrefixed()
    {
        if (IsLengthPrefixed || _pendingDirectRecoveryTickOffset < 0)
            return false;

        ActivateLengthPrefixed();
        return true;
    }

    public void ActivateDirectRecovery()
    {
        if (IsLengthPrefixed)
            return;

        _pendingDirectRecoveryTickOffset = -1;
        _allowUnalignedDirectRecovery = true;
    }

    public void MarkDirectCanonicalAlignment()
    {
        if (!IsLengthPrefixed)
        {
            _pendingDirectRecoveryTickOffset = -1;
            _remainingDirectPrefixLength = 0;
            _allowUnalignedDirectRecovery = false;
        }
    }

    private PacketTransportPumpResult PumpLengthPrefixedData()
    {
        var madeProgress = false;
        if (_remainingEnvelopeBodyLength == 0)
        {
            if (_raw.Length < PacketTransportCodec.LengthPrefixedHeaderLength)
            {
                return PacketTransportPumpResult.NeedMore;
            }

            var bodyLength = BinaryPrimitives.ReadInt32LittleEndian(_raw.Data);
            if (bodyLength <= 0 || bodyLength > PacketTransportCodec.MaximumEnvelopeBodyLength)
            {
                return PacketTransportPumpResult.Invalid;
            }

            _remainingEnvelopeBodyLength = bodyLength;
            ConsumeRaw(PacketTransportCodec.LengthPrefixedHeaderLength);
            madeProgress = true;
        }

        if (_raw.Length == 0)
        {
            return madeProgress
                ? PacketTransportPumpResult.Progress
                : PacketTransportPumpResult.NeedMore;
        }

        var availableBodyLength = Math.Min(_remainingEnvelopeBodyLength, _raw.Length);
        var discardedPrefixLength = Math.Min(_remainingCanonicalPrefixLength, availableBodyLength);
        if (discardedPrefixLength != 0)
        {
            ConsumeRaw(discardedPrefixLength);
            _remainingEnvelopeBodyLength -= discardedPrefixLength;
            _remainingCanonicalPrefixLength -= discardedPrefixLength;
            madeProgress = true;
        }

        if (_raw.Length == 0 || _remainingEnvelopeBodyLength == 0)
        {
            return PacketTransportPumpResult.Progress;
        }

        var availableCapacity = _canonical.Capacity - _canonical.Length;
        if (availableCapacity == 0)
        {
            return madeProgress
                ? PacketTransportPumpResult.Progress
                : PacketTransportPumpResult.NeedMore;
        }

        var bodyChunkLength = Math.Min(
            Math.Min(_remainingEnvelopeBodyLength, _raw.Length),
            availableCapacity);
        _canonical.Append(_raw.Data[..bodyChunkLength]);
        ConsumeRaw(bodyChunkLength);
        _remainingEnvelopeBodyLength -= bodyChunkLength;
        return PacketTransportPumpResult.Progress;
    }

    private void ConsumeRaw(int count)
    {
        _raw.Consume(count);
        TotalRawConsumedByteCount = checked(TotalRawConsumedByteCount + count);
    }

    private void Fault()
    {
        IsFaulted = true;
        _remainingCanonicalPrefixLength = 0;
        _remainingDirectPrefixLength = 0;
        _remainingEnvelopeBodyLength = 0;
        _pendingDirectRecoveryTickOffset = -1;
        _canonical.Clear();
        _raw.Clear();
    }

    private enum PacketTransportPumpResult : byte
    {
        NeedMore,
        Invalid,
        Progress
    }
}

internal enum PacketTransportDataAvailability : byte
{
    NeedMore,
    Invalid,
    Available
}

internal enum PacketTransportFraming : byte
{
    Auto,
    DirectAligned,
    DirectRecovery,
    LengthPrefixed
}
