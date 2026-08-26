using Cloris.Aion2Flow.SceneRuntime.Journal;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Observation;
using Cloris.Aion2Flow.SceneRuntime.Stores;

namespace Cloris.Aion2Flow.Tests.SceneRuntime;

public sealed class AuraStoreTests
{
    [Fact]
    public void TrackableOpen_CreatesStateAndTransition()
    {
        var harness = new Harness();

        var transition = harness.Open(
            targetEntityId: 1_001,
            originEntityId: 2_001,
            sequenceId: 31,
            observedAtMilliseconds: 1_200,
            durationMilliseconds: 5_000,
            stackCount: 3,
            resourceEffectRefRaw: 71_001);

        Assert.Equal(AuraLifecycleEventKind.Open, transition.Kind);
        Assert.False(transition.HasPreviousState);
        Assert.True(transition.HasState);
        Assert.False(transition.RemovedByReplacement);
        Assert.Equal(1, harness.Store.Count);
        Assert.Equal(1, harness.Store.Revision);
        Assert.True(harness.Store.TryGet(new AuraInstanceKey(1_001, 31), out var state));
        Assert.Equal(transition.State, state);
        Assert.Equal(1_001, state.TargetEntityId);
        Assert.Equal(2_001, state.OriginEntityId);
        Assert.Equal(31, state.InstanceSequenceId);
        Assert.Equal(3, state.StackCount);
        Assert.Equal(1, state.OpenMode);
        Assert.Equal(19, state.GroupCode);
        Assert.Equal(5_000, state.DurationMilliseconds);
        Assert.Equal(ResourceEffectRef.FromRaw(71_001), state.ResourceEffectRef);
        Assert.Equal(1_200, state.OpenedAtMilliseconds);
        Assert.Equal(1_200, state.RenewedAtMilliseconds);
        Assert.Equal(6_200, state.ExpiresAtMilliseconds);
        Assert.Equal(0, state.OpenObservationOrdinal);
        Assert.Equal(0, state.LastObservationOrdinal);
    }

    [Fact]
    public void TrackableOpen_UsesPackedPacketDuration()
    {
        var harness = new Harness();

        var transition = harness.Open(
            targetEntityId: 1_001,
            originEntityId: 2_001,
            sequenceId: 31,
            observedAtMilliseconds: 1_200,
            durationMilliseconds: 37_856,
            headMiddleRaw: 4);

        Assert.Equal(300_000, transition.State.DurationMilliseconds);
        Assert.Equal(301_200, transition.State.ExpiresAtMilliseconds);
    }

    [Fact]
    public void Phase19Renewal_BackfillsOriginAndResource()
    {
        var harness = new Harness();
        _ = harness.Open(1_001, 0, 31, 1_000, durationMilliseconds: 500);

        var transition = harness.Renew(
            targetEntityId: 1_001,
            originEntityId: 2_001,
            sequenceId: 31,
            observedAtMilliseconds: 1_200,
            resourceEffectRefRaw: 71_001);

        Assert.Equal(AuraLifecycleEventKind.Renew, transition.Kind);
        Assert.True(transition.HasPreviousState);
        Assert.True(transition.HasState);
        Assert.True(transition.PreviousState.ResourceEffectRef.IsEmpty);
        Assert.Equal(0, transition.PreviousState.OriginEntityId);
        Assert.Equal(2_001, transition.State.OriginEntityId);
        Assert.Equal(ResourceEffectRef.FromRaw(71_001), transition.State.ResourceEffectRef);
        Assert.Equal(1_000, transition.State.OpenedAtMilliseconds);
        Assert.Equal(1_200, transition.State.RenewedAtMilliseconds);
        Assert.Equal(1_700, transition.State.ExpiresAtMilliseconds);
        Assert.Equal(0, transition.State.OpenObservationOrdinal);
        Assert.Equal(1, transition.State.LastObservationOrdinal);
        Assert.Equal(2, harness.Store.Revision);
    }

    [Fact]
    public void Result_ClosesKnownInstance()
    {
        var harness = new Harness();
        _ = harness.Open(1_001, 2_001, 31, 1_000, resourceEffectRefRaw: 71_001);

        var transition = harness.Result(1_001, 31, 1_300, resourceEffectRefRaw: 71_002);

        Assert.Equal(AuraLifecycleEventKind.Result, transition.Kind);
        Assert.True(transition.HasPreviousState);
        Assert.True(transition.HasState);
        Assert.False(transition.RemovedByReplacement);
        Assert.Equal(ResourceEffectRef.FromRaw(71_002), transition.State.ResourceEffectRef);
        Assert.Equal(1, transition.State.LastObservationOrdinal);
        Assert.Equal(0, harness.Store.Count);
        Assert.Equal(2, harness.Store.Revision);
        Assert.False(harness.Store.TryGet(new AuraInstanceKey(1_001, 31), out _));
    }

    [Theory]
    [InlineData(1, 17)]
    [InlineData(2, 19)]
    public void NonTrackableReopen_ReplacesTrackedState(int openMode, int groupCode)
    {
        var harness = new Harness();
        var open = harness.Open(1_001, 2_001, 31, 1_000, resourceEffectRefRaw: 71_001);

        var replacement = harness.Open(
            targetEntityId: 1_001,
            originEntityId: 2_002,
            sequenceId: 31,
            observedAtMilliseconds: 1_100,
            openMode: openMode,
            groupCode: groupCode,
            resourceEffectRefRaw: 71_002);

        Assert.Equal(AuraLifecycleEventKind.None, replacement.Kind);
        Assert.True(replacement.HasPreviousState);
        Assert.Equal(open.State, replacement.PreviousState);
        Assert.False(replacement.HasState);
        Assert.True(replacement.RemovedByReplacement);
        Assert.Equal(0, harness.Store.Count);
        Assert.Equal(2, harness.Store.Revision);
    }

    [Fact]
    public void UnknownSequenceRenewalAndResult_AreIgnored()
    {
        var harness = new Harness();

        var renewal = harness.Renew(1_001, 2_001, 31, 1_000, resourceEffectRefRaw: 71_001);
        var result = harness.Result(1_001, 32, 1_100, resourceEffectRefRaw: 71_002);

        Assert.Equal(default, renewal);
        Assert.Equal(default, result);
        Assert.Equal(0, harness.Store.Count);
        Assert.Equal(0, harness.Store.Revision);
    }

    [Fact]
    public void Expiry_UsesFiniteBoundaryAndKeepsMaximumDurationActive()
    {
        var harness = new Harness();
        var finite = harness.Open(1_001, 2_001, 31, 1_000, durationMilliseconds: 500);
        var indefinite = harness.Open(1_002, 2_002, 32, 1_000, durationMilliseconds: ushort.MaxValue);

        Assert.Equal(1_500, finite.State.ExpiresAtMilliseconds);
        Assert.Null(indefinite.State.ExpiresAtMilliseconds);

        var beforeFiniteExpiry = harness.Store.CreateActiveSnapshot(1_499);
        Assert.Equal(2, beforeFiniteExpiry.Length);
        Assert.Equal(new AuraInstanceKey(1_001, 31), beforeFiniteExpiry[0].Key);
        Assert.Equal(new AuraInstanceKey(1_002, 32), beforeFiniteExpiry[1].Key);

        var atFiniteExpiry = harness.Store.CreateActiveSnapshot(1_500);
        var atMaximumTime = harness.Store.CreateActiveSnapshot(long.MaxValue);
        Assert.Single(atFiniteExpiry);
        Assert.Equal(new AuraInstanceKey(1_002, 32), atFiniteExpiry[0].Key);
        Assert.Single(atMaximumTime);
        Assert.Equal(new AuraInstanceKey(1_002, 32), atMaximumTime[0].Key);
    }

    [Fact]
    public void CopyActiveSnapshotTo_ReusesDestinationAndPreservesSnapshotOrder()
    {
        var harness = new Harness();
        _ = harness.Open(1_002, 2_002, 32, 1_000, durationMilliseconds: ushort.MaxValue);
        _ = harness.Open(1_001, 2_001, 31, 1_000, durationMilliseconds: 500);
        var destination = new List<AuraInstanceState> { default };

        harness.Store.CopyActiveSnapshotTo(1_499, destination);

        Assert.Equal(2, destination.Count);
        Assert.Equal(new AuraInstanceKey(1_001, 31), destination[0].Key);
        Assert.Equal(new AuraInstanceKey(1_002, 32), destination[1].Key);

        harness.Store.CopyActiveSnapshotTo(1_500, destination);

        Assert.Single(destination);
        Assert.Equal(new AuraInstanceKey(1_002, 32), destination[0].Key);
    }

    [Fact]
    public void SnapshotRestore_PreservesStateAndRevision()
    {
        var harness = new Harness();
        _ = harness.Open(1_001, 0, 31, 1_000, durationMilliseconds: 500);
        _ = harness.Renew(1_001, 2_001, 31, 1_200, resourceEffectRefRaw: 71_001);
        var expectedSnapshot = harness.Store.CreateSnapshot();

        var restored = AuraStore.FromSnapshot(expectedSnapshot);

        Assert.Equal(harness.Store.Count, restored.Count);
        Assert.Equal(harness.Store.Revision, restored.Revision);
        Assert.Equal(expectedSnapshot.Revision, restored.Revision);
        Assert.True(harness.Store.TryGet(new AuraInstanceKey(1_001, 31), out var expectedState));
        Assert.True(restored.TryGet(new AuraInstanceKey(1_001, 31), out var restoredState));
        Assert.Equal(expectedState, restoredState);
        Assert.Equal(expectedSnapshot.Instances[0], restoredState);
    }

    [Fact]
    public void JournalObservationOrdinal_IsCarriedAcrossLifecycleTransitions()
    {
        var harness = new Harness();
        _ = harness.Diagnostic(900);
        var open = harness.Open(1_001, 0, 31, 1_000, durationMilliseconds: 500);
        _ = harness.Diagnostic(1_100);
        var renewal = harness.Renew(1_001, 2_001, 31, 1_200);
        var result = harness.Result(1_001, 31, 1_300);

        Assert.Equal(1, open.State.OpenObservationOrdinal);
        Assert.Equal(1, open.State.LastObservationOrdinal);
        Assert.Equal(1, renewal.State.OpenObservationOrdinal);
        Assert.Equal(3, renewal.State.LastObservationOrdinal);
        Assert.Equal(1, result.State.OpenObservationOrdinal);
        Assert.Equal(4, result.State.LastObservationOrdinal);
    }

    private sealed class Harness
    {
        private readonly Guid _sceneSessionId = Guid.NewGuid();
        private readonly ObservedEventJournal _journal = new();

        public AuraStore Store { get; } = new();

        public AuraLifecycleTransition Open(
            int targetEntityId,
            int originEntityId,
            int sequenceId,
            long observedAtMilliseconds,
            ushort durationMilliseconds = 1_000,
            int openMode = 1,
            int groupCode = 19,
            int stackCount = 1,
            uint resourceEffectRefRaw = 0,
            ulong headMiddleRaw = 0)
        {
            var stamp = CreateNextStamp(observedAtMilliseconds);
            var observation = new AuraObservation
            {
                Kind = AuraObservationKind.Open,
                EntityId = targetEntityId,
                EchoSourceEntityId = originEntityId,
                InstanceSequenceId = sequenceId,
                OpenMode = openMode,
                GroupCode = groupCode,
                HeadValue = durationMilliseconds,
                HeadMiddleRaw = headMiddleRaw,
                StackCount = stackCount,
                BuffResourceEffectRef = ResourceEffectRef.FromRaw(resourceEffectRefRaw)
            };
            _journal.AppendAura(_sceneSessionId, stamp, originEntityId, targetEntityId, in observation);
            return Apply(stamp.ObservationOrdinal);
        }

        public AuraLifecycleTransition Renew(
            int targetEntityId,
            int originEntityId,
            int sequenceId,
            long observedAtMilliseconds,
            uint resourceEffectRefRaw = 0)
        {
            var stamp = CreateNextStamp(observedAtMilliseconds);
            var observation = new ActionObservation
            {
                SourceEntityId = targetEntityId,
                SourceEntityIdCopy = originEntityId,
                Phase = 19,
                InstanceSequenceId = sequenceId,
                ActionResourceEffectRef = ResourceEffectRef.FromRaw(resourceEffectRefRaw),
                StateValue = 0,
                DetailValue = 0
            };
            _journal.AppendAction(_sceneSessionId, stamp, targetEntityId, 0, in observation);
            return Apply(stamp.ObservationOrdinal);
        }

        public AuraLifecycleTransition Result(
            int targetEntityId,
            int sequenceId,
            long observedAtMilliseconds,
            uint resourceEffectRefRaw = 0)
        {
            var stamp = CreateNextStamp(observedAtMilliseconds);
            var observation = new AuraObservation
            {
                Kind = AuraObservationKind.Result,
                EntityId = targetEntityId,
                InstanceSequenceId = sequenceId,
                BuffResourceEffectRef = ResourceEffectRef.FromRaw(resourceEffectRefRaw)
            };
            _journal.AppendAura(_sceneSessionId, stamp, 0, targetEntityId, in observation);
            return Apply(stamp.ObservationOrdinal);
        }

        public AuraLifecycleTransition Diagnostic(long observedAtMilliseconds)
        {
            var stamp = CreateNextStamp(observedAtMilliseconds);
            var header = new ObservedEventHeader(_sceneSessionId, stamp, 0, 0, default);
            _journal.AppendDiagnostic(in header);
            return Apply(stamp.ObservationOrdinal);
        }

        private TimelineStamp CreateNextStamp(long observedAtMilliseconds) =>
            new(observedAtMilliseconds * TimeSpan.TicksPerMillisecond, _journal.NextObservationOrdinal, FlushId: 1);

        private AuraLifecycleTransition Apply(long observationOrdinal)
        {
            var transition = default(AuraLifecycleTransition);
            _journal.ReadEntry(observationOrdinal, entry => transition = Store.Apply(entry));
            return transition;
        }
    }
}
