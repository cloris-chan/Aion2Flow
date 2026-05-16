using Cloris.Aion2Flow.Protocol.Packets;

namespace Cloris.Aion2Flow.Tests.Protocol;

public sealed class Packet048DNicknameParserTests
{
    [Fact]
    public void Parses_Cross_Server_Own_Nickname_With_Inserted_Field()
    {
        var packet = Convert.FromHexString("28048DBD850174ABC600D84CEF0306E99B85E6988206416574686572010000000000000100");

        var ok = Packet048DNicknameParser.TryParse(packet, out var parsed);

        Assert.True(ok);
        Assert.Equal(9816, parsed.PlayerId);
        Assert.Equal("雅昂", parsed.Nickname);
        Assert.Equal(6, parsed.NicknameLength);
        Assert.Equal(495, parsed.OriginServerId);
        Assert.Equal(1, parsed.FactionCode);
    }

    [Fact]
    public void Parses_Cross_Server_Other_Nickname_From_Embedded_Payload()
    {
        var packet = Convert.FromHexString("048DECF60104EDC700B018EF0306E6B585E5B09D07E981A5E5858937");

        var ok = Packet048DNicknameParser.TryParsePayload(packet, out var parsed);

        Assert.True(ok);
        Assert.Equal(3120, parsed.PlayerId);
        Assert.Equal("浅尝", parsed.Nickname);
        Assert.Equal(495, parsed.OriginServerId);
    }

    [Fact]
    public void Parses_Cross_Server_Other_Nickname_With_NonDefault_Origin_Server()
    {
        var packet = Convert.FromHexString("34048D8E83027A030401DE0CD6070CE4BBA5E69C88E4B98BE5908D0CE4B88DE6BB85E9AD94E7958C020000000000000100");

        var ok = Packet048DNicknameParser.TryParse(packet, out var parsed);

        Assert.True(ok);
        Assert.Equal(1630, parsed.PlayerId);
        Assert.Equal("以月之名", parsed.Nickname);
        Assert.Equal(12, parsed.NicknameLength);
        Assert.Equal(982, parsed.OriginServerId);
        Assert.Equal(2, parsed.FactionCode);
    }

    [Fact]
    public void Parses_Dark_Faction_From_048d_Nickname_Tail()
    {
        var packet = Convert.FromHexString("2B048D8CBD0A1ABACB00C812DE0709E890A7E8AFA7E4B8B606E69A97E6B2B3020000000000000100");

        var ok = Packet048DNicknameParser.TryParse(packet, out var parsed);

        Assert.True(ok);
        Assert.Equal("萧诧丶", parsed.Nickname);
        Assert.Equal(2, parsed.FactionCode);
    }

    [Fact]
    public void Ignores_Entity_Lifecycle_048d_Frame_Without_Nickname_Fields()
    {
        var packet = Convert.FromHexString("1B048DF4C901000000000000000000000000000000000000");

        var ok = Packet048DNicknameParser.TryParse(packet, out _);

        Assert.False(ok);
    }
}
