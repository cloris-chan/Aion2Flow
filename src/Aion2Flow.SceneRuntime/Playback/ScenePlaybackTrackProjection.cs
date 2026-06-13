using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.SceneRuntime.Playback;

internal readonly record struct ScenePlaybackAuraInstanceKey(int EntityId, int InstanceSequenceId);

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
    private readonly HashSet<ScenePlaybackAuraInstanceKey> _instances = [];

    public ScenePlaybackLifecycleEventKind Apply(in ObservedEventEnvelope entry)
    {
        if (entry.Aura is { } aura)
        {
            var key = new ScenePlaybackAuraInstanceKey(aura.EntityId, aura.InstanceSequenceId);
            if (aura.Kind == AuraObservationKind.Open)
            {
                _instances.Remove(key);
                if (!ScenePlaybackAuraProtocol.IsTrackableOpen(in aura))
                    return ScenePlaybackLifecycleEventKind.None;

                _instances.Add(key);
                return ScenePlaybackLifecycleEventKind.Open;
            }

            return _instances.Remove(key)
                ? ScenePlaybackLifecycleEventKind.Result
                : ScenePlaybackLifecycleEventKind.None;
        }

        if (entry.Action is not { } action)
            return ScenePlaybackLifecycleEventKind.None;

        if (!ScenePlaybackAuraProtocol.IsRenewal(in action))
            return ScenePlaybackLifecycleEventKind.None;

        return _instances.Contains(new ScenePlaybackAuraInstanceKey(action.SourceEntityId, action.InstanceSequenceId))
            ? ScenePlaybackLifecycleEventKind.Renew
            : ScenePlaybackLifecycleEventKind.None;
    }
}

internal static class ScenePlaybackTrackProjection
{
    public static ScenePlaybackTrackMarker CreateMarker(in ObservedEventEnvelope entry, long offset, long position, ScenePlaybackLifecycleEventKind lifecycleEventKind)
    {
        var track = ResolveTrack(entry.Domain, lifecycleEventKind);
        var skillCode = 0;
        var amount = 0L;
        long? currentValue = null;
        long? maximumValue = null;
        var resourceKind = 0;
        var resultCode = 0;
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
            if (lifecycleEventKind != ScenePlaybackLifecycleEventKind.None)
            {
                resultCode = aura.ResultCode;
                instanceSequenceId = aura.InstanceSequenceId;
                durationMilliseconds = aura.Kind == AuraObservationKind.Open ? aura.HeadValue : 0;
                displayResourceEffectRefRaw = aura.BuffResourceEffectRef.RawId;
            }
        }
        else if (lifecycleEventKind == ScenePlaybackLifecycleEventKind.Renew && entry.Action is { } action)
        {
            sourceEntityId = action.SourceEntityIdCopy;
            targetEntityId = action.SourceEntityId;
            instanceSequenceId = action.InstanceSequenceId;
            displayResourceEffectRefRaw = action.ActionResourceEffectRef.RawId;
        }

        return new ScenePlaybackTrackMarker(track, position, offset, entry.Stamp.ObservationOrdinal, sourceEntityId, targetEntityId, skillCode, amount, currentValue, maximumValue, resourceKind, resultCode, lifecycleEventKind, instanceSequenceId, durationMilliseconds, displayResourceEffectRefRaw);
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
