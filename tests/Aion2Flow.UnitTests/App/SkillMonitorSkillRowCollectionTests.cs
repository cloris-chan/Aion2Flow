using System.Collections.Specialized;
using Cloris.Aion2Flow.ViewModels;

namespace Cloris.Aion2Flow.Tests.App;

public sealed class SkillMonitorSkillRowCollectionTests
{
    [Fact]
    public void Reconcile_UpdatesStableRowsWithoutCollectionMutation()
    {
        var rows = new SkillMonitorSkillRowCollection();
        rows.Reconcile(
        [
            CreateData(13_050_000, 0.9, "9.0s"),
            CreateData(13_130_000, 0.8, "8.0s")
        ]);
        var first = rows[0];
        var second = rows[1];
        var collectionActions = new List<NotifyCollectionChangedAction>();
        rows.CollectionChanged += (_, args) => collectionActions.Add(args.Action);

        rows.Reconcile(
        [
            CreateData(13_050_000, 0.7, "7.0s"),
            CreateData(13_130_000, 0.6, "6.0s")
        ]);

        Assert.Same(first, rows[0]);
        Assert.Same(second, rows[1]);
        Assert.Equal(0.7, rows[0].CooldownProgressValue);
        Assert.Equal("7.0s", rows[0].CooldownRemainingText);
        Assert.Empty(collectionActions);
    }

    [Fact]
    public void Reconcile_ChangesOnlyRowsWhoseSkillIdentityChanged()
    {
        var rows = new SkillMonitorSkillRowCollection();
        rows.Reconcile(
        [
            CreateData(13_050_000, 0.9, "9.0s"),
            CreateData(13_130_000, 0.8, "8.0s")
        ]);
        var retained = rows[1];
        var collectionActions = new List<NotifyCollectionChangedAction>();
        rows.CollectionChanged += (_, args) => collectionActions.Add(args.Action);

        rows.Reconcile(
        [
            CreateData(13_010_000, 0.7, "7.0s"),
            CreateData(13_130_000, 0.6, "6.0s"),
            CreateData(13_210_000, 0.5, "5.0s")
        ]);

        Assert.Equal([13_010_000, 13_130_000, 13_210_000], rows.Select(static row => row.RowBaseSkillId));
        Assert.Same(retained, rows[1]);
        Assert.Equal(3, collectionActions.Count);
        Assert.DoesNotContain(NotifyCollectionChangedAction.Reset, collectionActions);
        Assert.DoesNotContain(NotifyCollectionChangedAction.Replace, collectionActions);
    }

    [Fact]
    public void Reconcile_UnchangedRowsDoesNotAllocate()
    {
        var rows = new SkillMonitorSkillRowCollection();
        SkillMonitorSkillSlotData[] data =
        [
            CreateData(13_050_000, 0.9, "9.0s"),
            CreateData(13_130_000, 0.8, "8.0s")
        ];
        rows.Reconcile(data);
        rows.Reconcile(data);

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var iteration = 0; iteration < 1_000; iteration++)
            rows.Reconcile(data);

        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Assert.Equal(0, allocatedBytes);
    }

    private static SkillMonitorSkillSlotData CreateData(int skillId, double cooldownProgress, string cooldownText) =>
        new(
            skillId,
            $"Skill_{skillId}",
            $"Skill {skillId}",
            0,
            false,
            string.Empty,
            cooldownProgress,
            cooldownText,
            true,
            null,
            string.Empty,
            false,
            0);
}
