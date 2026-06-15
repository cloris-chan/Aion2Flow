using Cloris.Aion2Flow.Presentation;

namespace Cloris.Aion2Flow.ViewModels;

public sealed class BossFocusViewModel : FrameBatchedObservableObject
{
    private static readonly ProgressSegment[] EmptySegments = [];

    public BossFocusViewModel(UiFrameBatchService frameBatchService, long displayKey, int instanceId, int npcCode, int instanceCount, int hp, int maxHp, bool hasHp)
        : base(frameBatchService)
    {
        DisplayKey = displayKey;
        InstanceId = instanceId;
        NpcCode = npcCode;
        InstanceCount = instanceCount;
        Apply(hp, maxHp, hasHp);
    }

    public long DisplayKey { get; }

    public int InstanceId
    {
        get;
        set => SetFrameProperty(ref field, value);
    }

    public int NpcCode
    {
        get;
        set => SetFrameProperty(ref field, value);
    }

    public int InstanceCount
    {
        get;
        set
        {
            if (SetFrameProperty(ref field, Math.Max(1, value)))
            {
                QueueFramePropertyChanged(nameof(HasMultipleInstances));
                QueueFramePropertyChanged(nameof(InstanceCountText));
            }
        }
    } = 1;

    public bool HasMultipleInstances => InstanceCount > 1;

    public string InstanceCountText => $"x{InstanceCount}";

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

    public void Update(int instanceId, int npcCode, int instanceCount, int hp, int maxHp, bool hasHp)
    {
        InstanceId = instanceId;
        NpcCode = npcCode;
        InstanceCount = instanceCount;
        Apply(hp, maxHp, hasHp);
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
