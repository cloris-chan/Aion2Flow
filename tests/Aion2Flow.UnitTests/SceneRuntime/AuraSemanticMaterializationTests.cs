using Cloris.Aion2Flow.Protocol.Combat;
using Cloris.Aion2Flow.Resources.Catalog;
using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Identity;
using Cloris.Aion2Flow.SceneRuntime.Journal;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Observation;
using Cloris.Aion2Flow.SceneRuntime.Playback;
using Cloris.Aion2Flow.SceneRuntime.Projection;
using Cloris.Aion2Flow.SceneRuntime.Stores;

namespace Cloris.Aion2Flow.Tests.SceneRuntime;

public sealed class AuraSemanticMaterializationTests
{
    public AuraSemanticMaterializationTests()
    {
        CombatResourceRegistry.SetGameResources(ResourceCatalog.Load(ResourceLanguage.English));
    }

    [Theory]
    [InlineData(140_600_401u, AuraDisposition.Debuff)]
    [InlineData(174_200_101u, AuraDisposition.Buff)]
    public void ExactAuraNode_AnnotatesStateWithoutInferringOrigin(uint resourceEffectRefRaw, AuraDisposition expectedDisposition)
    {
        var (store, transition) = ApplyOpen(resourceEffectRefRaw, originEntityId: 0);

        Assert.Equal(expectedDisposition, transition.State.Semantics.Disposition);
        Assert.Equal(AuraSemanticMatchKind.ExactNode, transition.State.Semantics.Trace.Match);
        Assert.Equal(resourceEffectRefRaw, transition.State.Semantics.Trace.ResourceEffectRef.RawId);
        Assert.Equal(unchecked((int)resourceEffectRefRaw), transition.State.Semantics.Trace.ResourceNodeId);
        Assert.Equal(0, transition.State.OriginEntityId);
        Assert.True(store.TryGet(transition.State.Key, out var state));
        Assert.Equal(transition.State, state);
    }

    [Theory]
    [InlineData(100_000_910u, AuraDisposition.Debuff, 1_000_009)]
    [InlineData(100_050_010u, AuraDisposition.Buff, 1_000_500)]
    public void UnambiguousSlot_AnnotatesStateWithoutPromotingResourceOwner(uint resourceEffectRefRaw, AuraDisposition expectedDisposition, int expectedResourceSkillId)
    {
        var (_, transition) = ApplyOpen(resourceEffectRefRaw, originEntityId: 0);

        Assert.Equal(expectedDisposition, transition.State.Semantics.Disposition);
        Assert.Equal(AuraSemanticMatchKind.UnambiguousSlot, transition.State.Semantics.Trace.Match);
        Assert.Equal(expectedResourceSkillId, transition.State.Semantics.Trace.ResourceSkillId);
        Assert.Equal(1, transition.State.Semantics.Trace.ResourceCandidateSlotCount);
        Assert.Equal(0, transition.State.OriginEntityId);
    }

    [Fact]
    public void AmbiguousSlot_LeavesDispositionUnknown()
    {
        var (_, transition) = ApplyOpen(1_120_000_020u, originEntityId: 0);

        Assert.Equal(AuraDisposition.Unknown, transition.State.Semantics.Disposition);
        Assert.Equal(AuraSemanticMatchKind.None, transition.State.Semantics.Trace.Match);
        Assert.True(transition.State.Semantics.Trace.HasResourceEvidence);
        Assert.True(transition.State.Semantics.Trace.ResourceCandidateSlotCount > 1);
        Assert.Equal(0, transition.State.Semantics.Trace.ResourceSkillId);
        Assert.Equal(0, transition.State.OriginEntityId);
    }

    [Fact]
    public void RegistryMiss_PreservesPacketResourceReferenceInUnknownTrace()
    {
        const uint resourceEffectRefRaw = 4_000_000_000u;

        var (_, transition) = ApplyOpen(resourceEffectRefRaw, originEntityId: 0);

        Assert.Equal(ResourceEffectRef.FromRaw(resourceEffectRefRaw), transition.State.ResourceEffectRef);
        Assert.Equal(AuraDisposition.Unknown, transition.State.Semantics.Disposition);
        Assert.Equal(AuraSemanticMatchKind.None, transition.State.Semantics.Trace.Match);
        Assert.Equal(resourceEffectRefRaw, transition.State.Semantics.Trace.ResourceEffectRef.RawId);
        Assert.False(transition.State.Semantics.Trace.HasResourceEvidence);
        Assert.Equal(-1, transition.State.Semantics.Trace.EffectSlot);
    }

    [Fact]
    public void ConflictingAuraAxis_LeavesDispositionUnknown()
    {
        var both = SkillSemanticValue.Classified(auraFacets: SkillAuraFacet.Buff | SkillAuraFacet.Debuff);
        var resolution = new SkillSemanticResourceResolution(
            RawId: 123_456,
            NodeKind: SkillSemanticResourceNodeKind.SkillAbnormal,
            NodeId: 123_456,
            DirectSemantics: both,
            Semantics: both,
            Slot: null,
            CandidateSlotCount: 2);

        var value = AuraSemanticEvidenceResolver.Evaluate(in resolution);

        Assert.Equal(AuraDisposition.Unknown, value.Disposition);
        Assert.Equal(AuraSemanticMatchKind.ExactNode, value.Trace.Match);
        Assert.Equal(SkillAuraFacet.Buff | SkillAuraFacet.Debuff, value.Trace.Semantics.AuraFacets);
    }

    [Fact]
    public void SnapshotAndPlayback_PreserveAuraSemanticValue()
    {
        var journal = new ObservedEventJournal();
        var sceneSessionId = Guid.NewGuid();
        AppendOpen(journal, sceneSessionId, 0, 200, 0, 7, 174_200_101u);
        AppendDiagnostic(journal, sceneSessionId, 1, 500);

        var owner = new SceneReadModelOwner(journal, sceneSessionId, DateTimeOffset.UnixEpoch);
        owner.Refresh();
        Assert.True(owner.Auras.TryGet(new AuraInstanceKey(200, 7), out var liveState));
        Assert.Equal(AuraDisposition.Buff, liveState.Semantics.Disposition);

        var restored = AuraStore.FromSnapshot(owner.Auras.CreateSnapshot());
        Assert.True(restored.TryGet(liveState.Key, out var restoredState));
        Assert.Equal(liveState.Semantics, restoredState.Semantics);

        var segment = owner.CreateLiveTimelineSegment();
        var frame = new ScenePlaybackSession(new TestPlaybackSource(sceneSessionId, segment)).Seek(500);
        var playbackState = Assert.Single(frame.ActiveAuras);
        Assert.Equal(liveState.Semantics, playbackState.Semantics);

        var marker = Assert.Single(
            ScenePlaybackTrackIndex.Build(segment, TestContext.Current.CancellationToken)
                .ReadWindow(0, 0, segment.CurrentEndObservationOrdinalExclusive, 1)
                .AsSpan()
                .ToArray());
        Assert.Equal(AuraDisposition.Buff, marker.AuraDisposition);
        Assert.Equal(liveState.Semantics.Trace, marker.AuraSemanticTrace);

        var timeline = ScenePlaybackAuraTimelineReader.Read(segment, 200, 1_000, TestContext.Current.CancellationToken);
        Assert.Equal(AuraDisposition.Buff, Assert.Single(timeline.Coverages).Semantics.Disposition);
        Assert.Equal(AuraDisposition.Buff, Assert.Single(timeline.Applications).Semantics.Disposition);
    }

    [Fact]
    public void RenewalResource_BackfillsSemanticStateAndIndexedPlaybackMarkers()
    {
        var journal = new ObservedEventJournal();
        var sceneSessionId = Guid.NewGuid();
        AppendOpen(journal, sceneSessionId, 0, 200, 0, 7, 0);
        AppendRenew(journal, sceneSessionId, 1, 200, 0, 7, 174_200_101u);
        AppendDiagnostic(journal, sceneSessionId, 2, 1_000);

        var owner = new SceneReadModelOwner(journal, sceneSessionId, DateTimeOffset.UnixEpoch);
        owner.Refresh();
        Assert.True(owner.Auras.TryGet(new AuraInstanceKey(200, 7), out var liveState));
        Assert.Equal(AuraDisposition.Buff, liveState.Semantics.Disposition);
        Assert.Equal(0, liveState.OriginEntityId);

        var segment = owner.CreateLiveTimelineSegment();
        var markers = ScenePlaybackTrackIndex.Build(segment, TestContext.Current.CancellationToken)
            .ReadWindow(0, 500, segment.CurrentEndObservationOrdinalExclusive, 10)
            .AsSpan();
        Assert.Equal(2, markers.Length);
        Assert.All(markers.ToArray(), static marker => Assert.Equal(AuraDisposition.Buff, marker.AuraDisposition));

        var timeline = ScenePlaybackAuraTimelineReader.Read(segment, 200, 1_000, TestContext.Current.CancellationToken);
        Assert.All(timeline.Coverages, static coverage => Assert.Equal(AuraDisposition.Buff, coverage.Semantics.Disposition));
        Assert.All(timeline.Applications, static application => Assert.Equal(AuraDisposition.Buff, application.Semantics.Disposition));
    }

    [Fact]
    public void ResultResourceChange_PreservesPriorCoverageAndAnnotatesResultMarker()
    {
        const uint openResourceEffectRefRaw = 174_200_101u;
        const uint resultResourceEffectRefRaw = 140_600_401u;
        var journal = new ObservedEventJournal();
        var sceneSessionId = Guid.NewGuid();
        AppendOpen(journal, sceneSessionId, 0, 200, 0, 7, openResourceEffectRefRaw);
        AppendResult(journal, sceneSessionId, 1, 200, 7, resultResourceEffectRefRaw);
        AppendDiagnostic(journal, sceneSessionId, 2, 1_000);

        var owner = new SceneReadModelOwner(journal, sceneSessionId, DateTimeOffset.UnixEpoch);
        owner.Refresh();
        Assert.False(owner.Auras.TryGet(new AuraInstanceKey(200, 7), out _));

        var segment = owner.CreateLiveTimelineSegment();
        var playback = new ScenePlaybackSession(new TestPlaybackSource(sceneSessionId, segment));
        var beforeResult = Assert.Single(playback.Seek(499).ActiveAuras);
        Assert.Equal(openResourceEffectRefRaw, beforeResult.ResourceEffectRef.RawId);
        Assert.Equal(AuraDisposition.Buff, beforeResult.Semantics.Disposition);
        Assert.Empty(playback.Seek(500).ActiveAuras);

        var markers = ScenePlaybackTrackIndex.Build(segment, TestContext.Current.CancellationToken)
            .ReadWindow(0, 500, segment.CurrentEndObservationOrdinalExclusive, 10)
            .AsSpan()
            .ToArray();
        Assert.Equal(2, markers.Length);
        Assert.Equal(AuraLifecycleEventKind.Open, markers[0].LifecycleEventKind);
        Assert.Equal(openResourceEffectRefRaw, markers[0].DisplayResourceEffectRefRaw);
        Assert.Equal(AuraDisposition.Buff, markers[0].AuraDisposition);
        Assert.Equal(AuraLifecycleEventKind.Result, markers[1].LifecycleEventKind);
        Assert.Equal(resultResourceEffectRefRaw, markers[1].DisplayResourceEffectRefRaw);
        Assert.Equal(AuraDisposition.Debuff, markers[1].AuraDisposition);

        var timeline = ScenePlaybackAuraTimelineReader.Read(segment, 200, 1_000, TestContext.Current.CancellationToken);
        var coverage = Assert.Single(timeline.Coverages);
        Assert.Equal(openResourceEffectRefRaw, coverage.DisplayResourceEffectRef.RawId);
        Assert.Equal(AuraDisposition.Buff, coverage.Semantics.Disposition);
        Assert.Equal(0, coverage.StartMilliseconds);
        Assert.Equal(500, coverage.EndMilliseconds);
        var application = Assert.Single(timeline.Applications);
        Assert.Equal(openResourceEffectRefRaw, application.DisplayResourceEffectRef.RawId);
        Assert.Equal(AuraDisposition.Buff, application.Semantics.Disposition);
    }

    [Fact]
    public void SceneObserver_Reports_Packet_Lifecycle_And_Semantic_Disposition_Independently()
    {
        const uint buffRef = 174_200_101u;
        const uint debuffRef = 140_600_401u;
        var journal = new ObservedEventJournal();
        var sceneSessionId = Guid.NewGuid();
        AppendOpen(journal, sceneSessionId, 0, 200, 300, 7, buffRef);
        AppendRenew(journal, sceneSessionId, 1, 200, 300, 7, buffRef);
        AppendResult(journal, sceneSessionId, 2, 200, 7, debuffRef);
        AppendResult(journal, sceneSessionId, 3, 200, 99, buffRef);
        var observer = new RecordingAuraLifecycleObserver();
        var applier = new DomainEventApplier(
            new EntityStore(),
            new SceneBoundaryStore(),
            new RuntimeMetadataRegistry(),
            new CombatStore(),
            combatOccurrenceObserver: null,
            auraLifecycleObserver: observer);

        applier.ApplyJournal(journal);

        Assert.Equal(4, observer.Contexts.Count);
        Assert.Equal(
            [AuraPacketRule.TrackableOpen, AuraPacketRule.Renewal, AuraPacketRule.Result, AuraPacketRule.None],
            observer.Contexts.Select(static context => AuraPacketEvidenceResolver.Evaluate(in context).Rule));
        Assert.All(observer.Contexts, static context =>
        {
            var packet = AuraPacketEvidenceResolver.Evaluate(in context);
            Assert.Equal(AuraDisposition.Unknown, packet.Disposition);
            Assert.Equal(1, context.FlushId);
        });

        Assert.Equal(AuraDisposition.Buff, ResolveIndependentSemantics(observer.Contexts[0]).Disposition);
        Assert.Equal(AuraDisposition.Buff, ResolveIndependentSemantics(observer.Contexts[1]).Disposition);
        Assert.Equal(AuraDisposition.Debuff, ResolveIndependentSemantics(observer.Contexts[2]).Disposition);
        Assert.Equal(AuraDisposition.Buff, ResolveIndependentSemantics(observer.Contexts[3]).Disposition);
        Assert.Equal(AuraLifecycleEventKind.Open, observer.Contexts[0].ProductionTransition.Kind);
        Assert.Equal(AuraLifecycleEventKind.Renew, observer.Contexts[1].ProductionTransition.Kind);
        Assert.Equal(AuraLifecycleEventKind.Result, observer.Contexts[2].ProductionTransition.Kind);
        Assert.Equal(default, observer.Contexts[3].ProductionTransition);
    }

    private static AuraSemanticValue ResolveIndependentSemantics(in AuraLifecycleObservationContext context) =>
        AuraSemanticEvidenceResolver.Evaluate(context.EffectiveResourceEffectRef);

    private static (AuraStore Store, AuraLifecycleTransition Transition) ApplyOpen(uint resourceEffectRefRaw, int originEntityId)
    {
        var journal = new ObservedEventJournal();
        var sceneSessionId = Guid.NewGuid();
        AppendOpen(journal, sceneSessionId, 0, 200, originEntityId, 7, resourceEffectRefRaw);
        var store = new AuraStore();
        var transition = default(AuraLifecycleTransition);
        journal.ReadEntry(0, entry => transition = store.Apply(entry));
        return (store, transition);
    }

    private static void AppendOpen(
        ObservedEventJournal journal,
        Guid sceneSessionId,
        long observationOrdinal,
        int targetEntityId,
        int originEntityId,
        int instanceSequenceId,
        uint resourceEffectRefRaw)
    {
        var stamp = new TimelineStamp(observationOrdinal * 500 * TimeSpan.TicksPerMillisecond, observationOrdinal, FlushId: 1);
        var observation = new AuraObservation
        {
            Kind = AuraObservationKind.Open,
            EntityId = targetEntityId,
            EchoSourceEntityId = originEntityId,
            InstanceSequenceId = instanceSequenceId,
            OpenMode = 1,
            GroupCode = 19,
            HeadValue = 1_000,
            StackCount = 1,
            BuffResourceEffectRef = ResourceEffectRef.FromRaw(resourceEffectRefRaw)
        };
        journal.AppendAura(sceneSessionId, stamp, originEntityId, targetEntityId, in observation);
    }

    private static void AppendRenew(
        ObservedEventJournal journal,
        Guid sceneSessionId,
        long observationOrdinal,
        int targetEntityId,
        int originEntityId,
        int instanceSequenceId,
        uint resourceEffectRefRaw)
    {
        var stamp = new TimelineStamp(observationOrdinal * 500 * TimeSpan.TicksPerMillisecond, observationOrdinal, FlushId: 1);
        var observation = new ActionObservation
        {
            SourceEntityId = targetEntityId,
            SourceEntityIdCopy = originEntityId,
            Phase = 19,
            InstanceSequenceId = instanceSequenceId,
            ActionResourceEffectRef = ResourceEffectRef.FromRaw(resourceEffectRefRaw),
            StateValue = 0,
            DetailValue = 0
        };
        journal.AppendAction(sceneSessionId, stamp, targetEntityId, 0, in observation);
    }

    private static void AppendResult(
        ObservedEventJournal journal,
        Guid sceneSessionId,
        long observationOrdinal,
        int targetEntityId,
        int instanceSequenceId,
        uint resourceEffectRefRaw)
    {
        var stamp = new TimelineStamp(observationOrdinal * 500 * TimeSpan.TicksPerMillisecond, observationOrdinal, FlushId: 1);
        var observation = new AuraObservation
        {
            Kind = AuraObservationKind.Result,
            EntityId = targetEntityId,
            InstanceSequenceId = instanceSequenceId,
            BuffResourceEffectRef = ResourceEffectRef.FromRaw(resourceEffectRefRaw)
        };
        journal.AppendAura(sceneSessionId, stamp, 0, targetEntityId, in observation);
    }

    private static void AppendDiagnostic(ObservedEventJournal journal, Guid sceneSessionId, long observationOrdinal, long observedAtMilliseconds)
    {
        var stamp = new TimelineStamp(observedAtMilliseconds * TimeSpan.TicksPerMillisecond, observationOrdinal, FlushId: 1);
        var header = new ObservedEventHeader(sceneSessionId, stamp, 0, 0, default);
        journal.AppendDiagnostic(in header);
    }

    private sealed class TestPlaybackSource(Guid encounterId, SceneJournalSegment segment) : IScenePlaybackSource
    {
        public Guid EncounterId { get; } = encounterId;
        public DateTimeOffset SceneStarted => DateTimeOffset.UnixEpoch;
        public ScenePlaybackSourceKind SourceKind => ScenePlaybackSourceKind.Archived;
        public SceneJournalSegment CreateTimelineSegment() => segment;
        public SceneCombatSnapshot CreateSnapshot() => SceneCombatSnapshot.Empty;
    }

    private sealed class RecordingAuraLifecycleObserver : IAuraLifecycleObserver
    {
        public List<AuraLifecycleObservationContext> Contexts { get; } = [];

        public void Observe(in AuraLifecycleObservationContext context) => Contexts.Add(context);
    }
}
