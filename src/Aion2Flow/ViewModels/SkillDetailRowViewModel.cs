namespace Cloris.Aion2Flow.ViewModels;

public sealed record SkillDetailRowViewModel(
    int SkillCode,
    string SkillName,
    long TotalAmount,
    long DirectAmount,
    long PeriodicAmount,
    long DrainAmount,
    long ShieldAmount,
    int Hits,
    int Attempts,
    int PeriodicHits,
    int Evades,
    int Invincible,
    int Criticals,
    int Back,
    int Parry,
    int Perfect,
    int Smite,
    int MultiHit,
    int Endurance,
    int Regeneration,
    int Block,
    double SharePercent)
{
    public double CriticalRate => Hits > 0 ? Criticals / (double)Hits : 0d;

    public double BackRate => Hits > 0 ? Back / (double)Hits : 0d;

    public double ParryRate => Hits > 0 ? Parry / (double)Hits : 0d;

    public double PerfectRate => Hits > 0 ? Perfect / (double)Hits : 0d;

    public double SmiteRate => Hits > 0 ? Smite / (double)Hits : 0d;

    public double MultiHitRate => Hits > 0 ? MultiHit / (double)Hits : 0d;

    public double EnduranceRate => Hits > 0 ? Endurance / (double)Hits : 0d;

    public double RegenerationRate => Hits > 0 ? Regeneration / (double)Hits : 0d;

    public double BlockRate => Hits > 0 ? Block / (double)Hits : 0d;

    public double EvadeRate => Attempts > 0 ? Evades / (double)Attempts : 0d;

    public double InvincibleRate => Attempts > 0 ? Invincible / (double)Attempts : 0d;
}
