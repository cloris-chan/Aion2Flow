using System.Globalization;
using Avalonia.Media;
using Cloris.Aion2Flow.Presentation;
using Cloris.Aion2Flow.Protocol.Combat;
using Cloris.Aion2Flow.SceneRuntime.Playback;
using Cloris.Aion2Flow.SceneRuntime.Stores;
using Cloris.Aion2Flow.Services;

namespace Cloris.Aion2Flow.ViewModels;

internal static class ScenePlaybackTimelineBuilder
{
    private const int MaxTimelineMarkersPerTrack = 256;
    private const int MaxCombatantTimelineMarkersPerTrack = 96;

    private static readonly ScenePlaybackTrack[] TrackOrder =
    [
        ScenePlaybackTrack.Combat,
        ScenePlaybackTrack.Mechanic,
        ScenePlaybackTrack.Resource,
        ScenePlaybackTrack.EntityVital,
        ScenePlaybackTrack.Aura,
        ScenePlaybackTrack.State,
        ScenePlaybackTrack.Scene,
        ScenePlaybackTrack.Action,
        ScenePlaybackTrack.Diagnostic,
        ScenePlaybackTrack.Other
    ];

    private static readonly IBrush CombatBrush = Brush.Parse("#18D7F4");
    private static readonly IBrush MechanicBrush = Brush.Parse("#FFD166");
    private static readonly IBrush ResourceBrush = Brush.Parse("#55D6BE");
    public static readonly IBrush EntityVitalBrush = Brush.Parse("#8CE271");
    private static readonly IBrush AuraBrush = Brush.Parse("#C98EFF");
    private static readonly IBrush StateBrush = Brush.Parse("#FFD166");
    private static readonly IBrush SceneBrush = Brush.Parse("#FF8A65");
    private static readonly IBrush ActionBrush = Brush.Parse("#65A7FF");
    private static readonly IBrush DiagnosticBrush = Brush.Parse("#9AA8B4");
    private static readonly IBrush OtherBrush = Brush.Parse("#D4DCE5");
    private static readonly IBrush[] AuraAccentBrushes =
    [
        Brush.Parse("#22D3EE"),
        Brush.Parse("#89D66B"),
        Brush.Parse("#FFD166"),
        Brush.Parse("#C98EFF"),
        Brush.Parse("#FF8A65"),
        Brush.Parse("#65A7FF")
    ];
    private static readonly IBrush[] AuraFillBrushes =
    [
        Brush.Parse("#4022D3EE"),
        Brush.Parse("#4089D66B"),
        Brush.Parse("#40FFD166"),
        Brush.Parse("#40C98EFF"),
        Brush.Parse("#40FF8A65"),
        Brush.Parse("#4065A7FF")
    ];

    public static PlaybackTimelineBuildResult? TryBuildTimelineStrips(
        ScenePlaybackTrackMarkerWindow window,
        PlaybackTimelineViewport viewport,
        Func<ScenePlaybackTrackMarker, string> createMarkerText,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return null;

        try
        {
            return BuildTimelineStripsCore(window, viewport, createMarkerText, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private static PlaybackTimelineBuildResult BuildTimelineStripsCore(
        ScenePlaybackTrackMarkerWindow window,
        PlaybackTimelineViewport viewport,
        Func<ScenePlaybackTrackMarker, string> createMarkerText,
        CancellationToken cancellationToken)
    {
        if (window.Count == 0 || viewport.IsEmpty)
            return new PlaybackTimelineBuildResult(PlaybackTimelineStrip.Empty, []);

        var startMilliseconds = (long)Math.Floor(viewport.StartMilliseconds);
        var endMilliseconds = (long)Math.Ceiling(viewport.EndMilliseconds);
        var read = ScenePlaybackTrackReader.SampleTimeline(
            window.AsSpan(),
            startMilliseconds,
            endMilliseconds,
            MaxTimelineMarkersPerTrack,
            MaxCombatantTimelineMarkersPerTrack,
            cancellationToken);
        var global = CreateTimelineStrip(CreateTimelineGroups(read.Global.Samples, createMarkerText), read.Global.TrackCounts);
        var combatants = new Dictionary<int, PlaybackTimelineStrip>(read.Combatants.Combatants.Count);
        for (var i = 0; i < read.Combatants.Combatants.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var samples = read.Combatants.Combatants[i];
            var strip = CreateTimelineStrip(CreateTimelineGroups(samples.Samples, createMarkerText), samples.TrackCounts);
            if (strip.Bands.Count > 0)
                combatants[samples.CombatantId] = strip;
        }

        return new PlaybackTimelineBuildResult(global, combatants);
    }

    public static PlaybackAuraTimelineLane[] BuildAuraTimelineTracks(ScenePlaybackAuraTimeline timeline, long durationMilliseconds, LocalizationService localization, SceneDisplayContext displayContext)
    {
        if (timeline.Coverages.Count == 0 && timeline.Applications.Count == 0)
            return [];

        var groups = new Dictionary<ScenePlaybackAuraIdentity, AuraTimelineLaneBuilder>();
        for (var i = 0; i < timeline.Coverages.Count; i++)
        {
            var coverage = timeline.Coverages[i];
            var identity = ScenePlaybackAuraIdentity.Create(coverage.DisplayResourceEffectRef, coverage.InstanceSequenceId);
            var builder = GetAuraTimelineBuilder(groups, identity);
            builder.ApplySemantics(coverage.Semantics);
            builder.Coverages.Add((coverage.StartMilliseconds, coverage.EndMilliseconds));
        }

        for (var i = 0; i < timeline.Applications.Count; i++)
        {
            var application = timeline.Applications[i];
            var identity = ScenePlaybackAuraIdentity.Create(application.DisplayResourceEffectRef, application.InstanceSequenceId);
            var builder = GetAuraTimelineBuilder(groups, identity);
            builder.ApplySemantics(application.Semantics);
            builder.Applications.Add((application.PositionMilliseconds, application.Kind));
        }

        var result = new List<PlaybackAuraTimelineLane>(groups.Count);
        foreach (var builder in groups.Values)
        {
            var displayResourceEffectRefRaw = builder.DisplayResourceEffectRef.RawId;
            var paletteIndex = ResolveAuraPaletteIndex(displayResourceEffectRefRaw, builder.InstanceSequenceId);
            var accent = AuraAccentBrushes[paletteIndex];
            var fill = AuraFillBrushes[paletteIndex];
            var fallback = displayResourceEffectRefRaw > 0
                ? displayResourceEffectRefRaw.ToString(CultureInfo.InvariantCulture)
                : string.Format(CultureInfo.CurrentCulture, localization["Playback_AuraUnknownFormat"], builder.InstanceSequenceId);
            var markers = new PlaybackTimelineMarker[builder.Applications.Count];
            builder.Applications.Sort(static (left, right) => left.PositionMilliseconds.CompareTo(right.PositionMilliseconds));
            for (var i = 0; i < builder.Applications.Count; i++)
            {
                var application = builder.Applications[i];
                var text = application.Kind == AuraLifecycleEventKind.Renew
                    ? localization["Playback_Lifecycle_Renew"]
                    : localization["Playback_Lifecycle_OpenIndefinite"];
                markers[i] = new PlaybackTimelineMarker(application.PositionMilliseconds, 16d, accent, text, IsApplication: true);
            }

            var spans = MergeAuraCoverages(builder.Coverages, fill, accent);
            var activeMilliseconds = SumSpanDuration(spans);
            var coverage = durationMilliseconds > 0 ? activeMilliseconds / (double)durationMilliseconds : 0d;
            var semantics = builder.Semantics;
            result.Add(new PlaybackAuraTimelineLane(
                builder.Identity,
                fallback,
                markers,
                spans,
                builder.Applications.Count,
                coverage.ToString("P1", CultureInfo.CurrentCulture),
                FormatDuration(activeMilliseconds),
                semantics,
                FormatAuraDisposition(semantics.Disposition, localization),
                FormatAuraSemanticTrace(semantics.Trace, localization)));
        }

        result.Sort((left, right) =>
        {
            var leftName = left.SkillCode > 0 ? displayContext.ResolveSkillName(left.SkillCode) : left.FallbackText;
            var rightName = right.SkillCode > 0 ? displayContext.ResolveSkillName(right.SkillCode) : right.FallbackText;
            return string.Compare(leftName, rightName, StringComparison.CurrentCulture);
        });
        return result.ToArray();
    }

    private static Dictionary<ScenePlaybackTrack, List<PlaybackTimelineMarker>> CreateTimelineGroups(IReadOnlyList<ScenePlaybackTrackSample> samples, Func<ScenePlaybackTrackMarker, string> createMarkerText)
    {
        var groups = new Dictionary<ScenePlaybackTrack, List<PlaybackTimelineMarker>>();
        for (var i = 0; i < samples.Count; i++)
        {
            var sample = samples[i];
            var marker = sample.Marker;
            var track = marker.Track;
            if (!groups.TryGetValue(track, out var markers))
            {
                markers = [];
                groups.Add(track, markers);
            }

            markers.Add(new PlaybackTimelineMarker(marker.PositionMilliseconds, ResolveMarkerWeight(marker, sample.EventCount), ResolveTrackBrush(track), createMarkerText(marker)));
        }

        return groups;
    }

    private static PlaybackTimelineStrip CreateTimelineStrip(Dictionary<ScenePlaybackTrack, List<PlaybackTimelineMarker>> groups, IReadOnlyList<ScenePlaybackTrackCount> trackCounts)
    {
        var bands = new List<PlaybackTimelineBand>(TrackOrder.Length);
        foreach (var track in TrackOrder)
        {
            groups.TryGetValue(track, out var markers);
            if (markers is null || markers.Count == 0)
                continue;

            var trackCount = trackCounts.Count == 0 ? markers.Count : ResolveTrackCount(trackCounts, track);
            bands.Add(new PlaybackTimelineBand(track, ResolveTrackBrush(track), markers, trackCount));
        }

        var totalCount = 0;
        for (var i = 0; i < bands.Count; i++)
            totalCount += bands[i].Count;

        return bands.Count == 0 ? PlaybackTimelineStrip.Empty : new PlaybackTimelineStrip(bands, totalCount);
    }

    private static AuraTimelineLaneBuilder GetAuraTimelineBuilder(
        Dictionary<ScenePlaybackAuraIdentity, AuraTimelineLaneBuilder> groups,
        ScenePlaybackAuraIdentity identity)
    {
        if (groups.TryGetValue(identity, out var builder))
            return builder;

        builder = new AuraTimelineLaneBuilder(identity);
        groups.Add(identity, builder);
        return builder;
    }

    private static PlaybackTimelineSpan[] MergeAuraCoverages(List<(long StartMilliseconds, long EndMilliseconds)> coverages, IBrush fillBrush, IBrush borderBrush)
    {
        if (coverages.Count == 0)
            return [];

        coverages.Sort(static (left, right) =>
        {
            var comparison = left.StartMilliseconds.CompareTo(right.StartMilliseconds);
            return comparison != 0 ? comparison : left.EndMilliseconds.CompareTo(right.EndMilliseconds);
        });
        var result = new List<PlaybackTimelineSpan>(coverages.Count);
        var start = coverages[0].StartMilliseconds;
        var end = coverages[0].EndMilliseconds;
        for (var i = 1; i < coverages.Count; i++)
        {
            var (coverageStart, coverageEnd) = coverages[i];
            if (coverageStart <= end)
            {
                end = Math.Max(end, coverageEnd);
                continue;
            }

            result.Add(new PlaybackTimelineSpan(start, end, fillBrush, borderBrush));
            start = coverageStart;
            end = coverageEnd;
        }

        result.Add(new PlaybackTimelineSpan(start, end, fillBrush, borderBrush));
        return result.ToArray();
    }

    private static int ResolveAuraPaletteIndex(uint displayResourceEffectRefRaw, int instanceSequenceId)
    {
        var value = displayResourceEffectRefRaw != 0 ? displayResourceEffectRefRaw : unchecked((uint)instanceSequenceId);
        value ^= value >> 16;
        return (int)(value % AuraAccentBrushes.Length);
    }

    private static double ResolveMarkerWeight(ScenePlaybackTrackMarker marker, int eventCount)
    {
        var amount = marker.Amount >= 0 ? (double)marker.Amount : -(double)marker.Amount;
        var baseWeight = marker.Track == ScenePlaybackTrack.Combat && amount > 0
            ? Math.Clamp(Math.Log10(amount + 1) * 2.2d, 3d, 12d)
            : 5d;
        return Math.Clamp(baseWeight + Math.Log2(Math.Max(1, eventCount)) * 0.75d, 3d, 12d);
    }

    private static int ResolveTrackCount(IReadOnlyList<ScenePlaybackTrackCount> counts, ScenePlaybackTrack track)
    {
        for (var i = 0; i < counts.Count; i++)
        {
            if (counts[i].Track == track)
                return counts[i].Count;
        }

        return 0;
    }

    public static IBrush ResolveTrackBrush(ScenePlaybackTrack track) => track switch
    {
        ScenePlaybackTrack.Combat => CombatBrush,
        ScenePlaybackTrack.Mechanic => MechanicBrush,
        ScenePlaybackTrack.Resource => ResourceBrush,
        ScenePlaybackTrack.EntityVital => EntityVitalBrush,
        ScenePlaybackTrack.Aura => AuraBrush,
        ScenePlaybackTrack.State => StateBrush,
        ScenePlaybackTrack.Scene => SceneBrush,
        ScenePlaybackTrack.Action => ActionBrush,
        ScenePlaybackTrack.Diagnostic => DiagnosticBrush,
        _ => OtherBrush
    };

    private static long SumSpanDuration(IReadOnlyList<PlaybackTimelineSpan> spans)
    {
        var total = 0d;
        for (var i = 0; i < spans.Count; i++)
            total += Math.Max(0d, spans[i].EndMilliseconds - spans[i].StartMilliseconds);

        return (long)Math.Round(total, MidpointRounding.AwayFromZero);
    }

    private static string FormatDuration(long milliseconds)
    {
        var value = TimeSpan.FromMilliseconds(Math.Max(0, milliseconds));
        return value.TotalHours >= 1 ? value.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture) : value.ToString(@"mm\:ss", CultureInfo.InvariantCulture);
    }

    private static string FormatAuraDisposition(AuraDisposition disposition, LocalizationService localization) => disposition switch
    {
        AuraDisposition.Buff => localization["Playback_AuraDisposition_Buff"],
        AuraDisposition.Debuff => localization["Playback_AuraDisposition_Debuff"],
        _ => localization["Playback_AuraDisposition_Unknown"]
    };

    private static string FormatAuraSemanticTrace(AuraSemanticTrace trace, LocalizationService localization)
    {
        return trace.Match switch
        {
            AuraSemanticMatchKind.ExactNode => string.Format(
                CultureInfo.CurrentCulture,
                localization["Playback_AuraEvidence_ExactNodeFormat"],
                trace.ResourceNodeId),
            AuraSemanticMatchKind.UnambiguousSlot => string.Format(
                CultureInfo.CurrentCulture,
                localization["Playback_AuraEvidence_UnambiguousSlotFormat"],
                trace.ResourceSkillId,
                trace.EffectSlot),
            _ when trace.HasResourceEvidence && trace.ResourceCandidateSlotCount > 1 => string.Format(
                CultureInfo.CurrentCulture,
                localization["Playback_AuraEvidence_AmbiguousFormat"],
                trace.ResourceCandidateSlotCount),
            _ => localization["Playback_AuraEvidence_None"]
        };
    }
}

internal sealed class AuraTimelineLaneBuilder(ScenePlaybackAuraIdentity identity)
{
    public ScenePlaybackAuraIdentity Identity { get; } = identity;

    public ResourceEffectRef DisplayResourceEffectRef => Identity.DisplayResourceEffectRef;

    public int InstanceSequenceId => Identity.InstanceSequenceId;

    public List<(long StartMilliseconds, long EndMilliseconds)> Coverages { get; } = [];

    public List<(long PositionMilliseconds, AuraLifecycleEventKind Kind)> Applications { get; } = [];

    public AuraSemanticValue Semantics { get; private set; }

    private bool HasSemantics { get; set; }

    public void ApplySemantics(AuraSemanticValue semantics)
    {
        if (!HasSemantics || ResolveEvidenceScore(semantics) > ResolveEvidenceScore(Semantics))
        {
            Semantics = semantics;
            HasSemantics = true;
        }
    }

    private static int ResolveEvidenceScore(AuraSemanticValue semantics)
    {
        var score = semantics.Disposition == AuraDisposition.Unknown ? 0 : 8;
        score += semantics.Trace.Match switch
        {
            AuraSemanticMatchKind.ExactNode => 4,
            AuraSemanticMatchKind.UnambiguousSlot => 3,
            _ when semantics.Trace.HasResourceEvidence => 2,
            _ when !semantics.Trace.ResourceEffectRef.IsEmpty => 1,
            _ => 0
        };
        return score;
    }
}

internal readonly record struct PlaybackTimelineBuildResult(PlaybackTimelineStrip Global, Dictionary<int, PlaybackTimelineStrip> Combatants);
