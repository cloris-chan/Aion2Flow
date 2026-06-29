using Cloris.Aion2Flow.Protocol.Packets;

namespace Cloris.Aion2Flow.UnitTests.SceneRuntime;

public sealed class Packet0438ImplicitDetailSidecarParserTests
{
    [Theory]
    [InlineData("1F0438E67C0000E67C474005010F0008470B6601000000DF88010100", 15974, 17121351, 15, 0, 0x00000001660B4708, 17503)]
    [InlineData("200438DEF0040000DEF0045E420F00010253E1F50501000000904E0100", 79966, 1000030, 1, 2, 0x0000000105F5E153, 10000)]
    [InlineData("1F0438E67C0000E67C9FDB0301280227CA816501000000DF88010100", 15974, 17030047, 40, 2, 0x000000016581CA27, 17503)]
    [InlineData("1F0438E67C0000E67CC1620E010D0270939E6901000000DF88010100", 15974, 17720001, 13, 2, 0x00000001699E9370, 17503)]
    [InlineData("1F0438E67C0000E67C84B105011002A557396602000000DF88010200", 15974, 17150340, 16, 2, 0x00000002663957A5, 17503, 2)]
    public void TryParse_AcceptsImplicitDetailSidecarShape(string hex, int entityId, int bodySkillVariantRaw, int marker, int type, long detailRaw, int unknown, int value = 1)
    {
        var packet = Convert.FromHexString(hex);

        Assert.True(Packet0438ImplicitDetailSidecarParser.TryParse(packet, out var parsed));
        Assert.Equal(entityId, parsed.TargetId);
        Assert.Equal(entityId, parsed.SourceId);
        Assert.Equal(0, parsed.LayoutTag);
        Assert.Equal(0, parsed.Flag);
        Assert.Equal(bodySkillVariantRaw, parsed.BodySkillVariantRaw);
        Assert.Equal(marker, parsed.Marker);
        Assert.Equal(type, parsed.Type);
        Assert.Equal(detailRaw, parsed.DetailRaw);
        Assert.Equal(unknown, parsed.Unknown);
        Assert.Equal(value, parsed.Value);
        Assert.Equal(0, parsed.Loop);
    }

    [Fact]
    public void CompactValueParser_RejectsImplicitDetailSidecarShape()
    {
        var packet = Convert.FromHexString("1F0438E67C0000E67C474005010F0008470B6601000000DF88010100");

        Assert.False(Packet0438CompactValueParser.TryParse(packet, out _));
    }

    [Fact]
    public void TryParse_RejectsRegularLayoutZeroRegenerationSynthesis()
    {
        var packet = Convert.FromHexString("200438C7240400C7246F011D008002570E220101000000F571906C0100");
        Assert.False(Packet0438ImplicitDetailSidecarParser.TryParse(packet, out _));
    }
}
