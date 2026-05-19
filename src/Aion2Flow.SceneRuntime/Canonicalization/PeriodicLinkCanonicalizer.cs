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

    internal StateSnapshot CreateStateSnapshot()
    {
        var order = new SignatureStateSnapshot[_order.Count];
        var index = 0;
        foreach (var signature in _order)
            order[index++] = new SignatureStateSnapshot(signature.TargetId, signature.LinkId, signature.SequenceId, signature.TailRaw, signature.BatchOrdinal);
        return new StateSnapshot(order);
    }

    internal void RestoreState(StateSnapshot snapshot)
    {
        _resolved.Clear();
        _order.Clear();
        foreach (ref readonly var item in snapshot.Order.AsSpan())
        {
            var signature = new Signature(item.TargetId, item.LinkId, item.SequenceId, item.TailRaw, item.BatchOrdinal);
            _resolved.Add(signature);
            _order.Enqueue(signature);
        }
    }

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
        var invincible = new CombatObservation
        {
            OriginalSkillCode = skillCode,
            SkillCode = skillCode,
            Marker = observation.Marker,
            DetailRaw = observation.DetailRaw,
            Damage = 0,
            HitCount = 0,
            AttemptCount = 1,
            Modifiers = DamageModifiers.Invincible,
            EventKind = CombatEventKind.Damage,
            ValueKind = CombatValueKind.Damage,
            EffectTag = PacketEffectTag.PeriodicLinkInvincible
        };
        return CombatResourceRegistry.NormalizeObservationForStorage(sourceId, targetId, in invincible) with { ChainId = 0 };
    }

    private void TrimResolved()
    {
        while (_order.Count > MaxResolvedLinks)
        {
            var signature = _order.Dequeue();
            _resolved.Remove(signature);
        }
    }

    internal sealed class StateSnapshot(SignatureStateSnapshot[] order)
    {
        public SignatureStateSnapshot[] Order { get; } = order;

        public StateSnapshot DeepClone()
        {
            var order = new SignatureStateSnapshot[Order.Length];
            Array.Copy(Order, order, order.Length);
            return new StateSnapshot(order);
        }
    }

    internal readonly record struct SignatureStateSnapshot(int TargetId, int LinkId, int SequenceId, int TailRaw, long BatchOrdinal);
}
