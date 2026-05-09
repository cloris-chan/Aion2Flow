using System.Buffers;
using Cloris.Aion2Flow.Capture;
using Cloris.Aion2Flow.Capture.Streams;
using Cloris.Aion2Flow.SceneRuntime;
using Cloris.Aion2Flow.SceneRuntime.Observation;
using Cloris.Aion2Flow.Tests.Protocol;

namespace Cloris.Aion2Flow.Tests.Capture;

public sealed class PacketCaptureDispatcherTests
{
    private static readonly TcpConnection InboundConnection = new(0x0100007f, 0x0100007f, 57080, 49820);

    [Fact]
    public void Continues_To_Parse_Inbound_Payload_Into_Scene_Journal()
    {
        var scene = new SceneLiveReadModel();
        var dispatcher = new PacketCaptureDispatcher(SceneSinkFactory.CreateForLive(scene));
        var packet = CreatePacket(InboundConnection, HexHelper.FromFixture("combat/0538-dot.hex"), sequenceNumber: 200);

        try
        {
            var parsed = dispatcher.DispatchCapturedPacket(packet);

            Assert.True(parsed);
            scene.Owner.Refresh();
            Assert.Contains(scene.Owner.Combat.Events, static e => e.TargetId == 17640);
        }
        finally
        {
            packet.Return();
            CaptureConnectionGate.Unlock();
        }
    }

    [Fact]
    public void Uses_Runtime_Sink_Factory_For_New_Stream()
    {
        var scene = new SceneLiveReadModel();
        var factoryCalls = 0;
        var dispatcher = new PacketCaptureDispatcher(() =>
        {
            factoryCalls++;
            return scene.Synchronize(new JournalingRuntimeObservationSink(scene.Journal, scene.Clock, () => scene.SessionId, scene.NextBatchOrdinal));
        });
        var packet = CreatePacket(InboundConnection, HexHelper.FromFixture("combat/0538-dot.hex"), sequenceNumber: 200);

        try
        {
            var parsed = dispatcher.DispatchCapturedPacket(packet);

            Assert.True(parsed);
            Assert.Equal(1, factoryCalls);
            scene.Owner.Refresh();
            Assert.Contains(scene.Owner.Combat.Events, static e => e.TargetId == 17640);
        }
        finally
        {
            packet.Return();
            CaptureConnectionGate.Unlock();
        }
    }

    private static CapturedPacket CreatePacket(TcpConnection connection, byte[] payload, uint sequenceNumber)
    {
        var owner = MemoryPool<byte>.Shared.Rent(payload.Length);
        payload.AsSpan().CopyTo(owner.Memory.Span);
        return CapturedPacket.Create(connection, owner, 0, payload.Length, sequenceNumber);
    }
}
