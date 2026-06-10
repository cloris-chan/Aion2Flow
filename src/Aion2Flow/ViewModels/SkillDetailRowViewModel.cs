using Cloris.Aion2Flow.SceneRuntime.Combat;

namespace Cloris.Aion2Flow.ViewModels;

public sealed class SkillDetailRowViewModel(UiFrameBatchService frameBatchService) : FrameBatchedObservableObject(frameBatchService)
{
    public CombatActionKey ActionKey { get; set; }
    public int SkillCode { get; set; }
    public string DisplayName { get; set => SetFrameProperty(ref field, value); } = string.Empty;

    public long TotalAmount { get; set => SetFrameProperty(ref field, value); }
    public long DirectAmount { get; set => SetFrameProperty(ref field, value); }
    public long PeriodicAmount { get; set => SetFrameProperty(ref field, value); }
    public long DrainAmount { get; set => SetFrameProperty(ref field, value); }
    public long RegenerationAmount { get; set => SetFrameProperty(ref field, value); }
    public long ShieldAmount { get; set => SetFrameProperty(ref field, value); }
    public long ShieldAbsorbedAmount { get; set => SetFrameProperty(ref field, value); }
    public int Hits { get; set => SetFrameProperty(ref field, value); }
    public int Attempts { get; set => SetFrameProperty(ref field, value); }
    public int PeriodicHits { get; set => SetFrameProperty(ref field, value); }
    public int Evades { get; set => SetFrameProperty(ref field, value); }
    public int Invincible { get; set => SetFrameProperty(ref field, value); }
    public int Criticals { get; set => SetFrameProperty(ref field, value); }
    public int Back { get; set => SetFrameProperty(ref field, value); }
    public int Parry { get; set => SetFrameProperty(ref field, value); }
    public int PerfectParry { get; set => SetFrameProperty(ref field, value); }
    public int Perfect { get; set => SetFrameProperty(ref field, value); }
    public int Smite { get; set => SetFrameProperty(ref field, value); }
    public int MultiHit { get; set => SetFrameProperty(ref field, value); }
    public int Endurance { get; set => SetFrameProperty(ref field, value); }
    public int Regeneration { get; set => SetFrameProperty(ref field, value); }
    public int Block { get; set => SetFrameProperty(ref field, value); }
    public int PerfectBlock { get; set => SetFrameProperty(ref field, value); }
    public double SharePercent { get; set => SetFrameProperty(ref field, value); }
    public double CriticalRate { get; set => SetFrameProperty(ref field, value); }
    public double BackRate { get; set => SetFrameProperty(ref field, value); }
    public double ParryRate { get; set => SetFrameProperty(ref field, value); }
    public double PerfectParryRate { get; set => SetFrameProperty(ref field, value); }
    public double PerfectRate { get; set => SetFrameProperty(ref field, value); }
    public double SmiteRate { get; set => SetFrameProperty(ref field, value); }
    public double MultiHitRate { get; set => SetFrameProperty(ref field, value); }
    public double EnduranceRate { get; set => SetFrameProperty(ref field, value); }
    public double RegenerationRate { get; set => SetFrameProperty(ref field, value); }
    public double BlockRate { get; set => SetFrameProperty(ref field, value); }
    public double PerfectBlockRate { get; set => SetFrameProperty(ref field, value); }
    public double EvadeRate { get; set => SetFrameProperty(ref field, value); }
    public double InvincibleRate { get; set => SetFrameProperty(ref field, value); }

    public void ApplyFrom(in SkillDetailRowData data)
        => ApplyFromCore(in data);

    private void ApplyFromCore(in SkillDetailRowData data)
    {
        var criticalRate = data.Hits > 0 ? data.Criticals / (double)data.Hits : 0d;
        var backRate = data.Hits > 0 ? data.Back / (double)data.Hits : 0d;
        var parryRate = data.Hits > 0 ? data.Parry / (double)data.Hits : 0d;
        var perfectParryRate = data.Hits > 0 ? data.PerfectParry / (double)data.Hits : 0d;
        var perfectRate = data.Hits > 0 ? data.Perfect / (double)data.Hits : 0d;
        var smiteRate = data.Hits > 0 ? data.Smite / (double)data.Hits : 0d;
        var multiHitRate = data.Hits > 0 ? data.MultiHit / (double)data.Hits : 0d;
        var enduranceRate = data.Hits > 0 ? data.Endurance / (double)data.Hits : 0d;
        var regenerationRate = data.Hits > 0 ? data.Regeneration / (double)data.Hits : 0d;
        var blockRate = data.Hits > 0 ? data.Block / (double)data.Hits : 0d;
        var perfectBlockRate = data.Hits > 0 ? data.PerfectBlock / (double)data.Hits : 0d;
        var evadeRate = data.Attempts > 0 ? data.Evades / (double)data.Attempts : 0d;
        var invincibleRate = data.Attempts > 0 ? data.Invincible / (double)data.Attempts : 0d;

        ActionKey = data.ActionKey;
        SkillCode = data.SkillCode;
        DisplayName = data.DisplayName;
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
        PerfectParry = data.PerfectParry;
        Perfect = data.Perfect;
        Smite = data.Smite;
        MultiHit = data.MultiHit;
        Endurance = data.Endurance;
        Regeneration = data.Regeneration;
        Block = data.Block;
        PerfectBlock = data.PerfectBlock;
        SharePercent = data.SharePercent;
        CriticalRate = criticalRate;
        BackRate = backRate;
        ParryRate = parryRate;
        PerfectParryRate = perfectParryRate;
        PerfectRate = perfectRate;
        SmiteRate = smiteRate;
        MultiHitRate = multiHitRate;
        EnduranceRate = enduranceRate;
        RegenerationRate = regenerationRate;
        BlockRate = blockRate;
        PerfectBlockRate = perfectBlockRate;
        EvadeRate = evadeRate;
        InvincibleRate = invincibleRate;
    }
}

public struct SkillDetailRowData
{
    public CombatActionKey ActionKey;
    public int SkillCode;
    public string DisplayName;
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
    public int PerfectParry;
    public int Perfect;
    public int Smite;
    public int MultiHit;
    public int Endurance;
    public int Regeneration;
    public int Block;
    public int PerfectBlock;
    public double SharePercent;
}
