using Cloris.Aion2Flow.Protocol.Combat;
using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Journal;
using Cloris.Aion2Flow.SceneRuntime.Observation;
using Cloris.Aion2Flow.SceneRuntime.Stores;

namespace Cloris.Aion2Flow.SceneRuntime.Playback;

internal static class ScenePlaybackTrackProjection
{
    public static ScenePlaybackTrackMarker CreateMetricMarker(in CombatEventRecord record, int sourceEntityId)
    {
        var contribution = record.Contribution;
        return CreateMaterializedMarker(
            ScenePlaybackTrack.Combat,
            record.ObservedAtMilliseconds,
            record.SourceObservationOrdinal,
            sourceEntityId,
            record.TargetId,
            record.EventKey,
            ResolveFlags(in contribution),
            contribution.Amount);
    }

    public static ScenePlaybackTrackMarker CreateMechanicMarker(in CombatMechanicEventRecord record, int sourceEntityId)
        => CreateMaterializedMarker(
            ScenePlaybackTrack.Mechanic,
            record.ObservedAtMilliseconds,
            record.SourceObservationOrdinal,
            sourceEntityId,
            record.TargetId,
            record.EventKey,
            ScenePlaybackCombatEventFlags.Damage,
            0);

    public static ScenePlaybackTrackMarker CreateResourceMarker(in CombatResourceEventRecord record, int sourceEntityId)
        => CreateMaterializedMarker(
            ScenePlaybackTrack.Resource,
            record.ObservedAtMilliseconds,
            record.SourceObservationOrdinal,
            sourceEntityId,
            record.TargetId,
            record.EventKey,
            ScenePlaybackCombatEventFlags.None,
            record.Resource.Amount);

    public static ScenePlaybackTrackMarker CreateObservationMarker(ObservedEventEntry entry, long offset, long position, in AuraLifecycleTransition auraLifecycle)
    {
        if (entry.Domain == ObservedEventDomain.Combat)
            throw new ArgumentException("Raw combat observations must be materialized before playback projection.", nameof(entry));

        var lifecycleEventKind = auraLifecycle.Kind;
        var track = ResolveTrack(entry.Domain, lifecycleEventKind);
        var eventKey = default(CombatEventKey);
        var combatEventFlags = ScenePlaybackCombatEventFlags.None;
        var amount = 0L;
        long? currentHp = null;
        long? maxHp = null;
        var resultCode = 0;
        var instanceSequenceId = 0;
        var durationMilliseconds = 0;
        var displayResourceEffectRef = default(ResourceEffectRef);
        var auraSemantics = default(AuraSemanticValue);
        var sourceEntityId = entry.SourceEntityId;
        var targetEntityId = entry.TargetEntityId;
        switch (entry.Domain)
        {
            case ObservedEventDomain.EntityVital:
            {
                ref readonly var vital = ref entry.EntityVital;
                currentHp = vital.CurrentHp;
                maxHp = vital.MaxHp;
                break;
            }
            case ObservedEventDomain.Aura:
            {
                ref readonly var aura = ref entry.Aura;
                sourceEntityId = auraLifecycle.HasState ? auraLifecycle.State.OriginEntityId : 0;
                targetEntityId = aura.EntityId;
                if (auraLifecycle.HasState)
                {
                    resultCode = aura.ResultCode;
                    instanceSequenceId = auraLifecycle.State.InstanceSequenceId;
                    durationMilliseconds = auraLifecycle.State.DurationMilliseconds;
                    displayResourceEffectRef = auraLifecycle.State.ResourceEffectRef;
                    auraSemantics = auraLifecycle.State.Semantics;
                }
                break;
            }
            case ObservedEventDomain.Action when lifecycleEventKind == AuraLifecycleEventKind.Renew:
            {
                sourceEntityId = auraLifecycle.State.OriginEntityId;
                targetEntityId = auraLifecycle.State.TargetEntityId;
                instanceSequenceId = auraLifecycle.State.InstanceSequenceId;
                durationMilliseconds = auraLifecycle.State.DurationMilliseconds;
                displayResourceEffectRef = auraLifecycle.State.ResourceEffectRef;
                auraSemantics = auraLifecycle.State.Semantics;
                break;
            }
        }

        return new ScenePlaybackTrackMarker(
            track,
            position,
            offset,
            entry.Stamp.ObservationOrdinal,
            sourceEntityId,
            targetEntityId,
            eventKey,
            combatEventFlags,
            amount,
            currentHp,
            maxHp,
            resultCode,
            lifecycleEventKind,
            instanceSequenceId,
            durationMilliseconds,
            displayResourceEffectRef,
            auraSemantics);
    }

    private static ScenePlaybackTrackMarker CreateMaterializedMarker(
        ScenePlaybackTrack track,
        long observedAtMilliseconds,
        long sourceObservationOrdinal,
        int sourceEntityId,
        int targetEntityId,
        CombatEventKey eventKey,
        ScenePlaybackCombatEventFlags flags,
        long amount)
    {
        var positionMilliseconds = Math.Max(0, observedAtMilliseconds);
        return new ScenePlaybackTrackMarker(
            track,
            positionMilliseconds,
            positionMilliseconds,
            sourceObservationOrdinal,
            sourceEntityId,
            targetEntityId,
            eventKey,
            flags,
            amount,
            null,
            null,
            0,
            AuraLifecycleEventKind.None,
            0,
            0,
            default,
            default);
    }

    private static ScenePlaybackCombatEventFlags ResolveFlags(in CombatContribution contribution)
    {
        return contribution.Metric switch
        {
            CombatMetricKind.Damage => ScenePlaybackCombatEventFlags.Damage,
            CombatMetricKind.Healing => ScenePlaybackCombatEventFlags.Healing,
            CombatMetricKind.ShieldGranted or CombatMetricKind.ShieldAbsorbed => ScenePlaybackCombatEventFlags.Shield,
            _ => ScenePlaybackCombatEventFlags.None
        };
    }

    private static ScenePlaybackTrack ResolveTrack(ObservedEventDomain domain, AuraLifecycleEventKind lifecycleEventKind) => domain switch
    {
        ObservedEventDomain.EntityVital => ScenePlaybackTrack.EntityVital,
        ObservedEventDomain.Aura when lifecycleEventKind != AuraLifecycleEventKind.None => ScenePlaybackTrack.Aura,
        ObservedEventDomain.Aura => ScenePlaybackTrack.Action,
        ObservedEventDomain.Scene => ScenePlaybackTrack.Scene,
        ObservedEventDomain.State => ScenePlaybackTrack.State,
        ObservedEventDomain.Diagnostic => ScenePlaybackTrack.Diagnostic,
        ObservedEventDomain.Action when lifecycleEventKind == AuraLifecycleEventKind.Renew => ScenePlaybackTrack.Aura,
        ObservedEventDomain.Action => ScenePlaybackTrack.Action,
        _ => ScenePlaybackTrack.Other
    };
}
