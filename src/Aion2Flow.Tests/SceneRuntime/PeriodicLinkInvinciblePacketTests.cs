using Cloris.Aion2Flow.Capture.Diagnostics;
using Cloris.Aion2Flow.Capture.Streams;
using Cloris.Aion2Flow.Resources;
using Cloris.Aion2Flow.SceneRuntime.Journal;
using Cloris.Aion2Flow.SceneRuntime.Observation;
using Cloris.Aion2Flow.SceneRuntime.Runtime;
using Cloris.Aion2Flow.SceneRuntime.Stores;
using Cloris.Aion2Flow.Tests.Protocol;

namespace Cloris.Aion2Flow.Tests.SceneRuntime;

public class PeriodicLinkInvinciblePacketTests
{
    private static readonly TcpConnection TestConnection = new(0x0100007f, 0x0100007f, 49820, 57080);

    [Fact]
    public void StreamMode48PeriodicLink_CreatesPacketAuthoritativeInvincible()
    {
        CombatResourceRegistry.SetGameResources(BuildSkillMap(), new Dictionary<int, NpcCatalogEntry>());
        var journal = new ObservedEventJournal();
        var sink = new JournalingRuntimeObservationSink(journal, new SceneRuntimeClock(0), Guid.NewGuid());
        using var processor = new PacketStreamProcessor(sink);

        var parsed = processor.AppendAndProcess(HexHelper.FromFixture("combat/0538-mode48-link.hex"), TestConnection);

        Assert.True(parsed);
        var entry = journal.Read(0);
        var observation = entry.Combat!.Value;
        Assert.Equal(0x0538, entry.Raw.Opcode);
        Assert.Equal(29240, entry.SourceEntityId);
        Assert.Equal(16047, entry.TargetEntityId);
        Assert.Equal(1237540, observation.OriginalSkillCode);
        Assert.Equal(1230000, observation.SkillCode);
        Assert.Equal(29240, observation.DetailRaw);
        Assert.Equal(608, observation.Marker);
        Assert.Equal(48, observation.Type);
        Assert.Equal(0, observation.HitCount);
        Assert.Equal(1, observation.AttemptCount);
        Assert.Equal(DamageModifiers.Invincible, observation.Modifiers & DamageModifiers.Invincible);
        Assert.Equal(PacketEffectTag.PeriodicLinkInvincible, observation.EffectTag);
    }

    [Fact]
    public void ScenePath_StreamMode48PeriodicLink_ProjectsInvincibleTotals()
    {
        CombatResourceRegistry.SetGameResources(BuildSkillMap(), new Dictionary<int, NpcCatalogEntry>());
        var journal = new ObservedEventJournal();
        var sink = new JournalingRuntimeObservationSink(journal, new SceneRuntimeClock(0), Guid.NewGuid());
        using var processor = new PacketStreamProcessor(sink);

        Assert.True(processor.AppendAndProcess(HexHelper.FromFixture("combat/0538-mode48-link.hex"), TestConnection));
        var combat = Apply(journal);

        Assert.True(combat.TryGetPair(29240, 16047, out var pair));
        Assert.Equal(0, pair!.TotalDamage);
        Assert.Equal(0, pair.HitCount);
        Assert.Equal(1, pair.AttemptCount);
        Assert.Equal(1, pair.InvincibleCount);
        Assert.Equal(1230000, pair.LastSkillCode);
        Assert.True(combat.TryGetCombatant(29240, out var source));
        Assert.True(combat.TryGetCombatant(16047, out var target));
        Assert.Equal(1, source!.OutgoingInvincibles);
        Assert.Equal(1, target!.IncomingInvincibles);
    }

    [Fact]
    public void StreamMode56ActiveSkill_CreatesPacketAuthoritativeInvincible()
    {
        CombatResourceRegistry.SetGameResources(BuildSkillMap(), new Dictionary<int, NpcCatalogEntry>());
        var journal = new ObservedEventJournal();
        var sink = new JournalingRuntimeObservationSink(journal, new SceneRuntimeClock(0), Guid.NewGuid());
        using var processor = new PacketStreamProcessor(sink);

        var parsed = processor.AppendAndProcess(HexHelper.FromFixture("combat/0538-mode56-rescue-invincible.hex"), TestConnection);

        Assert.True(parsed);
        var entry = journal.Read(0);
        var observation = entry.Combat!.Value;
        Assert.Equal(0x0538, entry.Raw.Opcode);
        Assert.Equal(12509, entry.SourceEntityId);
        Assert.Equal(12509, entry.TargetEntityId);
        Assert.Equal(1727002011, observation.OriginalSkillCode);
        Assert.Equal(17270020, observation.SkillCode);
        Assert.Equal(24, observation.DetailRaw);
        Assert.Equal(1022, observation.Marker);
        Assert.Equal(56, observation.Type);
        Assert.Equal(0, observation.HitCount);
        Assert.Equal(1, observation.AttemptCount);
        Assert.Equal(0, observation.Damage);
        Assert.Equal(DamageModifiers.Invincible, observation.Modifiers & DamageModifiers.Invincible);
        Assert.Equal(PacketEffectTag.ActiveSkillInvincible, observation.EffectTag);
    }

    [Theory]
    [InlineData(1337004013u, 13370040)]
    [InlineData(1739004311u, 17390043)]
    public void StreamMode56MultiEffectSkills_CreatePacketAuthoritativeInvincible(uint rawSkillCode, int expectedSkillCode)
    {
        CombatResourceRegistry.SetGameResources(BuildSkillMap(), new Dictionary<int, NpcCatalogEntry>());
        var journal = new ObservedEventJournal();
        var sink = new JournalingRuntimeObservationSink(journal, new SceneRuntimeClock(0), Guid.NewGuid());
        using var processor = new PacketStreamProcessor(sink);

        var packet = HexHelper.FromFixture("combat/0538-mode56-rescue-invincible.hex");
        ReplaceUInt32(packet, 1727002011u, rawSkillCode);

        Assert.True(processor.AppendAndProcess(packet, TestConnection));
        var observation = journal.Read(0).Combat!.Value;

        Assert.Equal((int)rawSkillCode, observation.OriginalSkillCode);
        Assert.Equal(expectedSkillCode, observation.SkillCode);
        Assert.Equal(56, observation.Type);
        Assert.Equal(0, observation.Damage);
        Assert.Equal(1, observation.AttemptCount);
        Assert.Equal(DamageModifiers.Invincible, observation.Modifiers & DamageModifiers.Invincible);
        Assert.Equal(PacketEffectTag.ActiveSkillInvincible, observation.EffectTag);
    }

    [Fact]
    public void ScenePath_StreamMode56ActiveSkill_ProjectsInvincibleTotals()
    {
        CombatResourceRegistry.SetGameResources(BuildSkillMap(), new Dictionary<int, NpcCatalogEntry>());
        var journal = new ObservedEventJournal();
        var sink = new JournalingRuntimeObservationSink(journal, new SceneRuntimeClock(0), Guid.NewGuid());
        using var processor = new PacketStreamProcessor(sink);

        Assert.True(processor.AppendAndProcess(HexHelper.FromFixture("combat/0538-mode56-rescue-invincible.hex"), TestConnection));
        var combat = Apply(journal);

        Assert.True(combat.TryGetPair(12509, 12509, out var pair));
        Assert.Equal(0, pair!.TotalDamage);
        Assert.Equal(0, pair.HitCount);
        Assert.Equal(1, pair.AttemptCount);
        Assert.Equal(1, pair.InvincibleCount);
        Assert.Equal(17270020, pair.LastSkillCode);
        Assert.True(combat.TryGetCombatant(12509, out var player));
        Assert.Equal(1, player!.IncomingInvincibles);
    }

    [Fact]
    public void ScenePath_Replay_PeriodicLinkInvincibles_AreProjected()
    {
        CombatResourceRegistry.SetGameResources(ResourceDatabase.LoadCombatSkills(), new Dictionary<int, NpcCatalogEntry>());

        var replay = PacketLogReplayService.Replay(FixtureHelper.GetPath("logs/aion2flow.stream.20260412103519.log"));

        var combat = Apply(replay.SceneJournal);
        var pairs = combat.Pairs.Values
            .Where(static pair => pair.InvincibleCount > 0)
            .ToArray();

        Assert.NotEmpty(pairs);
        Assert.Contains(pairs, static pair => pair.AttemptCount >= pair.InvincibleCount);
        Assert.DoesNotContain(SceneReplayTestView.Packets(replay), static packet => packet.EffectTag.ToString().Contains("2C38", StringComparison.Ordinal));
    }

    private static CombatStore Apply(ObservedEventJournal journal)
    {
        var combat = new CombatStore();
        var applier = new DomainEventApplier(new EntityStore(), new SceneBoundaryStore(), combat);
        applier.ApplyJournal(journal);
        return combat;
    }

    private static void ReplaceUInt32(byte[] packet, uint oldValue, uint newValue)
    {
        Span<byte> oldBytes = stackalloc byte[4];
        Span<byte> newBytes = stackalloc byte[4];
        BitConverter.TryWriteBytes(oldBytes, oldValue);
        BitConverter.TryWriteBytes(newBytes, newValue);
        var index = packet.AsSpan().IndexOf(oldBytes);
        Assert.True(index >= 0);
        newBytes.CopyTo(packet.AsSpan(index, 4));
    }

    private static SkillCollection BuildSkillMap() =>
    [
        new Skill(1230000, "Fangs", SkillCategory.Npc, SkillSourceType.Unknown, "npc", null),
        new Skill(11800008, "Buff Tick", SkillCategory.Npc, SkillSourceType.Unknown, "npc", null),
        new Skill(17270020, "Rescue", SkillCategory.Cleric, SkillSourceType.PcSkill, "skill", null),
        new Skill(13370040, "Evasion Contract", SkillCategory.Assassin, SkillSourceType.PcSkill, "skill", null),
        new Skill(17390043, "Summon Resurrection", SkillCategory.Cleric, SkillSourceType.PcSkill, "skill", null)
    ];
}
