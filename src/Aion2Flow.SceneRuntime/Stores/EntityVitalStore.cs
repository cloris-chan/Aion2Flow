using System.Runtime.InteropServices;
using Cloris.Aion2Flow.SceneRuntime.Journal;
using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.SceneRuntime.Stores;

public sealed class EntityVitalStore(int capacity = 0)
{
    private readonly Dictionary<int, EntityVitalState> _states = capacity > 0 ? new Dictionary<int, EntityVitalState>(capacity) : [];
    private long _revision;

    private EntityVitalStore(EntityVitalStoreSnapshot snapshot)
        : this(snapshot.States.Length)
    {
        for (var i = 0; i < snapshot.States.Length; i++)
        {
            var state = snapshot.States[i];
            _states.Add(state.EntityId, state);
        }

        _revision = snapshot.Revision;
    }

    public IReadOnlyDictionary<int, EntityVitalState> States => _states;

    public long Revision => _revision;

    public EntityVitalState Apply(ObservedEventEntry entry)
    {
        if (entry.Domain != ObservedEventDomain.EntityVital)
            throw new ArgumentException("The journal entry is not an entity-vital observation.", nameof(entry));

        return Apply(in entry.EntityVital, entry.ObservedAtMilliseconds, entry.Stamp.ObservationOrdinal);
    }

    public EntityVitalState Apply(
        in EntityVitalObservation observation,
        long observedAtMilliseconds,
        long observationOrdinal)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(observation.EntityId);
        ArgumentOutOfRangeException.ThrowIfNegative(observedAtMilliseconds);
        ArgumentOutOfRangeException.ThrowIfNegative(observationOrdinal);

        ref var state = ref CollectionsMarshal.GetValueRefOrAddDefault(_states, observation.EntityId, out var exists);
        var maxHp = observation.MaxHp is > 0
            ? observation.MaxHp
            : exists
                ? state.MaxHp
                : null;
        state = new EntityVitalState(
            observation.EntityId,
            Math.Max(0, observation.CurrentHp),
            maxHp,
            observedAtMilliseconds,
            observationOrdinal,
            ++_revision);
        return state;
    }

    public bool TryGet(int entityId, out EntityVitalState state) => _states.TryGetValue(entityId, out state);

    public EntityVitalState[] CreateStateSnapshot()
    {
        if (_states.Count == 0)
            return [];

        var result = new EntityVitalState[_states.Count];
        var index = 0;
        foreach (var state in _states.Values)
            result[index++] = state;
        Array.Sort(result, static (left, right) => left.EntityId.CompareTo(right.EntityId));
        return result;
    }

    public void Clear()
    {
        if (_states.Count == 0)
            return;

        _states.Clear();
        _revision++;
    }

    internal EntityVitalStoreSnapshot CreateSnapshot() => new(CreateStateSnapshot(), _revision);

    internal static EntityVitalStore FromSnapshot(EntityVitalStoreSnapshot snapshot) => new(snapshot);
}

public readonly record struct EntityVitalState(
    int EntityId,
    long CurrentHp,
    long? MaxHp,
    long ObservedAtMilliseconds,
    long ObservationOrdinal,
    long Revision);

internal sealed record EntityVitalStoreSnapshot(EntityVitalState[] States, long Revision);
