using Cloris.Aion2Flow.Battle.Runtime;
using Cloris.Aion2Flow.PacketCapture.Capture;
using Cloris.Aion2Flow.PacketCapture.Streams;
using Cloris.Aion2Flow.Tests.Protocol;
using System.Buffers;

namespace Cloris.Aion2Flow.Tests.PacketCapture;

public sealed class PacketCaptureDispatcherTests
{
    private static readonly TcpConnection InboundConnection = new(0x0100007f, 0x0100007f, 57080, 49820);
    private static readonly TcpConnection OutboundConnection = new(0x0100007f, 0x0100007f, 49820, 57080);

    [Fact]
    public void Does_Not_Parse_Outbound_Payload_Into_Combat_Metrics()
    {
        var store = new CombatMetricsStore();
        var dispatcher = new PacketCaptureDispatcher(store);
        var packet = CreatePacket(OutboundConnection, HexHelper.FromFixture("combat/0538-dot.hex"), sequenceNumber: 100, isOutbound: true);

        try
        {
            var parsed = dispatcher.DispatchCapturedPacket(packet);

            Assert.False(parsed);
            Assert.Empty(store.CombatPacketsByTarget);
            Assert.False(CaptureConnectionGate.IsLocked);
        }
        finally
        {
            packet.Return();
            CaptureConnectionGate.Unlock();
        }
    }

    [Fact]
    public void Continues_To_Parse_Inbound_Payload_Into_Combat_Metrics()
    {
        var store = new CombatMetricsStore();
        var dispatcher = new PacketCaptureDispatcher(store);
        var packet = CreatePacket(InboundConnection, HexHelper.FromFixture("combat/0538-dot.hex"), sequenceNumber: 200, isOutbound: false);

        try
        {
            var parsed = dispatcher.DispatchCapturedPacket(packet);

            Assert.True(parsed);
            Assert.True(store.CombatPacketsByTarget.TryGetValue(17640, out var packets));
            Assert.Single(packets);
        }
        finally
        {
            packet.Return();
            CaptureConnectionGate.Unlock();
        }
    }

    private static CapturedPacket CreatePacket(TcpConnection connection, byte[] payload, uint sequenceNumber, bool isOutbound)
    {
        var owner = MemoryPool<byte>.Shared.Rent(payload.Length);
        payload.AsSpan().CopyTo(owner.Memory.Span);
        return CapturedPacket.Create(connection, owner, 0, payload.Length, sequenceNumber, acknowledgmentNumber: 0, isOutbound);
    }
}
