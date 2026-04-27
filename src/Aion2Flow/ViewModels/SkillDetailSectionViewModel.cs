using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;

namespace Cloris.Aion2Flow.ViewModels;

public sealed partial class SkillDetailSectionViewModel : ObservableObject
{
    public ObservableCollection<SkillDetailScopeOption> ScopeOptions { get; } = [];
    public ObservableCollection<SkillDetailRowViewModel> Rows { get; } = [];

    public event EventHandler? SelectedScopeChanged;

    public bool HasMultipleScopes => ScopeOptions.Count > 1;

    [ObservableProperty]
    public partial SkillDetailScopeOption? SelectedScope { get; set; }

    [ObservableProperty]
    public partial long Total { get; set; }

    [ObservableProperty]
    public partial long DirectTotal { get; set; }

    [ObservableProperty]
    public partial long PeriodicTotal { get; set; }

    [ObservableProperty]
    public partial long DrainTotal { get; set; }

    [ObservableProperty]
    public partial long RegenerationTotal { get; set; }

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
    public partial int PerfectCount { get; set; }

    [ObservableProperty]
    public partial int SmiteCount { get; set; }

    [ObservableProperty]
    public partial int MultiHitCount { get; set; }

    [ObservableProperty]
    public partial int BackCount { get; set; }

    [ObservableProperty]
    public partial int ParryCount { get; set; }

    [ObservableProperty]
    public partial int BlockCount { get; set; }

    [ObservableProperty]
    public partial int EnduranceCount { get; set; }

    [ObservableProperty]
    public partial int RegenerationCount { get; set; }

    [ObservableProperty]
    public partial long Shield { get; set; }

    [ObservableProperty]
    public partial long ShieldAbsorbed { get; set; }

    [ObservableProperty]
    public partial int SkillCount { get; set; }

    [ObservableProperty]
    public partial bool HasSkills { get; set; }

    [ObservableProperty]
    public partial double PerSecond { get; set; }

    [ObservableProperty]
    public partial double HitRate { get; set; }

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

    public void ReplaceRows(List<SkillDetailRowData> dataRows)
    {
        var existingBySkillCode = new Dictionary<int, SkillDetailRowViewModel>(Rows.Count);
        foreach (var row in Rows)
        {
            existingBySkillCode.TryAdd(row.SkillCode, row);
        }

        var newSkillCodes = new HashSet<int>(dataRows.Count);
        for (var i = 0; i < dataRows.Count; i++)
        {
            newSkillCodes.Add(dataRows[i].SkillCode);
        }

        for (var i = Rows.Count - 1; i >= 0; i--)
        {
            if (!newSkillCodes.Contains(Rows[i].SkillCode))
            {
                Rows.RemoveAt(i);
            }
        }

        for (var i = 0; i < dataRows.Count; i++)
        {
            ref var data = ref CollectionsMarshal.AsSpan(dataRows)[i];
            if (existingBySkillCode.TryGetValue(data.SkillCode, out var existing))
            {
                existing.ApplyFrom(in data);
                var currentIndex = Rows.IndexOf(existing);
                if (currentIndex != i && currentIndex >= 0)
                {
                    Rows.Move(currentIndex, i);
                }
            }
            else
            {
                var vm = new SkillDetailRowViewModel();
                vm.ApplyFrom(in data);
                if (i < Rows.Count)
                {
                    Rows.Insert(i, vm);
                }
                else
                {
                    Rows.Add(vm);
                }
            }
        }
    }

    public void Clear()
    {
        ScopeOptions.Clear();
        Rows.Clear();
        SelectedScope = null;
        Total = 0;
        DirectTotal = 0;
        PeriodicTotal = 0;
        DrainTotal = 0;
        RegenerationTotal = 0;
        Hits = 0;
        Attempts = 0;
        PeriodicHits = 0;
        Evades = 0;
        Invincible = 0;
        Criticals = 0;
        PerfectCount = 0;
        SmiteCount = 0;
        MultiHitCount = 0;
        BackCount = 0;
        ParryCount = 0;
        BlockCount = 0;
        EnduranceCount = 0;
        RegenerationCount = 0;
        Shield = 0;
        ShieldAbsorbed = 0;
        SkillCount = 0;
        HasSkills = false;
        PerSecond = 0d;
        HitRate = 0d;
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
        OnPropertyChanged(nameof(HasMultipleScopes));
    }
}
