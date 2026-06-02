using Avalonia.Media;
using Cloris.Aion2Flow.Presentation;
using Cloris.Aion2Flow.SceneRuntime.Model;

namespace Cloris.Aion2Flow.ViewModels;

public sealed class CombatantRowViewModel(UiFrameBatchService frameBatchService, CombatantColumnLayoutViewModel columns, int id, CharacterClass? characterClass, double damagePerSecond, double healingPerSecond, double damage, double healing) : FrameBatchedObservableObject(frameBatchService)
{
    private static readonly ProgressSegment[] EmptySegments = [];
    private static readonly CombatantBossShareViewModel[] EmptyBossShares = [];

    public CombatantColumnLayoutViewModel Columns { get; } = columns;

    public int Id { get; set; } = id;

    public CharacterClass? CharacterClass
    {
        get;
        set => SetFrameProperty(ref field, value);
    } = characterClass;

    public double DamagePerSecond
    {
        get;
        set => SetFrameProperty(ref field, value);
    } = damagePerSecond;

    public double HealingPerSecond
    {
        get;
        set => SetFrameProperty(ref field, value);
    } = healingPerSecond;

    public double Damage
    {
        get;
        set => SetFrameProperty(ref field, value);
    } = damage;

    public double Healing
    {
        get;
        set => SetFrameProperty(ref field, value);
    } = healing;

    public IReadOnlyList<ProgressSegment> BarSegments
    {
        get;
        private set => SetFrameProperty(ref field, value);
    } = EmptySegments;

    public IReadOnlyList<CombatantBossShareViewModel> BossShares
    {
        get;
        private set => SetFrameProperty(ref field, value);
    } = EmptyBossShares;

    public void UpdateBar(double ratio, IBrush brush)
    {
        var resolvedRatio = Math.Clamp(ratio, 0d, 1d);
        if (resolvedRatio <= 0)
        {
            if (BarSegments.Count != 0)
                BarSegments = EmptySegments;
            return;
        }

        var segments = BarSegments;
        if (segments.Count == 1 && Math.Abs(segments[0].Ratio - resolvedRatio) <= 0.000_001 && ReferenceEquals(segments[0].Brush, brush))
            return;

        BarSegments = [new ProgressSegment(resolvedRatio, brush)];
    }

    public void UpdateBossShares(IReadOnlyList<CombatantBossShareViewModel> shares)
    {
        if (AreSameBossShares(BossShares, shares))
            return;

        BossShares = shares.Count == 0 ? EmptyBossShares : [.. shares];
    }

    private static bool AreSameBossShares(IReadOnlyList<CombatantBossShareViewModel> left, IReadOnlyList<CombatantBossShareViewModel> right)
    {
        if (left.Count != right.Count)
            return false;

        for (var i = 0; i < left.Count; i++)
        {
            if (left[i].BossId != right[i].BossId ||
                Math.Abs(left[i].Ratio - right[i].Ratio) > 0.000_001 ||
                !ReferenceEquals(left[i].Brush, right[i].Brush))
            {
                return false;
            }
        }

        return true;
    }
}
