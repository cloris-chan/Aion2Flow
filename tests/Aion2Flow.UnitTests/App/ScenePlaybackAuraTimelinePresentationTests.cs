using Cloris.Aion2Flow.Protocol.Combat;
using Cloris.Aion2Flow.Presentation;
using Cloris.Aion2Flow.Resources.Catalog;
using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Identity;
using Cloris.Aion2Flow.SceneRuntime.Playback;
using Cloris.Aion2Flow.SceneRuntime.Stores;
using Cloris.Aion2Flow.Services;
using Cloris.Aion2Flow.ViewModels;

namespace Cloris.Aion2Flow.Tests.App;

public sealed class ScenePlaybackAuraTimelinePresentationTests
{
    [Fact]
    public void BuildAuraTimelineTracks_PreservesAndDisplaysExactSemanticEvidence()
    {
        const uint resourceEffectRefRaw = 174_200_101;
        var semanticFacets = SkillSemanticValue.Classified(auraFacets: SkillAuraFacet.Buff);
        var semantics = new AuraSemanticValue(
            AuraDisposition.Buff,
            new AuraSemanticTrace(
                AuraSemanticMatchKind.ExactNode,
                semanticFacets,
                semanticFacets,
                ResourceEffectRef.FromRaw(resourceEffectRefRaw),
                SkillSemanticResourceNodeKind.SkillAbnormal,
                unchecked((int)resourceEffectRefRaw),
                0,
                -1,
                1));
        var timeline = CreateTimeline(resourceEffectRefRaw, semantics);

        var lane = Assert.Single(BuildLanes(timeline));

        Assert.Equal(semantics, lane.Semantics);
        Assert.Equal(AuraDisposition.Buff, lane.Disposition);
        Assert.Equal(semantics.Trace, lane.SemanticTrace);
        Assert.Equal("Buff", lane.DispositionText);
        Assert.Equal($"Exact node #{resourceEffectRefRaw}", lane.SemanticTraceText);
    }

    [Fact]
    public void BuildAuraTimelineTracks_MakesAmbiguousUnknownEvidenceVisible()
    {
        const uint resourceEffectRefRaw = 1_120_000_020;
        var semantics = new AuraSemanticValue(
            AuraDisposition.Unknown,
            new AuraSemanticTrace(
                AuraSemanticMatchKind.None,
                default,
                default,
                ResourceEffectRef.FromRaw(resourceEffectRefRaw),
                SkillSemanticResourceNodeKind.SkillEffect,
                unchecked((int)resourceEffectRefRaw),
                0,
                -1,
                3));
        var timeline = CreateTimeline(resourceEffectRefRaw, semantics);

        var lane = Assert.Single(BuildLanes(timeline));

        Assert.Equal(semantics, lane.Semantics);
        Assert.Equal(AuraDisposition.Unknown, lane.Disposition);
        Assert.Equal("Unknown", lane.DispositionText);
        Assert.Equal("Ambiguous: 3 candidates", lane.SemanticTraceText);
    }

    [Fact]
    public void AuraTimelineLayout_BindsVisibleDispositionAndSemanticTraceText()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "Aion2Flow", "Views", "ScenePlaybackWindow.axaml"));

        Assert.Contains("Text=\"{Binding DispositionText}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding SemanticTraceText}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ToolTip.Tip=\"{Binding SemanticTraceText}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Playback_AuraDisposition_Label", xaml, StringComparison.Ordinal);
    }

    private static ScenePlaybackAuraTimeline CreateTimeline(uint resourceEffectRefRaw, AuraSemanticValue semantics)
    {
        var resourceEffectRef = ResourceEffectRef.FromRaw(resourceEffectRefRaw);
        return new ScenePlaybackAuraTimeline(
            [new ScenePlaybackAuraCoverage(200, 100, 7, resourceEffectRef, semantics, 0, 800)],
            [new ScenePlaybackAuraApplication(200, 100, 7, resourceEffectRef, semantics, 0, AuraLifecycleEventKind.Open)]);
    }

    private static PlaybackAuraTimelineLane[] BuildLanes(ScenePlaybackAuraTimeline timeline)
    {
        var language = new LanguageService();
        language.SetLanguage(LanguageService.English);
        using var localization = new LocalizationService(language);
        using var resources = new GameResourceService(language);
        var displayContext = new SceneDisplayContext(SceneIdentityScope.Empty, null, null, resources, "Unknown");
        return ScenePlaybackTimelineBuilder.BuildAuraTimelineTracks(timeline, 1_000, localization, displayContext);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Aion2Flow.slnx")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the Aion2Flow repository root.");
    }
}
