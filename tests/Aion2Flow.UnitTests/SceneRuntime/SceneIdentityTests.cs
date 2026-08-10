using Cloris.Aion2Flow.SceneRuntime.Identity;
using Cloris.Aion2Flow.SceneRuntime.Journal;
using Cloris.Aion2Flow.SceneRuntime.Model;
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
        builder.AddPcMetadata(new PcMetadata(300, "Player B", Faction.Light, LegionName: "Aether"));
        builder.AddPcMetadata(new PcMetadata(100, "Player A"));
        builder.AddNpcCode(9002, 2_100_351);
        builder.AddNpcCode(9001, 2_100_350);
        builder.AddMapCode(515552, 200003);

        var scope = builder.ToScope();

        Assert.Equal([100, 300], scope.PcMetadataAsSpan().ToArray().Select(static entry => entry.EntityId));
        Assert.True(scope.TryGetPcMetadata(300, out var pc));
        Assert.Equal("Player B", pc.Nickname);
        Assert.Equal(Faction.Light, pc.Faction);
        Assert.Equal("Aether", pc.LegionName);
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
        registry.UpsertPcMetadata(100, "Global Player", Faction.Dark);
        registry.UpsertNpcCode(entityId, 2_100_350);

        var builder = new SceneIdentityScopeBuilder();
        builder.AddPcMetadata(new PcMetadata(100, "Scoped Player", Faction.Light));
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
        registry.UpsertPcMetadata(100, "Live Player", Faction.Light);
        registry.UpsertNpcCode(9001, 2_100_350);

        var resolver = new SceneIdentityResolver(SceneIdentityScope.Empty, registry);

        Assert.True(resolver.TryGetPcMetadata(100, out var pc));
        Assert.Equal("Live Player", pc.Nickname);
        Assert.Equal(Faction.Light, pc.Faction);
        Assert.True(resolver.TryGetNpcCode(9001, out var npcCode));
        Assert.Equal(2_100_350, npcCode);
    }

    [Fact]
    public void DomainEventApplier_PreservesFactionInRuntimeMetadataRegistry()
    {
        var entities = new EntityStore();
        var boundary = new SceneBoundaryStore();
        var registry = new RuntimeMetadataRegistry();
        var applier = new DomainEventApplier(entities, boundary, registry, new CombatStore());

        ApplyState(applier, 100, new StateObservation(100, StateCodes.PlayerIdentity, 0, 0, 0, "Perigee", Faction.Light));

        Assert.True(registry.TryGetPcMetadata(100, out var metadata));
        Assert.Equal("Perigee", metadata.Nickname);
        Assert.Equal(Faction.Light, metadata.Faction);
    }

    [Fact]
    public void DomainEventApplier_PreservesLocalPlayerInRuntimeMetadataRegistry()
    {
        var entities = new EntityStore();
        var boundary = new SceneBoundaryStore();
        var registry = new RuntimeMetadataRegistry();
        var applier = new DomainEventApplier(entities, boundary, registry, new CombatStore());

        ApplyState(applier, 100, new StateObservation(100, StateCodes.PlayerIdentity, 0, 0, 0, "Perigee", Faction.Light, CharacterClass.Elementalist, IsLocalPlayer: true));
        ApplyState(applier, 100, new StateObservation(100, StateCodes.PlayerIdentity, 0, 0, 0, "Perigee", Faction.Light));

        Assert.True(registry.TryGetPcMetadata(100, out var metadata));
        Assert.True(metadata.IsLocalPlayer);
        Assert.Equal(CharacterClass.Elementalist, metadata.CharacterClass);
    }

    [Fact]
    public void DomainEventApplier_PreservesLegionNameInRuntimeMetadataRegistry()
    {
        var entities = new EntityStore();
        var boundary = new SceneBoundaryStore();
        var registry = new RuntimeMetadataRegistry();
        var applier = new DomainEventApplier(entities, boundary, registry, new CombatStore());

        ApplyState(applier, 100, new StateObservation(100, StateCodes.PlayerIdentity, 0, 0, 0, "Perigee", Faction.Light, LegionName: "Aether"));
        ApplyState(applier, 100, new StateObservation(100, StateCodes.PlayerIdentity, 0, 0, 0, "Perigee", Faction.Light));

        Assert.True(registry.TryGetPcMetadata(100, out var metadata));
        Assert.Equal("Aether", metadata.LegionName);
    }

    [Fact]
    public void RuntimeMetadataRegistry_PlayerGroupMembership_ComputesLocalRelations()
    {
        var registry = new RuntimeMetadataRegistry();
        registry.UpsertPcMetadata(100, "Self", isLocalPlayer: true);
        registry.UpsertPcMetadata(200, "Same Party");
        registry.UpsertPcMetadata(300, "Other Squad");

        registry.UpsertPlayerGroupMembership(100, PlayerGroupMembership.Force(7, 1, 1));
        registry.UpsertPlayerGroupMembership(200, PlayerGroupMembership.Force(7, 1, 2));
        registry.UpsertPlayerGroupMembership(300, PlayerGroupMembership.Force(7, 3, 1));

        Assert.True(registry.TryGetPcMetadata(100, out var self));
        Assert.True(registry.TryGetPcMetadata(200, out var sameParty));
        Assert.True(registry.TryGetPcMetadata(300, out var otherSquad));
        Assert.Equal(PlayerGroupRelation.Unknown, self.GroupRelation);
        Assert.Equal(PlayerGroupRelation.PartyMember, sameParty.GroupRelation);
        Assert.Equal(PlayerGroupRelation.ForceMember, otherSquad.GroupRelation);

        registry.UpsertPlayerGroupMembership(300, PlayerGroupMembership.Party(4));
        Assert.True(registry.TryGetPcMetadata(300, out otherSquad));
        Assert.Equal(PlayerGroupRelation.PartyMember, otherSquad.GroupRelation);

        registry.UpsertPlayerGroupMembership(300, PlayerGroupMembership.Force(7, 3, 1));
        Assert.True(registry.TryGetPcMetadata(300, out otherSquad));
        Assert.Equal(PlayerGroupRelation.PartyMember, otherSquad.GroupRelation);
    }

    [Fact]
    public void RuntimeMetadataRegistry_LocalPlayerMetadata_RecalculatesExistingGroupRelations()
    {
        var registry = new RuntimeMetadataRegistry();
        registry.UpsertPcMetadata(100, "Self", originServerId: 2001);
        registry.UpsertPcMetadata(200, "Same Party");
        registry.UpsertPcMetadata(300, "Other Squad");

        registry.UpsertPlayerGroupProfile(2001, "Self", PlayerGroupMembership.Party(5));
        registry.UpsertPlayerGroupMembership(100, PlayerGroupMembership.Force(7, 2, 1));
        registry.UpsertPlayerGroupMembership(200, PlayerGroupMembership.Force(7, 2, 2));
        registry.UpsertPlayerGroupMembership(300, PlayerGroupMembership.Force(7, 3, 1));
        Assert.True(registry.TryGetPcMetadata(100, out var preLocalSelf));
        Assert.Equal(PlayerGroupRelation.PartyMember, preLocalSelf.GroupRelation);

        registry.UpsertPcMetadata(100, "Self", isLocalPlayer: true, originServerId: 2001);

        Assert.True(registry.TryGetPcMetadata(100, out var self));
        Assert.True(registry.TryGetPcMetadata(200, out var sameParty));
        Assert.True(registry.TryGetPcMetadata(300, out var otherSquad));
        Assert.Equal(PlayerGroupRelation.Unknown, self.GroupRelation);
        Assert.Equal(PlayerGroupRelation.PartyMember, sameParty.GroupRelation);
        Assert.Equal(PlayerGroupRelation.ForceMember, otherSquad.GroupRelation);
    }

    [Fact]
    public void RuntimeMetadataRegistry_DirectForceProfile_MarksForceMemberWithoutLocalForceRow()
    {
        var registry = new RuntimeMetadataRegistry();
        registry.UpsertPcMetadata(100, "Self", isLocalPlayer: true);
        registry.UpsertPcMetadata(200, "Force");

        registry.UpsertPlayerGroupMembership(200, PlayerGroupMembership.Force(0, 0, 2));

        Assert.True(registry.TryGetPcMetadata(200, out var forceMember));
        Assert.Equal(PlayerGroupRelation.ForceMember, forceMember.GroupRelation);
    }

    [Fact]
    public void RuntimeMetadataRegistry_PlayerGroupProfile_ResolvesWhenMatchingPcMetadataArrives()
    {
        var registry = new RuntimeMetadataRegistry();

        registry.UpsertPlayerGroupProfile(2002, "浮屠", PlayerGroupMembership.Party(5));
        registry.UpsertPcMetadata(9551, "浮屠", originServerId: 2002);

        Assert.True(registry.TryGetPcMetadata(9551, out var partyMember));
        Assert.Equal(PlayerGroupRelation.PartyMember, partyMember.GroupRelation);
    }

    [Fact]
    public void RuntimeMetadataRegistry_PlayerGroupProfile_ResolvesForceWhenMatchingPcMetadataArrives()
    {
        var registry = new RuntimeMetadataRegistry();

        registry.UpsertPlayerGroupProfile(2005, "折柳", PlayerGroupMembership.Force(0, 0, 2));
        registry.UpsertPcMetadata(1339, "折柳", originServerId: 2005);

        Assert.True(registry.TryGetPcMetadata(1339, out var forceMember));
        Assert.Equal(PlayerGroupRelation.ForceMember, forceMember.GroupRelation);
    }

    [Fact]
    public void RuntimeMetadataRegistry_PlayerGroupProfile_DoesNotDowngradePartyProfileToForce()
    {
        var registry = new RuntimeMetadataRegistry();

        registry.UpsertPlayerGroupProfile(2006, "娜烏西卡", PlayerGroupMembership.Force(0, 0, 5));
        registry.UpsertPlayerGroupProfile(2006, "娜烏西卡", PlayerGroupMembership.Party(5));
        registry.UpsertPlayerGroupProfile(2006, "娜烏西卡", PlayerGroupMembership.Force(0, 0, 5));
        registry.UpsertPcMetadata(7740, "娜烏西卡", originServerId: 2006);

        Assert.True(registry.TryGetPcMetadata(7740, out var partyMember));
        Assert.Equal(PlayerGroupRelation.PartyMember, partyMember.GroupRelation);
    }

    [Fact]
    public void RuntimeMetadataRegistry_Continuity_RehydratesLocalAndGroupProfilesAcrossEntityIds()
    {
        var source = new RuntimeMetadataRegistry();
        source.UpsertPcMetadata(
            100,
            "Self",
            Faction.Light,
            CharacterClass.Cleric,
            isLocalPlayer: true,
            originServerId: 2001);
        source.UpsertPcMetadata(200, "Ally", originServerId: 2002);
        source.UpsertPlayerGroupMembership(200, PlayerGroupMembership.Party(2));

        var continuity = source.CreateContinuity();
        var next = new RuntimeMetadataRegistry(continuity);

        Assert.True(next.TryGetPcMetadata(100, out var carriedLocal));
        Assert.True(carriedLocal.IsLocalPlayer);
        Assert.Equal(CharacterClass.Cleric, carriedLocal.CharacterClass);

        next.UpsertPcMetadata(300, "Self", Faction.Light, originServerId: 2001);
        next.UpsertPcMetadata(400, "Ally", originServerId: 2002);

        Assert.True(next.TryGetPcMetadata(300, out var local));
        Assert.True(local.IsLocalPlayer);
        Assert.False(next.TryGetPcMetadata(100, out var staleLocal) && staleLocal.IsLocalPlayer);
        Assert.True(next.TryGetPcMetadata(400, out var ally));
        Assert.Equal(PlayerGroupRelation.PartyMember, ally.GroupRelation);
    }

    [Fact]
    public void RuntimeMetadataRegistry_Continuity_DemotesStaleEntityWhenIdentityChanges()
    {
        var source = new RuntimeMetadataRegistry();
        source.UpsertPcMetadata(100, "Self", isLocalPlayer: true, originServerId: 2001);
        var next = new RuntimeMetadataRegistry(source.CreateContinuity());

        next.UpsertPcMetadata(100, "Other", originServerId: 2002);
        next.UpsertPcMetadata(300, "Self", originServerId: 2001);

        Assert.True(next.TryGetPcMetadata(100, out var other));
        Assert.False(other.IsLocalPlayer);
        Assert.True(next.TryGetPcMetadata(300, out var local));
        Assert.True(local.IsLocalPlayer);
    }

    [Fact]
    public void DomainEventApplier_RecordsMapInstanceCodeInRuntimeMetadataRegistry()
    {
        var boundary = new SceneBoundaryStore();
        var registry = new RuntimeMetadataRegistry();
        var applier = new DomainEventApplier(new EntityStore(), boundary, registry, new CombatStore());

        ApplyScene(applier, new SceneObservation(200003, 0, SceneObservationKind.CurrentMap));
        ApplyScene(applier, new SceneObservation(0, 515552, SceneObservationKind.MapEventRegistered));

        Assert.True(registry.TryGetMapCode(515552, out var mapCode));
        Assert.Equal(200003u, mapCode);
    }

    private static void ApplyState(DomainEventApplier applier, int sourceEntityId, in StateObservation observation)
    {
        var journal = new ObservedEventJournal();
        var header = new ObservedEventHeader(Guid.Empty, default, sourceEntityId, 0, default);
        journal.Append(in header, in observation);
        journal.ReadEntry(0, entry =>
        {
            _ = applier.ApplyEntry(entry);
        });
    }

    private static void ApplyScene(DomainEventApplier applier, in SceneObservation observation)
    {
        var journal = new ObservedEventJournal();
        var header = new ObservedEventHeader(Guid.Empty, default, 0, 0, default);
        journal.Append(in header, in observation);
        journal.ReadEntry(0, entry =>
        {
            _ = applier.ApplyEntry(entry);
        });
    }
}
