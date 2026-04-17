using Cloris.Aion2Flow.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace Cloris.Aion2Flow.ViewModels;

public sealed class DetailCounterpartFilterViewModel : ObservableObject
{
    private readonly LocalizationService _localization;
    private bool _suppressSelectionChanged;

    public DetailCounterpartFilterViewModel(LocalizationService localization, string counterpartTitleKey)
    {
        _localization = localization;
        CounterpartTitleKey = counterpartTitleKey;
        _localization.PropertyChanged += HandleLocalizationPropertyChanged;
    }

    public LocalizationService Localization => _localization;

    public string CounterpartTitleKey { get; }

    public string CounterpartTitle => Localization[CounterpartTitleKey];

    public ObservableCollection<DetailCounterpartSelectionViewModel> Counterparts { get; } = [];

    public bool HasCounterparts => Counterparts.Count > 0;

    public bool? AreAllCounterpartsSelected
    {
        get
        {
            if (Counterparts.Count == 0)
            {
                return false;
            }

            var selectedCount = 0;
            foreach (var counterpart in Counterparts)
            {
                if (counterpart.IsSelected)
                {
                    selectedCount++;
                }
            }

            if (selectedCount == 0)
            {
                return false;
            }

            if (selectedCount == Counterparts.Count)
            {
                return true;
            }

            return null;
        }
        set
        {
            if (!value.HasValue)
            {
                return;
            }

            SetAllCounterpartsSelected(value.Value);
        }
    }

    public event EventHandler? SelectionChanged;

    public HashSet<int> GetSelectedCounterpartIds()
    {
        var selectedIds = new HashSet<int>();
        foreach (var counterpart in Counterparts)
        {
            if (counterpart.IsSelected)
            {
                selectedIds.Add(counterpart.CombatantId);
            }
        }

        return selectedIds;
    }

    public void ReplaceCounterparts(IReadOnlyCollection<DetailCounterpartOption> options)
    {
        var previousSelections = new Dictionary<int, bool>(Counterparts.Count);
        foreach (var counterpart in Counterparts)
        {
            previousSelections[counterpart.CombatantId] = counterpart.IsSelected;
            counterpart.SelectionChanged -= HandleCounterpartSelectionChanged;
        }

        var selectNewOptions = previousSelections.Count == 0 || previousSelections.Values.All(static value => value);

        _suppressSelectionChanged = true;
        try
        {
            Counterparts.Clear();
            foreach (var option in options)
            {
                var isSelected = previousSelections.TryGetValue(option.CombatantId, out var preservedSelection)
                    ? preservedSelection
                    : selectNewOptions;
                var counterpart = new DetailCounterpartSelectionViewModel(
                    option.CombatantId,
                    option.DisplayName,
                    option.DamageAmount,
                    option.DamageShare,
                    option.HealingAmount,
                    option.HealingShare,
                    option.ShieldAmount,
                    option.ShieldShare,
                    isSelected);
                counterpart.SelectionChanged += HandleCounterpartSelectionChanged;
                Counterparts.Add(counterpart);
            }
        }
        finally
        {
            _suppressSelectionChanged = false;
        }

        OnPropertyChanged(nameof(HasCounterparts));
        OnPropertyChanged(nameof(AreAllCounterpartsSelected));
    }

    public void Clear()
    {
        foreach (var counterpart in Counterparts)
        {
            counterpart.SelectionChanged -= HandleCounterpartSelectionChanged;
        }

        Counterparts.Clear();
        OnPropertyChanged(nameof(HasCounterparts));
        OnPropertyChanged(nameof(AreAllCounterpartsSelected));
    }

    private void SetAllCounterpartsSelected(bool isSelected)
    {
        if (Counterparts.Count == 0)
        {
            return;
        }

        var changed = false;
        _suppressSelectionChanged = true;
        try
        {
            foreach (var counterpart in Counterparts)
            {
                if (counterpart.IsSelected == isSelected)
                {
                    continue;
                }

                counterpart.IsSelected = isSelected;
                changed = true;
            }
        }
        finally
        {
            _suppressSelectionChanged = false;
        }

        OnPropertyChanged(nameof(AreAllCounterpartsSelected));
        if (changed)
        {
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void HandleCounterpartSelectionChanged(object? sender, EventArgs e)
    {
        if (_suppressSelectionChanged)
        {
            return;
        }

        OnPropertyChanged(nameof(AreAllCounterpartsSelected));
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void HandleLocalizationPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is "Item[]" or nameof(LocalizationService.CurrentLanguage))
        {
            OnPropertyChanged(nameof(CounterpartTitle));
        }
    }
}
