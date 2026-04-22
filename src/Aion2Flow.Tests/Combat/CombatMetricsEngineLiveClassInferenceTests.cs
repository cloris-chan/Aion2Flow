using System.Globalization;
using Cloris.Aion2Flow.Battle.Model;
using Cloris.Aion2Flow.Battle.Runtime;
using Cloris.Aion2Flow.PacketCapture.Streams;
using Cloris.Aion2Flow.Resources;
using Cloris.Aion2Flow.Tests.Protocol;

namespace Cloris.Aion2Flow.Tests.Combat;

public sealed class CombatMetricsEngineLiveClassInferenceTests
{
    [Fact]
    public void LiveReplay_20260423002750_Does_Not_Drop_Combatant_Class_After_It_Is_Inferred()
    {
        CombatMetricsEngine.SetGameResources(ResourceDatabase.LoadCombatSkills(), new Dictionary<int, NpcCatalogEntry>());

        var store = new CombatMetricsStore();
        var engine = new CombatMetricsEngine(store);
        using var processor = new PacketStreamProcessor(store);

        const int combatantId = 2906;
        CharacterClass? firstResolvedClass = null;
        var lostClassSnapshots = new List<string>();
        var overflowSnapshots = new List<string>();

        foreach (var entry in ReadStreamLogEntries("aion2flow.stream.20260423002750.log"))
        {
            if (!entry.IsInbound)
            {
                continue;
            }

            if (!processor.AppendAndProcess(entry.Payload, entry.Connection, entry.TimestampMilliseconds))
            {
                continue;
            }

            var snapshot = engine.CreateBattleSnapshot();
            if (!snapshot.Combatants.TryGetValue(combatantId, out var combatant))
            {
                continue;
            }

            if (combatant.CharacterClass is { } characterClass)
            {
                if (firstResolvedClass is null)
                {
                    firstResolvedClass = characterClass;
                }

                if (combatant.DamageContribution > 1.0000000001d)
                {
                    overflowSnapshots.Add(
                        $"ts={entry.TimestampMilliseconds} dmg={combatant.DamageAmount} dps={combatant.DamagePerSecond:F2} share={combatant.DamageContribution:P4} battle={snapshot.BattleTime} name={combatant.Nickname}");
                }

                continue;
            }

            if (firstResolvedClass is not null && combatant.DamageAmount > 0)
            {
                lostClassSnapshots.Add(
                    $"ts={entry.TimestampMilliseconds} dmg={combatant.DamageAmount} dps={combatant.DamagePerSecond:F2} share={combatant.DamageContribution:P4} battle={snapshot.BattleTime} name={combatant.Nickname}");
                break;
            }
        }

        Assert.NotNull(firstResolvedClass);
        Assert.True(lostClassSnapshots.Count == 0, string.Join(Environment.NewLine, lostClassSnapshots));
        Assert.True(overflowSnapshots.Count == 0, string.Join(Environment.NewLine, overflowSnapshots));
    }

    private static IEnumerable<StreamLogEntry> ReadStreamLogEntries(string fileName)
    {
        foreach (var line in File.ReadLines(FixtureHelper.GetPath($"logs/{fileName}")))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var parts = line.Split('|');
            if (parts.Length < 6)
            {
                continue;
            }

            var timestamp = DateTimeOffset.Parse(parts[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUnixTimeMilliseconds();
            var isInbound = parts[1].Equals("dir=inbound", StringComparison.OrdinalIgnoreCase);

            if (!TryParseConnection(parts[2], out var connection))
            {
                continue;
            }

            var dataPart = parts.FirstOrDefault(part => part.StartsWith("data=", StringComparison.OrdinalIgnoreCase));
            if (dataPart is null)
            {
                continue;
            }

            yield return new StreamLogEntry(timestamp, isInbound, connection, Convert.FromHexString(dataPart[5..]));
        }
    }

    private static bool TryParseConnection(string value, out TcpConnection connection)
    {
        connection = default;

        var arrowIndex = value.IndexOf("->", StringComparison.Ordinal);
        if (arrowIndex <= 0)
        {
            return false;
        }

        if (!TryParseEndpoint(value[..arrowIndex], out var sourceAddress, out var sourcePort))
        {
            return false;
        }

        if (!TryParseEndpoint(value[(arrowIndex + 2)..], out var destinationAddress, out var destinationPort))
        {
            return false;
        }

        connection = new TcpConnection(sourceAddress, destinationAddress, sourcePort, destinationPort);
        return true;
    }

    private static bool TryParseEndpoint(string value, out uint address, out ushort port)
    {
        address = 0;
        port = 0;

        var separatorIndex = value.IndexOf(':');
        if (separatorIndex <= 0)
        {
            return false;
        }

        return uint.TryParse(value[..separatorIndex], NumberStyles.Integer, CultureInfo.InvariantCulture, out address)
            && ushort.TryParse(value[(separatorIndex + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out port);
    }

    private readonly record struct StreamLogEntry(long TimestampMilliseconds, bool IsInbound, TcpConnection Connection, byte[] Payload);
}