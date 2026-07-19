using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.SceneRuntime.Combat;

public enum CombatContributionPath : byte
{
    ProductionFallback = 0,
    PacketOnly = 1,
    SemanticOnly = 2
}

public sealed class CombatContributionPathResolver
{
    private readonly HashSet<PeriodicPoolKey> _materializedSemanticPoolGrants = [];

    public CombatContributionPathResolver(CombatContributionPath path)
    {
        if (!Enum.IsDefined(path))
            throw new ArgumentOutOfRangeException(nameof(path), path, "Combat contribution path is invalid.");

        Path = path;
    }

    private CombatContributionPathResolver(CombatContributionPathResolverSnapshot snapshot)
    {
        Path = snapshot.Path;
        _materializedSemanticPoolGrants.UnionWith(snapshot.MaterializedSemanticPoolGrants);
    }

    public CombatContributionPath Path { get; }

    public bool TryResolve(
        int sourceId,
        int targetId,
        in CombatWireObservation observation,
        in CombatOccurrenceResolution occurrence,
        out CombatContribution contribution) =>
        TryResolve(sourceId, targetId, in observation, in occurrence, out contribution, out _);

    public bool TryResolve(
        int sourceId,
        int targetId,
        in CombatWireObservation observation,
        in CombatOccurrenceResolution occurrence,
        in CombatPacketEvidence packet,
        in CombatSemanticEvidence semantic,
        out CombatContribution contribution)
    {
        if (TrySuppressPoolOccurrence(targetId, in observation, in occurrence, out var poolKey))
        {
            contribution = default;
            return false;
        }

        var resolved = Path switch
        {
            CombatContributionPath.ProductionFallback => CombatContributionResolver.TryResolve(sourceId, targetId, in observation, in occurrence, out contribution),
            CombatContributionPath.PacketOnly => CombatContributionResolver.TryResolvePacketOnly(sourceId, targetId, in observation, in occurrence, in packet, out contribution),
            CombatContributionPath.SemanticOnly => CombatContributionResolver.TryResolveSemanticOnly(sourceId, targetId, in observation, in occurrence, in semantic, out contribution),
            _ => throw new InvalidOperationException($"Unsupported combat contribution path: {Path}.")
        };
        TrackSemanticPoolGrant(poolKey, in occurrence, resolved, in contribution);
        return resolved;
    }

    internal bool TryResolve(
        int sourceId,
        int targetId,
        in CombatWireObservation observation,
        in CombatOccurrenceResolution occurrence,
        out CombatContribution contribution,
        out bool suppressedOccurrence)
    {
        if (TrySuppressPoolOccurrence(targetId, in observation, in occurrence, out var poolKey))
        {
            contribution = default;
            suppressedOccurrence = true;
            return false;
        }

        suppressedOccurrence = false;
        var resolved = Path switch
        {
            CombatContributionPath.ProductionFallback => CombatContributionResolver.TryResolve(sourceId, targetId, in observation, in occurrence, out contribution),
            CombatContributionPath.PacketOnly => CombatContributionResolver.TryResolvePacketOnly(sourceId, targetId, in observation, in occurrence, out contribution),
            CombatContributionPath.SemanticOnly => CombatContributionResolver.TryResolveSemanticOnly(sourceId, targetId, in observation, in occurrence, out contribution),
            _ => throw new InvalidOperationException($"Unsupported combat contribution path: {Path}.")
        };

        TrackSemanticPoolGrant(poolKey, in occurrence, resolved, in contribution);
        return resolved;
    }

    private bool TrySuppressPoolOccurrence(
        int targetId,
        in CombatWireObservation observation,
        in CombatOccurrenceResolution occurrence,
        out PeriodicPoolKey poolKey)
    {
        poolKey = PeriodicPoolKey.Create(targetId, in observation);
        if (occurrence.Suppression == CombatSuppressionReason.PeriodicPoolClosed)
        {
            _materializedSemanticPoolGrants.Remove(poolKey);
            return true;
        }

        if (occurrence.Suppression == CombatSuppressionReason.PeriodicPoolSemanticCandidate)
            _materializedSemanticPoolGrants.Remove(poolKey);

        return occurrence.PacketRule == CombatPacketRule.PeriodicShieldGrant &&
               _materializedSemanticPoolGrants.Remove(poolKey);
    }

    private void TrackSemanticPoolGrant(
        PeriodicPoolKey poolKey,
        in CombatOccurrenceResolution occurrence,
        bool resolved,
        in CombatContribution contribution)
    {
        if (resolved &&
            occurrence.Suppression == CombatSuppressionReason.PeriodicPoolSemanticCandidate &&
            contribution is { Metric: CombatMetricKind.ShieldGranted, Delivery: CombatDeliveryKind.Pool })
        {
            _materializedSemanticPoolGrants.Add(poolKey);
        }
    }

    internal CombatContributionPathResolverSnapshot CreateSnapshot() =>
        new(Path, [.. _materializedSemanticPoolGrants]);

    internal static CombatContributionPathResolver FromSnapshot(CombatContributionPathResolverSnapshot snapshot) =>
        new(snapshot);
}

internal readonly record struct PeriodicPoolKey(int TargetId, int ChainId, int SkillIdentityCode)
{
    public static PeriodicPoolKey Create(int targetId, in CombatWireObservation observation) =>
        new(targetId, observation.ChainId, Math.Max(0, observation.PeriodicTailSkillCodeRaw));
}

internal sealed record CombatContributionPathResolverSnapshot(
    CombatContributionPath Path,
    PeriodicPoolKey[] MaterializedSemanticPoolGrants);
