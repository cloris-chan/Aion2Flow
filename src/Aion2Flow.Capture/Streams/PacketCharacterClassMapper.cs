using Cloris.Aion2Flow.SceneRuntime.Model;

namespace Cloris.Aion2Flow.Capture.Streams;

internal static class PacketCharacterClassMapper
{
    public static CharacterClass? ToCharacterClass(int? classCode)
    {
        if (classCode is not { } code || code is < 5 or > 36)
            return null;

        return ((code - 5) >> 2) switch
        {
            0 => CharacterClass.Gladiator,
            1 => CharacterClass.Templar,
            2 => CharacterClass.Ranger,
            3 => CharacterClass.Assassin,
            4 => CharacterClass.Elementalist,
            5 => CharacterClass.Sorcerer,
            6 => CharacterClass.Cleric,
            7 => CharacterClass.Chanter,
            _ => null
        };
    }
}
