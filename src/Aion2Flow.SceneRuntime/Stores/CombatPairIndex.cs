using System.Runtime.InteropServices;

namespace Cloris.Aion2Flow.SceneRuntime.Stores;

internal sealed class CombatPairIndex(int capacity = 0)
{
    private readonly Dictionary<int, HashSet<(int Source, int Target)>> _outgoing = capacity > 0 ? new(capacity) : [];
    private readonly Dictionary<int, HashSet<(int Source, int Target)>> _incoming = capacity > 0 ? new(capacity) : [];

    public HashSet<(int Source, int Target)>? GetOutgoing(int sourceId) => _outgoing.GetValueOrDefault(sourceId);

    public HashSet<(int Source, int Target)>? GetIncoming(int targetId) => _incoming.GetValueOrDefault(targetId);

    public void Add((int Source, int Target) pair)
    {
        Add(_outgoing, pair.Source, pair);
        Add(_incoming, pair.Target, pair);
    }

    public void EnsureCapacity(int capacity)
    {
        _outgoing.EnsureCapacity(capacity);
        _incoming.EnsureCapacity(capacity);
    }

    public void Clear()
    {
        _outgoing.Clear();
        _incoming.Clear();
    }

    private static void Add(Dictionary<int, HashSet<(int Source, int Target)>> index, int combatantId, (int Source, int Target) pair)
    {
        if (combatantId <= 0)
            return;

        ref var pairs = ref CollectionsMarshal.GetValueRefOrAddDefault(index, combatantId, out var exists);
        if (!exists)
            pairs = [];
        pairs!.Add(pair);
    }
}
