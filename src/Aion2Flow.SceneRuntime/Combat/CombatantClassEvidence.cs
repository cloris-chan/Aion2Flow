using Cloris.Aion2Flow.Resources.Catalog;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.SceneRuntime.Combat;

public struct CombatantClassEvidence
{
    private int _gladiator;
    private int _templar;
    private int _assassin;
    private int _ranger;
    private int _sorcerer;
    private int _elementalist;
    private int _cleric;
    private int _chanter;
    private int _brawler;

    public void Add(CharacterClass characterClass, int score)
    {
        if (score <= 0)
            return;

        switch (characterClass)
        {
            case CharacterClass.Gladiator:
                _gladiator += score;
                break;
            case CharacterClass.Templar:
                _templar += score;
                break;
            case CharacterClass.Assassin:
                _assassin += score;
                break;
            case CharacterClass.Ranger:
                _ranger += score;
                break;
            case CharacterClass.Sorcerer:
                _sorcerer += score;
                break;
            case CharacterClass.Elementalist:
                _elementalist += score;
                break;
            case CharacterClass.Cleric:
                _cleric += score;
                break;
            case CharacterClass.Chanter:
                _chanter += score;
                break;
            case CharacterClass.Brawler:
                _brawler += score;
                break;
        }
    }

    public readonly CharacterClass? Resolve()
    {
        CharacterClass? topClass = null;
        var topScore = 0;
        var secondScore = 0;

        Consider(CharacterClass.Gladiator, _gladiator, ref topClass, ref topScore, ref secondScore);
        Consider(CharacterClass.Templar, _templar, ref topClass, ref topScore, ref secondScore);
        Consider(CharacterClass.Assassin, _assassin, ref topClass, ref topScore, ref secondScore);
        Consider(CharacterClass.Ranger, _ranger, ref topClass, ref topScore, ref secondScore);
        Consider(CharacterClass.Sorcerer, _sorcerer, ref topClass, ref topScore, ref secondScore);
        Consider(CharacterClass.Elementalist, _elementalist, ref topClass, ref topScore, ref secondScore);
        Consider(CharacterClass.Cleric, _cleric, ref topClass, ref topScore, ref secondScore);
        Consider(CharacterClass.Chanter, _chanter, ref topClass, ref topScore, ref secondScore);
        Consider(CharacterClass.Brawler, _brawler, ref topClass, ref topScore, ref secondScore);

        if (topClass is null || topScore < 4)
            return null;

        return topScore - secondScore >= 2 ? topClass.Value : null;

        static void Consider(CharacterClass candidateClass, int candidateScore, ref CharacterClass? topClass, ref int topScore, ref int secondScore)
        {
            if (candidateScore <= 0)
                return;

            if (topClass is null || candidateScore > topScore || candidateScore == topScore && candidateClass < topClass.Value)
            {
                secondScore = topScore;
                topClass = candidateClass;
                topScore = candidateScore;
                return;
            }

            if (candidateScore > secondScore)
                secondScore = candidateScore;
        }
    }

    public static bool TryCreate(
        in CombatWireObservation observation,
        in CombatContribution contribution,
        out CharacterClass characterClass,
        out int score)
    {
        characterClass = default;
        score = 0;

        if (!CombatResourceRegistry.SkillMap.TryGetValue(observation.SkillCode, out var skill))
            return false;

        var mappedClass = MapSkillCategoryToClass(skill.Category);
        if (mappedClass is null ||
            skill.SourceType != SkillSourceType.PcSkill ||
            observation.PeriodicRelation != PeriodicEffectRelation.None ||
            contribution.Delivery != CombatDeliveryKind.Direct)
        {
            return false;
        }

        score = contribution.Metric switch
        {
            CombatMetricKind.Damage => 6,
            CombatMetricKind.ShieldGranted or CombatMetricKind.ShieldAbsorbed => 4,
            CombatMetricKind.Healing => 3,
            _ => 0
        };

        if (score <= 0)
            return false;

        characterClass = mappedClass.Value;
        return true;
    }

    public static CharacterClass? MapSkillCategoryToClass(SkillCategory category) =>
        category switch
        {
            SkillCategory.Gladiator => CharacterClass.Gladiator,
            SkillCategory.Templar => CharacterClass.Templar,
            SkillCategory.Ranger => CharacterClass.Ranger,
            SkillCategory.Assassin => CharacterClass.Assassin,
            SkillCategory.Sorcerer => CharacterClass.Sorcerer,
            SkillCategory.Cleric => CharacterClass.Cleric,
            SkillCategory.Elementalist => CharacterClass.Elementalist,
            SkillCategory.Chanter => CharacterClass.Chanter,
            SkillCategory.Brawler => CharacterClass.Brawler,
            _ => null,
        };
}
