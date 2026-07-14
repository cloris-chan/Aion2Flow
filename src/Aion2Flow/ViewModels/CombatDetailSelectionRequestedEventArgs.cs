using Cloris.Aion2Flow.SceneRuntime.Combat;

namespace Cloris.Aion2Flow.ViewModels;

public sealed class CombatDetailSelectionRequestedEventArgs(
    CombatContributionCategory category,
    SkillBaseKey? skillBaseKey,
    string? skillDisplayName) : EventArgs
{
    public CombatContributionCategory Category { get; } = category;

    public SkillBaseKey? SkillBaseKey { get; } = skillBaseKey;

    public string? SkillDisplayName { get; } = skillDisplayName;
}
