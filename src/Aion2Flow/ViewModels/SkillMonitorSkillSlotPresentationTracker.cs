using System.Runtime.InteropServices;
using Cloris.Aion2Flow.Controls;

namespace Cloris.Aion2Flow.ViewModels;

internal readonly record struct SkillMonitorSkillSlotPresentation(
    SkillMonitorSkillSlotState Slot,
    long CompletionStartedUtcMilliseconds);

internal readonly record struct SkillMonitorCompletionAnimation(
    SkillMonitorSkillSlotState Slot,
    long StartedUtcMilliseconds);

internal sealed class SkillMonitorSkillSlotPresentationTracker
{
    private readonly Dictionary<int, SkillMonitorSkillSlotState> _activeCooldownSlots = [];
    private readonly Dictionary<int, SkillMonitorSkillSlotState> _nextActiveCooldownSlots = [];
    private readonly Dictionary<int, SkillMonitorSkillSlotState> _currentSlots = [];
    private readonly Dictionary<int, SkillMonitorCompletionAnimation> _completionAnimations = [];
    private readonly List<int> _expiredAnimationSkillIds = [];
    private readonly List<SkillMonitorSkillSlotPresentation> _presentations = [];

    public void Clear()
    {
        _activeCooldownSlots.Clear();
        _nextActiveCooldownSlots.Clear();
        _currentSlots.Clear();
        _completionAnimations.Clear();
        _expiredAnimationSkillIds.Clear();
        _presentations.Clear();
    }

    public ReadOnlySpan<SkillMonitorSkillSlotPresentation> Update(
        ReadOnlySpan<SkillMonitorSkillSlotState> slots,
        long nowUtcMilliseconds)
    {
        _nextActiveCooldownSlots.Clear();
        _currentSlots.Clear();
        for (var index = 0; index < slots.Length; index++)
        {
            var slot = slots[index];
            _currentSlots[slot.RowBaseSkillId] = slot;
            if (slot.CooldownTimer is null)
                continue;

            _nextActiveCooldownSlots[slot.RowBaseSkillId] = slot;
            _completionAnimations.Remove(slot.RowBaseSkillId);
        }

        foreach (var pair in _activeCooldownSlots)
        {
            if (_nextActiveCooldownSlots.ContainsKey(pair.Key))
                continue;

            var completedSlot = _currentSlots.TryGetValue(pair.Key, out var currentSlot)
                ? currentSlot with { CooldownTimer = null }
                : pair.Value with { CooldownTimer = null };
            _completionAnimations[pair.Key] = new SkillMonitorCompletionAnimation(
                completedSlot,
                nowUtcMilliseconds);
        }

        _activeCooldownSlots.Clear();
        foreach (var pair in _nextActiveCooldownSlots)
            _activeCooldownSlots[pair.Key] = pair.Value;

        _expiredAnimationSkillIds.Clear();
        foreach (var pair in _completionAnimations)
        {
            if (nowUtcMilliseconds - pair.Value.StartedUtcMilliseconds >= CooldownSkillVisualClientAnimation.DurationMilliseconds)
                _expiredAnimationSkillIds.Add(pair.Key);
        }

        for (var index = 0; index < _expiredAnimationSkillIds.Count; index++)
            _completionAnimations.Remove(_expiredAnimationSkillIds[index]);

        foreach (var pair in _completionAnimations)
            _currentSlots.TryAdd(pair.Key, pair.Value.Slot);

        _presentations.Clear();
        foreach (var pair in _currentSlots)
        {
            var startedUtcMilliseconds = _completionAnimations.TryGetValue(pair.Key, out var animation)
                ? animation.StartedUtcMilliseconds
                : 0L;
            _presentations.Add(new SkillMonitorSkillSlotPresentation(pair.Value, startedUtcMilliseconds));
        }

        _presentations.Sort(static (left, right) => left.Slot.RowBaseSkillId.CompareTo(right.Slot.RowBaseSkillId));
        return CollectionsMarshal.AsSpan(_presentations);
    }
}
