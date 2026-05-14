using Cloris.Aion2Flow.SceneRuntime.Model;

namespace Cloris.Aion2Flow.ViewModels;

public sealed class CombatantRowViewModel : FrameBatchedObservableObject
{
    public CombatantRowViewModel(
        UiFrameBatchService frameBatchService,
        int id,
        CharacterClass? characterClass,
        double damagePerSecond,
        double healingPerSecond,
        double damage,
        double healing,
        double damageContribution)
        : base(frameBatchService)
    {
        Id = id;
        CharacterClass = characterClass;
        DamagePerSecond = damagePerSecond;
        HealingPerSecond = healingPerSecond;
        Damage = damage;
        Healing = healing;
        DamageContribution = damageContribution;
    }

    public int Id { get; set; }

    public CharacterClass? CharacterClass
    {
        get;
        set => SetFrameProperty(ref field, value);
    }

    public double DamagePerSecond
    {
        get;
        set => SetFrameProperty(ref field, value);
    }

    public double HealingPerSecond
    {
        get;
        set => SetFrameProperty(ref field, value);
    }

    public double Damage
    {
        get;
        set => SetFrameProperty(ref field, value);
    }

    public double Healing
    {
        get;
        set => SetFrameProperty(ref field, value);
    }

    public double DamageContribution
    {
        get;
        set => SetFrameProperty(ref field, value);
    }
}
