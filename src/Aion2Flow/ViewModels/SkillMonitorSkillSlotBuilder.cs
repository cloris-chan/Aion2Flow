using System.Runtime.InteropServices;

namespace Cloris.Aion2Flow.ViewModels;

internal readonly record struct SkillMonitorAuraCandidate(
    int TargetEntityId,
    int RowBaseSkillId,
    long DurationMilliseconds,
    long? ExpiresAtMilliseconds);

internal readonly record struct SkillMonitorTimer(
    long RemainingMilliseconds,
    long ReferenceMilliseconds,
    bool IsIndefinite)
{
    public double ProgressValue => IsIndefinite
        ? 1d
        : ReferenceMilliseconds > 0
            ? Math.Clamp(RemainingMilliseconds / (double)ReferenceMilliseconds, 0d, 1d)
            : 0d;
}

internal readonly record struct SkillMonitorSkillSlotState(
    int RowBaseSkillId,
    SkillMonitorTimer? BuffTimer,
    SkillMonitorTimer? CooldownTimer,
    int? AvailableCount);

internal sealed class SkillMonitorSkillSlotBuilder
{
    private readonly Dictionary<int, SkillMonitorSkillSlotState> _slots = [];
    private readonly List<SkillMonitorSkillSlotState> _results = [];

    public ReadOnlySpan<SkillMonitorSkillSlotState> Build(
        ReadOnlySpan<SkillMonitorAuraCandidate> auras,
        int localPlayerEntityId,
        IReadOnlyCollection<PacketCooldownState> cooldowns,
        Predicate<int> cooldownSelection,
        long nowMilliseconds)
    {
        _slots.Clear();
        AddLocalAuras(auras, localPlayerEntityId, nowMilliseconds, _slots);
        AddActiveCooldowns(cooldowns, cooldownSelection, nowMilliseconds, _slots);

        _results.Clear();
        if (_slots.Count == 0)
            return [];

        foreach (var slot in _slots.Values)
            _results.Add(slot);

        _results.Sort(static (left, right) => left.RowBaseSkillId.CompareTo(right.RowBaseSkillId));
        return CollectionsMarshal.AsSpan(_results);
    }

    private static void AddLocalAuras(
        ReadOnlySpan<SkillMonitorAuraCandidate> auras,
        int localPlayerEntityId,
        long nowMilliseconds,
        Dictionary<int, SkillMonitorSkillSlotState> slots)
    {
        if (localPlayerEntityId <= 0)
            return;

        for (var index = 0; index < auras.Length; index++)
        {
            var aura = auras[index];
            if (aura.TargetEntityId != localPlayerEntityId || aura.RowBaseSkillId <= 0)
                continue;

            var timer = CreateBuffTimer(in aura, nowMilliseconds);
            if (slots.TryGetValue(aura.RowBaseSkillId, out var slot))
            {
                if (ShouldReplaceBuffTimer(slot.BuffTimer, timer))
                    slots[aura.RowBaseSkillId] = slot with { BuffTimer = timer };
                continue;
            }

            slots.Add(aura.RowBaseSkillId, new SkillMonitorSkillSlotState(aura.RowBaseSkillId, timer, null, null));
        }
    }

    private static void AddActiveCooldowns(
        IReadOnlyCollection<PacketCooldownState> cooldowns,
        Predicate<int> cooldownSelection,
        long nowMilliseconds,
        Dictionary<int, SkillMonitorSkillSlotState> slots)
    {
        foreach (var cooldown in cooldowns)
        {
            if (!cooldownSelection(cooldown.RowBaseSkillId))
                continue;

            var remainingMilliseconds = ResolveCooldownRemaining(in cooldown, nowMilliseconds);
            if (remainingMilliseconds <= 0)
            {
                if (cooldown.AvailableCount is int availableCount &&
                    slots.TryGetValue(cooldown.RowBaseSkillId, out var buffSlot))
                {
                    slots[cooldown.RowBaseSkillId] = buffSlot with { AvailableCount = availableCount };
                }

                continue;
            }

            var referenceMilliseconds = cooldown.CycleDurationMilliseconds > 0
                ? cooldown.CycleDurationMilliseconds
                : cooldown.RemainingMilliseconds;
            var timer = new SkillMonitorTimer(
                remainingMilliseconds,
                Math.Max(remainingMilliseconds, referenceMilliseconds),
                IsIndefinite: false);

            if (slots.TryGetValue(cooldown.RowBaseSkillId, out var slot))
            {
                slots[cooldown.RowBaseSkillId] = slot with
                {
                    CooldownTimer = timer,
                    AvailableCount = cooldown.AvailableCount
                };
                continue;
            }

            slots.Add(cooldown.RowBaseSkillId, new SkillMonitorSkillSlotState(
                cooldown.RowBaseSkillId,
                null,
                timer,
                cooldown.AvailableCount));
        }
    }

    private static SkillMonitorTimer CreateBuffTimer(in SkillMonitorAuraCandidate aura, long nowMilliseconds)
    {
        if (aura.ExpiresAtMilliseconds is not long expiresAt)
            return new SkillMonitorTimer(0, 0, IsIndefinite: true);

        return new SkillMonitorTimer(
            Math.Max(0, expiresAt - nowMilliseconds),
            Math.Max(0, aura.DurationMilliseconds),
            IsIndefinite: false);
    }

    private static bool ShouldReplaceBuffTimer(SkillMonitorTimer? current, SkillMonitorTimer candidate)
        => current is not { } existing ||
           candidate.IsIndefinite ||
           (!existing.IsIndefinite && candidate.RemainingMilliseconds > existing.RemainingMilliseconds);

    private static long ResolveCooldownRemaining(in PacketCooldownState cooldown, long nowMilliseconds)
    {
        var elapsedMilliseconds = Math.Max(0, nowMilliseconds - cooldown.ObservedAtMilliseconds);
        return Math.Max(0, cooldown.RemainingMilliseconds - Math.Min(int.MaxValue, elapsedMilliseconds));
    }
}
