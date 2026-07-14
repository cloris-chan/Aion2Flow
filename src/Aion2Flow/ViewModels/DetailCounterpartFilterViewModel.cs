using System.ComponentModel;
using System.Globalization;
using Cloris.Aion2Flow.Collections;
using Cloris.Aion2Flow.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Cloris.Aion2Flow.ViewModels;

public sealed class DetailCounterpartFilterViewModel : ObservableObject, IDisposable
{
    private readonly Dictionary<int, bool> _previousSelections = [];
    private readonly Dictionary<int, DetailCounterpartSelectionViewModel> _existingByCombatantId = [];
    private readonly HashSet<int> _expectedCombatantIds = [];
    private bool _disposed;
    private bool _suppressSelectionChanged;

    public DetailCounterpartFilterViewModel(LocalizationService localization, string counterpartTitleKey)
    {
        Localization = localization;
        CounterpartTitleKey = counterpartTitleKey;
        Localization.PropertyChanged += HandleLocalizationPropertyChanged;
    }

    public LocalizationService Localization { get; }

    public string CounterpartTitleKey { get; }

    public string CounterpartTitle => Localization[CounterpartTitleKey];

    public string CounterpartSummary
    {
        get
        {
            var count = Counterparts.Count;
            if (count == 0)
                return CounterpartTitle;

            var selectedCount = CountSelectedCounterparts();
            return string.Create(CultureInfo.InvariantCulture, $"{CounterpartTitle} {selectedCount}/{count}");
        }
    }

    public KeyedObservableCollection<int, DetailCounterpartSelectionViewModel> Counterparts { get; } = new(static counterpart => counterpart.CombatantId)
    {
        ResetThreshold = 24
    };

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

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Localization.PropertyChanged -= HandleLocalizationPropertyChanged;
        foreach (var counterpart in Counterparts)
            counterpart.SelectionChanged -= HandleCounterpartSelectionChanged;
    }

    public void CopySelectedCounterpartIds(HashSet<int> destination)
    {
        destination.Clear();
        foreach (var counterpart in Counterparts)
        {
            if (counterpart.IsSelected)
            {
                destination.Add(counterpart.CombatantId);
            }
        }
    }

    public void ReplaceCounterparts(IReadOnlyCollection<DetailCounterpartOption> options)
    {
        var optionList = options as IList<DetailCounterpartOption> ?? [.. options];
        if (ApplyCounterpartsInPlaceIfOrderMatches(optionList))
        {
            OnPropertyChanged(nameof(HasCounterparts));
            OnPropertyChanged(nameof(AreAllCounterpartsSelected));
            OnPropertyChanged(nameof(CounterpartSummary));
            return;
        }

        _previousSelections.Clear();
        _existingByCombatantId.Clear();
        _expectedCombatantIds.Clear();
        foreach (var counterpart in Counterparts)
        {
            _previousSelections[counterpart.CombatantId] = counterpart.IsSelected;
            _existingByCombatantId[counterpart.CombatantId] = counterpart;
            counterpart.SelectionChanged -= HandleCounterpartSelectionChanged;
        }

        var selectNewOptions = _previousSelections.Count == 0 || _previousSelections.Values.All(static value => value);

        _suppressSelectionChanged = true;
        try
        {
            using (Counterparts.SuspendNotifications())
            {
                for (var index = 0; index < optionList.Count; index++)
                {
                    var option = optionList[index];
                    _expectedCombatantIds.Add(option.CombatantId);
                    var isSelected = _previousSelections.TryGetValue(option.CombatantId, out var preservedSelection)
                        ? preservedSelection
                        : selectNewOptions;

                    if (!_existingByCombatantId.TryGetValue(option.CombatantId, out var counterpart))
                    {
                        counterpart = new DetailCounterpartSelectionViewModel(
                            option.CombatantId,
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

                        _existingByCombatantId[option.CombatantId] = counterpart;
                        continue;
                    }

                    counterpart.SelectionChanged += HandleCounterpartSelectionChanged;
                    counterpart.ApplyFrom(option);
                    counterpart.IsSelected = isSelected;

                    var currentIndex = Counterparts.IndexOf(counterpart);
                    if (currentIndex >= 0 && currentIndex != index)
                    {
                        MoveCounterpart(currentIndex, index);
                    }
                }

                for (var index = Counterparts.Count - 1; index >= 0; index--)
                {
                    var counterpart = Counterparts[index];
                    if (_expectedCombatantIds.Contains(counterpart.CombatantId))
                    {
                        continue;
                    }

                    counterpart.SelectionChanged -= HandleCounterpartSelectionChanged;
                    Counterparts.RemoveAt(index);
                }
            }
        }
        finally
        {
            _suppressSelectionChanged = false;
        }

        OnPropertyChanged(nameof(HasCounterparts));
        OnPropertyChanged(nameof(AreAllCounterpartsSelected));
        OnPropertyChanged(nameof(CounterpartSummary));
    }

    private bool ApplyCounterpartsInPlaceIfOrderMatches(IList<DetailCounterpartOption> options)
    {
        if (Counterparts.Count != options.Count)
            return false;

        for (var i = 0; i < options.Count; i++)
        {
            if (Counterparts[i].CombatantId != options[i].CombatantId)
                return false;
        }

        _suppressSelectionChanged = true;
        try
        {
            for (var i = 0; i < options.Count; i++)
            {
                Counterparts[i].ApplyFrom(options[i]);
            }
        }
        finally
        {
            _suppressSelectionChanged = false;
        }

        return true;
    }

    private void MoveCounterpart(int oldIndex, int newIndex)
    {
        if (oldIndex == newIndex)
            return;

        var counterpart = Counterparts[oldIndex];
        Counterparts.RemoveAt(oldIndex);
        Counterparts.Insert(newIndex, counterpart);
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
        OnPropertyChanged(nameof(CounterpartSummary));
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
        OnPropertyChanged(nameof(CounterpartSummary));
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
        OnPropertyChanged(nameof(CounterpartSummary));
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void HandleLocalizationPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is "Item[]" or nameof(LocalizationService.CurrentLanguage))
        {
            OnPropertyChanged(nameof(CounterpartTitle));
            OnPropertyChanged(nameof(CounterpartSummary));
        }
    }

    private int CountSelectedCounterparts()
    {
        var selectedCount = 0;
        foreach (var counterpart in Counterparts)
        {
            if (counterpart.IsSelected)
            {
                selectedCount++;
            }
        }

        return selectedCount;
    }
}
