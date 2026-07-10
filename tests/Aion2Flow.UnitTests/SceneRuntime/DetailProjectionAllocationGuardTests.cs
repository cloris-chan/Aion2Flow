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
    public void DetailProjectionAndViewModel_UseCombatEventContributionContext()
    {
        var root = FindRepositoryRoot();
        var files = new[]
        {
            Path.Combine(root, "src", "Aion2Flow.SceneRuntime", "Projection", "CombatDetailEvent.cs"),
            Path.Combine(root, "src", "Aion2Flow.SceneRuntime", "Projection", "SceneCombatSnapshotAdapter.cs"),
            Path.Combine(root, "src", "Aion2Flow", "ViewModels", "CombatantDetailsFlyoutViewModel.cs")
        };

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("CombatEventKey.FromObservation", text, StringComparison.Ordinal);
            Assert.DoesNotContain("CombatContributionClassifier.Evaluate", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void DetailProjection_DoesNotPreallocateSelectedCombatantDetailsFromAllCombatEvents()
    {
        var root = FindRepositoryRoot();
        var subscription = File.ReadAllText(Path.Combine(root, "src", "Aion2Flow.SceneRuntime", "Projection", "CombatDetailSubscription.cs"));
        var adapter = File.ReadAllText(Path.Combine(root, "src", "Aion2Flow.SceneRuntime", "Projection", "SceneCombatSnapshotAdapter.cs"));
        var update = File.ReadAllText(Path.Combine(root, "src", "Aion2Flow.SceneRuntime", "Projection", "CombatDetailUpdate.cs"));

        Assert.DoesNotContain("EnsureCapacity(store.Events.Count)", subscription, StringComparison.Ordinal);
        Assert.DoesNotContain("new List<CombatDetailEvent>(combat.Events.Count)", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("EnsureCapacity(int capacity)", update, StringComparison.Ordinal);
    }

    [Fact]
    public void DetailSkillTables_ExposeEventCountsOnly()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "Aion2Flow", "Views", "CombatDirectionDetailView.axaml"));
        var sectionViewModel = File.ReadAllText(Path.Combine(root, "src", "Aion2Flow", "ViewModels", "SkillDetailSectionViewModel.cs"));
        var flyoutViewModel = File.ReadAllText(Path.Combine(root, "src", "Aion2Flow", "ViewModels", "CombatantDetailsFlyoutViewModel.cs"));

        Assert.Equal(3, CountOccurrences(xaml, "{markups:Translate Column_Events}"));
        Assert.Equal(3, CountOccurrences(xaml, "Value=\"{Binding EventCount}\""));
        Assert.Equal(3, CountOccurrences(xaml, "Tapped=\"SkillDetailRowTapped\""));
        Assert.Equal(3, CountOccurrences(xaml, "Classes.selected=\"{Binding IsSelected}\""));
        Assert.DoesNotContain("Column_" + "Actions", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Grouped" + "Action" + "Count", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Action" + "Count", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Contribution" + "Count", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("SkillDetail" + "ActionDetailsView", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Debug" + "Action", sectionViewModel, StringComparison.Ordinal);
        Assert.DoesNotContain("Debug" + "Action", flyoutViewModel, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(root, "src", "Aion2Flow", "Views", "SkillDetail" + "ActionDetailsView.axaml")));

        foreach (var localeFile in new[] { "Strings.resx", "Strings.zh-TW.resx", "Strings.ko-KR.resx" })
        {
            var resources = File.ReadAllText(Path.Combine(root, "src", "Aion2Flow", "Localization", localeFile));
            Assert.Contains("name=\"Column_Events\"", resources, StringComparison.Ordinal);
            Assert.DoesNotContain("name=\"Column_" + "Actions\"", resources, StringComparison.Ordinal);
            Assert.DoesNotContain("name=\"Column_Grouped" + "Actions\"", resources, StringComparison.Ordinal);
            Assert.DoesNotContain("name=\"Column_" + "Contributions\"", resources, StringComparison.Ordinal);
            Assert.DoesNotContain("name=\"Detail_Combat" + "Actions\"", resources, StringComparison.Ordinal);
            Assert.DoesNotContain("name=\"Detail_" + "Action" + "Contributions\"", resources, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void CombatantDetailsFlyout_BuildsCounterpartOptionsWithSingleDetailEventScan()
    {
        var root = FindRepositoryRoot();
        var flyoutText = File.ReadAllText(Path.Combine(root, "src", "Aion2Flow", "ViewModels", "CombatantDetailsFlyoutViewModel.cs"));
        var builderText = File.ReadAllText(Path.Combine(root, "src", "Aion2Flow", "ViewModels", "DetailCounterpartOptionBuilder.cs"));
        var rebuildBlock = ExtractSourceBlock(
            flyoutText,
            "    private void RebuildCounterpartSelections()",
            "    private void RefreshAllSections()");
        var accumulateBlock = ExtractSourceBlock(
            builderText,
            "    public void Accumulate(",
            "    public IReadOnlyCollection<DetailCounterpartOption> BuildOutgoingDamageOptions(");

        Assert.Contains("_counterpartOptionBuilder.Accumulate(", rebuildBlock, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(accumulateBlock, "foreach (ref readonly var detailEvent in detailEvents)"));
        Assert.DoesNotContain("BuildCounterpartOptions(DetailSectionKind", flyoutText, StringComparison.Ordinal);
    }

    [Fact]
    public void CombatantDetailsFlyout_RefreshesDirectionSectionsWithSingleDetailEventScan()
    {
        var root = FindRepositoryRoot();
        var text = File.ReadAllText(Path.Combine(root, "src", "Aion2Flow", "ViewModels", "CombatantDetailsFlyoutViewModel.cs"));
        var refreshDirectionBlock = ExtractSourceBlock(
            text,
            "    private void RefreshDirection(",
            "    private static void AccumulateSection(");

        Assert.Equal(1, CountOccurrences(refreshDirectionBlock, "foreach (ref readonly var detailPacket in packetsSpan)"));
        Assert.DoesNotContain("private void RefreshSection(", text, StringComparison.Ordinal);
    }

    [Fact]
    public void CombatantDetailsFlyout_RefreshesAllSectionsWithSingleDetailEventScan()
    {
        var root = FindRepositoryRoot();
        var text = File.ReadAllText(Path.Combine(root, "src", "Aion2Flow", "ViewModels", "CombatantDetailsFlyoutViewModel.cs"));
        var refreshAllBlock = ExtractSourceBlock(
            text,
            "    private void RefreshAllSections()",
            "    private void RefreshDirection(");

        Assert.Equal(1, CountOccurrences(refreshAllBlock, "foreach (ref readonly var detailPacket in packetsSpan)"));
        Assert.DoesNotContain("RefreshDirection(", refreshAllBlock, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeObservationCanonicalizationAndCapture_DoNotMaterializeParsedCombatPacket()
    {
        var root = FindRepositoryRoot();
        var files = EnumerateCanonicalizationFiles(root).Concat([
            Path.Combine(root, "src", "Aion2Flow.SceneRuntime", "Observation", "JournalingRuntimeObservationSink.cs"),
            Path.Combine(root, "src", "Aion2Flow.Capture", "Streams", "PacketCombatHandler.cs"),
            Path.Combine(root, "src", "Aion2Flow.Capture", "Diagnostics", "PacketLogReplayService.cs")
        ]).ToArray();

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
    public void CombatEventClassifier_UsesPacketFactsAndStructuredSemanticsWithoutDisplayHeuristics()
    {
        var root = FindRepositoryRoot();
        var text = File.ReadAllText(Path.Combine(root, "src", "Aion2Flow.SceneRuntime", "Combat", "CombatEventClassifier.cs"));
        var forbiddenTerms = new[]
        {
            "SkillMap",
            "SkillDisplayMap",
            "DisplaySkillNameFor",
            "SkillClientMetadata",
            "Localization",
            "Icon",
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

        Assert.Contains("CombatSemanticResolution", text, StringComparison.Ordinal);
        Assert.Contains("TryResolveDirectCombatResourceSemantics", text, StringComparison.Ordinal);
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
    public void CombatEventPath_DoesNotUseOrdinalContextKeys()
    {
        var root = FindRepositoryRoot();
        var files = new[]
        {
            Path.Combine(root, "src", "Aion2Flow.SceneRuntime", "Stores", "CombatStore.cs"),
            Path.Combine(root, "src", "Aion2Flow.SceneRuntime", "Stores", "DomainEventApplier.cs"),
            Path.Combine(root, "src", "Aion2Flow.SceneRuntime", "Stores", "CombatPacketContextReader.cs")
        }.Concat(EnumerateCanonicalizationFiles(root)).ToArray();

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("FrameOrdinal", text, StringComparison.Ordinal);
            Assert.DoesNotContain("BatchOrdinal", text, StringComparison.Ordinal);
        }

        var applierText = File.ReadAllText(Path.Combine(root, "src", "Aion2Flow.SceneRuntime", "Stores", "DomainEventApplier.cs"));
        Assert.DoesNotContain("LastObservationOrdinal", applierText, StringComparison.Ordinal);
        Assert.DoesNotContain("ObservationOrdinal + 1", applierText, StringComparison.Ordinal);
    }

    [Fact]
    public void PacketContextClassification_UsesOnlyRawPacketStructure()
    {
        var root = FindRepositoryRoot();
        var text = File.ReadAllText(Path.Combine(root, "src", "Aion2Flow.SceneRuntime", "Stores", "CombatPacketContextReader.cs"));
        var classifier = ExtractSourceBlock(
            text,
            "internal static CombatPacketEvidenceKind ClassifyPacketEvidence(",
            "internal static bool HasPacketContext(");
        var forbiddenTerms = new[]
        {
            "CombatEventKey",
            "SourceObservationOrdinal",
            "sourceObservationOrdinal",
            "SkillCode",
            "CombatObservation",
            "ObservedAtMilliseconds",
            "sourceId",
            "targetId",
            "BodyResourceEffectRef",
            "DetailResourceEffectRef",
            "ResourceEffectRef",
            "BaseSkill",
            "Godstone",
            "SkillDisplay",
            "DisplaySkill",
            "Skill.dat",
            "l10n",
            "FrameOrdinal",
            "BatchOrdinal",
            "FlushId"
        };

        Assert.Contains("raw.Opcode switch", classifier, StringComparison.Ordinal);
        Assert.Contains("Control0238", classifier, StringComparison.Ordinal);
        Assert.Contains("Control0638", classifier, StringComparison.Ordinal);
        Assert.Contains("Value0438", classifier, StringComparison.Ordinal);
        Assert.Contains("Effect0538", classifier, StringComparison.Ordinal);

        foreach (var term in forbiddenTerms)
            Assert.DoesNotContain(term, classifier, StringComparison.Ordinal);
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

    private static IEnumerable<string> EnumerateCanonicalizationFiles(string root)
    {
        var canonicalizationRoot = Path.Combine(root, "src", "Aion2Flow.SceneRuntime", "Canonicalization");
        return Directory.EnumerateFiles(canonicalizationRoot, "*.cs", SearchOption.TopDirectoryOnly)
            .OrderBy(static path => path, StringComparer.Ordinal);
    }

    private static string ExtractSourceBlock(string text, string startToken, string endToken)
    {
        var start = text.IndexOf(startToken, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing source start token: {startToken}");
        var end = text.IndexOf(endToken, start + startToken.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"Missing source end token: {endToken}");
        return text[start..end];
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var start = 0;
        while (true)
        {
            var index = text.IndexOf(value, start, StringComparison.Ordinal);
            if (index < 0)
                return count;

            count++;
            start = index + value.Length;
        }
    }
}
