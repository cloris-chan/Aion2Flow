using Cloris.Aion2Flow.Presentation;

namespace Cloris.Aion2Flow.ViewModels;

public sealed class BossFocusViewModel : FrameBatchedObservableObject
{
    private static readonly ProgressSegment[] EmptySegments = [];

    public BossFocusViewModel(UiFrameBatchService frameBatchService, int instanceId, int hp, int maxHp, bool hasHp)
        : base(frameBatchService)
    {
        InstanceId = instanceId;
        Apply(hp, maxHp, hasHp);
    }

    public int InstanceId { get; init; }

    public double Hp
    {
        get;
        set => SetFrameProperty(ref field, value);
    }

    public double MaxHp
    {
        get;
        set => SetFrameProperty(ref field, value);
    } = 1d;

    public bool HasHp
    {
        get;
        set
        {
            if (SetFrameProperty(ref field, value))
            {
                QueueFramePropertyChanged(nameof(IsHpUnknown));
            }
        }
    }

    public double HpRatio
    {
        get;
        set => SetFrameProperty(ref field, value);
    }

    public bool IsHpUnknown => !HasHp;

    public IReadOnlyList<ProgressSegment> BarSegments
    {
        get;
        private set => SetFrameProperty(ref field, value);
    } = EmptySegments;

    public void Update(int hp, int maxHp) => Update(hp, maxHp, hasHp: true);

    public void Update(int hp, int maxHp, bool hasHp) => Apply(hp, maxHp, hasHp);

    public void Clear()
    {
        Hp = 0;
        MaxHp = 1;
        HpRatio = 0;
        HasHp = false;
        BarSegments = EmptySegments;
    }

    public void UpdateSegments(IReadOnlyList<ProgressSegment> segments)
    {
        if (!AreSameSegments(BarSegments, segments))
            BarSegments = segments.Count == 0 ? EmptySegments : [.. segments];
    }

    private void Apply(int hp, int maxHp, bool hasHp)
    {
        var resolvedMaxHp = Math.Max(1, maxHp);
        if (hasHp)
        {
            var resolvedHp = Math.Max(0, hp);
            var resolvedHpRatio = Math.Clamp(resolvedHp / (double)resolvedMaxHp, 0d, 1d);
            ApplyValues(resolvedHp, resolvedMaxHp, hasHp: true, resolvedHpRatio);
        }
        else
        {
            ApplyValues(0, 1, hasHp: false, 0);
        }
    }

    private void ApplyValues(double hp, double maxHp, bool hasHp, double hpRatio)
    {
        Hp = hp;
        MaxHp = maxHp;
        HasHp = hasHp;
        HpRatio = hpRatio;
    }

    private static bool AreSameSegments(IReadOnlyList<ProgressSegment> left, IReadOnlyList<ProgressSegment> right)
    {
        if (left.Count != right.Count)
            return false;

        for (var i = 0; i < left.Count; i++)
        {
            if (Math.Abs(left[i].Ratio - right[i].Ratio) > 0.000_001 || !ReferenceEquals(left[i].Brush, right[i].Brush))
                return false;
        }

        return true;
    }
}
