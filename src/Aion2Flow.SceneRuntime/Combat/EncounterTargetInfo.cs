namespace Cloris.Aion2Flow.SceneRuntime.Combat;

public sealed class EncounterTargetInfo(int targetId, int damageAmount, long firstDamageTime, long lastDamageTime)
{
    private readonly HashSet<Guid> _processedPacketIds = new();

    public int TargetId { get; } = targetId;
    public int DamageAmount { get; private set; } = damageAmount;
    public long FirstDamageTime { get; private set; } = firstDamageTime;
    public long LastDamageTime { get; private set; } = lastDamageTime;
    public long EncounterTime => LastDamageTime - FirstDamageTime;

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
