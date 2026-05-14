namespace Cloris.Aion2Flow.ViewModels;

public sealed class BossFocusViewModel : FrameBatchedObservableObject
{
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

    public void Update(int hp, int maxHp)
        => Update(hp, maxHp, hasHp: true);

    public void Update(int hp, int maxHp, bool hasHp)
        => Apply(hp, maxHp, hasHp);

    public void Clear()
    {
        Hp = 0;
        MaxHp = 1;
        HpRatio = 0;
        HasHp = false;
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
}
