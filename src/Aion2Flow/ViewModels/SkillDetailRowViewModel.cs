using CommunityToolkit.Mvvm.ComponentModel;

namespace Cloris.Aion2Flow.ViewModels;

public sealed partial class SkillDetailRowViewModel : ObservableObject
{
    public int SkillCode { get; set; }

    [ObservableProperty]
    public partial string SkillName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial long TotalAmount { get; set; }

    [ObservableProperty]
    public partial long DirectAmount { get; set; }

    [ObservableProperty]
    public partial long PeriodicAmount { get; set; }

    [ObservableProperty]
    public partial long DrainAmount { get; set; }

    [ObservableProperty]
    public partial long RegenerationAmount { get; set; }

    [ObservableProperty]
    public partial long ShieldAmount { get; set; }

    [ObservableProperty]
    public partial long ShieldAbsorbedAmount { get; set; }

    [ObservableProperty]
    public partial int Hits { get; set; }

    [ObservableProperty]
    public partial int Attempts { get; set; }

    [ObservableProperty]
    public partial int PeriodicHits { get; set; }

    [ObservableProperty]
    public partial int Evades { get; set; }

    [ObservableProperty]
    public partial int Invincible { get; set; }

    [ObservableProperty]
    public partial int Criticals { get; set; }

    [ObservableProperty]
    public partial int Back { get; set; }

    [ObservableProperty]
    public partial int Parry { get; set; }

    [ObservableProperty]
    public partial int Perfect { get; set; }

    [ObservableProperty]
    public partial int Smite { get; set; }

    [ObservableProperty]
    public partial int MultiHit { get; set; }

    [ObservableProperty]
    public partial int Endurance { get; set; }

    [ObservableProperty]
    public partial int Regeneration { get; set; }

    [ObservableProperty]
    public partial int Block { get; set; }

    [ObservableProperty]
    public partial double SharePercent { get; set; }

    [ObservableProperty]
    public partial double CriticalRate { get; set; }

    [ObservableProperty]
    public partial double BackRate { get; set; }

    [ObservableProperty]
    public partial double ParryRate { get; set; }

    [ObservableProperty]
    public partial double PerfectRate { get; set; }

    [ObservableProperty]
    public partial double SmiteRate { get; set; }

    [ObservableProperty]
    public partial double MultiHitRate { get; set; }

    [ObservableProperty]
    public partial double EnduranceRate { get; set; }

    [ObservableProperty]
    public partial double RegenerationRate { get; set; }

    [ObservableProperty]
    public partial double BlockRate { get; set; }

    [ObservableProperty]
    public partial double EvadeRate { get; set; }

    [ObservableProperty]
    public partial double InvincibleRate { get; set; }

    public void ApplyFrom(in SkillDetailRowData data)
    {
        SkillCode = data.SkillCode;
        SkillName = data.SkillName;
        TotalAmount = data.TotalAmount;
        DirectAmount = data.DirectAmount;
        PeriodicAmount = data.PeriodicAmount;
        DrainAmount = data.DrainAmount;
        RegenerationAmount = data.RegenerationAmount;
        ShieldAmount = data.ShieldAmount;
        ShieldAbsorbedAmount = data.ShieldAbsorbedAmount;
        Hits = data.Hits;
        Attempts = data.Attempts;
        PeriodicHits = data.PeriodicHits;
        Evades = data.Evades;
        Invincible = data.Invincible;
        Criticals = data.Criticals;
        Back = data.Back;
        Parry = data.Parry;
        Perfect = data.Perfect;
        Smite = data.Smite;
        MultiHit = data.MultiHit;
        Endurance = data.Endurance;
        Regeneration = data.Regeneration;
        Block = data.Block;
        SharePercent = data.SharePercent;

        CriticalRate = data.Hits > 0 ? data.Criticals / (double)data.Hits : 0d;
        BackRate = data.Hits > 0 ? data.Back / (double)data.Hits : 0d;
        ParryRate = data.Hits > 0 ? data.Parry / (double)data.Hits : 0d;
        PerfectRate = data.Hits > 0 ? data.Perfect / (double)data.Hits : 0d;
        SmiteRate = data.Hits > 0 ? data.Smite / (double)data.Hits : 0d;
        MultiHitRate = data.Hits > 0 ? data.MultiHit / (double)data.Hits : 0d;
        EnduranceRate = data.Hits > 0 ? data.Endurance / (double)data.Hits : 0d;
        RegenerationRate = data.Hits > 0 ? data.Regeneration / (double)data.Hits : 0d;
        BlockRate = data.Hits > 0 ? data.Block / (double)data.Hits : 0d;
        EvadeRate = data.Attempts > 0 ? data.Evades / (double)data.Attempts : 0d;
        InvincibleRate = data.Attempts > 0 ? data.Invincible / (double)data.Attempts : 0d;
    }
}

public struct SkillDetailRowData
{
    public int SkillCode;
    public string SkillName;
    public long TotalAmount;
    public long DirectAmount;
    public long PeriodicAmount;
    public long DrainAmount;
    public long RegenerationAmount;
    public long ShieldAmount;
    public long ShieldAbsorbedAmount;
    public int Hits;
    public int Attempts;
    public int PeriodicHits;
    public int Evades;
    public int Invincible;
    public int Criticals;
    public int Back;
    public int Parry;
    public int Perfect;
    public int Smite;
    public int MultiHit;
    public int Endurance;
    public int Regeneration;
    public int Block;
    public double SharePercent;
}
