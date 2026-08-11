namespace Cloris.Aion2Flow.Capture.Streams;

internal readonly record struct CanonicalPacketTransportIdentity(
    TcpConnection Connection,
    long ConnectionOrdinal);

internal readonly record struct CanonicalPacketMirrorProbe(
    bool IsDuplicate,
    bool CanRemember,
    ulong Fingerprint,
    int PacketLength,
    long ObservedAtMilliseconds);

internal sealed class CanonicalPacketMirrorDeduplicator
{
    internal static readonly TimeSpan DefaultWindow = TimeSpan.FromSeconds(2);
    internal const int DefaultOccurrenceCountLimit = 4_096;
    internal const int DefaultRetainedByteLimit = 4 * 1024 * 1024;
    internal const int DefaultTransportCountPerOccurrenceLimit = 64;

    private readonly long _windowMilliseconds;
    private readonly int _occurrenceCountLimit;
    private readonly int _retainedByteLimit;
    private readonly int _transportCountPerOccurrenceLimit;
    private readonly Dictionary<CanonicalPacketKey, List<CanonicalPacketOccurrence>> _occurrencesByPacket = [];
    private readonly PriorityQueue<CanonicalPacketOccurrence, long> _occurrencesByTimestamp = new();
    private long _latestObservedAtMilliseconds;
    private int _retainedByteCount;
    private bool _hasObservedTimestamp;

    internal CanonicalPacketMirrorDeduplicator()
        : this(
            DefaultWindow,
            DefaultOccurrenceCountLimit,
            DefaultRetainedByteLimit,
            DefaultTransportCountPerOccurrenceLimit)
    {
    }

    internal CanonicalPacketMirrorDeduplicator(
        TimeSpan window,
        int occurrenceCountLimit,
        int retainedByteLimit,
        int transportCountPerOccurrenceLimit = DefaultTransportCountPerOccurrenceLimit)
    {
        if (window <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(window));
        if (occurrenceCountLimit <= 0)
            throw new ArgumentOutOfRangeException(nameof(occurrenceCountLimit));
        if (retainedByteLimit <= 0)
            throw new ArgumentOutOfRangeException(nameof(retainedByteLimit));
        if (transportCountPerOccurrenceLimit <= 0)
            throw new ArgumentOutOfRangeException(nameof(transportCountPerOccurrenceLimit));

        _windowMilliseconds = checked((long)window.TotalMilliseconds);
        _occurrenceCountLimit = occurrenceCountLimit;
        _retainedByteLimit = retainedByteLimit;
        _transportCountPerOccurrenceLimit = transportCountPerOccurrenceLimit;
    }

    internal int TrackedOccurrenceCount => _occurrencesByTimestamp.Count;

    internal int RetainedByteCount => _retainedByteCount;

    internal CanonicalPacketMirrorProbe Probe(
        in CanonicalPacketTransportIdentity transport,
        ReadOnlySpan<byte> packet,
        long observedAtMilliseconds)
    {
        Advance(observedAtMilliseconds);
        if (packet.Length > _retainedByteLimit || IsExpired(observedAtMilliseconds))
        {
            return new CanonicalPacketMirrorProbe(
                IsDuplicate: false,
                CanRemember: false,
                Fingerprint: 0,
                packet.Length,
                observedAtMilliseconds);
        }

        var fingerprint = ComputeFingerprint(packet);
        var key = new CanonicalPacketKey(packet.Length, fingerprint);
        if (_occurrencesByPacket.TryGetValue(key, out var occurrences))
        {
            for (var index = 0; index < occurrences.Count; index++)
            {
                var occurrence = occurrences[index];
                if (!IsWithinWindow(occurrence.ObservedAtMilliseconds, observedAtMilliseconds) ||
                    occurrence.Contains(in transport) ||
                    !packet.SequenceEqual(occurrence.Packet) ||
                    !occurrence.TryAdd(in transport, _transportCountPerOccurrenceLimit))
                {
                    continue;
                }

                return new CanonicalPacketMirrorProbe(
                    IsDuplicate: true,
                    CanRemember: false,
                    fingerprint,
                    packet.Length,
                    observedAtMilliseconds);
            }
        }

        return new CanonicalPacketMirrorProbe(
            IsDuplicate: false,
            CanRemember: true,
            fingerprint,
            packet.Length,
            observedAtMilliseconds);
    }

    internal void Remember(
        in CanonicalPacketTransportIdentity transport,
        ReadOnlySpan<byte> packet,
        in CanonicalPacketMirrorProbe probe)
    {
        if (!probe.CanRemember || probe.IsDuplicate)
            return;
        if (packet.Length != probe.PacketLength)
            throw new ArgumentException("The canonical packet does not match its mirror probe.", nameof(packet));

        Advance(probe.ObservedAtMilliseconds);
        if (IsExpired(probe.ObservedAtMilliseconds) || packet.Length > _retainedByteLimit)
            return;

        MakeRoom(packet.Length);
        var key = new CanonicalPacketKey(packet.Length, probe.Fingerprint);
        var occurrence = new CanonicalPacketOccurrence(
            key,
            packet.ToArray(),
            transport,
            probe.ObservedAtMilliseconds);
        if (!_occurrencesByPacket.TryGetValue(key, out var occurrences))
        {
            occurrences = [];
            _occurrencesByPacket.Add(key, occurrences);
        }

        occurrences.Add(occurrence);
        _occurrencesByTimestamp.Enqueue(occurrence, occurrence.ObservedAtMilliseconds);
        _retainedByteCount += occurrence.Packet.Length;
    }

    internal void Clear()
    {
        _occurrencesByPacket.Clear();
        _occurrencesByTimestamp.Clear();
        _latestObservedAtMilliseconds = 0;
        _retainedByteCount = 0;
        _hasObservedTimestamp = false;
    }

    private void Advance(long observedAtMilliseconds)
    {
        if (!_hasObservedTimestamp || observedAtMilliseconds > _latestObservedAtMilliseconds)
        {
            _latestObservedAtMilliseconds = observedAtMilliseconds;
            _hasObservedTimestamp = true;
        }

        while (_occurrencesByTimestamp.TryPeek(out var occurrence, out _) &&
               !IsWithinWindow(occurrence.ObservedAtMilliseconds, _latestObservedAtMilliseconds))
        {
            _occurrencesByTimestamp.Dequeue();
            Remove(occurrence);
        }
    }

    private bool IsExpired(long observedAtMilliseconds) =>
        _hasObservedTimestamp &&
        _latestObservedAtMilliseconds > observedAtMilliseconds &&
        _latestObservedAtMilliseconds - observedAtMilliseconds > _windowMilliseconds;

    private bool IsWithinWindow(long first, long second)
    {
        var delta = first >= second ? first - second : second - first;
        return delta <= _windowMilliseconds;
    }

    private void MakeRoom(int packetLength)
    {
        while (_occurrencesByTimestamp.Count >= _occurrenceCountLimit ||
               _retainedByteCount > _retainedByteLimit - packetLength)
        {
            var occurrence = _occurrencesByTimestamp.Dequeue();
            Remove(occurrence);
        }
    }

    private void Remove(CanonicalPacketOccurrence occurrence)
    {
        if (!_occurrencesByPacket.TryGetValue(occurrence.Key, out var occurrences) ||
            !occurrences.Remove(occurrence))
        {
            return;
        }

        _retainedByteCount -= occurrence.Packet.Length;
        if (occurrences.Count == 0)
            _occurrencesByPacket.Remove(occurrence.Key);
    }

    private static ulong ComputeFingerprint(ReadOnlySpan<byte> packet)
    {
        const ulong offsetBasis = 14_695_981_039_346_656_037;
        const ulong prime = 1_099_511_628_211;
        var hash = offsetBasis;
        foreach (var value in packet)
        {
            hash ^= value;
            hash = unchecked(hash * prime);
        }

        return hash;
    }

    private readonly record struct CanonicalPacketKey(int PacketLength, ulong Fingerprint);

    private sealed class CanonicalPacketOccurrence(
        CanonicalPacketKey key,
        byte[] packet,
        CanonicalPacketTransportIdentity transport,
        long observedAtMilliseconds)
    {
        private readonly List<CanonicalPacketTransportIdentity> _transports = [transport];

        public CanonicalPacketKey Key { get; } = key;

        public byte[] Packet { get; } = packet;

        public long ObservedAtMilliseconds { get; } = observedAtMilliseconds;

        public bool Contains(in CanonicalPacketTransportIdentity candidate)
        {
            for (var index = 0; index < _transports.Count; index++)
            {
                if (_transports[index] == candidate)
                    return true;
            }

            return false;
        }

        public bool TryAdd(
            in CanonicalPacketTransportIdentity candidate,
            int transportCountLimit)
        {
            if (_transports.Count >= transportCountLimit)
                return false;

            _transports.Add(candidate);
            return true;
        }
    }
}
