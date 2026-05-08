using Cloris.Aion2Flow.Protocol.Combat;
using Cloris.Aion2Flow.SceneRuntime.Combat;
using Cloris.Aion2Flow.SceneRuntime.Model;
using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.SceneRuntime.Canonicalization;

public sealed class PeriodicLinkCanonicalizer
{
    private const int MaxResolvedLinks = 128;
    private readonly record struct Signature(int TargetId, int LinkId, int SequenceId, int TailRaw, long BatchOrdinal);
    private readonly HashSet<Signature> _resolved = [];
    private readonly Queue<Signature> _order = [];

    public static bool IsLinkObservation(in CombatObservation observation) => observation.Type == 48 && observation.Damage == 0 && observation.HitCount == 0 && observation.AttemptCount == 0;

    public CombatCanonicalizationResult? Normalize(int sourceId, int targetId, in TimelineStamp stamp, in CombatObservation observation)
    {
        if (!IsLinkObservation(in observation) || targetId <= 0 || sourceId <= 0 || targetId != sourceId || observation.DetailRaw <= 0 || observation.DetailRaw > int.MaxValue || observation.Marker <= 0)
            return null;

        var linkId = (int)observation.DetailRaw;
        var signature = new Signature(targetId, linkId, observation.Marker, observation.SkillCode, stamp.BatchOrdinal);
        if (!_resolved.Add(signature))
            return null;

        _order.Enqueue(signature);
        TrimResolved();

        if (linkId == targetId)
            return null;

        return new CombatCanonicalizationResult(linkId, targetId, CreateInvincible(linkId, targetId, in observation));
    }

    private static CombatObservation CreateInvincible(int sourceId, int targetId, in CombatObservation observation)
    {
        var skillCode = observation.SkillCode > 0 ? observation.SkillCode : SyntheticCombatSkillCodes.UnresolvedInvincible;
        var packet = new ParsedCombatPacket
        {
            SourceId = sourceId,
            TargetId = targetId,
            OriginalSkillCode = skillCode,
            SkillCode = skillCode,
            Marker = observation.Marker,
            DetailRaw = observation.DetailRaw,
            Timestamp = 0,
            FrameOrdinal = 0,
            BatchOrdinal = 0,
            Damage = 0,
            HitContribution = 0,
            AttemptContribution = 1,
            Modifiers = DamageModifiers.Invincible,
            EventKind = CombatEventKind.Damage,
            ValueKind = CombatValueKind.Damage
        };
        packet.SetEffectTag(PacketEffectTag.PeriodicLinkInvincible);
        CombatResourceRegistry.NormalizePacketForStorage(packet);
        return observation with
        {
            SkillCode = packet.SkillCode,
            OriginalSkillCode = packet.OriginalSkillCode,
            BaseSkillCode = packet.BaseSkillCode,
            Damage = packet.Damage,
            HitCount = packet.HitContribution,
            AttemptCount = packet.AttemptContribution,
            DetailRaw = packet.DetailRaw,
            Marker = packet.Marker,
            Type = packet.Type,
            Flag = packet.Flag,
            LayoutTag = packet.LayoutTag,
            Loop = packet.Loop,
            MultiHitCount = packet.MultiHitCount,
            DrainHealAmount = packet.DrainHealAmount,
            RegenerationAmount = packet.RegenerationAmount,
            Modifiers = packet.Modifiers,
            ResourceKind = packet.ResourceKind,
            EventKind = packet.EventKind,
            ValueKind = packet.ValueKind,
            EffectTag = packet.EffectTag,
            PeriodicRelation = packet.PeriodicRelation,
            PeriodicMode = packet.PeriodicMode,
            ChainId = 0
        };
    }

    private void TrimResolved()
    {
        while (_order.Count > MaxResolvedLinks)
        {
            var signature = _order.Dequeue();
            _resolved.Remove(signature);
        }
    }
}
