using Cloris.Aion2Flow.Services;

namespace Cloris.Aion2Flow.ViewModels;

public sealed class UiFrameBatchService
{
    private List<FrameBatchedObservableObject>? _pendingViewModels;
    private List<FrameBatchedObservableObject>? _flushBuffer;

    public UiFrameBatchService()
    {
    }

    public UiFrameBatchService(AvaloniaFrameClockService frameClock)
    {
        frameClock.FrameCompleted += (_, _) => FlushFrame();
    }

    internal void Enqueue(FrameBatchedObservableObject viewModel)
    {
        var pending = _pendingViewModels ??= [];
        pending.Add(viewModel);
    }

    public void FlushFrame()
    {
        var pending = _pendingViewModels;
        if (pending is null || pending.Count == 0)
        {
            return;
        }

        var reentrantPending = _flushBuffer;
        _flushBuffer = pending;
        _pendingViewModels = reentrantPending;
        _pendingViewModels?.Clear();

        for (var i = 0; i < pending.Count; i++)
        {
            var viewModel = pending[i];
            viewModel.PrepareFrameFlush();
            viewModel.FlushPendingPropertyChanges();
        }

        pending.Clear();
        if (_pendingViewModels is null || _pendingViewModels.Count == 0)
        {
            _pendingViewModels = pending;
            _flushBuffer = reentrantPending;
        }
        else
        {
            _flushBuffer = pending;
        }
    }
}
