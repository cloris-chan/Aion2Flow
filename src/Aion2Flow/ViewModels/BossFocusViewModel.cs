using Cloris.Aion2Flow.Presentation;

namespace Cloris.Aion2Flow.ViewModels;

public sealed class BossFocusViewModel : FrameBatchedObservableObject
{
    private static readonly ProgressSegment[] EmptySegments = [];

    public BossFocusViewModel(UiFrameBatchService frameBatchService, long displayKey, int instanceId, int npcCode, int instanceCount, long hp, long maxHp, bool hasHp, bool hasMaxHp)
        : base(frameBatchService)
    {
        DisplayKey = displayKey;
        InstanceId = instanceId;
        NpcCode = npcCode;
        InstanceCount = instanceCount;
        Apply(hp, maxHp, hasHp, hasMaxHp);
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
                QueueFramePropertyChanged(nameof(IsHpVisible));
                QueueFramePropertyChanged(nameof(IsMaxHpVisible));
                QueueFramePropertyChanged(nameof(IsMaxHpUnknown));
                QueueFramePropertyChanged(nameof(IsHpUnknown));
            }
        }
    }

    public bool HasMaxHp
    {
        get;
        set
        {
            if (SetFrameProperty(ref field, value))
            {
                QueueFramePropertyChanged(nameof(IsMaxHpVisible));
                QueueFramePropertyChanged(nameof(IsMaxHpUnknown));
                QueueFramePropertyChanged(nameof(IsHpUnknown));
            }
        }
    }

    public double HpRatio
    {
        get;
        set => SetFrameProperty(ref field, value);
    }

    public bool IsHpVisible => HasHp;

    public bool IsMaxHpVisible => HasHp && HasMaxHp;

    public bool IsMaxHpUnknown => HasHp && !HasMaxHp;

    public bool IsHpUnknown => !HasHp;

    public IReadOnlyList<ProgressSegment> BarSegments
    {
        get;
        private set => SetFrameProperty(ref field, value);
    } = EmptySegments;

    public void Update(int instanceId, int npcCode, int instanceCount, long hp, long maxHp, bool hasHp, bool hasMaxHp)
    {
        InstanceId = instanceId;
        NpcCode = npcCode;
        InstanceCount = instanceCount;
        Apply(hp, maxHp, hasHp, hasMaxHp);
    }

    public void UpdateSegments(IReadOnlyList<ProgressSegment> segments)
    {
        if (!AreSameSegments(BarSegments, segments))
            BarSegments = segments.Count == 0 ? EmptySegments : [.. segments];
    }

    private void Apply(long hp, long maxHp, bool hasHp, bool hasMaxHp)
    {
        var resolvedMaxHp = Math.Max(1, maxHp);
        if (hasHp && hasMaxHp)
        {
            var resolvedHp = Math.Max(0, hp);
            var resolvedHpRatio = Math.Clamp(resolvedHp / (double)resolvedMaxHp, 0d, 1d);
            ApplyValues(resolvedHp, resolvedMaxHp, hasHp: true, hasMaxHp: true, resolvedHpRatio);
        }
        else if (hasHp)
        {
            ApplyValues(Math.Max(0, hp), 1, hasHp: true, hasMaxHp: false, 0);
        }
        else
        {
            ApplyValues(0, 1, hasHp: false, hasMaxHp: false, 0);
        }
    }

    private void ApplyValues(double hp, double maxHp, bool hasHp, bool hasMaxHp, double hpRatio)
    {
        Hp = hp;
        MaxHp = maxHp;
        HasHp = hasHp;
        HasMaxHp = hasMaxHp;
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
