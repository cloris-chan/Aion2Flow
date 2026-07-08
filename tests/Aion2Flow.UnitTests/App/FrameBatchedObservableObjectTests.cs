using System.Collections.Specialized;
using System.ComponentModel;
using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.ViewModels;

namespace Cloris.Aion2Flow.Tests.App;

public sealed class FrameBatchedObservableObjectTests
{
    [Fact]
    public void SetFrameProperty_RaisesChangingImmediately_AndChangedOnFlush()
    {
        var frameBatch = new UiFrameBatchService();
        var viewModel = new TestFrameViewModel(frameBatch);
        var changing = new List<string?>();
        var changed = new List<string?>();
        viewModel.PropertyChanging += (_, e) => changing.Add(e.PropertyName);
        viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        viewModel.Value = 1;

        Assert.Equal(["Value"], changing);
        Assert.Empty(changed);

        frameBatch.FlushFrame();

        Assert.Equal(["Value"], changed);
    }

    [Fact]
    public void FlushFrame_CoalescesSameProperty()
    {
        var frameBatch = new UiFrameBatchService();
        var viewModel = new TestFrameViewModel(frameBatch);
        var changedCount = 0;
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TestFrameViewModel.Value))
            {
                changedCount++;
            }
        };

        for (var i = 1; i <= 1000; i++)
        {
            viewModel.Value = i;
        }

        Assert.Equal(1000, viewModel.Value);
        Assert.Equal(0, changedCount);

        frameBatch.FlushFrame();

        Assert.Equal(1, changedCount);
    }

    [Fact]
    public void FlushFrame_PreservesFirstPendingOrder()
    {
        var frameBatch = new UiFrameBatchService();
        var viewModel = new TestFrameViewModel(frameBatch);
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        viewModel.Value = 1;
        viewModel.Other = 2;
        viewModel.Value = 3;

        frameBatch.FlushFrame();

        Assert.Equal(["Value", "Other"], changed);
    }

    [Fact]
    public void EventArgs_AreCachedAcrossFlushes()
    {
        var frameBatch = new UiFrameBatchService();
        var viewModel = new TestFrameViewModel(frameBatch);
        var changedArgs = new List<PropertyChangedEventArgs>();
        viewModel.PropertyChanged += (_, e) => changedArgs.Add(e);

        viewModel.Value = 1;
        frameBatch.FlushFrame();
        viewModel.Value = 2;
        frameBatch.FlushFrame();

        Assert.Equal(2, changedArgs.Count);
        Assert.Same(changedArgs[0], changedArgs[1]);
    }

    [Fact]
    public void SetFrameProperty_SkipsUnchangedValue()
    {
        var frameBatch = new UiFrameBatchService();
        var viewModel = new TestFrameViewModel(frameBatch);
        var changingCount = 0;
        var changedCount = 0;
        viewModel.PropertyChanging += (_, _) => changingCount++;
        viewModel.PropertyChanged += (_, _) => changedCount++;

        viewModel.Value = 0;
        frameBatch.FlushFrame();

        Assert.Equal(0, changingCount);
        Assert.Equal(0, changedCount);
    }

    [Fact]
    public void FlushFrame_OnlyNotifiesPendingViewModels()
    {
        var frameBatch = new UiFrameBatchService();
        var changedViewModel = new TestFrameViewModel(frameBatch);
        var idleViewModel = new TestFrameViewModel(frameBatch);
        var idleChangedCount = 0;
        idleViewModel.PropertyChanged += (_, _) => idleChangedCount++;

        changedViewModel.Value = 1;
        frameBatch.FlushFrame();

        Assert.Equal(0, idleChangedCount);
    }

    [Fact]
    public void FlushFrame_DefersReentrantChangesUntilNextFrame()
    {
        var frameBatch = new UiFrameBatchService();
        var viewModel = new TestFrameViewModel(frameBatch);
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, e) =>
        {
            changed.Add(e.PropertyName);
            if (e.PropertyName == nameof(TestFrameViewModel.Value))
            {
                viewModel.Other = 2;
            }
        };

        viewModel.Value = 1;
        frameBatch.FlushFrame();

        Assert.Equal(["Value"], changed);

        frameBatch.FlushFrame();

        Assert.Equal(["Value", "Other"], changed);
    }

    [Fact]
    public void SkillDetailSection_QueuesHotMetricChangesUntilFlush()
    {
        var frameBatch = new UiFrameBatchService();
        var section = new SkillDetailSectionViewModel(frameBatch);
        var changed = new List<string?>();
        section.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        section.Total = 100;
        section.PerSecond = 25;

        Assert.DoesNotContain(nameof(SkillDetailSectionViewModel.Total), changed);
        Assert.DoesNotContain(nameof(SkillDetailSectionViewModel.PerSecond), changed);

        frameBatch.FlushFrame();

        Assert.Contains(nameof(SkillDetailSectionViewModel.Total), changed);
        Assert.Contains(nameof(SkillDetailSectionViewModel.PerSecond), changed);
    }

    [Fact]
    public void SkillDetailSection_InitialRowsUseFrameBatchedApplyFrom()
    {
        var frameBatch = new UiFrameBatchService();
        var section = new SkillDetailSectionViewModel(frameBatch);
        var baseKey = SkillBaseKey.FromEventKey(new CombatEventKey(11000010, default, default));
        var rowData = new SkillDetailRowData
        {
            BaseKey = baseKey,
            SkillCode = 11000010,
            EventCount = 2,
            TotalAmount = 500,
            DirectAmount = 500,
            Hits = 2,
            Attempts = 2,
            Criticals = 1
        };

        section.ReplaceRows([rowData]);

        var row = Assert.Single(section.Rows);
        Assert.Equal(rowData.SkillCode, row.SkillCode);
        Assert.Equal(rowData.EventCount, row.EventCount);
        Assert.Equal(rowData.TotalAmount, row.TotalAmount);
        Assert.Equal(0.5d, row.CriticalRate);
    }

    [Fact]
    public void SkillDetailSection_StableRows_UpdateInPlaceWithoutCollectionNotification()
    {
        var frameBatch = new UiFrameBatchService();
        var section = new SkillDetailSectionViewModel(frameBatch);
        var firstKey = new SkillBaseKey(new CombatEventKey(11000010, default, default));
        var secondKey = new SkillBaseKey(new CombatEventKey(12000010, default, default));

        section.ReplaceRows(
        [
            CreateRowData(firstKey, 11000010, 1, 100),
            CreateRowData(secondKey, 12000010, 2, 200)
        ]);

        var firstRow = section.Rows[0];
        var secondRow = section.Rows[1];
        var collectionChangeCount = 0;
        section.Rows.CollectionChanged += (_, _) => collectionChangeCount++;

        section.ReplaceRows(
        [
            CreateRowData(firstKey, 11000010, 3, 300),
            CreateRowData(secondKey, 12000010, 4, 400)
        ]);

        Assert.Equal(0, collectionChangeCount);
        Assert.Same(firstRow, section.Rows[0]);
        Assert.Same(secondRow, section.Rows[1]);
        Assert.Equal(300, section.Rows[0].TotalAmount);
        Assert.Equal(400, section.Rows[1].TotalAmount);
    }

    [Fact]
    public void SkillDetailSection_LargeStructuralRowChanges_ResetOnce()
    {
        var frameBatch = new UiFrameBatchService();
        var section = new SkillDetailSectionViewModel(frameBatch);
        section.Rows.ResetThreshold = 4;

        section.ReplaceRows(CreateRows(11000010, 6));

        var actions = new List<NotifyCollectionChangedAction>();
        section.Rows.CollectionChanged += (_, e) => actions.Add(e.Action);

        section.ReplaceRows(CreateRows(12000010, 6));

        Assert.Equal([NotifyCollectionChangedAction.Reset], actions);
        Assert.Equal(6, section.Rows.Count);
        Assert.Equal(12000010, section.Rows[0].SkillCode);
    }

    [Fact]
    public void SkillDetailSection_SelectRow_MarksSelectedRow()
    {
        var frameBatch = new UiFrameBatchService();
        var section = new SkillDetailSectionViewModel(frameBatch);
        var baseKey = SkillBaseKey.FromEventKey(new CombatEventKey(11000010, default, default));

        section.ReplaceRows(
        [
            CreateRowData(baseKey, 11000010, 1, 100)
        ]);

        var row = Assert.Single(section.Rows);
        section.SelectRow(row);

        Assert.Same(row, section.SelectedRow);
        Assert.True(row.IsSelected);
    }

    [Fact]
    public void SkillDetailSection_ReplaceRows_PreservesSelection()
    {
        var frameBatch = new UiFrameBatchService();
        var section = new SkillDetailSectionViewModel(frameBatch);
        var baseKey = SkillBaseKey.FromEventKey(new CombatEventKey(11000010, default, default));

        section.ReplaceRows(
        [
            CreateRowData(baseKey, 11000010, 1, 100)
        ]);

        var selectedRow = Assert.Single(section.Rows);
        section.SelectRow(selectedRow);

        section.ReplaceRows(
        [
            CreateRowData(baseKey, 11000010, 8, 300)
        ]);

        Assert.Same(selectedRow, section.SelectedRow);
        Assert.True(selectedRow.IsSelected);
        Assert.Equal(8, selectedRow.EventCount);
        Assert.Equal(300, selectedRow.TotalAmount);

        section.ReplaceRows([]);

        Assert.Null(section.SelectedRow);
        Assert.False(selectedRow.IsSelected);
    }

    private static SkillDetailRowData CreateRowData(SkillBaseKey baseKey, int skillCode, int eventCount, long damage)
    {
        return new SkillDetailRowData
        {
            BaseKey = baseKey,
            SkillCode = skillCode,
            DisplayName = "Strike",
            EventCount = eventCount,
            TotalAmount = damage,
            DirectAmount = damage,
            Hits = 1,
            Attempts = 1
        };
    }

    private static List<SkillDetailRowData> CreateRows(int firstSkillCode, int count)
    {
        var rows = new List<SkillDetailRowData>(count);
        for (var i = 0; i < count; i++)
        {
            var skillCode = firstSkillCode + i;
            rows.Add(CreateRowData(new SkillBaseKey(new CombatEventKey(skillCode, default, default)), skillCode, i, 100 + i));
        }

        return rows;
    }

    private sealed class TestFrameViewModel(UiFrameBatchService frameBatchService) : FrameBatchedObservableObject(frameBatchService)
    {
        private int _value;
        private int _other;

        public int Value
        {
            get => _value;
            set => SetFrameProperty(ref _value, value);
        }

        public int Other
        {
            get => _other;
            set => SetFrameProperty(ref _other, value);
        }
    }
}
