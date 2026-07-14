using Cloris.Aion2Flow.SceneRuntime.Combat;

namespace Cloris.Aion2Flow.ViewModels;

public sealed class SkillDetailRowViewModel(UiFrameBatchService frameBatchService) : FrameBatchedObservableObject(frameBatchService)
{
    public SkillBaseKey BaseKey { get; set; }
    public int SkillCode { get; set => SetFrameProperty(ref field, value); }
    public string DisplayName { get; set => SetFrameProperty(ref field, value); } = string.Empty;

    public int EventCount { get; set => SetFrameProperty(ref field, value); }
    public bool IsSelected { get; set => SetFrameProperty(ref field, value); }
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
    public int Front { get; set => SetFrameProperty(ref field, value); }
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
    public double FrontRate { get; set => SetFrameProperty(ref field, value); }
    public double EnduranceRate { get; set => SetFrameProperty(ref field, value); }
    public double RegenerationRate { get; set => SetFrameProperty(ref field, value); }
    public double BlockRate { get; set => SetFrameProperty(ref field, value); }
    public double PerfectBlockRate { get; set => SetFrameProperty(ref field, value); }
    public double EvadeRate { get; set => SetFrameProperty(ref field, value); }
    public double InvincibleRate { get; set => SetFrameProperty(ref field, value); }

    public void ApplyFrom(in SkillDetailRowData data)
    {
        BaseKey = data.BaseKey;
        SkillCode = data.SkillCode;
        DisplayName = data.DisplayName;
        EventCount = data.EventCount;
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
        Front = data.Front;
        Endurance = data.Endurance;
        Regeneration = data.Regeneration;
        Block = data.Block;
        PerfectBlock = data.PerfectBlock;
        SharePercent = data.SharePercent;
        CriticalRate = data.Hits > 0 ? data.Criticals / (double)data.Hits : 0d;
        BackRate = data.Hits > 0 ? data.Back / (double)data.Hits : 0d;
        ParryRate = data.Hits > 0 ? data.Parry / (double)data.Hits : 0d;
        PerfectParryRate = data.Hits > 0 ? data.PerfectParry / (double)data.Hits : 0d;
        PerfectRate = data.Hits > 0 ? data.Perfect / (double)data.Hits : 0d;
        SmiteRate = data.Hits > 0 ? data.Smite / (double)data.Hits : 0d;
        MultiHitRate = data.Hits > 0 ? data.MultiHit / (double)data.Hits : 0d;
        FrontRate = data.Hits > 0 ? data.Front / (double)data.Hits : 0d;
        EnduranceRate = data.Hits > 0 ? data.Endurance / (double)data.Hits : 0d;
        RegenerationRate = data.Hits > 0 ? data.Regeneration / (double)data.Hits : 0d;
        BlockRate = data.Hits > 0 ? data.Block / (double)data.Hits : 0d;
        PerfectBlockRate = data.Hits > 0 ? data.PerfectBlock / (double)data.Hits : 0d;
        EvadeRate = data.Attempts > 0 ? data.Evades / (double)data.Attempts : 0d;
        InvincibleRate = data.Attempts > 0 ? data.Invincible / (double)data.Attempts : 0d;
    }
}

internal static class SkillDetailBaseAggregator
{
    public static void AddOrMerge(List<SkillDetailRowData> rows, Dictionary<SkillBaseKey, int> rowIndexes, in SkillDetailRowData row)
    {
        if (rowIndexes.TryGetValue(row.BaseKey, out var index))
        {
            System.Runtime.InteropServices.CollectionsMarshal.AsSpan(rows)[index].Merge(in row);
            return;
        }

        rowIndexes.Add(row.BaseKey, rows.Count);
        rows.Add(row);
    }
}

public struct SkillDetailRowData
{
    public SkillBaseKey BaseKey;
    public int SkillCode;
    public string DisplayName;
    public int EventCount;
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
    public int Front;
    public int Endurance;
    public int Regeneration;
    public int Block;
    public int PerfectBlock;
    public double SharePercent;

    public void Merge(in SkillDetailRowData other)
    {
        if (other.SkillCode == BaseKey.SkillCode ||
            SkillCode != BaseKey.SkillCode && other.SkillCode < SkillCode)
        {
            SkillCode = other.SkillCode;
            DisplayName = other.DisplayName;
        }

        EventCount += other.EventCount;
        TotalAmount += other.TotalAmount;
        DirectAmount += other.DirectAmount;
        PeriodicAmount += other.PeriodicAmount;
        DrainAmount += other.DrainAmount;
        RegenerationAmount += other.RegenerationAmount;
        ShieldAmount += other.ShieldAmount;
        ShieldAbsorbedAmount += other.ShieldAbsorbedAmount;
        Hits += other.Hits;
        Attempts += other.Attempts;
        PeriodicHits += other.PeriodicHits;
        Evades += other.Evades;
        Invincible += other.Invincible;
        Criticals += other.Criticals;
        Back += other.Back;
        Parry += other.Parry;
        PerfectParry += other.PerfectParry;
        Perfect += other.Perfect;
        Smite += other.Smite;
        MultiHit += other.MultiHit;
        Front += other.Front;
        Endurance += other.Endurance;
        Regeneration += other.Regeneration;
        Block += other.Block;
        PerfectBlock += other.PerfectBlock;
    }
}
