using System.Runtime.InteropServices;
using Cloris.Aion2Flow.Collections;
using Cloris.Aion2Flow.Protocol.Combat;
using Cloris.Aion2Flow.SceneRuntime.Combat;

namespace Cloris.Aion2Flow.ViewModels;

public sealed class ResourceDetailSectionViewModel(UiFrameBatchService frameBatchService) : FrameBatchedObservableObject(frameBatchService)
{
    private readonly UiFrameBatchService _frameBatchService = frameBatchService;
    private readonly Dictionary<SkillBaseKey, ResourceDetailRowViewModel> _existingByBaseKey = [];

    public KeyedObservableCollection<SkillBaseKey, ResourceDetailRowViewModel> Rows { get; } = new(static row => row.BaseKey)
    {
        ResetThreshold = 24
    };

    public long ManaChange { get; set => SetFrameProperty(ref field, value); }
    public int DirectEvents { get; set => SetFrameProperty(ref field, value); }
    public int PeriodicEvents { get; set => SetFrameProperty(ref field, value); }
    public int EventCount { get; set => SetFrameProperty(ref field, value); }
    public int SkillCount { get; set => SetFrameProperty(ref field, value); }
    public bool HasResources { get; set => SetFrameProperty(ref field, value); }

    internal void ReplaceRows(List<ResourceDetailRowData> dataRows)
    {
        if (Rows.Count == dataRows.Count)
        {
            var dataSpan = CollectionsMarshal.AsSpan(dataRows);
            var orderMatches = true;
            for (var i = 0; i < dataSpan.Length; i++)
            {
                if (Rows[i].BaseKey != dataSpan[i].BaseKey)
                {
                    orderMatches = false;
                    break;
                }
            }

            if (orderMatches)
            {
                for (var i = 0; i < dataSpan.Length; i++)
                    Rows[i].ApplyFrom(in dataSpan[i]);
                return;
            }
        }

        _existingByBaseKey.Clear();
        foreach (var row in Rows)
            _existingByBaseKey.TryAdd(row.BaseKey, row);

        using (Rows.SuspendNotifications())
        {
            Rows.Clear();
            foreach (ref readonly var data in CollectionsMarshal.AsSpan(dataRows))
            {
                if (!_existingByBaseKey.TryGetValue(data.BaseKey, out var row))
                    row = new ResourceDetailRowViewModel(_frameBatchService);

                row.ApplyFrom(in data);
                Rows.Add(row);
            }
        }
    }

    public void Clear()
    {
        Rows.Clear();
        ManaChange = 0;
        DirectEvents = 0;
        PeriodicEvents = 0;
        EventCount = 0;
        SkillCount = 0;
        HasResources = false;
    }
}

public sealed class ResourceDetailRowViewModel(UiFrameBatchService frameBatchService) : FrameBatchedObservableObject(frameBatchService)
{
    public SkillBaseKey BaseKey { get; private set; }
    public int SkillCode { get; private set => SetFrameProperty(ref field, value); }
    public string DisplayName { get; private set => SetFrameProperty(ref field, value); } = string.Empty;
    public long ManaChange { get; private set => SetFrameProperty(ref field, value); }
    public int DirectEvents { get; private set => SetFrameProperty(ref field, value); }
    public int PeriodicEvents { get; private set => SetFrameProperty(ref field, value); }
    public int EventCount { get; private set => SetFrameProperty(ref field, value); }

    internal void ApplyFrom(in ResourceDetailRowData data)
    {
        BaseKey = data.BaseKey;
        SkillCode = data.SkillCode;
        DisplayName = data.DisplayName;
        ManaChange = data.ManaChange;
        DirectEvents = data.DirectEvents;
        PeriodicEvents = data.PeriodicEvents;
        EventCount = data.EventCount;
    }
}

internal struct ResourceDetailRowData
{
    public SkillBaseKey BaseKey;
    public int SkillCode;
    public string DisplayName;
    public long ManaChange;
    public int DirectEvents;
    public int PeriodicEvents;
    public int EventCount;

    public void Merge(in ResourceDetailRowData other)
    {
        if (other.SkillCode == BaseKey.SkillCode ||
            SkillCode != BaseKey.SkillCode && other.SkillCode < SkillCode)
        {
            SkillCode = other.SkillCode;
            DisplayName = other.DisplayName;
        }

        ManaChange += other.ManaChange;
        DirectEvents += other.DirectEvents;
        PeriodicEvents += other.PeriodicEvents;
        EventCount += other.EventCount;
    }
}

internal struct ResourceSkillMetrics
{
    public long ManaChange;
    public int DirectEvents;
    public int PeriodicEvents;

    public readonly int EventCount => DirectEvents + PeriodicEvents;

    public void Process(in CombatResourceOccurrence resource)
    {
        if (resource.Resource != CombatResourceKind.Mana)
            return;

        ManaChange += resource.Amount;

        if (resource.Delivery == CombatResourceDeliveryKind.Periodic)
            PeriodicEvents++;
        else
            DirectEvents++;
    }
}

internal sealed class ResourceDetailSectionAggregation
{
    public Dictionary<CombatEventKey, ResourceSkillMetrics> Skills { get; } = [];
    public bool HasSubsetFilter { get; private set; }

    public void Reset(bool hasSubsetFilter)
    {
        Skills.Clear();
        HasSubsetFilter = hasSubsetFilter;
    }
}
