using Cloris.Aion2Flow.Protocol.Packets;

namespace Cloris.Aion2Flow.Tests.Capture;

public sealed class PacketIdentityParserTests
{
    [Fact]
    public void Packet048D_Reads_OriginServerId_And_Faction_Field()
    {
        var packet = Convert.FromHexString("2E048DC9BF07408E0301992DEF030CE8AC9DE8AC9DE683A0E9A1A706416574686572010000000000000100");

        Assert.True(Packet048DNicknameParser.TryParse(packet, out var parsed));
        Assert.Equal(5785, parsed.PlayerId);
        Assert.Equal("謝謝惠顧", parsed.Nickname);
        Assert.Equal(1007, parsed.OriginServerId);
        Assert.Equal(1, parsed.FactionCode);
    }

    [Fact]
    public void Packet048D_Prefers_Direct_Faction_Field_Over_OriginServerId()
    {
        var packet = Convert.FromHexString("2E048DC9BF07408E0301992DEF030CE8AC9DE8AC9DE683A0E9A1A706416574686572020000000000000100");

        Assert.True(Packet048DNicknameParser.TryParse(packet, out var parsed));
        Assert.Equal(1007, parsed.OriginServerId);
        Assert.Equal(2, parsed.FactionCode);
    }

    [Fact]
    public void Packet3336_Reads_OriginServerId_Class_And_Faction_Field()
    {
        var packet = Convert.FromHexString("C40E3336C724DFA1C1080706636C6F726973EF0316000000012D0000003C0800003C0800002D00000000000000000000000100000000000000000000005001700019000B0002010100000000000000000000000000050101000000000000000000000002010000000000000000000000033500FF0000000000000000000478000000000000000000000005010000000000000000000000000000000000000000000000000000000580520E558347E463984600DF0547DAB813430C6901906C90");

        Assert.True(Packet3336NicknameParser.TryParse(packet, out var parsed));
        Assert.Equal(4679, parsed.PlayerId);
        Assert.Equal("cloris", parsed.Nickname);
        Assert.Equal(1007, parsed.OriginServerId);
        Assert.Equal(22, parsed.ClassCode);
        Assert.Equal(1, parsed.FactionCode);
    }

    [Fact]
    public void Packet4436_Reads_Faction_Field_After_Class()
    {
        var packet = Convert.FromHexString("F50C4436C9060320A401070CE7BAA2E8B186E586B0E7B3951E000000010280D2F65F9446F9D95847000055C509005C421C2701F1E501F1E501E7190000E7190000588C0200588C0200F0490200F049020002000000A0860100A08601008A420600B08F060001000000016C4ECB0125D1CC01D1FFA700EF03FA0200000000EF03064165746865720100020000000000000000000000000000001111112BC4C901FFFFFFFFFFFFFFFF8075D52ABB030000C9060100CE941848F0CC08480038F046");

        Assert.True(Packet4436NicknameParser.TryParse(packet, out var parsed));
        Assert.Equal(841, parsed.PlayerId);
        Assert.Equal("红豆冰糕", parsed.Nickname);
        Assert.Equal(30, parsed.ClassCode);
        Assert.Null(parsed.OriginServerId);
        Assert.Equal(1, parsed.FactionCode);
    }

    [Fact]
    public void Packet4536_Reads_Faction_Field_After_Class()
    {
        var packet = Convert.FromHexString("8F0D4536710120A00107074C79736869636B18000000020280D2C7A965470E7593460000F6461B53D84151B401DC7701DC77011815000018150000588C0200588C020000000000F049020001000000A0860100A086010070F3050070F30500010000000112E2CF016C4ECB01EF0311110BC8C901FFFFFFFFFFFFFFFF8075D52ABB03000071010100A7547B4790A4964600EAF546111153C4C901FFFFFFFFFFFFFFFF8075D52ABB030000710101A7547B4790A4964600EAF546");

        Assert.True(Packet4536PcMetadataParser.TryParse(packet, out var parsed));
        Assert.Equal(113, parsed.EntityId);
        Assert.Equal("Lyshick", parsed.Nickname);
        Assert.Equal(24, parsed.ClassCode);
        Assert.Null(parsed.OriginServerId);
        Assert.Equal(2, parsed.FactionCode);
    }

    [Fact]
    public void Packet4536_Reads_Faction_Field_Immediately_After_Class()
    {
        var packet = Convert.FromHexString("F90F4536EF430320A001070549326F736506000000010240D2880D62C73FB9A3C700741B461BFA3943408401C2EB02C2EB02551C0000551C0000588C0200588C0200F0490200F049020002000000A0860100A0860100A0BB0D00A0BB0D0001010565D101F4DFCF01E7175302EF03F00100000000EF0307456D5065526F720100020000000000000000000000000000000F1112D7C7C901FFFFFFFFFFFFFFFF8075D52ABB030000EF4301000080114400F8BEC580149A471113E1C7C901FFFFFF");

        Assert.True(Packet4536PcMetadataParser.TryParse(packet, out var parsed));
        Assert.Equal(8687, parsed.EntityId);
        Assert.Equal("I2ose", parsed.Nickname);
        Assert.Equal(6, parsed.ClassCode);
        Assert.Equal(1, parsed.FactionCode);
    }
}
