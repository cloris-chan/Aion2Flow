using Cloris.Aion2Flow.SceneRuntime.Identity;
using Cloris.Aion2Flow.SceneRuntime.Observation;
using Cloris.Aion2Flow.SceneRuntime.Stores;

namespace Cloris.Aion2Flow.Tests.SceneRuntime;

public sealed class SceneIdentityTests
{
    [Fact]
    public void SceneIdentityScope_StoresSortedFrozenMappings()
    {
        var builder = new SceneIdentityScopeBuilder();
        builder.Reset(2);
        builder.AddPcMetadata(new PcMetadata(300, "Player B", 495, Faction.Light));
        builder.AddPcMetadata(new PcMetadata(100, "Player A", null));
        builder.AddNpcCode(9002, 2_100_351);
        builder.AddNpcCode(9001, 2_100_350);
        builder.AddMapCode(515552, 200003);

        var scope = builder.ToScope();

        Assert.Equal([100, 300], scope.PcMetadataAsSpan().ToArray().Select(static entry => entry.EntityId));
        Assert.True(scope.TryGetPcMetadata(300, out var pc));
        Assert.Equal("Player B", pc.Nickname);
        Assert.Equal(495, pc.OriginServerId);
        Assert.Equal(Faction.Light, pc.Faction);
        Assert.True(scope.TryGetNpcCode(9001, out var npcCode));
        Assert.Equal(2_100_350, npcCode);
        Assert.True(scope.TryGetMapCode(515552, out var mapCode));
        Assert.Equal(200003u, mapCode);
    }

    [Fact]
    public void SceneIdentityResolver_UsesSceneScopeBeforeGlobalRegistry()
    {
        const int entityId = 9002;
        var registry = new RuntimeMetadataRegistry();
        registry.UpsertPcMetadata(100, "Global Player", 1, Faction.Dark);
        registry.UpsertNpcCode(entityId, 2_100_350);

        var builder = new SceneIdentityScopeBuilder();
        builder.AddPcMetadata(new PcMetadata(100, "Scoped Player", 2, Faction.Light));
        builder.AddNpcCode(entityId, 2_100_351);
        var resolver = new SceneIdentityResolver(builder.ToScope(), registry);

        Assert.True(resolver.TryGetPcMetadata(100, out var pc));
        Assert.Equal("Scoped Player", pc.Nickname);
        Assert.Equal(Faction.Light, pc.Faction);
        Assert.True(resolver.TryGetNpcCode(entityId, out var npcCode));
        Assert.Equal(2_100_351, npcCode);
    }

    [Fact]
    public void SceneIdentityResolver_FallsBackToGlobalRegistryForLiveEmptyScope()
    {
        var registry = new RuntimeMetadataRegistry();
        registry.UpsertPcMetadata(100, "Live Player", 495, Faction.Light);
        registry.UpsertNpcCode(9001, 2_100_350);

        var resolver = new SceneIdentityResolver(SceneIdentityScope.Empty, registry);

        Assert.True(resolver.TryGetPcMetadata(100, out var pc));
        Assert.Equal("Live Player", pc.Nickname);
        Assert.Equal(Faction.Light, pc.Faction);
        Assert.True(resolver.TryGetNpcCode(9001, out var npcCode));
        Assert.Equal(2_100_350, npcCode);
    }

    [Fact]
    public void DomainEventApplier_PreservesOriginServerIdInRuntimeMetadataRegistry()
    {
        var entities = new EntityStore();
        var boundary = new SceneBoundaryStore();
        var registry = new RuntimeMetadataRegistry();
        var applier = new DomainEventApplier(entities, boundary, registry, new CombatStore());

        applier.ApplyEntry(new ObservedEventEnvelope
        {
            Domain = ObservedEventDomain.State,
            SourceEntityId = 100,
            State = new StateObservation(100, StateCodes.PlayerIdentity, 0, 0, 0, "Perigee", 495, Faction.Light)
        });

        Assert.True(registry.TryGetPcMetadata(100, out var metadata));
        Assert.Equal("Perigee", metadata.Nickname);
        Assert.Equal(495, metadata.OriginServerId);
        Assert.Equal(Faction.Light, metadata.Faction);
    }

    [Fact]
    public void DomainEventApplier_RecordsMapInstanceCodeInRuntimeMetadataRegistry()
    {
        var boundary = new SceneBoundaryStore();
        var registry = new RuntimeMetadataRegistry();
        var applier = new DomainEventApplier(new EntityStore(), boundary, registry, new CombatStore());

        applier.ApplyEntry(new ObservedEventEnvelope
        {
            Domain = ObservedEventDomain.Scene,
            Scene = new SceneObservation(200003, 0, 0, 0, "stage-destination-map")
        });
        applier.ApplyEntry(new ObservedEventEnvelope
        {
            Domain = ObservedEventDomain.Scene,
            Scene = new SceneObservation(0, 515552, 0, 0, "stage-destination-instance")
        });

        Assert.True(registry.TryGetMapCode(515552, out var mapCode));
        Assert.Equal(200003u, mapCode);
    }
}
