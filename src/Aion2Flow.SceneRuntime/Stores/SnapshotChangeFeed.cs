namespace Cloris.Aion2Flow.SceneRuntime.Stores;

public readonly record struct SnapshotChangeCursor(long Revision, int Offset);

public readonly record struct SnapshotChangeBatch<TChange>(long FromRevision, long ToRevision, IReadOnlyList<TChange> Changes, bool HasMore);

public interface ISnapshotChangeFeed<TChange>
{
    SnapshotChangeCursor CreateCursor(long afterRevision);
    SnapshotChangeBatch<TChange> ReadChanges(SnapshotChangeCursor cursor, int maxChanges);
}

public readonly record struct SnapshotChangeCopyResult(SnapshotChangeCursor Cursor, long FromRevision, long ToRevision, int Count, bool HasMore);

public enum CombatSnapshotChangeKind : byte { PairUpdated, CombatantUpdated }

public readonly record struct CombatSnapshotChange(CombatSnapshotChangeKind Kind, int CombatantId, (int Source, int Target) PairKey, long Revision);
