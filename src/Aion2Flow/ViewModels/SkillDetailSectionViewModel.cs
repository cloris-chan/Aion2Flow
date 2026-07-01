using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Cloris.Aion2Flow.ViewModels;

public sealed partial class SkillDetailSectionViewModel(UiFrameBatchService frameBatchService) : FrameBatchedObservableObject(frameBatchService)
{
    private readonly UiFrameBatchService _frameBatchService = frameBatchService;
    private readonly Dictionary<SkillPresentationKey, SkillDetailRowViewModel> _existingByPresentationKey = [];
    private readonly HashSet<SkillPresentationKey> _newPresentationKeys = [];

    public ObservableCollection<SkillDetailScopeOption> ScopeOptions { get; } = [];
    public ObservableCollection<SkillDetailRowViewModel> Rows { get; } = [];

    public event EventHandler? SelectedScopeChanged;

    public bool HasMultipleScopes => ScopeOptions.Count > 1;

    [ObservableProperty]
    public partial SkillDetailScopeOption? SelectedScope { get; set; }

    public long Total { get; set => SetFrameProperty(ref field, value); }
    public long DirectTotal { get; set => SetFrameProperty(ref field, value); }
    public long PeriodicTotal { get; set => SetFrameProperty(ref field, value); }
    public long DrainTotal { get; set => SetFrameProperty(ref field, value); }
    public long RegenerationTotal { get; set => SetFrameProperty(ref field, value); }
    public int Hits { get; set => SetFrameProperty(ref field, value); }
    public int Attempts { get; set => SetFrameProperty(ref field, value); }
    public int PeriodicHits { get; set => SetFrameProperty(ref field, value); }
    public int Evades { get; set => SetFrameProperty(ref field, value); }
    public int Invincible { get; set => SetFrameProperty(ref field, value); }
    public int Criticals { get; set => SetFrameProperty(ref field, value); }
    public int PerfectCount { get; set => SetFrameProperty(ref field, value); }
    public int SmiteCount { get; set => SetFrameProperty(ref field, value); }
    public int MultiHitCount { get; set => SetFrameProperty(ref field, value); }
    public int FrontCount { get; set => SetFrameProperty(ref field, value); }
    public int BackCount { get; set => SetFrameProperty(ref field, value); }
    public int ParryCount { get; set => SetFrameProperty(ref field, value); }
    public int BlockCount { get; set => SetFrameProperty(ref field, value); }
    public int PerfectParryCount { get; set => SetFrameProperty(ref field, value); }
    public int PerfectBlockCount { get; set => SetFrameProperty(ref field, value); }
    public int EnduranceCount { get; set => SetFrameProperty(ref field, value); }
    public int RegenerationCount { get; set => SetFrameProperty(ref field, value); }
    public long Shield { get; set => SetFrameProperty(ref field, value); }
    public long ShieldAbsorbed { get; set => SetFrameProperty(ref field, value); }
    public int SkillCount { get; set => SetFrameProperty(ref field, value); }
    public bool HasSkills { get; set => SetFrameProperty(ref field, value); }
    public double PerSecond { get; set => SetFrameProperty(ref field, value); }
    public double DurationSeconds { get; set => SetFrameProperty(ref field, value); }

    public bool UsesSceneDuration { get; set; }

    public double HitRate { get; set => SetFrameProperty(ref field, value); }
    public double CriticalRate { get; set => SetFrameProperty(ref field, value); }
    public double SmiteRate { get; set => SetFrameProperty(ref field, value); }
    public double MultiHitRate { get; set => SetFrameProperty(ref field, value); }
    public double FrontRate { get; set => SetFrameProperty(ref field, value); }
    public double ParryRate { get; set => SetFrameProperty(ref field, value); }
    public double PerfectRate { get; set => SetFrameProperty(ref field, value); }
    public double PerfectParryRate { get; set => SetFrameProperty(ref field, value); }
    public double EnduranceRate { get; set => SetFrameProperty(ref field, value); }
    public double BackRate { get; set => SetFrameProperty(ref field, value); }
    public double RegenerationRate { get; set => SetFrameProperty(ref field, value); }
    public double BlockRate { get; set => SetFrameProperty(ref field, value); }
    public double PerfectBlockRate { get; set => SetFrameProperty(ref field, value); }
    public double EvadeRate { get; set => SetFrameProperty(ref field, value); }
    public double InvincibleRate { get; set => SetFrameProperty(ref field, value); }

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
        _existingByPresentationKey.Clear();
        foreach (var row in Rows)
        {
            _existingByPresentationKey.TryAdd(row.PresentationKey, row);
        }

        _newPresentationKeys.Clear();
        for (var i = 0; i < dataRows.Count; i++)
        {
            _newPresentationKeys.Add(dataRows[i].PresentationKey);
        }

        for (var i = Rows.Count - 1; i >= 0; i--)
        {
            if (!_newPresentationKeys.Contains(Rows[i].PresentationKey))
            {
                Rows.RemoveAt(i);
            }
        }

        for (var i = 0; i < dataRows.Count; i++)
        {
            ref var data = ref CollectionsMarshal.AsSpan(dataRows)[i];
            if (_existingByPresentationKey.TryGetValue(data.PresentationKey, out var existing))
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
                var vm = new SkillDetailRowViewModel(_frameBatchService);
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
        FrontCount = 0;
        BackCount = 0;
        ParryCount = 0;
        BlockCount = 0;
        PerfectParryCount = 0;
        PerfectBlockCount = 0;
        EnduranceCount = 0;
        RegenerationCount = 0;
        Shield = 0;
        ShieldAbsorbed = 0;
        SkillCount = 0;
        HasSkills = false;
        PerSecond = 0d;
        DurationSeconds = 0d;
        UsesSceneDuration = false;
        HitRate = 0d;
        CriticalRate = 0d;
        SmiteRate = 0d;
        MultiHitRate = 0d;
        FrontRate = 0d;
        ParryRate = 0d;
        PerfectRate = 0d;
        PerfectParryRate = 0d;
        EnduranceRate = 0d;
        BackRate = 0d;
        RegenerationRate = 0d;
        BlockRate = 0d;
        PerfectBlockRate = 0d;
        EvadeRate = 0d;
        InvincibleRate = 0d;
        OnPropertyChanged(nameof(HasMultipleScopes));
    }
}
