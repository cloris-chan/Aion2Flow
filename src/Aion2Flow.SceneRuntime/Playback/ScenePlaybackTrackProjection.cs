using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.SceneRuntime.Playback;

internal readonly record struct ScenePlaybackAuraInstanceKey(int EntityId, int InstanceSequenceId);

internal sealed class ScenePlaybackLifecycleTrackState
{
    private readonly HashSet<ScenePlaybackAuraInstanceKey> _instances = [];

    public bool Apply(in ObservedEventEnvelope entry)
    {
        if (entry.Aura is { } aura)
        {
            var key = new ScenePlaybackAuraInstanceKey(aura.EntityId, aura.InstanceSequenceId);
            if (aura.Kind == AuraObservationKind.Open)
                _instances.Add(key);
            else
                _instances.Remove(key);
            return false;
        }

        if (entry.Action is not { } action)
            return false;

        if (!IsRenewalShape(in action))
            return false;

        return _instances.Contains(new ScenePlaybackAuraInstanceKey(action.SourceEntityId, action.InstanceSequenceId));
    }

    public static bool IsRenewalShape(in ActionObservation action)
        => action.Phase == 19 && action.StateValue == 0 && action.DetailValue == 0;
}

internal static class ScenePlaybackTrackProjection
{
    public static ScenePlaybackTrackMarker CreateMarker(in ObservedEventEnvelope entry, long offset, long position, bool isAuraRenewal)
    {
        var track = ResolveTrack(entry.Domain, isAuraRenewal);
        var skillCode = 0;
        var amount = 0L;
        long? currentValue = null;
        long? maximumValue = null;
        var resourceKind = 0;
        var resultCode = 0;
        var lifecycleEventKind = ScenePlaybackLifecycleEventKind.None;
        var instanceSequenceId = 0;
        var durationMilliseconds = 0;
        var displayResourceEffectRefRaw = 0u;
        var sourceEntityId = entry.SourceEntityId;
        var targetEntityId = entry.TargetEntityId;
        if (entry.Combat is { } combat)
        {
            skillCode = combat.SkillCode;
            amount = combat.Damage;
        }
        else if (entry.Resource is { } resource)
        {
            currentValue = resource.CurrentValue;
            maximumValue = resource.MaximumValue;
            resourceKind = resource.ResourceKind;
            amount = resource.Delta ?? 0;
        }
        else if (entry.Aura is { } aura)
        {
            sourceEntityId = aura.Kind == AuraObservationKind.Open ? aura.EchoSourceEntityId : 0;
            targetEntityId = aura.EntityId;
            resultCode = aura.ResultCode;
            lifecycleEventKind = aura.Kind == AuraObservationKind.Open ? ScenePlaybackLifecycleEventKind.Open : ScenePlaybackLifecycleEventKind.Result;
            instanceSequenceId = aura.InstanceSequenceId;
            durationMilliseconds = aura.Kind == AuraObservationKind.Open ? aura.HeadValue : 0;
            displayResourceEffectRefRaw = aura.BuffResourceEffectRef.RawId;
        }
        else if (isAuraRenewal && entry.Action is { } action)
        {
            sourceEntityId = action.SourceEntityIdCopy;
            targetEntityId = action.SourceEntityId;
            lifecycleEventKind = ScenePlaybackLifecycleEventKind.Renew;
            instanceSequenceId = action.InstanceSequenceId;
            displayResourceEffectRefRaw = action.ActionResourceEffectRef.RawId;
        }

        return new ScenePlaybackTrackMarker(track, position, offset, entry.Stamp.ObservationOrdinal, sourceEntityId, targetEntityId, skillCode, amount, currentValue, maximumValue, resourceKind, resultCode, lifecycleEventKind, instanceSequenceId, durationMilliseconds, displayResourceEffectRefRaw);
    }

    private static ScenePlaybackTrack ResolveTrack(ObservedEventDomain domain, bool isAuraRenewal) => domain switch
    {
        ObservedEventDomain.Combat => ScenePlaybackTrack.Combat,
        ObservedEventDomain.Resource => ScenePlaybackTrack.Resource,
        ObservedEventDomain.Aura => ScenePlaybackTrack.Aura,
        ObservedEventDomain.Scene => ScenePlaybackTrack.Scene,
        ObservedEventDomain.State => ScenePlaybackTrack.State,
        ObservedEventDomain.Diagnostic => ScenePlaybackTrack.Diagnostic,
        ObservedEventDomain.Action when isAuraRenewal => ScenePlaybackTrack.Aura,
        ObservedEventDomain.Action => ScenePlaybackTrack.Action,
        _ => ScenePlaybackTrack.Other
    };
}
