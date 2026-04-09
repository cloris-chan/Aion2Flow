using System.Globalization;

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
    public string CriticalSummary => FormatHitModifierSummary(Criticals);

    public string BackSummary => FormatHitModifierSummary(Back);

    public string ParrySummary => FormatHitModifierSummary(Parry);

    public string PerfectSummary => FormatHitModifierSummary(Perfect);

    public string SmiteSummary => FormatHitModifierSummary(Smite);

    public string MultiHitSummary => FormatHitModifierSummary(MultiHit);

    public string EnduranceSummary => FormatHitModifierSummary(Endurance);

    public string RegenerationSummary => FormatHitModifierSummary(Regeneration);

    public string BlockSummary => FormatHitModifierSummary(Block);

    public string EvadeSummary => FormatAttemptModifierSummary(Evades);

    public string InvincibleSummary => FormatAttemptModifierSummary(Invincible);

    private string FormatHitModifierSummary(int count)
    {
        var rate = Hits > 0 ? count / (double)Hits : 0d;
        return string.Format(CultureInfo.CurrentCulture, "{0} ({1:P1})", count, rate);
    }

    private string FormatAttemptModifierSummary(int count)
    {
        var rate = Attempts > 0 ? count / (double)Attempts : 0d;
        return string.Format(CultureInfo.CurrentCulture, "{0} ({1:P1})", count, rate);
    }
}
