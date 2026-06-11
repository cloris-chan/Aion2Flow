using Cloris.Aion2Flow.Tests.Protocol;

namespace Cloris.Aion2Flow.Tests;

public static class ReplayScenarioCatalog
{
    public const string CanonicalSceneReplayLog = "aion2flow.stream.20260415211500.log";
    public const string DeterministicReplaySecondaryLog = "aion2flow.stream.20260419204630.log";
    public const string GroundAoeAttribution = "aion2flow.stream.20260417003456.log";
    public const string ClericHealingNoFalseDrain = "aion2flow.stream.20260417023559.log";
    public const string LightOfRegenerationPeriodicHealing = "aion2flow.stream.20260417141813.log";
    public const string InstanceClearRestoreAndMapBoundary = "aion2flow.stream.20260419204630.log";
    public const string VisibleDamageContributionBoundary = "aion2flow.stream.20260423001617.log";
    public const string EnhanceSpiritBenedictionSelfAndSummonHealing = "aion2flow.stream.20260426031332.log";
    public const string SummonRestoresAndTargetSupport = "aion2flow.stream.20260426140354.log";
    public const string PeriodicLinkInvariant = "aion2flow.stream.20260412103519.log";
    public const string CompactPrimaryInvariantA = "aion2flow.stream.20260411174533.log";
    public const string CompactPrimaryInvariantB = "aion2flow.stream.20260411215842.log";
    public const string CompactSidecarCancellation = "aion2flow.stream.20260512223507.log";
    public const string ShieldAbsorbedInvariant = "aion2flow.stream.20260411192501.log";
    public const string SplitTransportFrameRecovery = "aion2flow.stream.20260610222129.log";
    public const string PcMetadata048D = "aion2flow.stream.20260610232724.log";
    public const string NpcCatalogState4136 = "aion2flow.stream.20260610232724.log";
    public const string BossCatalogState4136 = "aion2flow.stream.20260610232551.log";
    public const string PcMetadata4536 = "aion2flow.stream.20260610235630.log";
    public const string Direct0438BodySkillVariant = "aion2flow.stream.20260610235630.log";
    public const string SummonCreateState4136 = "aion2flow.stream.20260611024229.log";
    public const string ElementalistSummonBossDamageAttribution = "aion2flow.stream.20260611034030.log";
    public const string PlaybackSceneRelativeTimestamps = "aion2flow.stream.20260611151958.log";

    public static IEnumerable<object[]> April11IncomingAvoidance =>
    [
        [new ReplayAvoidanceScenario("aion2flow.stream.20260411174533.log", 3, 0)],
        [new ReplayAvoidanceScenario("aion2flow.stream.20260411174739.log", 0, 3)],
        [new ReplayAvoidanceScenario("aion2flow.stream.20260411184521.log", 2, 2)],
        [new ReplayAvoidanceScenario("aion2flow.stream.20260411192501.log", 6, 1)],
        [new ReplayAvoidanceScenario("aion2flow.stream.20260411205158.log", 3, 2)],
        [new ReplayAvoidanceScenario("aion2flow.stream.20260411210634.log", 5, 0)],
        [new ReplayAvoidanceScenario("aion2flow.stream.20260411212441.log", 1, 0)],
        [new ReplayAvoidanceScenario("aion2flow.stream.20260411215842.log", 7, 0)],
        [new ReplayAvoidanceScenario("aion2flow.stream.20260411232425.log", 10, 3)],
        [new ReplayAvoidanceScenario("aion2flow.stream.20260411235759.log", 1, 1)],
        [new ReplayAvoidanceScenario("aion2flow.stream.20260412103519.log", 18, 7)],
        [new ReplayAvoidanceScenario("aion2flow.stream.20260412110721.log", 10, 7)]
    ];

    public static IEnumerable<object[]> ReportedMultiSourceInvincibles =>
    [
        [new ReplayAvoidanceScenario("aion2flow.stream.20260412103519.log", 18, 7)],
        [new ReplayAvoidanceScenario("aion2flow.stream.20260412110721.log", 10, 7)]
    ];

    public static IEnumerable<object[]> OutgoingCombatStats =>
    [
        [new ReplayOutgoingCombatStatsScenario(CanonicalSceneReplayLog, 20_211_224, 1_304, 1_312, ExpectedOutgoingInvincibles: 8)],
        [new ReplayOutgoingCombatStatsScenario("aion2flow.stream.20260416021557.log", 7_920_567, 1_166, 1_166, ExpectedIncomingHealing: 569_015)],
        [new ReplayOutgoingCombatStatsScenario("aion2flow.stream.20260416021406.log", 3_961_239, 524, 524, ExpectedIncomingHealing: 139_411)]
    ];

    public static IEnumerable<object[]> Mode10PacketOnlyDamage =>
    [
        [new ReplayMode10DamageScenario("aion2flow.stream.20260602221303.log", 5_346, 18_532, 5_346, 16_140_030, 12, 16_480)],
        [new ReplayMode10DamageScenario("aion2flow.stream.20260603005149.log", 30_299, 29_736, 2_359, 16_001_112, 6, 3_102)],
        [new ReplayMode10DamageScenario("aion2flow.stream.20260604124721.log", 7_386, 20_368, 7_386, 16_140_030, 7, 11_293)],
        [new ReplayMode10DamageScenario("aion2flow.stream.20260604133258.log", 18_117, 25_154, 7_386, 16_001_112, 5, 2_415)],
        [new ReplayMode10DamageScenario("aion2flow.stream.20260605000843.log", 16_332, 26_450, 16_332, 13_730_007, 35, 60_198)]
    ];

    public static IEnumerable<object[]> MultiHitDiagnostics =>
    [
        [new ReplayMultiHitScenario("aion2flow.stream.20260413173207.log", 3)],
        [new ReplayMultiHitScenario("aion2flow.stream.20260412121709.log", 38)],
        [new ReplayMultiHitScenario("aion2flow.stream.20260412180806.log", 41)],
        [new ReplayMultiHitScenario("aion2flow.stream.20260412182736.log", 51)],
        [new ReplayMultiHitScenario("aion2flow.stream.20260413012324.log", 7)],
        [new ReplayMultiHitScenario("aion2flow.stream.20260413012534.log", 6)],
        [new ReplayMultiHitScenario("aion2flow.stream.20260413012637.log", 19)],
        [new ReplayMultiHitScenario("aion2flow.stream.20260413021020.log", 39)],
        [new ReplayMultiHitScenario("aion2flow.stream.20260413021314.log", 17)],
        [new ReplayMultiHitScenario("aion2flow.stream.20260413021419.log", 30)],
        [new ReplayMultiHitScenario("aion2flow.stream.20260414044851.log", 54)],
        [new ReplayMultiHitScenario("aion2flow.stream.20260414045123.log", 61)],
        [new ReplayMultiHitScenario("aion2flow.stream.20260414045207.log", 119)]
    ];

    public static IEnumerable<object[]> DeterministicReplayLogs =>
    [
        [CanonicalSceneReplayLog],
        [DeterministicReplaySecondaryLog]
    ];

    public static IEnumerable<string> VendoredStreamLogNames()
    {
        var dir = FixtureHelper.GetPath("logs");
        foreach (var path in Directory.GetFiles(dir, "*.stream.*.log").Order(StringComparer.Ordinal))
            yield return Path.GetFileName(path);
    }
}

public sealed record ReplayAvoidanceScenario(string FileName, int ExpectedEvades, int ExpectedInvincibles)
{
    public override string ToString() => FileName;
}

public sealed record ReplayOutgoingCombatStatsScenario(string FileName, long ExpectedOutgoingDamage, int ExpectedOutgoingHits, int ExpectedOutgoingAttempts, int? ExpectedOutgoingInvincibles = null, int? ExpectedIncomingHealing = null)
{
    public override string ToString() => FileName;
}

public sealed record ReplayMode10DamageScenario(string FileName, int SourceId, int TargetId, int CombatantId, int TailSkillCode, int ExpectedPacketCount, long ExpectedDamage)
{
    public override string ToString() => FileName;
}

public sealed record ReplayMultiHitScenario(string FileName, int ExpectedMultiHitCount)
{
    public override string ToString() => FileName;
}
