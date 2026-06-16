namespace Cloris.Aion2Flow.Tests.SceneRuntime;

public sealed class DetailProjectionAllocationGuardTests
{
    [Fact]
    public void DetailProjectionArchiveAndViewModel_DoNotMaterializeParsedCombatPacket()
    {
        var root = FindRepositoryRoot();
        var files = new[]
        {
            Path.Combine(root, "src", "Aion2Flow.SceneRuntime", "Projection", "CombatDetailEvent.cs"),
            Path.Combine(root, "src", "Aion2Flow.SceneRuntime", "Projection", "SceneCombatSnapshotAdapter.cs"),
            Path.Combine(root, "src", "Aion2Flow.SceneRuntime", "Projection", "CombatDetailSubscription.cs"),
            Path.Combine(root, "src", "Aion2Flow.SceneRuntime", "Archive", "SceneArchivePayload.cs"),
            Path.Combine(root, "src", "Aion2Flow", "ViewModels", "CombatantDetailsFlyoutViewModel.cs")
        };

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("new ParsedCombatPacket", text, StringComparison.Ordinal);
            Assert.DoesNotContain("ParsedCombatPacket", text, StringComparison.Ordinal);
            Assert.DoesNotContain(".Packet.", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void RuntimeObservationCanonicalizationAndCapture_DoNotMaterializeParsedCombatPacket()
    {
        var root = FindRepositoryRoot();
        var files = new[]
        {
            Path.Combine(root, "src", "Aion2Flow.SceneRuntime", "Canonicalization", "PeriodicPoolCanonicalizer.cs"),
            Path.Combine(root, "src", "Aion2Flow.SceneRuntime", "Canonicalization", "CompactAvoidanceCanonicalizer.cs"),
            Path.Combine(root, "src", "Aion2Flow.SceneRuntime", "Observation", "JournalingRuntimeObservationSink.cs"),
            Path.Combine(root, "src", "Aion2Flow.Capture", "Streams", "PacketCombatHandler.cs"),
            Path.Combine(root, "src", "Aion2Flow.Capture", "Diagnostics", "PacketLogReplayService.cs")
        };

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("new ParsedCombatPacket", text, StringComparison.Ordinal);
            Assert.DoesNotContain("NormalizePacketForStorage", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void PeriodicPoolCanonicalizer_DoesNotUseSkillWhitelistClassifiers()
    {
        var root = FindRepositoryRoot();
        var text = File.ReadAllText(Path.Combine(root, "src", "Aion2Flow.SceneRuntime", "Canonicalization", "PeriodicPoolCanonicalizer.cs"));

        Assert.DoesNotContain("IsKnownShield", text, StringComparison.Ordinal);
        Assert.DoesNotContain("IsKnownPeriodicHealing", text, StringComparison.Ordinal);
        Assert.DoesNotContain("MatchesExact", text, StringComparison.Ordinal);
        Assert.DoesNotContain("MatchesBase", text, StringComparison.Ordinal);
    }

    [Fact]
    public void CombatEventClassifier_UsesOnlyPacketStructureAndRelations()
    {
        var root = FindRepositoryRoot();
        var text = File.ReadAllText(Path.Combine(root, "src", "Aion2Flow.SceneRuntime", "Combat", "CombatEventClassifier.cs"));
        var forbiddenTerms = new[]
        {
            "SkillMap",
            "SkillDisplayMap",
            "DisplaySkillNameFor",
            "ResourceEffectRef",
            "observation.SkillCode",
            "BodySkillVariantRaw",
            "ParseSkillVariant",
            "InferOriginalSkillCode",
            "OriginalSkillCode",
            "BaseSkillCode",
            "ResourceSkillCode",
            "EffectIndex",
            "MatchesExact",
            "MatchesBase"
        };

        foreach (var term in forbiddenTerms)
            Assert.DoesNotContain(term, text, StringComparison.Ordinal);
    }

    [Fact]
    public void PeriodicPoolCanonicalizer_Mode10Path_UsesOnlyPacketStructure()
    {
        var root = FindRepositoryRoot();
        var text = File.ReadAllText(Path.Combine(root, "src", "Aion2Flow.SceneRuntime", "Canonicalization", "PeriodicPoolCanonicalizer.cs"));

        Assert.DoesNotContain("observation.SkillCode", text, StringComparison.Ordinal);
        Assert.DoesNotContain("OriginalSkillCode", text, StringComparison.Ordinal);
        Assert.DoesNotContain("PeriodicBodySkillCode", text, StringComparison.Ordinal);
        Assert.DoesNotContain("InferOriginalSkillCode", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ParseSkillVariant", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ObserveCompactControl0638", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ObserveResource", text, StringComparison.Ordinal);
        Assert.DoesNotContain("CurrentValue", text, StringComparison.Ordinal);
        Assert.DoesNotContain("MaximumValue", text, StringComparison.Ordinal);
        Assert.DoesNotContain("HpCorrelation", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ResourceValueCanonicalizers_UseOnlyPacketStructureAndEntityRelations()
    {
        var root = FindRepositoryRoot();
        var files = new[]
        {
            Path.Combine(root, "src", "Aion2Flow.SceneRuntime", "Canonicalization", "SystemPeriodicRecoveryCanonicalizer.cs"),
            Path.Combine(root, "src", "Aion2Flow.SceneRuntime", "Canonicalization", "OwnerTargetSummonResourceCanonicalizer.cs")
        };
        var forbiddenTerms = new[]
        {
            "OriginalSkillCode",
            "BaseSkillCode",
            "SkillCode ==",
            "ParseSkillVariant",
            "InferOriginalSkillCode",
            "EffectRef",
            "CurrentValue",
            "MaximumValue",
            "HpCorrelation"
        };

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            foreach (var term in forbiddenTerms)
                Assert.DoesNotContain(term, text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void CompactAvoidanceCanonicalizer_DoesNotUseSkillResourceClassifiers()
    {
        var root = FindRepositoryRoot();
        var text = File.ReadAllText(Path.Combine(root, "src", "Aion2Flow.SceneRuntime", "Canonicalization", "CompactAvoidanceCanonicalizer.cs"));

        Assert.DoesNotContain("ParseSkillVariant", text, StringComparison.Ordinal);
        Assert.DoesNotContain("InferOriginalSkillCode", text, StringComparison.Ordinal);
        Assert.DoesNotContain("SkillSourceType", text, StringComparison.Ordinal);
        Assert.DoesNotContain("observation.SkillCode <=", text, StringComparison.Ordinal);
        Assert.DoesNotContain("observation.SkillCode ==", text, StringComparison.Ordinal);
        Assert.DoesNotContain("pending.DisplaySkillCode ==", text, StringComparison.Ordinal);
    }

    [Fact]
    public void MultiHitCapturePath_DoesNotUseSidecarAttribution()
    {
        var root = FindRepositoryRoot();
        var files = new[]
        {
            Path.Combine(root, "src", "Aion2Flow.Capture", "Streams", "PacketCombatHandler.cs"),
            Path.Combine(root, "src", "Aion2Flow.Capture", "Streams", "PacketStateHandler.cs"),
            Path.Combine(root, "src", "Aion2Flow.Protocol", "Packets", "Packet3538SidecarParser.cs"),
            Path.Combine(root, "src", "Aion2Flow.Protocol", "Packets", "Packet8456EnvelopeParser.cs")
        };

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("DamageModifiers.MultiHit", text, StringComparison.Ordinal);
            Assert.DoesNotContain("TailMultiHitCount", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void SceneMetadataAndArchivePayload_DoNotExposeOldDisplayNameStores()
    {
        var root = FindRepositoryRoot();
        var boundaryStore = File.ReadAllText(Path.Combine(root, "src", "Aion2Flow.SceneRuntime", "Stores", "SceneBoundaryStore.cs"));
        var archivePayload = File.ReadAllText(Path.Combine(root, "src", "Aion2Flow.SceneRuntime", "Archive", "SceneArchivePayload.cs"));

        Assert.DoesNotContain("DisplayNamesByEntityId", boundaryStore, StringComparison.Ordinal);
        Assert.DoesNotContain("NpcNamesByCode", boundaryStore, StringComparison.Ordinal);
        Assert.DoesNotContain("TryGetDisplayName", boundaryStore, StringComparison.Ordinal);
        Assert.DoesNotContain("TryGetNpcName", boundaryStore, StringComparison.Ordinal);
        Assert.DoesNotContain("public IReadOnlyDictionary<int, string> DisplayNames", archivePayload, StringComparison.Ordinal);
        Assert.DoesNotContain("NpcNamesByCode", archivePayload, StringComparison.Ordinal);
    }

    [Fact]
    public void PlaybackCheckpoints_DoNotStoreProjectionOrStoreSnapshots()
    {
        var root = FindRepositoryRoot();
        var checkpoint = File.ReadAllText(Path.Combine(root, "src", "Aion2Flow.SceneRuntime", "Playback", "ScenePlaybackCheckpoint.cs"));
        var session = File.ReadAllText(Path.Combine(root, "src", "Aion2Flow.SceneRuntime", "Playback", "ScenePlaybackSession.cs"));
        var forbiddenTerms = new[]
        {
            "ScenePlaybackProjectionSnapshot",
            "EntityStoreSnapshot",
            "SceneBoundaryStoreSnapshot",
            "RuntimeMetadataRegistrySnapshot",
            "CombatStoreSnapshot",
            "DomainEventApplierSnapshot",
            "SceneCombatSnapshotAdapterSnapshot",
            "checkpoint.Projection"
        };

        foreach (var term in forbiddenTerms)
        {
            Assert.DoesNotContain(term, checkpoint, StringComparison.Ordinal);
            Assert.DoesNotContain(term, session, StringComparison.Ordinal);
        }

        var checkpointType = typeof(Cloris.Aion2Flow.SceneRuntime.Playback.ScenePlaybackCheckpoint);
        var propertyNames = checkpointType.GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.DeclaredOnly)
            .Select(static property => property.Name)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();
        var fieldCount = checkpointType.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly).Length;

        Assert.Equal(["JournalCursor", "PositionMilliseconds"], propertyNames);
        Assert.Equal(2, fieldCount);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Aion2Flow.slnx")))
            directory = directory.Parent;

        return directory!.FullName;
    }
}
