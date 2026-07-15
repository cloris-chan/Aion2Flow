using Cloris.Aion2Flow.Resources.Catalog;
using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Projection;
using Cloris.Aion2Flow.Services;
using Cloris.Aion2Flow.Services.Settings;

namespace Cloris.Aion2Flow.ViewModels;

internal static class BossFocusDisplayBuilder
{
    public static void Build<TSnapshots>(
        TSnapshots snapshots,
        IReadOnlyList<BossDamageContribution> damageContributions,
        SceneDisplayContext? displayContext,
        CombatantStatisticsScope statisticsScope,
        Func<int, bool> isDisplayedCombatant,
        List<BossFocusDisplayGroup> groups)
        where TSnapshots : IReadOnlyList<SceneBossFocusSnapshot>
    {
        groups.Clear();
        for (var i = 0; i < snapshots.Count; i++)
        {
            var snapshot = snapshots[i];
            if (!TryResolveDisplayActivity(snapshot, damageContributions, statisticsScope, isDisplayedCombatant, out var displayObservedAtMilliseconds))
                continue;

            var npcCode = ResolveNpcCode(displayContext, snapshot.InstanceId);
            var displayKey = ResolveDisplayKey(displayContext, snapshot.InstanceId, npcCode);
            var existingIndex = FindDisplayIndex(groups, displayKey);
            if (existingIndex < 0)
            {
                groups.Add(new BossFocusDisplayGroup(displayKey, snapshot, npcCode, 1, ResolveShareEffectiveHp(npcCode, snapshot), displayObservedAtMilliseconds));
                continue;
            }

            var existing = groups[existingIndex];
            var candidateWins = IsBetterRepresentative(existing.Representative, existing.RepresentativeObservedAtMilliseconds, snapshot, displayObservedAtMilliseconds);
            var representative = candidateWins ? snapshot : existing.Representative;
            var representativeNpcCode = candidateWins ? npcCode : existing.NpcCode;
            var representativeObservedAtMilliseconds = candidateWins ? displayObservedAtMilliseconds : existing.RepresentativeObservedAtMilliseconds;
            groups[existingIndex] = new BossFocusDisplayGroup(
                displayKey,
                representative,
                representativeNpcCode,
                existing.InstanceCount + 1,
                existing.EffectiveHp + ResolveShareEffectiveHp(npcCode, snapshot),
                representativeObservedAtMilliseconds);
        }
    }

    public static CombatantBossShareScope CreateShareScope(IReadOnlyList<BossFocusDisplayGroup> groups)
    {
        var effectiveHp = 0L;
        for (var i = 0; i < groups.Count; i++)
        {
            var group = groups[i];
            var boss = group.Representative;
            if (boss.Kind != NpcKind.Boss || group.EffectiveHp <= 0)
                continue;

            effectiveHp += group.EffectiveHp;
        }

        return new CombatantBossShareScope(effectiveHp);
    }

    public static long FindAggregateContributionAmount(
        IReadOnlyList<BossFocusDisplayGroup> groups,
        IReadOnlyList<BossDamageContribution> damageContributions,
        int combatantId)
    {
        var damage = 0L;
        for (var i = 0; i < groups.Count; i++)
        {
            var group = groups[i];
            var boss = group.Representative;
            if (boss.Kind != NpcKind.Boss || group.EffectiveHp <= 0)
                continue;

            damage += FindContributionAmount(damageContributions, boss.InstanceId, combatantId);
        }

        return damage;
    }

    public static long NormalizeHpForDisplay(NpcKind kind, int npcCode, long value)
    {
        if (value <= 0)
            return 0;

        if (kind != NpcKind.Boss)
            return value;

        var divisor = ResolveHpDisplayDivisor(npcCode);
        return divisor <= 1 ? value : (value + divisor / 2) / divisor;
    }

    public static int FindContributionStart(IReadOnlyList<BossDamageContribution> damageContributions, int bossId)
    {
        var left = 0;
        var right = damageContributions.Count - 1;
        while (left <= right)
        {
            var mid = left + ((right - left) >> 1);
            var contributionBossId = damageContributions[mid].BossId;
            if (contributionBossId == bossId)
            {
                while (mid > 0 && damageContributions[mid - 1].BossId == bossId)
                    mid--;
                return mid;
            }

            if (contributionBossId < bossId)
                left = mid + 1;
            else
                right = mid - 1;
        }

        return -1;
    }

    private static bool TryResolveDisplayActivity(
        SceneBossFocusSnapshot snapshot,
        IReadOnlyList<BossDamageContribution> damageContributions,
        CombatantStatisticsScope statisticsScope,
        Func<int, bool> isDisplayedCombatant,
        out long observedAtMilliseconds)
    {
        observedAtMilliseconds = snapshot.LastObservedAtMilliseconds;
        if (statisticsScope == CombatantStatisticsScope.All)
            return true;

        var start = FindContributionStart(damageContributions, snapshot.InstanceId);
        if (start < 0)
            return false;

        observedAtMilliseconds = 0;
        var hasDisplayActivity = false;
        for (var i = start; i < damageContributions.Count && damageContributions[i].BossId == snapshot.InstanceId; i++)
        {
            var contribution = damageContributions[i];
            if (contribution.DamageAmount > 0 && isDisplayedCombatant(contribution.SourceCombatantId))
            {
                hasDisplayActivity = true;
                observedAtMilliseconds = Math.Max(observedAtMilliseconds, contribution.LastObservedAtMilliseconds);
            }
        }

        return hasDisplayActivity &&
               Math.Max(0, snapshot.LastObservedAtMilliseconds - observedAtMilliseconds) <= SceneReadModelOwner.BossFocusVisibilityTimeoutMilliseconds;
    }

    private static long ResolveDisplayKey(SceneDisplayContext? displayContext, int instanceId, int npcCode)
    {
        if (npcCode > 0 &&
            displayContext?.ResolveNpcCodeCatalogEntry(npcCode) is { Kind: var kind } &&
            kind == NpcCatalogKind.TrainingDummy)
        {
            return -(long)npcCode;
        }

        return instanceId;
    }

    private static bool IsBetterRepresentative(
        SceneBossFocusSnapshot current,
        long currentObservedAtMilliseconds,
        SceneBossFocusSnapshot candidate,
        long candidateObservedAtMilliseconds)
    {
        var cmp = candidateObservedAtMilliseconds.CompareTo(currentObservedAtMilliseconds);
        return cmp > 0 || (cmp == 0 && candidate.InstanceId < current.InstanceId);
    }

    private static int ResolveNpcCode(SceneDisplayContext? displayContext, int instanceId)
        => displayContext is not null && displayContext.TryResolveNpcCode(instanceId, out var npcCode) ? npcCode : 0;

    private static int FindDisplayIndex(IReadOnlyList<BossFocusDisplayGroup> groups, long displayKey)
    {
        for (var i = 0; i < groups.Count; i++)
        {
            if (groups[i].DisplayKey == displayKey)
                return i;
        }

        return -1;
    }

    private static long ResolveShareEffectiveHp(int npcCode, SceneBossFocusSnapshot boss)
        => boss.HasHp && boss.HasMaxHp && boss.EffectiveHp > 0
            ? NormalizeHpForDisplay(boss.Kind, npcCode, boss.EffectiveHp)
            : 0;

    private static int ResolveHpDisplayDivisor(int npcCode)
    {
        if (npcCode > 0 && CombatResourceRegistry.TryResolveNpcCatalogEntry(npcCode, out var entry))
            return entry.HpDisplayDivisor;

        return 1;
    }

    private static long FindContributionAmount(IReadOnlyList<BossDamageContribution> damageContributions, int bossId, int sourceCombatantId)
    {
        var start = FindContributionStart(damageContributions, bossId);
        if (start < 0)
            return 0;

        for (var i = start; i < damageContributions.Count && damageContributions[i].BossId == bossId; i++)
        {
            var contribution = damageContributions[i];
            if (contribution.SourceCombatantId == sourceCombatantId)
                return Math.Max(0L, contribution.DamageAmount);
        }

        return 0;
    }
}

internal readonly record struct BossFocusDisplayGroup(
    long DisplayKey,
    SceneBossFocusSnapshot Representative,
    int NpcCode,
    int InstanceCount,
    long EffectiveHp,
    long RepresentativeObservedAtMilliseconds);

internal readonly record struct CombatantBossShareScope(long EffectiveHp);
