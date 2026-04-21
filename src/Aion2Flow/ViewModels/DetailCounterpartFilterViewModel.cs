using System.Collections.ObjectModel;
using System.ComponentModel;
using Cloris.Aion2Flow.Services;
using CommunityToolkit.Mvvm.ComponentModel;

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
        var existingByCombatantId = new Dictionary<int, DetailCounterpartSelectionViewModel>(Counterparts.Count);
        foreach (var counterpart in Counterparts)
        {
            previousSelections[counterpart.CombatantId] = counterpart.IsSelected;
            existingByCombatantId[counterpart.CombatantId] = counterpart;
            counterpart.SelectionChanged -= HandleCounterpartSelectionChanged;
        }

        var selectNewOptions = previousSelections.Count == 0 || previousSelections.Values.All(static value => value);
        var optionList = options as IList<DetailCounterpartOption> ?? options.ToList();
        var expectedCombatantIds = new HashSet<int>(optionList.Count);

        _suppressSelectionChanged = true;
        try
        {
            for (var index = 0; index < optionList.Count; index++)
            {
                var option = optionList[index];
                expectedCombatantIds.Add(option.CombatantId);
                var isSelected = previousSelections.TryGetValue(option.CombatantId, out var preservedSelection)
                    ? preservedSelection
                    : selectNewOptions;

                if (!existingByCombatantId.TryGetValue(option.CombatantId, out var counterpart))
                {
                    counterpart = new DetailCounterpartSelectionViewModel(
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

                    if (index < Counterparts.Count)
                    {
                        Counterparts.Insert(index, counterpart);
                    }
                    else
                    {
                        Counterparts.Add(counterpart);
                    }

                    existingByCombatantId[option.CombatantId] = counterpart;
                    continue;
                }

                counterpart.SelectionChanged += HandleCounterpartSelectionChanged;
                counterpart.ApplyFrom(option);
                counterpart.IsSelected = isSelected;

                var currentIndex = Counterparts.IndexOf(counterpart);
                if (currentIndex >= 0 && currentIndex != index)
                {
                    Counterparts.Move(currentIndex, index);
                }
            }

            for (var index = Counterparts.Count - 1; index >= 0; index--)
            {
                var counterpart = Counterparts[index];
                if (expectedCombatantIds.Contains(counterpart.CombatantId))
                {
                    continue;
                }

                counterpart.SelectionChanged -= HandleCounterpartSelectionChanged;
                Counterparts.RemoveAt(index);
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
