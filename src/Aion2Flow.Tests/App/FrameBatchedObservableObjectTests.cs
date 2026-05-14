using System.ComponentModel;
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
        var rowData = new SkillDetailRowData
        {
            SkillCode = 11000010,
            TotalAmount = 500,
            DirectAmount = 500,
            Hits = 2,
            Attempts = 2,
            Criticals = 1
        };

        section.ReplaceRows([rowData]);

        var row = Assert.Single(section.Rows);
        Assert.Equal(rowData.SkillCode, row.SkillCode);
        Assert.Equal(rowData.TotalAmount, row.TotalAmount);
        Assert.Equal(0.5d, row.CriticalRate);
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
