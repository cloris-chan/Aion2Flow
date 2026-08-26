namespace Cloris.Aion2Flow.ViewModels;

internal sealed class PacketCooldownTracker
{
    private const long TransitionToleranceMilliseconds = 100;
    private readonly Dictionary<int, PacketCooldownState> _states = [];

    public IReadOnlyCollection<PacketCooldownState> States => _states.Values;

    public void Clear()
    {
        _states.Clear();
    }

    public bool ObserveStart0238(
        int localPlayerEntityId,
        int sourceEntityId,
        int rowBaseSkillId,
        int packetSkillCode,
        int remainingMilliseconds,
        long observedAtMilliseconds,
        int maxAvailableCount = 0,
        int? availableCountAfterControl = null)
    {
        if (!PacketCooldownSourceFilter.MatchesKnownLocalPlayer(localPlayerEntityId, sourceEntityId) ||
            rowBaseSkillId <= 0 || packetSkillCode <= 0 || remainingMilliseconds <= 0 || observedAtMilliseconds < 0)
        {
            return false;
        }

        var transition = ResolveTransition(rowBaseSkillId, remainingMilliseconds, observedAtMilliseconds);
        var cycleDurationMilliseconds = ResolveCycleDuration(rowBaseSkillId, remainingMilliseconds, observedAtMilliseconds);
        var availableCount = ResolveAvailableCount(rowBaseSkillId, maxAvailableCount, availableCountAfterControl);
        _states[rowBaseSkillId] = new PacketCooldownState(
            rowBaseSkillId,
            packetSkillCode,
            sourceEntityId,
            remainingMilliseconds,
            observedAtMilliseconds,
            transition,
            availableCount,
            0x0238,
            cycleDurationMilliseconds);
        return true;
    }

    public bool ObserveControl0238(
        int rowBaseSkillId,
        int packetSkillCode,
        int sourceEntityId,
        long observedAtMilliseconds,
        int maxAvailableCount = 0)
    {
        if (rowBaseSkillId <= 0 || packetSkillCode <= 0 || sourceEntityId <= 0 ||
            observedAtMilliseconds < 0)
        {
            return false;
        }

        var availableCount = ConsumeAvailableCharge(rowBaseSkillId, maxAvailableCount);
        if (availableCount is null)
        {
            return true;
        }

        if (_states.TryGetValue(rowBaseSkillId, out var state))
        {
            _states[rowBaseSkillId] = state with
            {
                PacketSkillCode = packetSkillCode,
                SourceEntityId = sourceEntityId,
                AvailableCount = availableCount,
                EvidenceOpcode = 0x0238
            };
        }
        else
        {
            _states[rowBaseSkillId] = new PacketCooldownState(
                rowBaseSkillId,
                packetSkillCode,
                sourceEntityId,
                0,
                observedAtMilliseconds,
                PacketCooldownTransition.Ready,
                availableCount,
                0x0238,
                0);
        }

        return true;
    }

    public bool ObserveCharge2238(
        int rowBaseSkillId,
        int packetSkillCode,
        int availableCount,
        int nextChargeRemainingMilliseconds,
        long observedAtMilliseconds)
    {
        if (rowBaseSkillId <= 0 || packetSkillCode <= 0 || availableCount < 0 || nextChargeRemainingMilliseconds < 0 || observedAtMilliseconds < 0)
            return false;

        var transition = ResolveTransition(rowBaseSkillId, nextChargeRemainingMilliseconds, observedAtMilliseconds);
        var cycleDurationMilliseconds = ResolveCycleDuration(rowBaseSkillId, nextChargeRemainingMilliseconds, observedAtMilliseconds);
        _states[rowBaseSkillId] = new PacketCooldownState(
            rowBaseSkillId,
            packetSkillCode,
            0,
            nextChargeRemainingMilliseconds,
            observedAtMilliseconds,
            transition,
            availableCount,
            0x2238,
            cycleDurationMilliseconds);
        return true;
    }

    public bool ObserveUpdate4738(
        int rowBaseSkillId,
        int packetSkillCode,
        int remainingMilliseconds,
        long observedAtMilliseconds,
        int maxAvailableCount = 0)
    {
        if (rowBaseSkillId <= 0 || packetSkillCode <= 0 || remainingMilliseconds < 0 || observedAtMilliseconds < 0)
            return false;

        var transition = ResolveTransition(rowBaseSkillId, remainingMilliseconds, observedAtMilliseconds);
        var availableCount = ResolveAvailableCount(rowBaseSkillId, maxAvailableCount);
        var cycleDurationMilliseconds = ResolveCycleDuration(rowBaseSkillId, remainingMilliseconds, observedAtMilliseconds);

        _states[rowBaseSkillId] = new PacketCooldownState(
            rowBaseSkillId,
            packetSkillCode,
            0,
            remainingMilliseconds,
            observedAtMilliseconds,
            transition,
            availableCount,
            0x4738,
            cycleDurationMilliseconds);
        return true;
    }

    private PacketCooldownTransition ResolveTransition(
        int rowBaseSkillId,
        int remainingMilliseconds,
        long observedAtMilliseconds)
    {
        if (!_states.TryGetValue(rowBaseSkillId, out var previous))
        {
            return remainingMilliseconds == 0
                ? PacketCooldownTransition.Ready
                : PacketCooldownTransition.Observed;
        }

        var elapsed = Math.Max(0, observedAtMilliseconds - previous.ObservedAtMilliseconds);
        var naturalExpected = Math.Max(0, previous.RemainingMilliseconds - Math.Min(int.MaxValue, elapsed));
        if (remainingMilliseconds == 0)
        {
            return naturalExpected > TransitionToleranceMilliseconds
                ? PacketCooldownTransition.ResetOrReduced
                : PacketCooldownTransition.Ready;
        }

        if (remainingMilliseconds < naturalExpected - TransitionToleranceMilliseconds)
            return PacketCooldownTransition.ResetOrReduced;

        return remainingMilliseconds > naturalExpected + TransitionToleranceMilliseconds
            ? PacketCooldownTransition.Refresh
            : PacketCooldownTransition.NaturalDecay;
    }

    private int ResolveCycleDuration(
        int rowBaseSkillId,
        int remainingMilliseconds,
        long observedAtMilliseconds)
    {
        if (remainingMilliseconds == 0)
            return 0;

        if (!_states.TryGetValue(rowBaseSkillId, out var previous))
            return remainingMilliseconds;

        var elapsedMilliseconds = Math.Max(0, observedAtMilliseconds - previous.ObservedAtMilliseconds);
        if (elapsedMilliseconds > previous.RemainingMilliseconds + TransitionToleranceMilliseconds)
            return remainingMilliseconds;

        var previousCycleDuration = previous.CycleDurationMilliseconds > 0
            ? previous.CycleDurationMilliseconds
            : previous.RemainingMilliseconds;
        return Math.Max(previousCycleDuration, remainingMilliseconds);
    }

    private int? ResolveAvailableCount(
        int rowBaseSkillId,
        int maxAvailableCount,
        int? availableCountAfterControl = null)
    {
        if (maxAvailableCount > 1 &&
            availableCountAfterControl is int reportedCount &&
            reportedCount >= 0 && reportedCount <= maxAvailableCount)
        {
            return reportedCount;
        }

        if (_states.TryGetValue(rowBaseSkillId, out var state) && state.AvailableCount is int availableCount)
        {
            return availableCount;
        }

        return maxAvailableCount > 0 ? maxAvailableCount : null;
    }

    private int? ConsumeAvailableCharge(int rowBaseSkillId, int maxAvailableCount)
    {
        var availableCount = ResolveAvailableCount(rowBaseSkillId, maxAvailableCount);
        if (availableCount is not int count)
        {
            return null;
        }

        return Math.Max(0, count - 1);
    }
}

internal enum PacketCooldownTransition : byte
{
    Observed,
    Refresh,
    Ready,
    ResetOrReduced,
    NaturalDecay
}

internal readonly record struct PacketCooldownState(
    int RowBaseSkillId,
    int PacketSkillCode,
    int SourceEntityId,
    int RemainingMilliseconds,
    long ObservedAtMilliseconds,
    PacketCooldownTransition Transition,
    int? AvailableCount,
    ushort EvidenceOpcode,
    int CycleDurationMilliseconds = 0);
