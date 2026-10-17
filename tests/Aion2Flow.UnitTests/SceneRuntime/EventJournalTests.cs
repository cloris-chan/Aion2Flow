using System.Runtime.CompilerServices;
using Cloris.Aion2Flow.SceneRuntime.Stores;

namespace Cloris.Aion2Flow.Tests.SceneRuntime;

public sealed class EventJournalTests
{
    [Fact]
    public void Freeze_AllEventDomainsShareRecordsAndKeepFixedBounds()
    {
        VerifyFreeze(static revision => new CombatEventRecord { Revision = revision });
        VerifyFreeze(static revision => new CombatMechanicEventRecord { Revision = revision });
        VerifyFreeze(static revision => new CombatResourceEventRecord { Revision = revision });
    }

    private static void VerifyFreeze<T>(Func<int, T> create) where T : struct
    {
        var count = EventJournal<T>.SegmentCapacity + 1;
        var journal = new EventJournal<T>(count);
        for (var i = 0; i < count; i++)
            journal.Append(create(i + 1));
        _ = journal.Freeze();

        var before = GC.GetAllocatedBytesForCurrentThread();
        var frozen = journal.Freeze();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.InRange(allocated, 0, 64 + 2 * IntPtr.Size);

        ref readonly var live = ref journal.GetEvent(count - 1);
        ref readonly var archived = ref frozen[count - 1];
        Assert.True(Unsafe.AreSame(ref Unsafe.AsRef(in live), ref Unsafe.AsRef(in archived)));

        var range = journal.AsRange()[1..];
        journal.Append(create(count + 1));
        Assert.Equal(count - 1, range.Length);
        Assert.Equal(create(count), range[^1]);
        Assert.Equal(count, frozen.Count);
        Assert.Equal(create(1), frozen[0]);
        Assert.Equal(create(count), frozen[count - 1]);
        Assert.Equal(Enumerable.Range(1, count).Select(create), (IEnumerable<T>)frozen);

        journal = new EventJournal<T>();
        journal.Append(create(-1));
        Assert.Equal(create(count), frozen[count - 1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => frozen.GetEvent(count));
    }
}
