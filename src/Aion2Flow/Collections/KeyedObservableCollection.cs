using System.Buffers;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Cloris.Aion2Flow.Collections;

public class KeyedObservableCollection<TKey, TItem>(Func<TItem, TKey> keySelector, IEqualityComparer<TKey>? comparer = null) : KeyedCollection<TKey, TItem>(comparer), IReadOnlyList<TItem>, INotifyCollectionChanged, INotifyPropertyChanged
    where TKey : notnull
    where TItem : class
{
    private int _suspendLevel;
    private bool _isModifiedDuringSuspension;
    private List<TItem>? _snapshot;

    public int ResetThreshold { get; set; } = 50;

    public IEnumerable<TKey> Keys => Dictionary?.Keys ?? [];

    public event PropertyChangedEventHandler? PropertyChanged;
    public event NotifyCollectionChangedEventHandler? CollectionChanged;

    public KeyedObservableCollection(Func<TItem, TKey> keySelector, IEnumerable<TItem> collection, IEqualityComparer<TKey>? comparer = null) : this(keySelector, comparer)
    {
        ArgumentNullException.ThrowIfNull(collection);
        foreach (var (index, item) in collection.Index())
        {
            base.InsertItem(index, item);
        }
    }

    public void Sort(Comparison<TItem> comparison)
    {
        using (SuspendNotifications())
        {
            if (Items is List<TItem> list)
            {
                list.Sort(comparison);
                _isModifiedDuringSuspension = true;
            }
            else if (Items is TItem[] array)
            {
                Array.Sort(array, comparison);
                _isModifiedDuringSuspension = true;
            }
            else
            {
                var sorted = this.Order(Comparer<TItem>.Create(comparison)).ToArray();
                base.ClearItems();
                foreach (var item in sorted) base.InsertItem(Count, item);
            }
        }
    }

    protected override TKey GetKeyForItem(TItem item) => keySelector(item);

    protected override void InsertItem(int index, TItem item)
    {
        base.InsertItem(index, item);
        if (CheckAndRecordSuspension()) return;
        NotifyAdd(item, index);
    }

    protected override void SetItem(int index, TItem item)
    {
        var oldItem = this[index];
        base.SetItem(index, item);
        if (CheckAndRecordSuspension()) return;
        NotifyReplace(item, oldItem, index);
    }

    protected override void RemoveItem(int index)
    {
        var item = this[index];
        base.RemoveItem(index);
        if (CheckAndRecordSuspension()) return;
        NotifyRemove(item, index);
    }

    protected override void ClearItems()
    {
        base.ClearItems();
        if (CheckAndRecordSuspension()) return;
        NotifyReset();
    }

    private bool CheckAndRecordSuspension()
    {
        if (_suspendLevel > 0)
        {
            _isModifiedDuringSuspension = true;
            return true;
        }
        return false;
    }

    public NotificationDeferral SuspendNotifications(BatchUpdateMode mode = BatchUpdateMode.Default)
    {
        if (_suspendLevel == 0)
        {
            if (_snapshot is null)
            {
                _snapshot = new List<TItem>(Items);
            }
            else
            {
                _snapshot.Clear();
                _snapshot.AddRange(Items);
            }
        }
        _suspendLevel++;
        return new NotificationDeferral(this, mode);
    }

    private void ResumeNotifications(BatchUpdateMode mode)
    {
        _suspendLevel--;

        if (_suspendLevel == 0)
        {
            if (_isModifiedDuringSuspension && _snapshot != null)
            {
                switch (mode)
                {
                    case BatchUpdateMode.ForceReset:
                        NotifyReset();
                        break;
                    case BatchUpdateMode.ForceGranular:
                        ApplyDiffAndNotify(_snapshot, Items, forceGranular: true);
                        break;
                    default:
                        ApplyDiffAndNotify(_snapshot, Items);
                        break;
                }
            }

            _snapshot = null;
            _isModifiedDuringSuspension = false;
        }
    }

    private void ApplyDiffAndNotify(List<TItem> uiState, IList<TItem> newList, bool forceGranular = false)
    {
        var operations = ArrayPool<DiffOperation>.Shared.Rent(ResetThreshold);
        int opCount = 0;

        try
        {
            bool RecordOperation(in DiffOperation op)
            {
                if (!forceGranular && opCount >= ResetThreshold) return true;
                if (opCount >= operations.Length)
                {
                    ArrayPool<DiffOperation>.Shared.Return(operations, true);
                    var newOps = ArrayPool<DiffOperation>.Shared.Rent(operations.Length * 2);
                    Array.Copy(operations, newOps, opCount);
                    operations = newOps;
                }
                operations[opCount++] = op;
                return false;
            }

            for (int i = uiState.Count - 1; i >= 0; i--)
            {
                TKey key = GetKeyForItem(uiState[i]);
                if (!Contains(key))
                {
                    if (RecordOperation(new DiffOperation(NotifyCollectionChangedAction.Remove, uiState[i], default, i)))
                    {
                        NotifyReset();
                        return;
                    }
                    uiState.RemoveAt(i);
                }
            }

            for (int i = 0; i < newList.Count; i++)
            {
                var newItem = newList[i];
                var newKey = GetKeyForItem(newItem);

                if (i < uiState.Count && Comparer.Equals(GetKeyForItem(uiState[i]), newKey))
                {
                    if (!EqualityComparer<TItem>.Default.Equals(uiState[i], newItem))
                    {
                        if (RecordOperation(new DiffOperation(NotifyCollectionChangedAction.Replace, uiState[i], newItem, i)))
                        {
                            NotifyReset();
                            return;
                        }
                        uiState[i] = newItem;
                    }
                    continue;
                }

                int oldIndex = -1;
                for (int j = i + 1; j < uiState.Count; j++)
                {
                    if (Comparer.Equals(GetKeyForItem(uiState[j]), newKey))
                    {
                        oldIndex = j;
                        break;
                    }
                }

                if (oldIndex != -1)
                {
                    if (RecordOperation(new DiffOperation(NotifyCollectionChangedAction.Move, default, newItem, i, oldIndex)))
                    {
                        NotifyReset();
                        return;
                    }
                    uiState.RemoveAt(oldIndex);
                    uiState.Insert(i, newItem);
                }
                else
                {
                    if (RecordOperation(new DiffOperation(NotifyCollectionChangedAction.Add, default, newItem, i)))
                    {
                        NotifyReset();
                        return;
                    }
                    uiState.Insert(i, newItem);
                }
            }

            for (int i = 0; i < opCount; i++)
            {
                ref var op = ref operations[i];

                switch (op.Action)
                {
                    case NotifyCollectionChangedAction.Remove:
                        NotifyRemove(op.OldItem!, op.Index);
                        break;
                    case NotifyCollectionChangedAction.Add:
                        NotifyAdd(op.NewItem!, op.Index);
                        break;
                    case NotifyCollectionChangedAction.Replace:
                        NotifyReplace(op.NewItem!, op.OldItem!, op.Index);
                        break;
                    case NotifyCollectionChangedAction.Move:
                        OnPropertyChanged(EventArgsCache.IndexerPropertyChanged);
                        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Move, op.NewItem!, op.Index, op.OldIndex));
                        break;
                }
            }
        }
        finally
        {
            ArrayPool<DiffOperation>.Shared.Return(operations, true);
        }
    }

    protected virtual void OnPropertyChanged(PropertyChangedEventArgs e) => PropertyChanged?.Invoke(this, e);
    protected virtual void OnCollectionChanged(NotifyCollectionChangedEventArgs e) => CollectionChanged?.Invoke(this, e);
    private void NotifyAdd(TItem item, int index)
    {
        OnPropertyChanged(EventArgsCache.CountPropertyChanged);
        OnPropertyChanged(EventArgsCache.IndexerPropertyChanged);
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, item, index));
    }
    private void NotifyRemove(TItem item, int index)
    {
        OnPropertyChanged(EventArgsCache.CountPropertyChanged);
        OnPropertyChanged(EventArgsCache.IndexerPropertyChanged);
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, item, index));
    }
    private void NotifyReplace(TItem newItem, TItem oldItem, int index)
    {
        OnPropertyChanged(EventArgsCache.IndexerPropertyChanged);
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Replace, newItem, oldItem, index));
    }
    private void NotifyReset()
    {
        OnPropertyChanged(EventArgsCache.CountPropertyChanged);
        OnPropertyChanged(EventArgsCache.IndexerPropertyChanged);
        OnCollectionChanged(EventArgsCache.ResetCollectionChanged);
    }

    public enum BatchUpdateMode
    {
        Default,
        ForceReset,
        ForceGranular
    }

    public readonly struct NotificationDeferral(KeyedObservableCollection<TKey, TItem> collection, BatchUpdateMode mode) : IDisposable
    {
        public  IReadOnlyList<TItem> Snapshot => collection._snapshot ?? [];
        public  void Dispose() => collection.ResumeNotifications(mode);
    }

    private readonly struct DiffOperation(NotifyCollectionChangedAction action, TItem? oldItem, TItem? newItem, int index, int oldIndex = -1)
    {
        public readonly NotifyCollectionChangedAction Action = action;
        public readonly TItem? OldItem = oldItem;
        public readonly TItem? NewItem = newItem;
        public readonly int Index = index;
        public readonly int OldIndex = oldIndex;
    }

    private static class EventArgsCache
    {
        internal static readonly PropertyChangedEventArgs CountPropertyChanged = new("Count");
        internal static readonly PropertyChangedEventArgs IndexerPropertyChanged = new("Item[]");
        internal static readonly NotifyCollectionChangedEventArgs ResetCollectionChanged = new(NotifyCollectionChangedAction.Reset);
    }
}
