using Cloris.Aion2Flow.SceneRuntime.Model;

namespace Cloris.Aion2Flow.Capture.Streams;

internal static class PacketCharacterClassMapper
{
    public static CharacterClass? ToCharacterClass(int? classCode)
    {
        return classCode switch
        {
            >= 5 and <= 8 => CharacterClass.Gladiator,
            >= 9 and <= 12 => CharacterClass.Templar,
            >= 13 and <= 16 => CharacterClass.Ranger,
            >= 17 and <= 20 => CharacterClass.Assassin,
            >= 21 and <= 24 => CharacterClass.Elementalist,
            >= 25 and <= 28 => CharacterClass.Sorcerer,
            >= 29 and <= 32 => CharacterClass.Cleric,
            >= 33 and <= 36 => CharacterClass.Chanter,
            >= 45 and <= 48 => CharacterClass.Brawler,
            _ => null
        };
    }
}
