using System.Runtime.InteropServices;

namespace Cloris.Aion2Flow.SceneRuntime.Stores;

internal sealed class CombatSnapshotChangeLog(int capacity = 0)
{
    private readonly List<CombatSnapshotChange> _changes = capacity > 0 ? new(capacity) : [];

    private long Revision => _changes.Count == 0 ? 0 : _changes[^1].Revision;

    public void Add(CombatSnapshotChange change) => _changes.Add(change);

    public void EnsureCapacity(int capacity) => _changes.EnsureCapacity(capacity);

    public void Clear() => _changes.Clear();

    public SnapshotChangeBatch<CombatSnapshotChange> ReadChanges(SnapshotChangeCursor cursor, int maxChanges)
    {
        var start = ResolveStart(cursor);
        if (start == _changes.Count)
            return new SnapshotChangeBatch<CombatSnapshotChange>(cursor.Revision, Revision, [], false);

        var end = start + Math.Min(Math.Max(1, maxChanges), _changes.Count - start);
        if (end < _changes.Count && _changes[end - 1].Revision == _changes[end].Revision)
        {
            var revision = _changes[end].Revision;
            while (end > start && _changes[end - 1].Revision == revision)
                end--;
            if (end == start)
                end = UpperBound(revision);
        }

        return new SnapshotChangeBatch<CombatSnapshotChange>(
            cursor.Revision, _changes[end - 1].Revision, _changes.GetRange(start, end - start), end < _changes.Count);
    }

    public SnapshotChangeCopyResult CopyChanges(SnapshotChangeCursor cursor, Span<CombatSnapshotChange> destination)
    {
        var start = ResolveStart(cursor);
        var count = Math.Min(destination.Length, _changes.Count - start);
        if (count == 0)
            return new SnapshotChangeCopyResult(cursor, cursor.Revision, Revision, 0, start < _changes.Count);

        CollectionsMarshal.AsSpan(_changes).Slice(start, count).CopyTo(destination);
        var end = start + count;
        var revision = _changes[end - 1].Revision;
        var next = new SnapshotChangeCursor(revision, 0);
        if (end < _changes.Count && _changes[end].Revision == revision)
        {
            var groupStart = end - 1;
            while (groupStart > 0 && _changes[groupStart - 1].Revision == revision)
                groupStart--;
            var previousRevision = groupStart == 0 ? 0 : _changes[groupStart - 1].Revision;
            next = new SnapshotChangeCursor(previousRevision, end - groupStart);
        }

        return new SnapshotChangeCopyResult(next, cursor.Revision, revision, count, end < _changes.Count);
    }

    private int ResolveStart(SnapshotChangeCursor cursor)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(cursor.Offset);
        var start = UpperBound(cursor.Revision);
        if (cursor.Offset == 0)
            return start;

        if (start == _changes.Count || cursor.Offset >= UpperBound(_changes[start].Revision) - start)
            throw new ArgumentOutOfRangeException(nameof(cursor), "The offset must lie within the next revision group.");

        return start + cursor.Offset;
    }

    private int UpperBound(long revision)
    {
        var low = 0;
        var high = _changes.Count;
        while (low < high)
        {
            var middle = low + ((high - low) >> 1);
            if (_changes[middle].Revision <= revision)
                low = middle + 1;
            else
                high = middle;
        }

        return low;
    }
}
