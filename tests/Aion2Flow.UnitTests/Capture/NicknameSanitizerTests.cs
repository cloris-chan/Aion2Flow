using System.Text;
using Cloris.Aion2Flow.Protocol.Packets;

namespace Cloris.Aion2Flow.Tests.Capture;

public sealed class NicknameSanitizerTests
{
    [Theory]
    [InlineData("空")]
    [InlineData("𤅊")]
    [InlineData("𠔻")]
    [InlineData("xD")]
    [InlineData("AD")]
    [InlineData("123")]
    [InlineData("梵丶")]
    public void SanitizeExact_Accepts_Exact_Length_Identity_Text(string nickname)
    {
        Assert.Equal(nickname, NicknameSanitizer.SanitizeExact(nickname));
    }

    [Fact]
    public void TryReadLengthPrefixedNickname_Accepts_Supplementary_Plane_Han()
    {
        var encoded = Encoding.UTF8.GetBytes("𤅊");
        Span<byte> packet = stackalloc byte[1 + encoded.Length];
        packet[0] = (byte)encoded.Length;
        encoded.CopyTo(packet[1..]);

        Assert.True(NicknameParserUtil.TryReadLengthPrefixedNickname(packet, 0, out var nickname, out var nicknameLength, out var tailOffset));
        Assert.Equal("𤅊", nickname);
        Assert.Equal(encoded.Length, nicknameLength);
        Assert.Equal(packet.Length, tailOffset);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("A B")]
    [InlineData("abc\u0000")]
    [InlineData("abc\uFFFD")]
    [InlineData("\uD850")]
    public void SanitizeExact_Rejects_Invalid_Identity_Text(string nickname)
    {
        Assert.Null(NicknameSanitizer.SanitizeExact(nickname));
    }
}
