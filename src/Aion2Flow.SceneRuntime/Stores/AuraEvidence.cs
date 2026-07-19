using Cloris.Aion2Flow.Protocol.Combat;
using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.SceneRuntime.Stores;

public enum AuraPacketRule : byte
{
    None = 0,
    TrackableOpen = 1,
    Renewal = 2,
    Result = 3,
    ReplacementRemoval = 4
}

public readonly record struct AuraPacketEvidence(
    AuraPacketRule Rule,
    AuraLifecycleEventKind Lifecycle,
    AuraDisposition Disposition)
{
    public bool HasLifecycleEvidence => Rule != AuraPacketRule.None;
    public bool HasDispositionEvidence => Disposition != AuraDisposition.Unknown;
}

public readonly record struct AuraLifecycleObservationContext(
    AuraLifecycleSourceKind SourceKind,
    int SourceId,
    int TargetId,
    AuraObservation Aura,
    ActionObservation Action,
    AuraLifecycleTransition ProductionTransition,
    long ObservedAtMilliseconds,
    long SourceObservationOrdinal,
    long FlushId,
    RawPacketReference Raw)
{
    public ResourceEffectRef ObservedResourceEffectRef => SourceKind switch
    {
        AuraLifecycleSourceKind.Aura => Aura.BuffResourceEffectRef,
        AuraLifecycleSourceKind.Action => Action.ActionResourceEffectRef,
        _ => default
    };

    public ResourceEffectRef EffectiveResourceEffectRef =>
        ProductionTransition.HasState
            ? ProductionTransition.State.ResourceEffectRef
            : ProductionTransition.HasPreviousState
                ? ProductionTransition.PreviousState.ResourceEffectRef
                : ObservedResourceEffectRef;

    public AuraSemanticValue ProductionSemantics =>
        ProductionTransition.HasState
            ? ProductionTransition.State.Semantics
            : ProductionTransition.HasPreviousState
                ? ProductionTransition.PreviousState.Semantics
                : default;
}

public enum AuraLifecycleSourceKind : byte
{
    Aura = 1,
    Action = 2
}

public interface IAuraLifecycleObserver
{
    void Observe(in AuraLifecycleObservationContext context);
}

public interface ISceneEventObserver : ICombatOccurrenceObserver, IAuraLifecycleObserver
{
}

public static class AuraPacketEvidenceResolver
{
    public static AuraPacketEvidence Evaluate(in AuraLifecycleObservationContext context)
    {
        var transition = context.ProductionTransition;
        var rule = transition.Kind switch
        {
            AuraLifecycleEventKind.Open => AuraPacketRule.TrackableOpen,
            AuraLifecycleEventKind.Renew => AuraPacketRule.Renewal,
            AuraLifecycleEventKind.Result => AuraPacketRule.Result,
            _ when transition.RemovedByReplacement => AuraPacketRule.ReplacementRemoval,
            _ => AuraPacketRule.None
        };

        // Current packet shapes prove lifecycle, but do not carry trustworthy buff/debuff polarity.
        return new AuraPacketEvidence(rule, transition.Kind, AuraDisposition.Unknown);
    }
}
