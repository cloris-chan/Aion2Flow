using System.Collections.ObjectModel;
using System.Globalization;
using Cloris.Aion2Flow.Resources.Catalog;
using Cloris.Aion2Flow.Services;
using Cloris.Aion2Flow.Services.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cloris.Aion2Flow.ViewModels;

public sealed partial class SkillMonitorSettingsViewModel : ObservableObject, IDisposable
{
    private static readonly SkillCategory[] ProfessionCategories =
    [
        SkillCategory.Gladiator,
        SkillCategory.Templar,
        SkillCategory.Assassin,
        SkillCategory.Ranger,
        SkillCategory.Sorcerer,
        SkillCategory.Cleric,
        SkillCategory.Chanter,
        SkillCategory.Elementalist,
        SkillCategory.Brawler
    ];

    private readonly GameResourceService _resources;
    private readonly SettingsService _settings;
    private readonly LocalizationService _localization;
    private HashSet<int> _knownSkillIds = [];
    private bool _isDisposed;

    public SkillMonitorSettingsViewModel(
        GameResourceService resources,
        SettingsService settings,
        LocalizationService localization)
    {
        _resources = resources;
        _settings = settings;
        _localization = localization;
        _resources.ResourcesChanged += OnResourcesChanged;
        _localization.LanguageChanged += OnLanguageChanged;
        SkillMonitorScalePercent = _settings.Current.SkillMonitorScalePercent;
        RebuildGroups();
    }

    public ObservableCollection<SkillMonitorClassGroup> Groups { get; } = [];

    [ObservableProperty]
    public partial int SkillMonitorScalePercent { get; set; } = 100;

    public string BuffSelectionSummary => FormatSelectionSummary(
        "Settings_SkillMonitor_BuffSelectedFormat",
        ResolveSelectedSkillIds(SkillMonitorSelectionKind.Buff).Count);

    public string CooldownSelectionSummary => FormatSelectionSummary(
        "Settings_SkillMonitor_CooldownSelectedFormat",
        ResolveSelectedSkillIds(SkillMonitorSelectionKind.Cooldown).Count);

    [RelayCommand]
    private void SelectAllBuffs() => UpdateSelection(SkillMonitorSelectionKind.Buff, selectAll: true, []);

    [RelayCommand]
    private void ClearAllBuffs() => UpdateSelection(SkillMonitorSelectionKind.Buff, selectAll: false, []);

    [RelayCommand]
    private void SelectAllCooldowns() => UpdateSelection(SkillMonitorSelectionKind.Cooldown, selectAll: true, []);

    [RelayCommand]
    private void ClearAllCooldowns() => UpdateSelection(SkillMonitorSelectionKind.Cooldown, selectAll: false, []);

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        _resources.ResourcesChanged -= OnResourcesChanged;
        _localization.LanguageChanged -= OnLanguageChanged;
    }

    private void RebuildGroups()
    {
        var entries = _resources.Skills
            .Where(PlayerProfessionSkillFilter.Includes)
            .Select(entry => new SkillMonitorCatalogEntry(
                _resources.ResolveBaseSkillIdForCode(entry.SkillId),
                entry.Name,
                entry.Category))
            .Where(static entry => entry.RowBaseSkillId > 0)
            .GroupBy(static entry => entry.RowBaseSkillId)
            .Select(group => new SkillMonitorCatalogEntry(
                group.Key,
                _resources.ResolveSkillName(group.Key),
                group.First().Category))
            .OrderBy(static entry => entry.Category)
            .ThenBy(static entry => entry.Name, StringComparer.CurrentCulture)
            .ThenBy(static entry => entry.RowBaseSkillId)
            .ToArray();

        _knownSkillIds = entries.Select(static entry => entry.RowBaseSkillId).ToHashSet();
        var selectedBuffs = ResolveSelectedSkillIds(SkillMonitorSelectionKind.Buff);
        var selectedCooldowns = ResolveSelectedSkillIds(SkillMonitorSelectionKind.Cooldown);

        Groups.Clear();
        foreach (var category in ProfessionCategories)
        {
            var categoryEntries = entries.Where(entry => entry.Category == category).ToArray();
            if (categoryEntries.Length == 0)
                continue;

            var group = new SkillMonitorClassGroup(
                category,
                ResolveCategoryName(category),
                categoryEntries.Length,
                OnBuffSelectionChanged,
                OnCooldownSelectionChanged);
            foreach (var entry in categoryEntries)
            {
                group.Skills.Add(new SkillMonitorSkillOption(
                    entry.RowBaseSkillId,
                    string.IsNullOrWhiteSpace(entry.Name)
                        ? entry.RowBaseSkillId.ToString(CultureInfo.InvariantCulture)
                        : entry.Name,
                    _resources.ResolveSkillIconAssetName(entry.RowBaseSkillId),
                    selectedBuffs.Contains(entry.RowBaseSkillId),
                    selectedCooldowns.Contains(entry.RowBaseSkillId),
                    group.NotifyBuffSelectionChanged,
                    group.NotifyCooldownSelectionChanged));
            }

            Groups.Add(group);
        }

        OnPropertyChanged(nameof(BuffSelectionSummary));
        OnPropertyChanged(nameof(CooldownSelectionSummary));
    }

    private void OnBuffSelectionChanged(SkillMonitorSkillOption option, bool isSelected)
        => OnSkillSelectionChanged(option, SkillMonitorSelectionKind.Buff, isSelected);

    private void OnCooldownSelectionChanged(SkillMonitorSkillOption option, bool isSelected)
        => OnSkillSelectionChanged(option, SkillMonitorSelectionKind.Cooldown, isSelected);

    private void OnSkillSelectionChanged(
        SkillMonitorSkillOption option,
        SkillMonitorSelectionKind kind,
        bool isSelected)
    {
        var selected = ResolveSelectedSkillIds(kind);
        if (isSelected)
            selected.Add(option.SkillId);
        else
            selected.Remove(option.SkillId);

        var selectAll = selected.Count == _knownSkillIds.Count && _knownSkillIds.Count > 0;
        UpdateSelection(kind, selectAll, selectAll ? [] : selected);
    }

    private void UpdateSelection(
        SkillMonitorSelectionKind kind,
        bool selectAll,
        IEnumerable<int> selectedIds)
    {
        var normalized = selectAll
            ? []
            : selectedIds.Where(_knownSkillIds.Contains).Distinct().Order().ToList();
        _settings.Update(current =>
        {
            if (kind == SkillMonitorSelectionKind.Buff)
            {
                current.SkillMonitorBuffSelectAll = selectAll;
                current.SkillMonitorBuffSkillIds = normalized;
            }
            else
            {
                current.SkillMonitorCooldownSelectAll = selectAll;
                current.SkillMonitorCooldownSkillIds = normalized;
            }
        });

        var selected = selectAll ? _knownSkillIds : normalized.ToHashSet();
        foreach (var group in Groups)
        {
            foreach (var skill in group.Skills)
            {
                if (kind == SkillMonitorSelectionKind.Buff)
                    skill.ApplyBuffSelection(selected.Contains(skill.SkillId));
                else
                    skill.ApplyCooldownSelection(selected.Contains(skill.SkillId));
            }
        }

        if (kind == SkillMonitorSelectionKind.Buff)
        {
            OnPropertyChanged(nameof(BuffSelectionSummary));
        }
        else
        {
            OnPropertyChanged(nameof(CooldownSelectionSummary));
        }
    }

    private HashSet<int> ResolveSelectedSkillIds(SkillMonitorSelectionKind kind)
    {
        var settings = _settings.Current;
        var selectAll = kind == SkillMonitorSelectionKind.Buff
            ? settings.SkillMonitorBuffSelectAll
            : settings.SkillMonitorCooldownSelectAll;
        if (selectAll)
            return _knownSkillIds.ToHashSet();

        var selectedIds = kind == SkillMonitorSelectionKind.Buff
            ? settings.SkillMonitorBuffSkillIds
            : settings.SkillMonitorCooldownSkillIds;
        var selected = selectedIds.ToHashSet();
        selected.IntersectWith(_knownSkillIds);
        return selected;
    }

    private string FormatSelectionSummary(string resourceKey, int selectedCount)
        => string.Format(
            CultureInfo.InvariantCulture,
            _localization[resourceKey],
            selectedCount,
            _knownSkillIds.Count);

    partial void OnSkillMonitorScalePercentChanged(int value)
    {
        _settings.Update(current => current.SkillMonitorScalePercent = value);
    }

    private string ResolveCategoryName(SkillCategory category)
        => _localization[$"CharacterClass_{category}"];

    private void OnResourcesChanged(object? sender, string language) => RebuildGroups();

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        foreach (var group in Groups)
            group.DisplayName = ResolveCategoryName(group.Category);
        OnPropertyChanged(nameof(BuffSelectionSummary));
        OnPropertyChanged(nameof(CooldownSelectionSummary));
    }

    private readonly record struct SkillMonitorCatalogEntry(
        int RowBaseSkillId,
        string Name,
        SkillCategory Category);

    private enum SkillMonitorSelectionKind : byte
    {
        Buff,
        Cooldown
    }
}

public sealed partial class SkillMonitorClassGroup : ObservableObject
{
    private readonly Action<SkillMonitorSkillOption, bool> _buffSelectionChanged;
    private readonly Action<SkillMonitorSkillOption, bool> _cooldownSelectionChanged;
    private string _displayName;

    public SkillMonitorClassGroup(
        SkillCategory category,
        string displayName,
        int skillCount,
        Action<SkillMonitorSkillOption, bool> buffSelectionChanged,
        Action<SkillMonitorSkillOption, bool> cooldownSelectionChanged)
    {
        Category = category;
        _displayName = displayName;
        SkillCount = skillCount;
        _buffSelectionChanged = buffSelectionChanged;
        _cooldownSelectionChanged = cooldownSelectionChanged;
    }

    public SkillCategory Category { get; }

    public string DisplayName
    {
        get => _displayName;
        set
        {
            if (SetProperty(ref _displayName, value))
                OnPropertyChanged(nameof(HeaderText));
        }
    }

    public int SkillCount { get; }

    public string HeaderText => $"{DisplayName} ({SkillCount.ToString(CultureInfo.InvariantCulture)})";

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    public ObservableCollection<SkillMonitorSkillOption> Skills { get; } = [];

    internal void NotifyBuffSelectionChanged(SkillMonitorSkillOption option, bool isSelected)
        => _buffSelectionChanged(option, isSelected);

    internal void NotifyCooldownSelectionChanged(SkillMonitorSkillOption option, bool isSelected)
        => _cooldownSelectionChanged(option, isSelected);
}

public sealed class SkillMonitorSkillOption : ObservableObject
{
    private readonly Action<SkillMonitorSkillOption, bool> _buffSelectionChanged;
    private readonly Action<SkillMonitorSkillOption, bool> _cooldownSelectionChanged;
    private bool _isBuffSelected;
    private bool _isCooldownSelected;

    public SkillMonitorSkillOption(
        int skillId,
        string displayName,
        string? iconAssetName,
        bool isBuffSelected,
        bool isCooldownSelected,
        Action<SkillMonitorSkillOption, bool> buffSelectionChanged,
        Action<SkillMonitorSkillOption, bool> cooldownSelectionChanged)
    {
        SkillId = skillId;
        DisplayName = displayName;
        IconAssetName = iconAssetName;
        _isBuffSelected = isBuffSelected;
        _isCooldownSelected = isCooldownSelected;
        _buffSelectionChanged = buffSelectionChanged;
        _cooldownSelectionChanged = cooldownSelectionChanged;
    }

    public int SkillId { get; }

    public string DisplayName { get; }

    public string? IconAssetName { get; }

    public bool IsBuffSelected
    {
        get => _isBuffSelected;
        set
        {
            if (SetProperty(ref _isBuffSelected, value))
                _buffSelectionChanged(this, value);
        }
    }

    public bool IsCooldownSelected
    {
        get => _isCooldownSelected;
        set
        {
            if (SetProperty(ref _isCooldownSelected, value))
                _cooldownSelectionChanged(this, value);
        }
    }

    internal void ApplyBuffSelection(bool value)
    {
        if (_isBuffSelected == value)
            return;

        _isBuffSelected = value;
        OnPropertyChanged(nameof(IsBuffSelected));
    }

    internal void ApplyCooldownSelection(bool value)
    {
        if (_isCooldownSelected == value)
            return;

        _isCooldownSelected = value;
        OnPropertyChanged(nameof(IsCooldownSelected));
    }
}
