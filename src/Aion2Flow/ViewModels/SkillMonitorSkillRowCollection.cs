using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Cloris.Aion2Flow.ViewModels;

internal readonly record struct SkillMonitorSkillSlotData(
    int RowBaseSkillId,
    string? IconAssetName,
    string ToolTipText,
    double BuffProgressValue,
    bool HasBuff,
    string BuffRemainingText,
    double CooldownProgressValue,
    string CooldownRemainingText,
    bool HasCooldown,
    int? AvailableCount,
    string AvailableCountText,
    bool HasAvailableCount,
    long CompletionStartedUtcMilliseconds);

internal sealed class SkillMonitorSkillRowCollection : ObservableCollection<SkillMonitorSkillSlot>
{
    public void Reconcile(ReadOnlySpan<SkillMonitorSkillSlotData> slots)
    {
        var rowIndex = 0;
        for (var slotIndex = 0; slotIndex < slots.Length; slotIndex++)
        {
            ref readonly var slot = ref slots[slotIndex];
            while (rowIndex < Count && this[rowIndex].RowBaseSkillId < slot.RowBaseSkillId)
                RemoveAt(rowIndex);

            if (rowIndex < Count && this[rowIndex].RowBaseSkillId == slot.RowBaseSkillId)
                this[rowIndex].Update(in slot);
            else
                Insert(rowIndex, new SkillMonitorSkillSlot(in slot));

            rowIndex++;
        }

        while (Count > rowIndex)
            RemoveAt(Count - 1);
    }
}

public sealed class SkillMonitorSkillSlot : ObservableObject
{
    private string? _iconAssetName;
    private string _toolTipText;
    private double _buffProgressValue;
    private bool _hasBuff;
    private string _buffRemainingText;
    private double _cooldownProgressValue;
    private string _cooldownRemainingText;
    private bool _hasCooldown;
    private int? _availableCount;
    private string _availableCountText;
    private bool _hasAvailableCount;
    private long _completionStartedUtcMilliseconds;

    internal SkillMonitorSkillSlot(in SkillMonitorSkillSlotData data)
    {
        RowBaseSkillId = data.RowBaseSkillId;
        _iconAssetName = data.IconAssetName;
        _toolTipText = data.ToolTipText;
        _buffProgressValue = data.BuffProgressValue;
        _hasBuff = data.HasBuff;
        _buffRemainingText = data.BuffRemainingText;
        _cooldownProgressValue = data.CooldownProgressValue;
        _cooldownRemainingText = data.CooldownRemainingText;
        _hasCooldown = data.HasCooldown;
        _availableCount = data.AvailableCount;
        _availableCountText = data.AvailableCountText;
        _hasAvailableCount = data.HasAvailableCount;
        _completionStartedUtcMilliseconds = data.CompletionStartedUtcMilliseconds;
    }

    public int RowBaseSkillId { get; }

    public string? IconAssetName
    {
        get => _iconAssetName;
        private set => SetProperty(ref _iconAssetName, value);
    }

    public string ToolTipText
    {
        get => _toolTipText;
        private set => SetProperty(ref _toolTipText, value);
    }

    public double BuffProgressValue
    {
        get => _buffProgressValue;
        private set => SetProperty(ref _buffProgressValue, value);
    }

    public bool HasBuff
    {
        get => _hasBuff;
        private set => SetProperty(ref _hasBuff, value);
    }

    public string BuffRemainingText
    {
        get => _buffRemainingText;
        private set => SetProperty(ref _buffRemainingText, value);
    }

    public double CooldownProgressValue
    {
        get => _cooldownProgressValue;
        private set => SetProperty(ref _cooldownProgressValue, value);
    }

    public string CooldownRemainingText
    {
        get => _cooldownRemainingText;
        private set => SetProperty(ref _cooldownRemainingText, value);
    }

    public bool HasCooldown
    {
        get => _hasCooldown;
        private set => SetProperty(ref _hasCooldown, value);
    }

    public int? AvailableCount
    {
        get => _availableCount;
        private set => SetProperty(ref _availableCount, value);
    }

    public string AvailableCountText
    {
        get => _availableCountText;
        private set => SetProperty(ref _availableCountText, value);
    }

    public bool HasAvailableCount
    {
        get => _hasAvailableCount;
        private set => SetProperty(ref _hasAvailableCount, value);
    }

    public long CompletionStartedUtcMilliseconds
    {
        get => _completionStartedUtcMilliseconds;
        private set => SetProperty(ref _completionStartedUtcMilliseconds, value);
    }

    internal void Update(in SkillMonitorSkillSlotData data)
    {
        IconAssetName = data.IconAssetName;
        ToolTipText = data.ToolTipText;
        BuffProgressValue = data.BuffProgressValue;
        HasBuff = data.HasBuff;
        BuffRemainingText = data.BuffRemainingText;
        CooldownProgressValue = data.CooldownProgressValue;
        CooldownRemainingText = data.CooldownRemainingText;
        HasCooldown = data.HasCooldown;
        AvailableCount = data.AvailableCount;
        AvailableCountText = data.AvailableCountText;
        HasAvailableCount = data.HasAvailableCount;
        CompletionStartedUtcMilliseconds = data.CompletionStartedUtcMilliseconds;
    }
}
