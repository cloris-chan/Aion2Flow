using Cloris.Aion2Flow.Combat.Classification;
using Cloris.Aion2Flow.Combat.Metrics;

namespace Cloris.Aion2Flow.Combat;

public sealed class EncounterTargetInfo
{
    private readonly HashSet<Guid> _processedPacketIds = new();

    public EncounterTargetInfo(int targetId, int damageAmount, long firstDamageTime, long lastDamageTime)
    {
        TargetId = targetId;
        DamageAmount = damageAmount;
        FirstDamageTime = firstDamageTime;
        LastDamageTime = lastDamageTime;
    }

    public int TargetId { get; }
    public int DamageAmount { get; private set; }
    public long FirstDamageTime { get; private set; }
    public long LastDamageTime { get; private set; }
    public long BattleTime => LastDamageTime - FirstDamageTime;

    public void ProcessPacket(ParsedCombatPacket packet)
    {
        if (_processedPacketIds.Contains(packet.Id))
        {
            return;
        }

        if (!CombatEventClassifier.CountsTowardsDamage(packet))
        {
            return;
        }

        DamageAmount += packet.Damage;
        var timestamp = packet.Timestamp;
        if (timestamp < FirstDamageTime)
        {
            FirstDamageTime = timestamp;
        }
        else if (timestamp > LastDamageTime)
        {
            LastDamageTime = timestamp;
        }

        _processedPacketIds.Add(packet.Id);
    }
}
