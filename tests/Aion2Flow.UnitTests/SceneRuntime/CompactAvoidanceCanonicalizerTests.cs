using Cloris.Aion2Flow.Capture.Diagnostics;
using Cloris.Aion2Flow.Resources;
using Cloris.Aion2Flow.SceneRuntime.Journal;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Observation;
using Cloris.Aion2Flow.SceneRuntime.Stores;
using Cloris.Aion2Flow.Tests.Protocol;

namespace Cloris.Aion2Flow.Tests.SceneRuntime;

public class CompactAvoidanceCanonicalizerTests
{
    [Fact]
    public void ScenePath_FlushesCompactType1AvoidAsEvade()
    {
        CombatResourceRegistry.SetGameResources(BuildCompactEvadeSkillMap(), new Dictionary<int, NpcCatalogEntry>());
        var journal = new ObservedEventJournal();
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = Guid.NewGuid(),
            Stamp = new TimelineStamp { ObservationOrdinal = 0, FrameOrdinal = 1, BatchOrdinal = 100 },
            Domain = ObservedEventDomain.Combat,
            SourceEntityId = 26029,
            TargetEntityId = 933,
            Raw = new RawPacketReference(0x0438, 0, 0, 1_000),
            Combat = new CombatObservation
            {
                SkillCode = 1216310,
                Damage = 0,
                HitCount = 0,
                AttemptCount = 0,
                Marker = 6,
                Type = 1,
                LayoutTag = 0
            }
        });

        var combat = Apply(journal);

        Assert.True(combat.TryGetPair(26029, 933, out var pair));
        Assert.Equal(0, pair!.TotalDamage);
        Assert.Equal(0, pair.HitCount);
        Assert.Equal(1, pair.AttemptCount);
        Assert.Equal(1, pair.EvadeCount);
    }

    [Fact]
    public void ScenePath_KeepsDirectBlockedDamageWhenDodgeControlArrives()
    {
        CombatResourceRegistry.SetGameResources(BuildCompactEvadeSkillMap(), new Dictionary<int, NpcCatalogEntry>());
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneId,
            Stamp = new TimelineStamp { ObservationOrdinal = 0, FrameOrdinal = 1, BatchOrdinal = 100 },
            Domain = ObservedEventDomain.Combat,
            SourceEntityId = 26029,
            TargetEntityId = 933,
            Combat = new CombatObservation
            {
                SkillCode = 1216310,
                OriginalSkillCode = 1216310,
                Damage = 1,
                HitCount = 1,
                AttemptCount = 1,
                Marker = 8,
                EventKind = CombatEventKind.Damage,
                ValueKind = CombatValueKind.Damage
            }
        });
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneId,
            Stamp = new TimelineStamp { ObservationOrdinal = 1, FrameOrdinal = 2, BatchOrdinal = 100 },
            Domain = ObservedEventDomain.Combat,
            SourceEntityId = 933,
            TargetEntityId = 0,
            Raw = new RawPacketReference(0x0238, 0, 0, 1_001),
            Combat = new CombatObservation
            {
                SkillCode = 17000100,
                Damage = 0,
                HitCount = 0,
                AttemptCount = 0,
                Marker = 72
            }
        });

        var combat = Apply(journal);

        Assert.True(combat.TryGetPair(26029, 933, out var pair));
        Assert.Equal(1, pair!.TotalDamage);
        Assert.Equal(1, pair.HitCount);
        Assert.Equal(1, pair.AttemptCount);
        Assert.Equal(0, pair.EvadeCount);
    }

    [Fact]
    public void ScenePath_KeepsDirectBlockedDamageWhenNoDodgeControlArrives()
    {
        CombatResourceRegistry.SetGameResources(BuildCompactEvadeSkillMap(), new Dictionary<int, NpcCatalogEntry>());
        var journal = new ObservedEventJournal();
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = Guid.NewGuid(),
            Stamp = new TimelineStamp { ObservationOrdinal = 0, FrameOrdinal = 1, BatchOrdinal = 100 },
            Domain = ObservedEventDomain.Combat,
            SourceEntityId = 26029,
            TargetEntityId = 933,
            Combat = new CombatObservation
            {
                SkillCode = 1216310,
                OriginalSkillCode = 1216310,
                Damage = 1,
                HitCount = 1,
                AttemptCount = 1,
                Marker = 6,
                EventKind = CombatEventKind.Damage,
                ValueKind = CombatValueKind.Damage
            }
        });

        var combat = Apply(journal);

        Assert.True(combat.TryGetPair(26029, 933, out var pair));
        Assert.Equal(1, pair!.TotalDamage);
        Assert.Equal(1, pair.HitCount);
        Assert.Equal(1, pair.AttemptCount);
        Assert.Equal(0, pair.EvadeCount);
    }

    [Fact]
    public void ScenePath_DoesNotStoreCompactType2SidecarAsCombat()
    {
        CombatResourceRegistry.SetGameResources(BuildCompactEvadeSkillMap(), new Dictionary<int, NpcCatalogEntry>());
        var journal = new ObservedEventJournal();
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = Guid.NewGuid(),
            Stamp = new TimelineStamp { ObservationOrdinal = 0, FrameOrdinal = 1, BatchOrdinal = 100 },
            Domain = ObservedEventDomain.Combat,
            SourceEntityId = 168467,
            TargetEntityId = 1734,
            Raw = new RawPacketReference(0x0438, 0, 0, 1_000),
            Combat = new CombatObservation
            {
                SkillCode = 1800055,
                Damage = 1,
                HitCount = 0,
                AttemptCount = 0,
                Marker = 16,
                Type = 2,
                LayoutTag = 0
            }
        });

        var combat = Apply(journal);

        Assert.False(combat.TryGetPair(168467, 1734, out _));
        Assert.Equal(0, combat.Revision);
    }

    [Fact]
    public void ScenePath_CompactType2SidecarCancelsPendingCompactEvade()
    {
        CombatResourceRegistry.SetGameResources(BuildCompactEvadeSkillMap(), new Dictionary<int, NpcCatalogEntry>());
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneId,
            Stamp = new TimelineStamp { ObservationOrdinal = 0, FrameOrdinal = 1, BatchOrdinal = 100 },
            Domain = ObservedEventDomain.Combat,
            SourceEntityId = 26029,
            TargetEntityId = 933,
            Raw = new RawPacketReference(0x0438, 0, 0, 1_000),
            Combat = new CombatObservation
            {
                SkillCode = 1216310,
                Damage = 0,
                HitCount = 0,
                AttemptCount = 0,
                Marker = 6,
                Type = 1,
                LayoutTag = 0
            }
        });
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneId,
            Stamp = new TimelineStamp { ObservationOrdinal = 1, FrameOrdinal = 2, BatchOrdinal = 100 },
            Domain = ObservedEventDomain.Combat,
            SourceEntityId = 26029,
            TargetEntityId = 933,
            Raw = new RawPacketReference(0x0438, 0, 0, 1_001),
            Combat = new CombatObservation
            {
                SkillCode = 1216310,
                Damage = 108,
                HitCount = 0,
                AttemptCount = 0,
                Marker = 6,
                Type = 2,
                LayoutTag = 0
            }
        });

        var combat = Apply(journal);

        Assert.False(combat.TryGetPair(26029, 933, out _));
        Assert.Equal(0, combat.Revision);
    }

    [Fact]
    public void ScenePath_CompactType2SidecarCancelsPendingCompactEvadeBeforeBatchSwitch()
    {
        CombatResourceRegistry.SetGameResources(BuildCompactEvadeSkillMap(), new Dictionary<int, NpcCatalogEntry>());
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneId,
            Stamp = new TimelineStamp { ObservationOrdinal = 0, FrameOrdinal = 1, BatchOrdinal = 100 },
            Domain = ObservedEventDomain.Combat,
            SourceEntityId = 26029,
            TargetEntityId = 933,
            Raw = new RawPacketReference(0x0438, 0, 0, 1_000),
            Combat = new CombatObservation
            {
                SkillCode = 1216310,
                Damage = 0,
                HitCount = 0,
                AttemptCount = 0,
                Marker = 6,
                Type = 1,
                LayoutTag = 0
            }
        });
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneId,
            Stamp = new TimelineStamp { ObservationOrdinal = 1, FrameOrdinal = 2, BatchOrdinal = 101 },
            Domain = ObservedEventDomain.Combat,
            SourceEntityId = 26029,
            TargetEntityId = 933,
            Raw = new RawPacketReference(0x0438, 0, 0, 1_001),
            Combat = new CombatObservation
            {
                SkillCode = 1216310,
                Damage = 108,
                HitCount = 0,
                AttemptCount = 0,
                Marker = 6,
                Type = 2,
                LayoutTag = 0
            }
        });

        var combat = Apply(journal);

        Assert.False(combat.TryGetPair(26029, 933, out _));
        Assert.Equal(0, combat.Revision);
    }

    [Fact]
    public void ScenePath_CompactType2SidecarCancelsPendingCompactEvadeInSameParentScope()
    {
        CombatResourceRegistry.SetGameResources(BuildCompactEvadeSkillMap(), new Dictionary<int, NpcCatalogEntry>());
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        var structurePath = CreateStructurePath(parentScopeId: 10, leafScopeId: 11);
        AppendCompact0438(journal, sceneId, ordinal: 0, frame: 1, batch: 100, sourceId: 26029, targetId: 933, skillCode: 1216310, marker: 6, type: 1, structurePath);
        AppendCompact0438(journal, sceneId, ordinal: 1, frame: 2, batch: 101, sourceId: 26029, targetId: 933, skillCode: 1216310, marker: 6, type: 2, structurePath);

        var combat = Apply(journal);

        Assert.False(combat.TryGetPair(26029, 933, out _));
        Assert.Equal(0, combat.Revision);
    }

    [Fact]
    public void ScenePath_CompactType2SidecarDoesNotCancelPendingCompactEvadeInDifferentParentScope()
    {
        CombatResourceRegistry.SetGameResources(BuildCompactEvadeSkillMap(), new Dictionary<int, NpcCatalogEntry>());
        var journal = new ObservedEventJournal();
        var sceneId = Guid.NewGuid();
        AppendCompact0438(journal, sceneId, ordinal: 0, frame: 1, batch: 100, sourceId: 26029, targetId: 933, skillCode: 1216310, marker: 6, type: 1, CreateStructurePath(parentScopeId: 10, leafScopeId: 11));
        AppendCompact0438(journal, sceneId, ordinal: 1, frame: 2, batch: 100, sourceId: 26029, targetId: 933, skillCode: 1216310, marker: 6, type: 2, CreateStructurePath(parentScopeId: 20, leafScopeId: 21));

        var combat = Apply(journal);

        Assert.True(combat.TryGetPair(26029, 933, out var pair));
        Assert.Equal(0, pair!.TotalDamage);
        Assert.Equal(0, pair.HitCount);
        Assert.Equal(1, pair.AttemptCount);
        Assert.Equal(1, pair.EvadeCount);
    }

    [Theory]
    [InlineData("aion2flow.stream.20260411174533.log")]
    [InlineData("aion2flow.stream.20260411215842.log")]
    [InlineData("aion2flow.stream.20260412103519.log")]
    public void ScenePath_Replay_IncomingCompactEvades_MatchesBaselinePrimary(string fileName)
    {
        CombatResourceRegistry.SetGameResources(BuildReplaySkillMap(), new Dictionary<int, NpcCatalogEntry>());
        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath($"logs/{fileName}"));

        var combat = Apply(replay.SceneJournal);
        var legacyPrimary = replay.Combatants
            .OrderByDescending(static summary => summary.IncomingEvades + summary.IncomingInvincibles)
            .ThenByDescending(static summary => summary.IncomingDamage)
            .First();

        Assert.True(combat.TryGetCombatant(legacyPrimary.CombatantId, out var scenePrimary));
        Assert.Equal(legacyPrimary.IncomingEvades, scenePrimary!.IncomingEvades);
    }

    private static CombatStore Apply(ObservedEventJournal journal)
    {
        var combat = new CombatStore();
        var applier = new DomainEventApplier(new EntityStore(), new SceneBoundaryStore(), combat);
        applier.ApplyJournal(journal);
        return combat;
    }

    private static void AppendCompact0438(ObservedEventJournal journal, Guid sceneId, long ordinal, long frame, long batch, int sourceId, int targetId, int skillCode, int marker, int type, PacketStructurePath structurePath)
    {
        journal.Append(new ObservedEventEnvelope
        {
            SceneSessionId = sceneId,
            Stamp = new TimelineStamp { ObservationOrdinal = ordinal, FrameOrdinal = frame, BatchOrdinal = batch },
            Domain = ObservedEventDomain.Combat,
            SourceEntityId = sourceId,
            TargetEntityId = targetId,
            Raw = new RawPacketReference(0x0438, 0, 0, 1_000 + ordinal, structurePath),
            Combat = new CombatObservation
            {
                SkillCode = skillCode,
                Damage = type == 2 ? 108 : 0,
                HitCount = 0,
                AttemptCount = 0,
                Marker = marker,
                Type = type,
                LayoutTag = 0
            }
        });
    }

    private static PacketStructurePath CreateStructurePath(int parentScopeId, int leafScopeId)
    {
        var root = new PacketStructureReference(PacketStructureKind.TransportPacket, parentScopeId, 0, 1, 0, 0, 64, 0, 64);
        var leaf = new PacketStructureReference(PacketStructureKind.FrameBatchEntry, leafScopeId, parentScopeId, 2, 0, 0, 32, 2, 30);
        return new PacketStructurePath(root, leaf, default, default, leaf, 2);
    }

    private static SkillCollection BuildCompactEvadeSkillMap() =>
    [
        new Skill(12000100, "Dodge", SkillCategory.Templar, SkillSourceType.PcSkill, "pc", null),
        new Skill(12160000, "Slash", SkillCategory.Templar, SkillSourceType.PcSkill, "pc", null),
        new Skill(17000100, "Dodge", SkillCategory.Cleric, SkillSourceType.PcSkill, "pc", null)
    ];

    private static SkillCollection BuildReplaySkillMap() =>
    [
        new Skill(1230000, "Fangs", SkillCategory.Npc, SkillSourceType.Unknown, "npc", null),
        new Skill(17000100, "Dodge", SkillCategory.Cleric, SkillSourceType.PcSkill, "pc", null),
        new Skill(17010230, "Earth's Retribution", SkillCategory.Cleric, SkillSourceType.PcSkill, "pc", null),
        new Skill(17730000, "Empyrean Lord's Grace", SkillCategory.Cleric, SkillSourceType.PcSkill, "pc", null)
    ];
}
