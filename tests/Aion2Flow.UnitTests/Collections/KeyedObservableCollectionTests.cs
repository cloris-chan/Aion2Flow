using System.Collections.Specialized;
using Cloris.Aion2Flow.Collections;

namespace Cloris.Aion2Flow.Tests.Collections;

public sealed class KeyedObservableCollectionTests
{
    [Theory]
    [InlineData(50, false, false)]
    [InlineData(1, false, true)]
    [InlineData(1, true, false)]
    public void DeferredChangesKeepObserverOrderWhenReplacingMovingAndInserting(int resetThreshold, bool forceGranular, bool expectReset)
    {
        Item[] initial = [new(1, "one"), new(2, "two"), new(3, "three"), new(4, "four")];
        var collection = new KeyedObservableCollection<int, Item>(static item => item.Id, initial) { ResetThreshold = resetThreshold };
        List<Item> observed = [.. initial];
        List<NotifyCollectionChangedAction> actions = [];
        collection.CollectionChanged += (_, change) =>
        {
            actions.Add(change.Action);
            switch (change.Action)
            {
                case NotifyCollectionChangedAction.Remove:
                    Assert.Same(change.OldItems?[0], observed[change.OldStartingIndex]);
                    observed.RemoveAt(change.OldStartingIndex);
                    break;
                case NotifyCollectionChangedAction.Replace:
                    Assert.Same(change.OldItems?[0], observed[change.OldStartingIndex]);
                    observed[change.NewStartingIndex] = Assert.IsType<Item>(change.NewItems?[0]);
                    break;
                case NotifyCollectionChangedAction.Move:
                    var item = observed[change.OldStartingIndex];
                    Assert.Same(change.NewItems?[0], item);
                    observed.RemoveAt(change.OldStartingIndex);
                    observed.Insert(change.NewStartingIndex, item);
                    break;
                case NotifyCollectionChangedAction.Add:
                    observed.Insert(change.NewStartingIndex, Assert.IsType<Item>(change.NewItems?[0]));
                    break;
                case NotifyCollectionChangedAction.Reset:
                    observed = [.. collection];
                    break;
            }
        };

        Item[] updated = [new(4, "four updated"), new(5, "five"), new(2, "two updated")];
        var mode = forceGranular
            ? KeyedObservableCollection<int, Item>.BatchUpdateMode.ForceGranular
            : KeyedObservableCollection<int, Item>.BatchUpdateMode.Default;
        using (collection.SuspendNotifications(mode))
        {
            collection.Clear();
            foreach (var item in updated)
                collection.Add(item);
        }

        Assert.Equal(updated, observed);
        for (var i = 0; i < updated.Length; i++)
        {
            Assert.Same(updated[i], observed[i]);
            Assert.True(collection.TryGetValue(updated[i].Id, out var indexed));
            Assert.Same(updated[i], indexed);
        }

        Assert.Equal(expectReset
            ? [NotifyCollectionChangedAction.Reset]
            : new[]
            {
                NotifyCollectionChangedAction.Remove, NotifyCollectionChangedAction.Remove,
                NotifyCollectionChangedAction.Replace, NotifyCollectionChangedAction.Move,
                NotifyCollectionChangedAction.Add, NotifyCollectionChangedAction.Replace
            }, actions);
    }

    private sealed record Item(int Id, string Name);
}
