using Avalonia.Media;
using Cloris.Aion2Flow.Presentation;
using Cloris.Aion2Flow.SceneRuntime.Model;

namespace Cloris.Aion2Flow.ViewModels;

public sealed class CombatantRowViewModel(UiFrameBatchService frameBatchService, CombatantColumnLayoutViewModel columns, int id, CharacterClass? characterClass, double damagePerSecond, double healingPerSecond, double damage, double healing) : FrameBatchedObservableObject(frameBatchService)
{
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

    public ProgressSegment? BarSegment
    {
        get;
        private set => SetFrameProperty(ref field, value);
    }

    public bool HasBossShare
    {
        get;
        private set => SetFrameProperty(ref field, value);
    }

    public double BossShareRatio
    {
        get;
        private set => SetFrameProperty(ref field, value);
    }

    public void UpdateBar(double ratio, IBrush brush)
    {
        var resolvedRatio = Math.Clamp(ratio, 0d, 1d);
        if (resolvedRatio <= 0)
        {
            if (BarSegment.HasValue)
                BarSegment = null;
            return;
        }

        var segment = BarSegment;
        if (segment.HasValue && Math.Abs(segment.Value.Ratio - resolvedRatio) <= 0.000_001 && ReferenceEquals(segment.Value.Brush, brush))
            return;

        BarSegment = new ProgressSegment(resolvedRatio, brush);
    }

    public void UpdateBossShare(double ratio, bool isVisible)
    {
        var resolvedRatio = Math.Max(0d, ratio);
        if (!isVisible)
        {
            if (BossShareRatio != 0)
                BossShareRatio = 0;
            if (HasBossShare)
                HasBossShare = false;
            return;
        }

        if (Math.Abs(BossShareRatio - resolvedRatio) > 0.000_001)
            BossShareRatio = resolvedRatio;
        if (!HasBossShare)
            HasBossShare = true;
    }
}
