using System.Globalization;
using Cloris.Aion2Flow.Protocol.Combat;
using Cloris.Aion2Flow.SceneRuntime.Observation;

namespace Cloris.Aion2Flow.SceneRuntime.Combat;

public readonly record struct CombatEventKey(int SkillCode, ResourceEffectRef BodyResourceEffectRef, ResourceEffectRef DetailResourceEffectRef) : IComparable<CombatEventKey>
{
    private const string DefaultUnknownEffectLabel = "Unknown effect";

    public bool HasSkillCode => SkillCode > 0;

    public static CombatEventKey FromObservation(in CombatObservation observation)
    {
        if (observation.SkillCode > 0)
            return new CombatEventKey(observation.SkillCode, default, default);

        return new CombatEventKey(0, observation.BodyResourceEffectRef, observation.DetailResourceEffectRef);
    }

    public string FormatFallbackLabel(string unknownEffectLabel = DefaultUnknownEffectLabel)
    {
        if (HasSkillCode)
            return string.Empty;

        var label = string.IsNullOrWhiteSpace(unknownEffectLabel) ? DefaultUnknownEffectLabel : unknownEffectLabel;

        if (!BodyResourceEffectRef.IsEmpty && !DetailResourceEffectRef.IsEmpty)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{label} B:{BodyResourceEffectRef.RawId} D:{DetailResourceEffectRef.RawId}");
        }

        if (!BodyResourceEffectRef.IsEmpty)
            return string.Create(CultureInfo.InvariantCulture, $"{label} B:{BodyResourceEffectRef.RawId}");

        if (!DetailResourceEffectRef.IsEmpty)
            return string.Create(CultureInfo.InvariantCulture, $"{label} D:{DetailResourceEffectRef.RawId}");

        return label;
    }

    public string FormatSortKey(string unknownEffectLabel = DefaultUnknownEffectLabel) => HasSkillCode ? SkillCode.ToString(CultureInfo.InvariantCulture) : FormatFallbackLabel(unknownEffectLabel);

    public int CompareTo(CombatEventKey other)
    {
        var cmp = SkillCode.CompareTo(other.SkillCode);
        if (cmp != 0) return cmp;
        cmp = BodyResourceEffectRef.RawId.CompareTo(other.BodyResourceEffectRef.RawId);
        if (cmp != 0) return cmp;
        return DetailResourceEffectRef.RawId.CompareTo(other.DetailResourceEffectRef.RawId);
    }
}
