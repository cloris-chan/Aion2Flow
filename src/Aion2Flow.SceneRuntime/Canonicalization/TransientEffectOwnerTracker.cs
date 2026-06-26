using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.SceneRuntime.Canonicalization;

internal sealed class TransientEffectOwnerTracker
{
    private const int MaxPendingOwnerSkills = 128;
    private const long MaxOwnerSkillAgeMilliseconds = 1_500;

    private readonly List<PendingOwnerSkill> _pendingOwnerSkills = new(MaxPendingOwnerSkills);

    public void ObserveOwnerSkill(int ownerId, in CombatObservation observation, long observedAtMilliseconds)
    {
        var skillFamily = ResolveSkillFamily(in observation);
        if (ownerId <= 0 || skillFamily <= 0 || ResolveSkillCode(in observation) % 10 != 0)
            return;

        TrimExpired(observedAtMilliseconds);
        _pendingOwnerSkills.Add(new PendingOwnerSkill(ownerId, skillFamily, observation.ChainId, observedAtMilliseconds));
        TrimCapacity();
    }

    public int ResolveOwner(int sourceId, int targetId, in CombatObservation observation, long observedAtMilliseconds)
    {
        var skillFamily = ResolveSkillFamily(in observation);
        if (sourceId <= 0 || skillFamily <= 0)
            return 0;

        TrimExpired(observedAtMilliseconds);
        if (TryResolveOwner(sourceId, targetId, skillFamily, observedAtMilliseconds, requireTargetHint: true, out var targetHintOwnerId))
            return targetHintOwnerId;

        return TryResolveOwner(sourceId, targetId, skillFamily, observedAtMilliseconds, requireTargetHint: false, out var ownerId) ? ownerId : 0;
    }

    internal TransientEffectOwnerTrackerSnapshot CreateSnapshot() => new([.. _pendingOwnerSkills]);

    internal static TransientEffectOwnerTracker FromSnapshot(TransientEffectOwnerTrackerSnapshot snapshot)
    {
        var tracker = new TransientEffectOwnerTracker();
        tracker._pendingOwnerSkills.AddRange(snapshot.PendingOwnerSkills);
        return tracker;
    }

    private static int ResolveSkillFamily(in CombatObservation observation)
    {
        var skillCode = ResolveSkillCode(in observation);
        return skillCode > 0 ? skillCode / 10 : 0;
    }

    private static int ResolveSkillCode(in CombatObservation observation)
    {
        var skillCode = observation.BodySkillVariantRaw > 0 ? observation.BodySkillVariantRaw : observation.SkillCode;
        return skillCode > 0 ? skillCode : 0;
    }

    private bool TryResolveOwner(int sourceId, int targetId, int skillFamily, long observedAtMilliseconds, bool requireTargetHint, out int ownerId)
    {
        ownerId = 0;
        var latestObservedAt = long.MinValue;

        for (var i = _pendingOwnerSkills.Count - 1; i >= 0; i--)
        {
            var pending = _pendingOwnerSkills[i];
            if (pending.SkillFamily != skillFamily || pending.OwnerId == sourceId || !IsWithinAge(in pending, observedAtMilliseconds))
                continue;

            if (requireTargetHint && (targetId <= 0 || pending.TargetHintId != targetId))
                continue;

            if (ownerId != 0 && pending.OwnerId != ownerId)
            {
                ownerId = 0;
                return false;
            }

            if (pending.ObservedAtMilliseconds > latestObservedAt)
            {
                ownerId = pending.OwnerId;
                latestObservedAt = pending.ObservedAtMilliseconds;
            }
        }

        return ownerId != 0;
    }

    private static bool IsWithinAge(in PendingOwnerSkill pending, long observedAtMilliseconds)
    {
        var age = observedAtMilliseconds - pending.ObservedAtMilliseconds;
        return age >= 0 && age <= MaxOwnerSkillAgeMilliseconds;
    }

    private void TrimExpired(long observedAtMilliseconds)
    {
        for (var i = _pendingOwnerSkills.Count - 1; i >= 0; i--)
        {
            var pending = _pendingOwnerSkills[i];
            if (IsExpired(in pending, observedAtMilliseconds))
                _pendingOwnerSkills.RemoveAt(i);
        }
    }

    private static bool IsExpired(in PendingOwnerSkill pending, long observedAtMilliseconds) =>
        observedAtMilliseconds - pending.ObservedAtMilliseconds > MaxOwnerSkillAgeMilliseconds;

    private void TrimCapacity()
    {
        while (_pendingOwnerSkills.Count > MaxPendingOwnerSkills)
            _pendingOwnerSkills.RemoveAt(0);
    }
}

internal readonly record struct PendingOwnerSkill(int OwnerId, int SkillFamily, int TargetHintId, long ObservedAtMilliseconds);

internal sealed record TransientEffectOwnerTrackerSnapshot(PendingOwnerSkill[] PendingOwnerSkills);
