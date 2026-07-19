using Cloris.Aion2Flow.Protocol.Combat;
using Cloris.Aion2Flow.SceneRuntime.Journal;
using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.SceneRuntime.Stores;

public enum AuraLifecycleEventKind : byte
{
    None,
    Open,
    Renew,
    Result
}

public readonly record struct AuraInstanceKey(int TargetEntityId, int InstanceSequenceId);

public readonly record struct AuraInstanceState(
    int TargetEntityId,
    int OriginEntityId,
    int InstanceSequenceId,
    int StackCount,
    int OpenMode,
    int GroupCode,
    ushort DurationMilliseconds,
    ResourceEffectRef ResourceEffectRef,
    AuraSemanticValue Semantics,
    long OpenedAtMilliseconds,
    long RenewedAtMilliseconds,
    long? ExpiresAtMilliseconds,
    long OpenObservationOrdinal,
    long LastObservationOrdinal)
{
    public AuraInstanceKey Key => new(TargetEntityId, InstanceSequenceId);

    public bool HasIdentity => TargetEntityId > 0 && InstanceSequenceId > 0;

    public bool IsActiveAt(long observedAtMilliseconds) =>
        ExpiresAtMilliseconds is not long expiresAt || expiresAt > observedAtMilliseconds;
}

public readonly record struct AuraLifecycleTransition(
    AuraLifecycleEventKind Kind,
    AuraInstanceState PreviousState,
    AuraInstanceState State,
    bool RemovedByReplacement)
{
    public bool HasPreviousState => PreviousState.HasIdentity;

    public bool HasState => State.HasIdentity;
}

public sealed class AuraStore
{
    private readonly Dictionary<AuraInstanceKey, AuraInstanceState> _instances;

    public AuraStore(int capacity = 0)
        => _instances = capacity > 0 ? new Dictionary<AuraInstanceKey, AuraInstanceState>(capacity) : [];

    private AuraStore(AuraStoreSnapshot snapshot)
    {
        _instances = new Dictionary<AuraInstanceKey, AuraInstanceState>(snapshot.Instances.Length);
        for (var i = 0; i < snapshot.Instances.Length; i++)
        {
            var state = snapshot.Instances[i];
            if (state.HasIdentity)
                _instances[state.Key] = state;
        }

        Revision = snapshot.Revision;
    }

    public int Count => _instances.Count;

    public long Revision { get; private set; }

    public AuraLifecycleTransition Apply(ObservedEventEntry entry)
    {
        var observedAtMilliseconds = Math.Max(0, entry.ObservedAtMilliseconds);
        return entry.Domain switch
        {
            ObservedEventDomain.Aura => Apply(in entry.Aura, observedAtMilliseconds, entry.Stamp.ObservationOrdinal),
            ObservedEventDomain.Action => Apply(in entry.Action, observedAtMilliseconds, entry.Stamp.ObservationOrdinal),
            _ => default
        };
    }

    public AuraLifecycleTransition Apply(in AuraObservation observation, long observedAtMilliseconds, long observationOrdinal)
    {
        if (observation.EntityId <= 0 || observation.InstanceSequenceId <= 0)
            return default;

        var key = new AuraInstanceKey(observation.EntityId, observation.InstanceSequenceId);
        if (observation.Kind == AuraObservationKind.Open)
            return ApplyOpen(key, in observation, observedAtMilliseconds, observationOrdinal);

        if (observation.Kind != AuraObservationKind.Result || !_instances.Remove(key, out var previous))
            return default;

        var resourceEffectRef = observation.BuffResourceEffectRef.IsEmpty
            ? previous.ResourceEffectRef
            : observation.BuffResourceEffectRef;
        var state = previous with
        {
            ResourceEffectRef = resourceEffectRef,
            Semantics = ResolveSemantics(resourceEffectRef, in previous),
            LastObservationOrdinal = observationOrdinal
        };
        Revision++;
        return new AuraLifecycleTransition(AuraLifecycleEventKind.Result, previous, state, RemovedByReplacement: false);
    }

    public AuraLifecycleTransition Apply(in ActionObservation observation, long observedAtMilliseconds, long observationOrdinal)
    {
        if (!IsRenewal(in observation) || observation.SourceEntityId <= 0 || observation.InstanceSequenceId <= 0)
            return default;

        var key = new AuraInstanceKey(observation.SourceEntityId, observation.InstanceSequenceId);
        if (!_instances.TryGetValue(key, out var previous))
            return default;

        var state = previous with
        {
            OriginEntityId = observation.SourceEntityIdCopy > 0 ? observation.SourceEntityIdCopy : previous.OriginEntityId,
            ResourceEffectRef = observation.ActionResourceEffectRef.IsEmpty ? previous.ResourceEffectRef : observation.ActionResourceEffectRef,
            Semantics = observation.ActionResourceEffectRef.IsEmpty
                ? previous.Semantics
                : ResolveSemantics(observation.ActionResourceEffectRef, in previous),
            RenewedAtMilliseconds = observedAtMilliseconds,
            ExpiresAtMilliseconds = ResolveExpiration(observedAtMilliseconds, previous.DurationMilliseconds),
            LastObservationOrdinal = observationOrdinal
        };
        _instances[key] = state;
        Revision++;
        return new AuraLifecycleTransition(AuraLifecycleEventKind.Renew, previous, state, RemovedByReplacement: false);
    }

    public bool TryGet(AuraInstanceKey key, out AuraInstanceState state) => _instances.TryGetValue(key, out state);

    public AuraInstanceState[] CreateActiveSnapshot(long observedAtMilliseconds)
    {
        if (_instances.Count == 0)
            return [];

        var count = 0;
        foreach (var state in _instances.Values)
        {
            if (state.IsActiveAt(observedAtMilliseconds))
                count++;
        }

        if (count == 0)
            return [];

        var result = new AuraInstanceState[count];
        var index = 0;
        foreach (var state in _instances.Values)
        {
            if (state.IsActiveAt(observedAtMilliseconds))
                result[index++] = state;
        }

        Sort(result);
        return result;
    }

    internal AuraStoreSnapshot CreateSnapshot()
    {
        var instances = _instances.Count == 0 ? [] : _instances.Values.ToArray();
        Sort(instances);
        return new AuraStoreSnapshot(instances, Revision);
    }

    internal static AuraStore FromSnapshot(AuraStoreSnapshot snapshot) => new(snapshot);

    internal static bool IsTrackableOpen(in AuraObservation observation) =>
        observation.Kind == AuraObservationKind.Open &&
        observation.EntityId > 0 &&
        observation.InstanceSequenceId > 0 &&
        observation.OpenMode == 1 &&
        observation.GroupCode == 19;

    internal static bool IsRenewal(in ActionObservation observation) =>
        observation.Phase == 19 && observation.StateValue == 0 && observation.DetailValue == 0;

    private AuraLifecycleTransition ApplyOpen(
        AuraInstanceKey key,
        in AuraObservation observation,
        long observedAtMilliseconds,
        long observationOrdinal)
    {
        _instances.Remove(key, out var previous);
        if (!IsTrackableOpen(in observation))
        {
            if (!previous.HasIdentity)
                return default;

            Revision++;
            return new AuraLifecycleTransition(AuraLifecycleEventKind.None, previous, default, RemovedByReplacement: true);
        }

        var state = new AuraInstanceState(
            observation.EntityId,
            observation.EchoSourceEntityId,
            observation.InstanceSequenceId,
            observation.StackCount,
            observation.OpenMode,
            observation.GroupCode,
            observation.HeadValue,
            observation.BuffResourceEffectRef,
            AuraSemanticEvidenceResolver.Evaluate(observation.BuffResourceEffectRef),
            observedAtMilliseconds,
            observedAtMilliseconds,
            ResolveExpiration(observedAtMilliseconds, observation.HeadValue),
            observationOrdinal,
            observationOrdinal);
        _instances.Add(key, state);
        Revision++;
        return new AuraLifecycleTransition(AuraLifecycleEventKind.Open, previous, state, RemovedByReplacement: false);
    }

    private static AuraSemanticValue ResolveSemantics(ResourceEffectRef resourceEffectRef, in AuraInstanceState previous) =>
        resourceEffectRef == previous.ResourceEffectRef
            ? previous.Semantics
            : AuraSemanticEvidenceResolver.Evaluate(resourceEffectRef);

    private static long? ResolveExpiration(long observedAtMilliseconds, ushort durationMilliseconds)
    {
        if (durationMilliseconds == ushort.MaxValue)
            return null;

        return observedAtMilliseconds > long.MaxValue - durationMilliseconds
            ? long.MaxValue
            : observedAtMilliseconds + durationMilliseconds;
    }

    private static void Sort(AuraInstanceState[] states) =>
        Array.Sort(states, static (left, right) =>
        {
            var comparison = left.TargetEntityId.CompareTo(right.TargetEntityId);
            return comparison != 0 ? comparison : left.InstanceSequenceId.CompareTo(right.InstanceSequenceId);
        });
}

internal sealed record AuraStoreSnapshot(AuraInstanceState[] Instances, long Revision);
