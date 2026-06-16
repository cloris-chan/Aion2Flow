using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Cloris.Aion2Flow.ViewModels;

public abstract class FrameBatchedObservableObject(UiFrameBatchService frameBatchService) : ObservableObject
{
    private static readonly ConcurrentDictionary<string, PropertyChangingEventArgs> ChangingEventArgsCache = new();
    private static readonly ConcurrentDictionary<string, PropertyChangedEventArgs> ChangedEventArgsCache = new();
    private List<string>? _pendingPropertyNames;
    private List<string>? _flushBuffer;
    private bool _isQueuedForFrame;

    protected bool SetFrameProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        propertyName = RequirePropertyName(propertyName);
        OnPropertyChanging(GetChangingEventArgs(propertyName));
        field = value;
        QueueFramePropertyChanged(propertyName);
        return true;
    }

    protected void QueueFramePropertyChanged([CallerMemberName] string? propertyName = null)
    {
        propertyName = RequirePropertyName(propertyName);
        var pending = _pendingPropertyNames ??= [];
        if (!pending.Contains(propertyName))
        {
            pending.Add(propertyName);
        }

        if (!_isQueuedForFrame)
        {
            _isQueuedForFrame = true;
            frameBatchService.Enqueue(this);
        }
    }

    internal void PrepareFrameFlush()
    {
        _isQueuedForFrame = false;
    }

    internal void FlushPendingPropertyChanges()
    {
        var pending = _pendingPropertyNames;
        if (pending is null || pending.Count == 0)
        {
            return;
        }

        var reentrantPending = _flushBuffer;
        _flushBuffer = pending;
        _pendingPropertyNames = reentrantPending;
        _pendingPropertyNames?.Clear();

        for (var i = 0; i < pending.Count; i++)
        {
            OnPropertyChanged(GetChangedEventArgs(pending[i]));
        }

        pending.Clear();
        if (_pendingPropertyNames is null || _pendingPropertyNames.Count == 0)
        {
            _pendingPropertyNames = pending;
            _flushBuffer = reentrantPending;
        }
        else
        {
            _flushBuffer = pending;
        }
    }

    private static string RequirePropertyName(string? propertyName)
        => string.IsNullOrEmpty(propertyName)
            ? throw new ArgumentException("A property name is required.", nameof(propertyName))
            : propertyName;

    private static PropertyChangingEventArgs GetChangingEventArgs(string propertyName)
        => ChangingEventArgsCache.GetOrAdd(propertyName, static name => new PropertyChangingEventArgs(name));

    private static PropertyChangedEventArgs GetChangedEventArgs(string propertyName)
        => ChangedEventArgsCache.GetOrAdd(propertyName, static name => new PropertyChangedEventArgs(name));
}
