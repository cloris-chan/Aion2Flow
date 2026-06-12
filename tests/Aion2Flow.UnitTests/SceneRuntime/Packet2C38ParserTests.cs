using Cloris.Aion2Flow.Protocol.Packets;

namespace Cloris.Aion2Flow.UnitTests.SceneRuntime;

public sealed class Packet2C38ParserTests
{
    [Fact]
    public void TryParse_ReadsEverySimpleResultInBatch()
    {
        var packet = Convert.FromHexString("112C38D3270200B2020700B30207");

        Assert.True(Packet2C38Parser.TryParse(packet, out var batch));
        Assert.Equal(5_075, batch.EntityId);
        Assert.Equal(2, batch.ResultCount);

        Assert.True(batch.TryRead(out var first));
        Assert.Equal(0, first.ResultIndex);
        Assert.Equal(0, first.StateCode);
        Assert.Equal(306, first.InstanceSequenceId);
        Assert.Equal(7, first.ResultCode);

        Assert.True(batch.TryRead(out var second));
        Assert.Equal(1, second.ResultIndex);
        Assert.Equal(0, second.StateCode);
        Assert.Equal(307, second.InstanceSequenceId);
        Assert.Equal(7, second.ResultCode);
        Assert.False(batch.TryRead(out _));
    }

    [Fact]
    public void TryParse_ReadsStructuredResultDetail()
    {
        var packet = Convert.FromHexString("172C38D5390107B16C0BD539C6B8F8004DDF2761");

        Assert.True(Packet2C38Parser.TryParse(packet, out var batch));
        Assert.True(batch.TryRead(out var result));
        Assert.Equal(7, result.StateCode);
        Assert.Equal(13_873, result.InstanceSequenceId);
        Assert.Equal(11, result.ResultCode);
        Assert.Equal(7_381, result.DetailEntityId);
        Assert.Equal(0x00F8B8C6u, result.DetailValue0);
        Assert.Equal(0x6127DF4Du, result.DetailValue1);
        Assert.False(batch.TryRead(out _));
    }

    [Fact]
    public void TryParse_RejectsIncompleteOrTrailingResultData()
    {
        Assert.False(Packet2C38Parser.TryParse(Convert.FromHexString("112C38D3270200B20207"), out _));
        Assert.False(Packet2C38Parser.TryParse(Convert.FromHexString("122C38D3270200B2020700B3020700"), out _));
    }
}
