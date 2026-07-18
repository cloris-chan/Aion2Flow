using System.Runtime.InteropServices;
using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.Services;

namespace Cloris.Aion2Flow.ViewModels;

internal static class ResourceDetailRowBuilder
{
    public static void Build(
        Dictionary<CombatEventKey, ResourceSkillMetrics> skills,
        SceneDisplayContext? displayContext,
        LocalizationService localization,
        List<ResourceDetailRowData> rows,
        Dictionary<SkillBaseKey, int> rowIndexes)
    {
        rows.Clear();
        rowIndexes.Clear();

        foreach (var (eventKey, metrics) in skills)
        {
            if (metrics.EventCount <= 0)
                continue;

            var projection = SkillDetailRowBuilder.ResolveSkillBaseProjection(eventKey, displayContext, localization);
            var row = new ResourceDetailRowData
            {
                BaseKey = projection.Key,
                SkillCode = projection.SkillCode,
                DisplayName = projection.DisplayName,
                ManaChange = metrics.ManaChange,
                DirectEvents = metrics.DirectEvents,
                PeriodicEvents = metrics.PeriodicEvents,
                EventCount = metrics.EventCount
            };
            AddOrMerge(rows, rowIndexes, in row);
        }

        rows.Sort(static (left, right) =>
        {
            var comparison = right.ManaChange.CompareTo(left.ManaChange);
            if (comparison != 0)
                return comparison;

            comparison = right.EventCount.CompareTo(left.EventCount);
            if (comparison != 0)
                return comparison;

            comparison = StringComparer.CurrentCulture.Compare(left.DisplayName, right.DisplayName);
            return comparison != 0 ? comparison : left.BaseKey.CompareTo(right.BaseKey);
        });
    }

    public static void ApplySummary(ResourceDetailSectionViewModel section, List<ResourceDetailRowData> rows)
    {
        long manaChange = 0;
        int directEvents = 0, periodicEvents = 0;
        foreach (ref readonly var row in CollectionsMarshal.AsSpan(rows))
        {
            manaChange += row.ManaChange;
            directEvents += row.DirectEvents;
            periodicEvents += row.PeriodicEvents;
        }

        section.ReplaceRows(rows);
        section.ManaChange = manaChange;
        section.DirectEvents = directEvents;
        section.PeriodicEvents = periodicEvents;
        section.EventCount = directEvents + periodicEvents;
        section.SkillCount = rows.Count;
        section.HasResources = rows.Count > 0;
    }

    private static void AddOrMerge(
        List<ResourceDetailRowData> rows,
        Dictionary<SkillBaseKey, int> rowIndexes,
        in ResourceDetailRowData row)
    {
        if (rowIndexes.TryGetValue(row.BaseKey, out var index))
        {
            CollectionsMarshal.AsSpan(rows)[index].Merge(in row);
            return;
        }

        rowIndexes.Add(row.BaseKey, rows.Count);
        rows.Add(row);
    }
}
