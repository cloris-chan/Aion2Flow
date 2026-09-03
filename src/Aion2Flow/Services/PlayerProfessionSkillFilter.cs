using Cloris.Aion2Flow.Resources.Catalog;

namespace Cloris.Aion2Flow.Services;

internal static class PlayerProfessionSkillFilter
{
    public static bool Includes(SkillDisplayEntry skill)
        => skill.SourceType == SkillSourceType.PcSkill &&
           skill.Category is SkillCategory.Gladiator or
               SkillCategory.Templar or
               SkillCategory.Assassin or
               SkillCategory.Ranger or
               SkillCategory.Sorcerer or
               SkillCategory.Cleric or
               SkillCategory.Chanter or
               SkillCategory.Elementalist or
               SkillCategory.Brawler;
}
