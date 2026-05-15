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
    private static readonly TcpConnection SecondInboundConnection = new(0x0100007f, 0x0100007f, 57081, 49820);

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

    [Fact]
    public void New_Parsed_Inbound_Connection_Appends_Transport_Boundary()
    {
        var scene = new SceneLiveReadModel();
        var dispatcher = new PacketCaptureDispatcher(SceneSinkFactory.CreateForLive(scene));
        var payload = HexHelper.FromFixture("combat/0538-dot.hex");
        var first = CreatePacket(InboundConnection, payload, sequenceNumber: 200);
        var second = CreatePacket(SecondInboundConnection, payload, sequenceNumber: 200);

        try
        {
            Assert.True(dispatcher.DispatchCapturedPacket(first));
            CaptureConnectionGate.Unlock();

            Assert.True(dispatcher.DispatchCapturedPacket(second));

            var found = false;
            for (var i = 0; i < scene.Journal.Count; i++)
            {
                if (scene.Journal.Read(i).Scene?.DiagnosticKey == "scene-transport-boundary")
                {
                    found = true;
                    break;
                }
            }

            Assert.True(found);
        }
        finally
        {
            first.Return();
            second.Return();
            CaptureConnectionGate.Unlock();
        }
    }

    [Fact]
    public void Parsed_New_Inbound_Connection_Switches_Locked_Connection()
    {
        var scene = new SceneLiveReadModel();
        var dispatcher = new PacketCaptureDispatcher(SceneSinkFactory.CreateForLive(scene));
        var payload = HexHelper.FromFixture("combat/0538-dot.hex");
        var first = CreatePacket(InboundConnection, payload, sequenceNumber: 200);
        var second = CreatePacket(SecondInboundConnection, payload, sequenceNumber: 200);

        try
        {
            Assert.True(dispatcher.DispatchCapturedPacket(first));
            Assert.True(CaptureConnectionGate.TryGetLockedConnection(out var locked));
            Assert.True(locked.IsSameConnection(InboundConnection, out _));

            Assert.True(dispatcher.DispatchCapturedPacket(second));

            Assert.True(CaptureConnectionGate.TryGetLockedConnection(out locked));
            Assert.True(locked.IsSameConnection(SecondInboundConnection, out _));
            Assert.Contains(Enumerable.Range(0, scene.Journal.Count), i => scene.Journal.Read(i).Scene?.DiagnosticKey == "scene-transport-boundary");
        }
        finally
        {
            first.Return();
            second.Return();
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
