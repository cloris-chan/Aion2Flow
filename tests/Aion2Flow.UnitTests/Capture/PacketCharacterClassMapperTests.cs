using Cloris.Aion2Flow.Capture.Streams;
using Cloris.Aion2Flow.SceneRuntime.Model;

namespace Cloris.Aion2Flow.Tests.Capture;

public sealed class PacketCharacterClassMapperTests
{
    [Theory]
    [InlineData(5, CharacterClass.Gladiator)]
    [InlineData(8, CharacterClass.Gladiator)]
    [InlineData(9, CharacterClass.Templar)]
    [InlineData(12, CharacterClass.Templar)]
    [InlineData(13, CharacterClass.Ranger)]
    [InlineData(16, CharacterClass.Ranger)]
    [InlineData(17, CharacterClass.Assassin)]
    [InlineData(20, CharacterClass.Assassin)]
    [InlineData(21, CharacterClass.Elementalist)]
    [InlineData(24, CharacterClass.Elementalist)]
    [InlineData(25, CharacterClass.Sorcerer)]
    [InlineData(28, CharacterClass.Sorcerer)]
    [InlineData(29, CharacterClass.Cleric)]
    [InlineData(32, CharacterClass.Cleric)]
    [InlineData(33, CharacterClass.Chanter)]
    [InlineData(36, CharacterClass.Chanter)]
    public void Maps_PcMetadata_Class_Code_Bands(int code, CharacterClass expected)
    {
        Assert.Equal(expected, PacketCharacterClassMapper.ToCharacterClass(code));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(4)]
    [InlineData(37)]
    public void Rejects_Out_Of_Band_Codes(int? code)
    {
        Assert.Null(PacketCharacterClassMapper.ToCharacterClass(code));
    }
}
