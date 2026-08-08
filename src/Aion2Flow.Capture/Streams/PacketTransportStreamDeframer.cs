using System.Buffers.Binary;

namespace Cloris.Aion2Flow.Capture.Streams;

internal sealed class PacketTransportStreamDeframer : IDisposable
{
    private readonly PacketTailBuffer _raw = new(CaptureBufferLimits.TransportRawBufferSize);
    private readonly PacketTailBuffer _canonical = new(CaptureBufferLimits.StreamTailBufferSize);
    private bool _allowUnalignedDirectRecovery;
    private int _remainingCanonicalPrefixLength;
    private int _remainingEnvelopeBodyLength;

    public PacketTransportStreamDeframer(
        PacketTransportFraming framing = PacketTransportFraming.Auto,
        int canonicalPrefixLength = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(canonicalPrefixLength);
        if (framing != PacketTransportFraming.LengthPrefixed && canonicalPrefixLength != 0)
        {
            throw new ArgumentException(
                "A canonical prefix can only be discarded from a length-prefixed stream.",
                nameof(canonicalPrefixLength));
        }

        if (framing == PacketTransportFraming.LengthPrefixed)
        {
            ActivateLengthPrefixed(canonicalPrefixLength);
        }
        else
        {
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
        _remainingCanonicalPrefixLength != 0;

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
            else
            {
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
        LengthPrefixedStartByteOffset = TotalRawConsumedByteCount;
        _remainingCanonicalPrefixLength = canonicalPrefixLength;
    }

    public void MarkDirectCanonicalAlignment()
    {
        if (!IsLengthPrefixed)
        {
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
        _remainingEnvelopeBodyLength = 0;
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
