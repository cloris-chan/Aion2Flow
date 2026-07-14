using Cloris.Aion2Flow.Protocol.Combat;
using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Journal;
using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.SceneRuntime.Playback;

internal readonly record struct ScenePlaybackAuraInstanceKey(int EntityId, int InstanceSequenceId);

internal readonly record struct ScenePlaybackLifecycleProjection(
    ScenePlaybackLifecycleEventKind Kind,
    ResourceEffectRef DisplayResourceEffectRef)
{
    public static ScenePlaybackLifecycleProjection None { get; } = default;
}

internal static class ScenePlaybackAuraProtocol
{
    public static bool IsTrackableOpen(in AuraObservation aura)
        => aura.Kind == AuraObservationKind.Open &&
           aura.EntityId > 0 &&
           aura.InstanceSequenceId > 0 &&
           aura.OpenMode == 1 &&
           aura.GroupCode == 19;

    public static bool IsRenewal(in ActionObservation action)
        => action.Phase == 19 && action.StateValue == 0 && action.DetailValue == 0;
}

internal sealed class ScenePlaybackLifecycleTrackState
{
    private readonly Dictionary<ScenePlaybackAuraInstanceKey, ResourceEffectRef> _instances = [];

    public ScenePlaybackLifecycleProjection Apply(ObservedEventEntry entry)
    {
        if (entry.Domain == ObservedEventDomain.Aura)
        {
            ref readonly var aura = ref entry.Aura;
            var key = new ScenePlaybackAuraInstanceKey(aura.EntityId, aura.InstanceSequenceId);
            if (aura.Kind == AuraObservationKind.Open)
            {
                _instances.Remove(key);
                if (!ScenePlaybackAuraProtocol.IsTrackableOpen(in aura))
                    return ScenePlaybackLifecycleProjection.None;

                _instances.Add(key, aura.BuffResourceEffectRef);
                return new ScenePlaybackLifecycleProjection(ScenePlaybackLifecycleEventKind.Open, aura.BuffResourceEffectRef);
            }

            return _instances.Remove(key, out var displayResourceEffectRef)
                ? new ScenePlaybackLifecycleProjection(ScenePlaybackLifecycleEventKind.Result, displayResourceEffectRef)
                : ScenePlaybackLifecycleProjection.None;
        }

        if (entry.Domain != ObservedEventDomain.Action)
            return ScenePlaybackLifecycleProjection.None;

        ref readonly var action = ref entry.Action;
        if (!ScenePlaybackAuraProtocol.IsRenewal(in action))
            return ScenePlaybackLifecycleProjection.None;

        var renewalKey = new ScenePlaybackAuraInstanceKey(action.SourceEntityId, action.InstanceSequenceId);
        if (!_instances.TryGetValue(renewalKey, out var renewalDisplayResourceEffectRef))
            return ScenePlaybackLifecycleProjection.None;

        if (renewalDisplayResourceEffectRef.IsEmpty && !action.ActionResourceEffectRef.IsEmpty)
        {
            renewalDisplayResourceEffectRef = action.ActionResourceEffectRef;
            _instances[renewalKey] = renewalDisplayResourceEffectRef;
        }

        return new ScenePlaybackLifecycleProjection(ScenePlaybackLifecycleEventKind.Renew, renewalDisplayResourceEffectRef);
    }
}

internal static class ScenePlaybackTrackProjection
{
    public static ScenePlaybackTrackMarker CreateMarker(ObservedEventEntry entry, long offset, long position, ScenePlaybackLifecycleProjection lifecycle)
    {
        var track = ResolveTrack(entry.Domain, lifecycle.Kind);
        var eventKey = default(CombatEventKey);
        var combatEventFlags = ScenePlaybackCombatEventFlags.None;
        var amount = 0L;
        long? currentValue = null;
        long? maximumValue = null;
        var resourceKind = 0;
        var resultCode = 0;
        var instanceSequenceId = 0;
        var durationMilliseconds = 0;
        var displayResourceEffectRef = default(ResourceEffectRef);
        var sourceEntityId = entry.SourceEntityId;
        var targetEntityId = entry.TargetEntityId;
        switch (entry.Domain)
        {
            case ObservedEventDomain.Combat:
            {
                ref readonly var combat = ref entry.Combat;
                eventKey = CombatEventKey.FromObservation(in combat);
                var contribution = CombatContributionClassifier.Evaluate(in combat);
                combatEventFlags = ResolveCombatEventFlags(in contribution);
                amount = ResolvePrimaryCombatAmount(in combat, in contribution);
                break;
            }
            case ObservedEventDomain.Resource:
            {
                ref readonly var resource = ref entry.Resource;
                currentValue = resource.CurrentValue;
                maximumValue = resource.MaximumValue;
                resourceKind = resource.ResourceKind;
                amount = resource.Delta ?? 0;
                break;
            }
            case ObservedEventDomain.Aura:
            {
                ref readonly var aura = ref entry.Aura;
                sourceEntityId = aura.Kind == AuraObservationKind.Open ? aura.EchoSourceEntityId : 0;
                targetEntityId = aura.EntityId;
                if (lifecycle.Kind != ScenePlaybackLifecycleEventKind.None)
                {
                    resultCode = aura.ResultCode;
                    instanceSequenceId = aura.InstanceSequenceId;
                    durationMilliseconds = aura.Kind == AuraObservationKind.Open ? aura.HeadValue : 0;
                    displayResourceEffectRef = aura.BuffResourceEffectRef.IsEmpty
                        ? lifecycle.DisplayResourceEffectRef
                        : aura.BuffResourceEffectRef;
                }
                break;
            }
            case ObservedEventDomain.Action when lifecycle.Kind == ScenePlaybackLifecycleEventKind.Renew:
            {
                ref readonly var action = ref entry.Action;
                sourceEntityId = action.SourceEntityIdCopy;
                targetEntityId = action.SourceEntityId;
                instanceSequenceId = action.InstanceSequenceId;
                displayResourceEffectRef = action.ActionResourceEffectRef.IsEmpty
                    ? lifecycle.DisplayResourceEffectRef
                    : action.ActionResourceEffectRef;
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
            currentValue,
            maximumValue,
            resourceKind,
            resultCode,
            lifecycle.Kind,
            instanceSequenceId,
            durationMilliseconds,
            displayResourceEffectRef);
    }

    private static ScenePlaybackCombatEventFlags ResolveCombatEventFlags(in CombatContribution contribution)
    {
        var flags = ScenePlaybackCombatEventFlags.None;
        if (contribution.CountsAsDamage)
            flags |= ScenePlaybackCombatEventFlags.Damage;
        if (contribution.CountsAsHealing)
            flags |= ScenePlaybackCombatEventFlags.Healing;
        if (contribution.CountsAsShieldGrant || contribution.CountsAsShieldAbsorbed)
            flags |= ScenePlaybackCombatEventFlags.Shield;
        return flags;
    }

    private static long ResolvePrimaryCombatAmount(in CombatObservation observation, in CombatContribution contribution)
    {
        if (contribution.CountsAsDamage)
            return contribution.DamageAmount;
        if (contribution.CountsAsHealing)
            return contribution.HealingAmount;
        if (contribution.CountsAsShieldGrant)
            return contribution.ShieldGrantAmount;
        if (contribution.CountsAsShieldAbsorbed)
            return contribution.ShieldAbsorbedAmount;
        return observation.Damage;
    }

    private static ScenePlaybackTrack ResolveTrack(ObservedEventDomain domain, ScenePlaybackLifecycleEventKind lifecycleEventKind) => domain switch
    {
        ObservedEventDomain.Combat => ScenePlaybackTrack.Combat,
        ObservedEventDomain.Resource => ScenePlaybackTrack.Resource,
        ObservedEventDomain.Aura when lifecycleEventKind != ScenePlaybackLifecycleEventKind.None => ScenePlaybackTrack.Aura,
        ObservedEventDomain.Aura => ScenePlaybackTrack.Action,
        ObservedEventDomain.Scene => ScenePlaybackTrack.Scene,
        ObservedEventDomain.State => ScenePlaybackTrack.State,
        ObservedEventDomain.Diagnostic => ScenePlaybackTrack.Diagnostic,
        ObservedEventDomain.Action when lifecycleEventKind == ScenePlaybackLifecycleEventKind.Renew => ScenePlaybackTrack.Aura,
        ObservedEventDomain.Action => ScenePlaybackTrack.Action,
        _ => ScenePlaybackTrack.Other
    };
}
