using Cloris.Aion2Flow.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.Globalization;

namespace Cloris.Aion2Flow.ViewModels;

public sealed partial class SkillDetailSectionViewModel : ObservableObject
{
    private LocalizationService? _localization;

    public ObservableCollection<SkillDetailScopeOption> ScopeOptions { get; } = [];
    public ObservableCollection<SkillDetailRowViewModel> Rows { get; } = [];

    public event EventHandler? SelectedScopeChanged;

    public bool HasMultipleScopes => ScopeOptions.Count > 1;

    [ObservableProperty]
    public partial SkillDetailScopeOption? SelectedScope { get; set; }

    [ObservableProperty]
    public partial long Total { get; set; }

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
    public partial long Shield { get; set; }

    [ObservableProperty]
    public partial int SkillCount { get; set; }

    [ObservableProperty]
    public partial bool HasSkills { get; set; }

    [ObservableProperty]
    public partial double PerSecond { get; set; }

    [ObservableProperty]
    public partial double CriticalRate { get; set; }

    [ObservableProperty]
    public partial double SmiteRate { get; set; }

    [ObservableProperty]
    public partial double MultiHitRate { get; set; }

    [ObservableProperty]
    public partial double ParryRate { get; set; }

    [ObservableProperty]
    public partial double PerfectRate { get; set; }

    [ObservableProperty]
    public partial double EnduranceRate { get; set; }

    [ObservableProperty]
    public partial double BackRate { get; set; }

    [ObservableProperty]
    public partial double RegenerationRate { get; set; }

    [ObservableProperty]
    public partial double BlockRate { get; set; }

    [ObservableProperty]
    public partial double EvadeRate { get; set; }

    [ObservableProperty]
    public partial double InvincibleRate { get; set; }

    [ObservableProperty]
    public partial string CriticalSummary { get; set; } = FormatModifierSummary(0, 0d);

    [ObservableProperty]
    public partial string SmiteSummary { get; set; } = FormatModifierSummary(0, 0d);

    [ObservableProperty]
    public partial string MultiHitSummary { get; set; } = FormatModifierSummary(0, 0d);

    [ObservableProperty]
    public partial string ParrySummary { get; set; } = FormatModifierSummary(0, 0d);

    [ObservableProperty]
    public partial string PerfectSummary { get; set; } = FormatModifierSummary(0, 0d);

    [ObservableProperty]
    public partial string EnduranceSummary { get; set; } = FormatModifierSummary(0, 0d);

    [ObservableProperty]
    public partial string BackSummary { get; set; } = FormatModifierSummary(0, 0d);

    [ObservableProperty]
    public partial string RegenerationSummary { get; set; } = FormatModifierSummary(0, 0d);

    [ObservableProperty]
    public partial string BlockSummary { get; set; } = FormatModifierSummary(0, 0d);

    [ObservableProperty]
    public partial string EvadeSummary { get; set; } = FormatModifierSummary(0, 0d);

    [ObservableProperty]
    public partial string InvincibleSummary { get; set; } = FormatModifierSummary(0, 0d);

    [ObservableProperty]
    public partial string HitCountColumnHeader { get; set; } = "";

    [ObservableProperty]
    public partial string PeriodicHitsColumnHeader { get; set; } = "";

    [ObservableProperty]
    public partial string CriticalColumnHeader { get; set; } = "";

    [ObservableProperty]
    public partial string PerfectColumnHeader { get; set; } = "";

    [ObservableProperty]
    public partial string SmiteColumnHeader { get; set; } = "";

    [ObservableProperty]
    public partial string MultiHitColumnHeader { get; set; } = "";

    [ObservableProperty]
    public partial string BackColumnHeader { get; set; } = "";

    [ObservableProperty]
    public partial string ParryColumnHeader { get; set; } = "";

    [ObservableProperty]
    public partial string BlockColumnHeader { get; set; } = "";

    [ObservableProperty]
    public partial string EnduranceColumnHeader { get; set; } = "";

    [ObservableProperty]
    public partial string RegenerationColumnHeader { get; set; } = "";

    [ObservableProperty]
    public partial string EvadeColumnHeader { get; set; } = "";

    [ObservableProperty]
    public partial string InvincibleColumnHeader { get; set; } = "";

    [ObservableProperty]
    public partial string TotalColumnHeader { get; set; } = "";

    [ObservableProperty]
    public partial string HealColumnHeader { get; set; } = "";

    [ObservableProperty]
    public partial string HotColumnHeader { get; set; } = "";

    [ObservableProperty]
    public partial string DrainHealColumnHeader { get; set; } = "";

    [ObservableProperty]
    public partial string HitsColumnHeader { get; set; } = "";

    public void SetLocalization(LocalizationService localization)
    {
        _localization = localization;
    }

    partial void OnSelectedScopeChanged(SkillDetailScopeOption? value)
    {
        SelectedScopeChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ReplaceScopeOptions(IReadOnlyCollection<SkillDetailScopeOption> scopes)
    {
        ScopeOptions.Clear();
        foreach (var scope in scopes)
        {
            ScopeOptions.Add(scope);
        }

        OnPropertyChanged(nameof(HasMultipleScopes));
    }

    public void ReplaceRows(IReadOnlyCollection<SkillDetailRowViewModel> rows)
    {
        Rows.Clear();
        foreach (var row in rows)
        {
            Rows.Add(row);
        }
    }

    public void SetDamageModifierSummaries(
        int critical,
        int perfect,
        int smite,
        int multiHit,
        int parry,
        int block,
        int endure,
        int regeneration,
        int back,
        int evades,
        int invincible)
    {
        CriticalSummary = FormatModifierSummary(critical, CriticalRate);
        PerfectSummary = FormatModifierSummary(perfect, PerfectRate);
        SmiteSummary = FormatModifierSummary(smite, SmiteRate);
        MultiHitSummary = FormatModifierSummary(multiHit, MultiHitRate);
        ParrySummary = FormatModifierSummary(parry, ParryRate);
        BlockSummary = FormatModifierSummary(block, BlockRate);
        EnduranceSummary = FormatModifierSummary(endure, EnduranceRate);
        RegenerationSummary = FormatModifierSummary(regeneration, RegenerationRate);
        BackSummary = FormatModifierSummary(back, BackRate);
        EvadeSummary = FormatModifierSummary(evades, EvadeRate);
        InvincibleSummary = FormatModifierSummary(invincible, InvincibleRate);

        UpdateDamageColumnHeaders(critical, perfect, smite, multiHit, parry, block, endure, regeneration, back, evades, invincible);
    }

    public void UpdateHealingColumnHeaders()
    {
        if (_localization is null) return;

        var directHealing = Rows.Sum(static r => r.DirectAmount);
        var periodicHealing = Rows.Sum(static r => r.PeriodicAmount);
        var drainHealing = Rows.Sum(static r => r.DrainAmount);

        TotalColumnHeader = FormatCountColumnHeader("Column.Total", Total);
        HealColumnHeader = FormatCountColumnHeader("Column.Heal", directHealing);
        HotColumnHeader = FormatCountColumnHeader("Column.Hot", periodicHealing);
        DrainHealColumnHeader = FormatCountColumnHeader("Column.DrainHeal", drainHealing);
        HitsColumnHeader = FormatCountColumnHeader("Column.HitCount", Hits);
    }

    public void UpdateShieldColumnHeaders()
    {
        if (_localization is null) return;

        TotalColumnHeader = FormatCountColumnHeader("Column.Total", Total);
        HitsColumnHeader = FormatCountColumnHeader("Column.HitCount", Hits);
    }

    private void UpdateDamageColumnHeaders(
        int critical, int perfect, int smite, int multiHit,
        int parry, int block, int endure, int regeneration,
        int back, int evades, int invincible)
    {
        if (_localization is null) return;

        var hitRate = Attempts > 0 ? (double)Hits / Attempts : 0d;
        HitCountColumnHeader = FormatRateColumnHeader("Column.HitCount", Hits, hitRate);
        PeriodicHitsColumnHeader = FormatCountColumnHeader("Column.PeriodicHits", PeriodicHits);
        CriticalColumnHeader = FormatRateColumnHeader("Column.Critical", critical, CriticalRate);
        PerfectColumnHeader = FormatRateColumnHeader("Column.Perfect", perfect, PerfectRate);
        SmiteColumnHeader = FormatRateColumnHeader("Column.Smite", smite, SmiteRate);
        MultiHitColumnHeader = FormatRateColumnHeader("Column.MultiHit", multiHit, MultiHitRate);
        BackColumnHeader = FormatRateColumnHeader("Column.Back", back, BackRate);
        ParryColumnHeader = FormatRateColumnHeader("Column.Parry", parry, ParryRate);
        BlockColumnHeader = FormatRateColumnHeader("Column.Block", block, BlockRate);
        EnduranceColumnHeader = FormatRateColumnHeader("Column.Endurance", endure, EnduranceRate);
        RegenerationColumnHeader = FormatRateColumnHeader("Column.Regeneration", regeneration, RegenerationRate);
        EvadeColumnHeader = FormatRateColumnHeader("Column.Evade", evades, EvadeRate);
        InvincibleColumnHeader = FormatRateColumnHeader("Column.Invincible", invincible, InvincibleRate);
    }

    public void Clear()
    {
        ScopeOptions.Clear();
        Rows.Clear();
        SelectedScope = null;
        Total = 0;
        Hits = 0;
        Attempts = 0;
        PeriodicHits = 0;
        Evades = 0;
        Invincible = 0;
        Criticals = 0;
        Shield = 0;
        SkillCount = 0;
        HasSkills = false;
        PerSecond = 0d;
        CriticalRate = 0d;
        SmiteRate = 0d;
        MultiHitRate = 0d;
        ParryRate = 0d;
        PerfectRate = 0d;
        EnduranceRate = 0d;
        BackRate = 0d;
        RegenerationRate = 0d;
        BlockRate = 0d;
        EvadeRate = 0d;
        InvincibleRate = 0d;
        SetDamageModifierSummaries(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        ClearColumnHeaders();
        OnPropertyChanged(nameof(HasMultipleScopes));
    }

    private void ClearColumnHeaders()
    {
        HitCountColumnHeader = "";
        PeriodicHitsColumnHeader = "";
        CriticalColumnHeader = "";
        PerfectColumnHeader = "";
        SmiteColumnHeader = "";
        MultiHitColumnHeader = "";
        BackColumnHeader = "";
        ParryColumnHeader = "";
        BlockColumnHeader = "";
        EnduranceColumnHeader = "";
        RegenerationColumnHeader = "";
        EvadeColumnHeader = "";
        InvincibleColumnHeader = "";
        TotalColumnHeader = "";
        HealColumnHeader = "";
        HotColumnHeader = "";
        DrainHealColumnHeader = "";
        HitsColumnHeader = "";
    }

    private string FormatRateColumnHeader(string key, int count, double rate)
        => string.Format(CultureInfo.CurrentCulture, "{0}\n{1}/{2:P1}", _localization![key], count, rate);

    private string FormatCountColumnHeader(string key, long count)
        => string.Format(CultureInfo.CurrentCulture, "{0}\n{1}", _localization![key], count);

    private static string FormatModifierSummary(int count, double rate)
        => string.Format(CultureInfo.CurrentCulture, "{0} ({1:P1})", count, rate);
}
